using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Core.Rendering;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Diegetic sonar-return VFX: each echo becomes a real expanding water ripple,
     * emitted at the contact's actual world position via DistortionRippleBus.
     *
     * Because the distortion ring expands from its true world point, on-screen
     * contacts ripple right where they sit, while off-screen contacts sweep into
     * frame as a natural ARC from their direction — no special half-ring rendering
     * needed. In EchoWavefront mode the ripple is released at the physical
     * reflection moment (half the echo's round trip) with its expansion speed
     * matched to the sonar's ping speed, so the wavefront washes over the sub at
     * the exact instant SonarReturnAudio beeps.
     *
     * Everything about the wave reads distance: near contacts arrive as big, slow,
     * fat, repeated swells; far ones as a single faint thin ring that barely kisses
     * the screen edge — the "which way did that come from?" mini-game.
     *
     * Sibling of SonarSystem inside the sonar prefab (same pattern as
     * SonarReturnAudio); per-sub by construction via the SubmarineComponent base.
     */
    public class SonarReturnRipples : SubmarineComponent
    {
        /** When the ripple starts its life relative to the echo's timeline. */
        public enum RippleSync
        {
            /** Emit at the physical reflection moment so the ring reaches the sub as the return beep plays. */
            EchoWavefront,
            /** Emit the moment the return resolves (simpler: ripple starts at the contact when the beep plays). */
            OnReturn
        }

        // =====================
        // Behaviour
        // =====================

        [FoldoutGroup("Behaviour")]
        [Tooltip("Master toggle so the ripple layer can be auditioned alongside the canvas HUD.")]
        [SerializeField] private bool emitRipples = true;

        [FoldoutGroup("Behaviour")]
        [Tooltip("EchoWavefront: ripple launches from the contact at the reflection moment and arrives at the " +
                 "sub in sync with the return beep. OnReturn: ripple starts at the contact when the beep plays.")]
        [SerializeField] private RippleSync syncMode = RippleSync.EchoWavefront;

        [FoldoutGroup("Behaviour")]
        [Tooltip("Lowest sonar tier that shows return ripples. The ripple inherently reveals bearing, " +
                 "so Direction is the honest default; drop to Presence to leak direction early.")]
        [SerializeField] private SonarTier minimumTier = SonarTier.Direction;

        [FoldoutGroup("Behaviour")]
        [Tooltip("Cap on contacts that get ripples per ping. The distortion pool holds 16 rings shared " +
                 "with wakes/impacts, so a crowded ping should not flood it (first detected wins).")]
        [SerializeField, Range(1, 12)] private int maxRipplesPerPing = 5;

        [FoldoutGroup("Behaviour")]
        [Tooltip("Multiplier on the sonar ping speed for the visual wavefront. 1 = physically synced " +
                 "with the echo; lower makes waves lag dramatically behind the beep, higher snaps them in.")]
        [SerializeField, Range(0.25f, 3f)] private float waveTravelScale = 1f;

        // =====================
        // Signature Voice
        // =====================

        [FoldoutGroup("Signature Voice")]
        [InfoBox("One gate for every identity trait: at this tier and above, a contact's wave takes on " +
                 "its signature's Ripple Voice (tint, chromatic glint, rhythm, frequency/width character) " +
                 "and its size class biases strength. Below it, every echo ripples generically — the " +
                 "distance mapping above is never gated.")]
        [Tooltip("Sonar tier that unlocks signature-flavoured waves.")]
        [SerializeField] private SonarTier voiceTier = SonarTier.Identify;

        [FoldoutGroup("Signature Voice")]
        [Tooltip("How much the signature's size class biases wave strength once voiced " +
                 "(0 = distance only, 1 = full Tiny 0.4× … Huge 1.6× swing).")]
        [SerializeField, Range(0f, 1f)] private float sizeStrengthInfluence = 0.5f;

        // =====================
        // Wave Shape
        // =====================

        [FoldoutGroup("Wave Shape")]
        [InfoBox("Each pair maps a contact's proximity onto the wave: Near = on top of the sub, " +
                 "Far = at max sonar range. Tune Far low/faint to make distant returns a spotting game.")]
        [Tooltip("Displacement strength the ring should still have when it reaches the sub — near contact.")]
        [SerializeField, Range(0f, 0.2f)] private float arrivalStrengthNear = 0.12f;

        [FoldoutGroup("Wave Shape")]
        [Tooltip("Arrival strength for a contact at max range. Keep barely-visible for the mini-game.")]
        [SerializeField, Range(0f, 0.2f)] private float arrivalStrengthFar = 0.02f;

        [FoldoutGroup("Wave Shape")]
        [Tooltip("Shapes proximity → strength (like the audio's loudness contrast): >1 keeps far/mid " +
                 "contacts subtle and lets only genuinely close ones hit hard.")]
        [SerializeField, Range(1f, 4f)] private float strengthContrast = 1.6f;

        [FoldoutGroup("Wave Shape")]
        [Tooltip("Wave cycles in the ring for a near contact (lower = broad swell).")]
        [SerializeField, Range(1f, 30f)] private float frequencyNear = 10f;

        [FoldoutGroup("Wave Shape")]
        [Tooltip("Wave cycles for a far contact (higher = tight shimmer).")]
        [SerializeField, Range(1f, 30f)] private float frequencyFar = 20f;

        [FoldoutGroup("Wave Shape")]
        [Tooltip("Phase/oscillation speed for a near contact — higher throbs harder.")]
        [SerializeField, Range(0f, 30f)] private float waveSpeedNear = 14f;

        [FoldoutGroup("Wave Shape")]
        [Tooltip("Phase/oscillation speed for a far contact.")]
        [SerializeField, Range(0f, 30f)] private float waveSpeedFar = 7f;

        [FoldoutGroup("Wave Shape")]
        [Tooltip("Ring band width in viewport units for a near contact (fat, soft front).")]
        [SerializeField, Range(0.01f, 0.5f)] private float ringWidthNear = 0.12f;

        [FoldoutGroup("Wave Shape")]
        [Tooltip("Ring band width for a far contact (thin, precise line).")]
        [SerializeField, Range(0.01f, 0.5f)] private float ringWidthFar = 0.04f;

        // =====================
        // Pulse Train
        // =====================

        [FoldoutGroup("Pulse Train")]
        [Tooltip("Ripple pulses for a near contact — close things send a train of waves.")]
        [SerializeField, Range(1, 5)] private int pulseCountNear = 3;

        [FoldoutGroup("Pulse Train")]
        [Tooltip("Ripple pulses for a far contact — usually a single lonely ring.")]
        [SerializeField, Range(1, 5)] private int pulseCountFar = 1;

        [FoldoutGroup("Pulse Train")]
        [Tooltip("Seconds between pulses in a train.")]
        [SerializeField, Range(0.05f, 1f)] private float pulseInterval = 0.22f;

        [FoldoutGroup("Pulse Train")]
        [Tooltip("Strength multiplier applied to each successive pulse (trailing waves die off).")]
        [SerializeField, Range(0.2f, 1f)] private float pulseDecay = 0.65f;

        // =====================
        // Timing
        // =====================

        [FoldoutGroup("Timing")]
        [Tooltip("Seconds the ring keeps travelling/fading after it reaches the sub, so the wave " +
                 "visibly washes past instead of vanishing on arrival.")]
        [SerializeField, Range(0.25f, 4f)] private float lingerAfterArrival = 1.25f;

        [FoldoutGroup("Timing")]
        [Tooltip("Safety cap on the emitted strength after fade compensation (long flights emit hot " +
                 "so they still read on arrival; this stops extreme values).")]
        [SerializeField, Range(0.05f, 0.5f)] private float maxEmitStrength = 0.3f;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired each time a return ripple is emitted, with its world position — for layering juice.")]
        public UnityEvent<Vector2> onRippleEmitted;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private int PendingRippleCount => _pending.Count;

        // =====================
        // State
        // =====================

        /** A ripple emission scheduled for a future moment of the echo's flight. */
        private struct PendingRipple
        {
            public float dueTime;          // absolute Time.time to fire
            public Vector3 worldPos;       // contact's reflection point
            public float worldSpeed;       // wavefront speed in world units/sec
            public float travelTime;       // seconds the ring needs to reach the sub
            public float arrivalStrength;  // desired strength when the ring reaches the sub
            public float frequency;        // spatial wave cycles
            public float phaseSpeed;       // oscillation rate
            public float ringWidth;        // band width in viewport units
            public Color tint;             // identity glow (clear when unvoiced)
            public float chromaticBoost;   // identity chromatic fringe (1 = neutral)
        }

        private SonarSystem _sonar;
        private readonly List<PendingRipple> _pending = new();
        private int _ripplesThisPing;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        /** Bind in Start so the sibling SonarSystem (registered in its own Awake) is resolvable. */
        private void Start()
        {
            _sonar = Sub != null ? Sub.Sonar : null;
            if (_sonar == null) return;
            _sonar.PingEmitted += OnPingEmitted;
            _sonar.ContactScheduled += OnContactScheduled;
        }

        protected override void OnDestroy()
        {
            if (_sonar != null)
            {
                _sonar.PingEmitted -= OnPingEmitted;
                _sonar.ContactScheduled -= OnContactScheduled;
            }
            base.OnDestroy();
        }

        private void Update()
        {
            // Fire any scheduled ripples whose moment has come (reverse for safe removal).
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (Time.time < _pending[i].dueTime) continue;
                FireRipple(_pending[i]);
                _pending.RemoveAt(i);
            }
        }

        // -------------------------------------------------------
        // Scheduling
        // -------------------------------------------------------

        /** New ping: reset the per-ping ripple budget. */
        private void OnPingEmitted(Vector2 origin) => _ripplesThisPing = 0;

        /** Gate by toggle/tier/budget, then plan this contact's ripple train. */
        private void OnContactScheduled(SonarContact contact)
        {
            if (!emitRipples || _sonar == null) return;
            if (_sonar.CurrentTier < minimumTier) return;
            if (_ripplesThisPing >= maxRipplesPerPing) return;

            _ripplesThisPing++;
            ScheduleContactRipples(contact);
        }

        /**
         * Converts one contact into a scheduled pulse train. All distance-driven
         * character (strength, frequency, width, pulse count) is resolved now; the
         * screen-space conversion waits until fire time so camera zoom stays honest.
         *
         * Timeline example (EchoWavefront, contact 20u away, ping speed 40u/s):
         * return beep at t=1.0s, wave travel 0.5s → ripple launches at t=0.5s (the
         * reflection moment) and its ring is exactly at the sub when the beep plays.
         */
        private void ScheduleContactRipples(SonarContact contact)
        {
            // Proximity: 1 on top of the sub, 0 at max range — the master tuning axis.
            float proximity = 1f - Mathf.Clamp01(contact.Distance / Mathf.Max(0.01f, _sonar.ResolvedRange));
            float shaped = Mathf.Pow(proximity, strengthContrast);

            // Wavefront flight: how long the visible ring takes to cross contact → sub.
            float worldSpeed = Mathf.Max(0.01f, _sonar.ResolvedPingSpeed * waveTravelScale);
            float travelTime = contact.Distance / worldSpeed;

            // Anchor to the echo timeline: launch early enough to arrive with the beep,
            // or right at the beep, depending on sync mode. Never in the past.
            float returnTime = Time.time + contact.ReturnDelay;
            float emitTime = syncMode == RippleSync.EchoWavefront ? returnTime - travelTime : returnTime;
            emitTime = Mathf.Max(emitTime, Time.time);

            // Distance-driven wave character.
            float arrivalStrength = Mathf.Lerp(arrivalStrengthFar, arrivalStrengthNear, shaped);
            float frequency = Mathf.Lerp(frequencyFar, frequencyNear, proximity);
            float phaseSpeed = Mathf.Lerp(waveSpeedFar, waveSpeedNear, proximity);
            float ringWidth = Mathf.Lerp(ringWidthFar, ringWidthNear, proximity);
            int pulses = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(pulseCountFar, pulseCountNear, proximity)));
            float interval = pulseInterval;

            // Identity voice: one gate unlocks every distinguishing trait — the wave takes on
            // the signature's colour/glint/rhythm/character and size class biases strength.
            SonarSignature sig = contact.Signature;
            Color tint = Color.clear;
            float chromaticBoost = 1f;
            if (sig != null && _sonar.CurrentTier >= voiceTier)
            {
                arrivalStrength *= Mathf.Lerp(1f, sig.SizeRangeFactor, sizeStrengthInfluence);
                frequency *= sig.rippleFrequencyScale;
                phaseSpeed *= sig.ripplePhaseSpeedScale;
                ringWidth *= sig.rippleWidthScale;
                chromaticBoost = sig.rippleChromaticBoost;

                // Tint: normalize the HDR blip colour to unit peak so authored bloom levels
                // (e.g. an intensity-4 colour) don't blow the additive glow out to a solid
                // ring — rippleTintStrength alone owns the glow's intensity.
                tint = sig.blipColor;
                float peak = Mathf.Max(tint.r, Mathf.Max(tint.g, tint.b));
                if (peak > 1e-4f) { tint.r /= peak; tint.g /= peak; tint.b /= peak; }
                tint.a = sig.rippleTintStrength;

                // Rhythm: an authored pattern replaces the proximity-driven pulse count.
                switch (sig.ripplePulsePattern)
                {
                    case RipplePulsePattern.Single:     pulses = 1; break;
                    case RipplePulsePattern.DoubleBeat: pulses = 2; interval *= 0.45f; break;
                    case RipplePulsePattern.TripleBeat: pulses = 3; interval *= 0.45f; break;
                }
            }

            // Queue the train — each successive pulse trails and weakens.
            for (int k = 0; k < pulses; k++)
            {
                _pending.Add(new PendingRipple
                {
                    dueTime = emitTime + k * interval,
                    worldPos = contact.WorldPosition,
                    worldSpeed = worldSpeed,
                    travelTime = travelTime,
                    arrivalStrength = arrivalStrength * Mathf.Pow(pulseDecay, k),
                    frequency = frequency,
                    phaseSpeed = phaseSpeed,
                    ringWidth = ringWidth,
                    tint = tint,
                    chromaticBoost = chromaticBoost
                });
            }
        }

        // -------------------------------------------------------
        // Emission
        // -------------------------------------------------------

        /**
         * Emits one ripple through the distortion bus. World speed converts to the
         * shader's viewport units here (at fire time) so the current camera framing is
         * respected, and the emitted strength is boosted to counter the controller's
         * (1 - t/lifetime) fade — so the ring still carries its intended punch when it
         * finally reaches the sub after a long flight.
         */
        private void FireRipple(PendingRipple p)
        {
            // Viewport-height units: 1 unit = one full screen height of world travel.
            Camera cam = Camera.main;
            float viewportSpeed = p.worldSpeed / Mathf.Max(0.0001f, 2f * HalfHeightAtPlane(cam, p.worldPos));

            // Live long enough to arrive, then linger while washing past the sub.
            float lifetime = p.travelTime + lingerAfterArrival;

            // Fade compensation: e.g. travel 2s of a 3.25s life → fade leaves ~38%,
            // so emit at arrivalStrength / 0.38 to land on target (capped for safety).
            float fadeAtArrival = Mathf.Max(0.05f, 1f - p.travelTime / lifetime);
            float strength = Mathf.Min(maxEmitStrength, p.arrivalStrength / fadeAtArrival);

            DistortionRippleBus.Emit(new RippleRequest(
                p.worldPos, strength, p.frequency, p.phaseSpeed, lifetime, viewportSpeed, p.ringWidth,
                p.tint, p.chromaticBoost));
            onRippleEmitted?.Invoke(p.worldPos);
        }

        /**
         * Half the camera's vertical world extent at the given depth plane — ortho size
         * today, but written projection-agnostic so a perspective switch keeps working.
         */
        private static float HalfHeightAtPlane(Camera cam, Vector3 worldPos)
        {
            if (cam == null) return 5f;
            if (cam.orthographic) return cam.orthographicSize;
            float dist = Mathf.Abs(cam.transform.position.z - worldPos.z);
            return dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        }

        // -------------------------------------------------------
        // Editor Utilities
        // -------------------------------------------------------

#if UNITY_EDITOR
        [FoldoutGroup("Debug")]
        [Tooltip("Optional signature the test buttons impersonate, to audition a Ripple Voice " +
                 "without real targets. Empty = generic wave.")]
        [SerializeField] private SonarSignature debugSignature;

        /** Fakes a contact at a fraction of sonar range to audition the ripple without targets. */
        [FoldoutGroup("Debug")]
        [Button("Test Ripple @ 25% Range"), GUIColor(0.4f, 0.8f, 1f)]
        private void DebugNearRipple() => DebugRipple(0.25f);

        [FoldoutGroup("Debug")]
        [Button("Test Ripple @ 90% Range"), GUIColor(0.4f, 0.8f, 1f)]
        private void DebugFarRipple() => DebugRipple(0.9f);

        /** Builds a synthetic contact in a random direction and runs it through the real pipeline. */
        private void DebugRipple(float rangeFraction)
        {
            if (!Application.isPlaying || _sonar == null) { Debug.Log("[SonarReturnRipples] Play mode with a bound sonar only."); return; }

            Vector2 dir = Random.insideUnitCircle.normalized;
            float distance = _sonar.ResolvedRange * rangeFraction;
            Vector2 pos = _sonar.RadiusOrigin + dir * distance;
            float delay = 2f * distance / Mathf.Max(0.01f, _sonar.ResolvedPingSpeed);

            ScheduleContactRipples(new SonarContact(null, pos, dir, distance, debugSignature, delay));
            Debug.Log($"[SonarReturnRipples] Test contact {distance:0.0}u {dir} — beep in {delay:0.00}s.");
        }
#endif
    }
}
