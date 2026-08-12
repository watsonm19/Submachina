using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Progression tiers for sonar capability. Each rung reveals more of a contact;
     * the active tier is resolved from unlocked upgrade features (see CurrentTier).
     *
     *   None      — sonar not yet unlocked; pinging does nothing.
     *   Presence  — a generic contact ("something is out there"). Metal-detector feel.
     *   Direction — adds which way the contact lies.
     *   Size      — adds size class and a distance readout.
     *   Identify  — adds object identification (the contact's signature is recognised).
     */
    public enum SonarTier { None, Presence, Direction, Size, Identify }

    /**
     * A single resolved sonar return. Always fully populated by the system — the
     * presentation layer (HUD/audio) decides how much of it to reveal based on the
     * current tier, so the data model stays tier-agnostic and testable.
     */
    public readonly struct SonarContact
    {
        public readonly SonarTarget Target;      // source target (may be destroyed by the time it returns)
        public readonly Vector2 WorldPosition;   // where the echo reflected from
        public readonly Vector2 Direction;       // normalized, from the sub toward the contact
        public readonly float Distance;          // world units from the sub at emit time
        public readonly SonarSignature Signature;// color/shape/size/identity of the contact
        public readonly float ReturnDelay;       // seconds the echo took to come back (∝ distance)

        public SonarContact(SonarTarget target, Vector2 worldPosition, Vector2 direction,
                            float distance, SonarSignature signature, float returnDelay)
        {
            Target = target;
            WorldPosition = worldPosition;
            Direction = direction;
            Distance = distance;
            Signature = signature;
            ReturnDelay = returnDelay;
        }
    }

    /**
     * Submarine sonar — emit a pulse, wait, and receive echoes from reflective objects.
     *
     * Emission is a manual, cooldown-gated button press. Detection reuses the proven
     * OverlapCircle idiom (see PickupRangeDetector): when a ping fires, every SonarTarget
     * within its reflect range schedules a return whose delay is proportional to its
     * distance — so closer contacts "ping back" sooner, which is the core distance cue
     * (a literal expanding-wavefront simulation can replace the scheduler later without
     * changing this component's public API).
     *
     * The system is tier-agnostic: it always produces full SonarContacts and exposes the
     * current tier. The HUD and audio down-sample what they reveal based on CurrentTier,
     * which is resolved from unlocked upgrade features.
     *
     * Multiplayer-safe by construction — it is a SubmarineComponent that finds its own
     * sub via the hierarchy, so multiple subs each run an independent sonar.
     */
    [UsesFeedbacks(nameof(SubFeedbacks.SonarPingEmit), nameof(SubFeedbacks.SonarReturn))]
    public class SonarSystem : InputSubmarineComponent
    {
        // =====================
        // Input
        // =====================

        [FoldoutGroup("Input")]
        [Tooltip("Button InputAction that emits a sonar pulse.")]
        [SerializeField] private InputActionReference pingAction;

        // =====================
        // Sonar Settings
        // =====================

        [FoldoutGroup("Sonar")]
        [Tooltip("Base detection range in world units, before per-object size/strength and " +
                 "the SonarRange stat modifier. A Medium contact reflects at exactly this range.")]
        [SerializeField, Min(1f)] private float baseRange = 12f;

        [FoldoutGroup("Sonar")]
        [Tooltip("Seconds between pulses. Reduced by the SonarCooldown stat modifier.")]
        [SerializeField, Min(0f)] private float cooldown = 2.5f;

        [FoldoutGroup("Sonar")]
        [Tooltip("How fast the ping travels (world units/sec) for the return-delay simulation. " +
                 "Echo delay = round-trip distance / this speed, so closer contacts return sooner. " +
                 "Higher = snappier returns. Tunable via the SonarPingSpeed stat modifier.")]
        [SerializeField, Min(1f)] private float pingSpeed = 40f;

        [FoldoutGroup("Sonar")]
        [Tooltip("Seconds a returned contact stays 'fresh' in ActiveContacts before it fades out. " +
                 "Drives how long HUD blips linger after a ping.")]
        [SerializeField, Min(0.1f)] private float contactFadeDuration = 4f;

        [FoldoutGroup("Sonar")]
        [Tooltip("Optional override for the ping origin. Leave empty to use this transform.")]
        [SerializeField] private Transform pingCenter;

        // =====================
        // Detection by Size
        // =====================
        // Detect-range multipliers per SonarSizeClass, owned by the sonar (not the signature)
        // so detectability can be tuned independently of how a size class LOOKS in the return
        // wave (SonarSignature.SizeRangeFactor, used by SonarReturnRipples, is presentation-only).

        [FoldoutGroup("Detection by Size")]
        [InfoBox("Max detect range = base range × this size factor × the signature's reflectionStrength. " +
                 "E.g. Huge 1.6 reflects from 60% beyond base range; set a factor to 0 to make a size " +
                 "class undetectable.")]
        [SerializeField, Range(0f, 3f)] private float tinyDetectFactor = 0.4f;

        [FoldoutGroup("Detection by Size")]
        [SerializeField, Range(0f, 3f)] private float smallDetectFactor = 0.7f;

        [FoldoutGroup("Detection by Size")]
        [SerializeField, Range(0f, 3f)] private float mediumDetectFactor = 1.0f;

        [FoldoutGroup("Detection by Size")]
        [SerializeField, Range(0f, 3f)] private float largeDetectFactor = 1.3f;

        [FoldoutGroup("Detection by Size")]
        [SerializeField, Range(0f, 3f)] private float hugeDetectFactor = 1.6f;

        // =====================
        // Tier Features
        // =====================

        [FoldoutGroup("Tier Features")]
        [InfoBox("Assign the UpgradeFeature asset that unlocks each tier. CurrentTier resolves to " +
                 "the highest feature currently active on this sub's UpgradeManager.")]
        [Tooltip("Unlocks the Presence tier (generic contact).")]
        [SerializeField] private UpgradeFeature tier1PresenceFeature;

        [FoldoutGroup("Tier Features")]
        [Tooltip("Unlocks the Direction tier.")]
        [SerializeField] private UpgradeFeature tier2DirectionFeature;

        [FoldoutGroup("Tier Features")]
        [Tooltip("Unlocks the Size/Distance tier.")]
        [SerializeField] private UpgradeFeature tier3SizeFeature;

        [FoldoutGroup("Tier Features")]
        [Tooltip("Unlocks the Identify tier (object recognition).")]
        [SerializeField] private UpgradeFeature tier4IdentifyFeature;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired the instant a pulse is emitted. Wire to MMF juice / ring VFX.")]
        public UnityEvent onPingEmitted;

        [FoldoutGroup("Events")]
        [Tooltip("Fired for each echo as it returns. Passes the contact's world position.")]
        public UnityEvent<Vector2> onContactReturned;

        // C# events carry the richer payload for code consumers (HUD, audio).

        /** Raised when a pulse is emitted. Argument is the ping origin. */
        public event Action<Vector2> PingEmitted;

        /** Raised for each echo as it returns, with the full contact. */
        public event Action<SonarContact> ContactReturned;

        /**
         * Raised at ping time for each contact whose return has just been scheduled.
         * Lets presentation layers (e.g. the return-ripple VFX) act during the echo's
         * flight — the contact's ReturnDelay says when it will mature.
         */
        public event Action<SonarContact> ContactScheduled;

        /** Raised once all scheduled returns for a pulse have resolved. */
        public event Action PingResolved;

        // =====================
        // Public API
        // =====================

        /** World-space centre the pulse emits from. */
        public Vector2 RadiusOrigin =>
            pingCenter != null ? (Vector2)pingCenter.position : (Vector2)transform.position;

        /** Seconds remaining until the next ping can fire (0 = ready). */
        public float CooldownRemaining => Mathf.Max(0f, _cooldownEnd - Time.time);

        /** True when a pulse can currently be emitted. */
        public bool CanPing => CurrentTier != SonarTier.None && CooldownRemaining <= 0f;

        /** Contacts that have returned recently and not yet faded. */
        public IReadOnlyList<SonarContact> ActiveContacts => _activeContacts;

        /** Resolved base range after the SonarRange stat modifier. */
        public float ResolvedRange => Sub?.Upgrades != null
            ? Sub.Upgrades.Stats.Resolve(SubStats.SonarRange, baseRange) : baseRange;

        /** Ping travel speed (world units/sec) after the SonarPingSpeed stat modifier. */
        public float ResolvedPingSpeed => Sub?.Upgrades != null
            ? Sub.Upgrades.Stats.Resolve(SubStats.SonarPingSpeed, pingSpeed) : pingSpeed;

        /**
         * Highest sonar tier currently unlocked, resolved from active upgrade features.
         * Falls back to the configured base tier when no UpgradeManager is present so the
         * system is usable in isolation (and the editor force-override wins in edit/play).
         */
        public SonarTier CurrentTier
        {
            get
            {
#if UNITY_EDITOR
                if (_debugOverrideTier) return _debugTier;
#endif
                var up = Sub?.Upgrades;
                if (up == null) return fallbackTier;

                if (tier4IdentifyFeature != null && up.IsFeatureActive(tier4IdentifyFeature)) return SonarTier.Identify;
                if (tier3SizeFeature != null && up.IsFeatureActive(tier3SizeFeature))         return SonarTier.Size;
                if (tier2DirectionFeature != null && up.IsFeatureActive(tier2DirectionFeature)) return SonarTier.Direction;
                if (tier1PresenceFeature != null && up.IsFeatureActive(tier1PresenceFeature)) return SonarTier.Presence;
                return SonarTier.None;
            }
        }

        [FoldoutGroup("Tier Features")]
        [Tooltip("Tier used when no UpgradeManager is present (e.g. testing the sonar prefab in " +
                 "isolation). In a real sub, the upgrade features above drive the tier instead.")]
        [SerializeField] private SonarTier fallbackTier = SonarTier.None;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private SonarTier ResolvedTier => CurrentTier;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private int ActiveContactCount => _activeContacts.Count;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private int PendingReturnCount => _pendingReturns.Count;

        // =====================
        // State
        // =====================

        /** A return that has been scheduled but has not yet matured. */
        private struct PendingReturn
        {
            public SonarContact contact;
            public float dueTime;
        }

        private float _cooldownEnd = -1f;
        private readonly List<PendingReturn> _pendingReturns = new();
        private readonly List<SonarContact> _activeContacts = new();
        private readonly List<float> _activeExpiry = new();
        private readonly HashSet<SonarTarget> _seenThisPing = new();

        /** Detect-range multiplier for a size class (this sonar's own tuning, not the signature's). */
        public float SizeDetectFactor(SonarSizeClass size) => size switch
        {
            SonarSizeClass.Tiny   => tinyDetectFactor,
            SonarSizeClass.Small  => smallDetectFactor,
            SonarSizeClass.Medium => mediumDetectFactor,
            SonarSizeClass.Large  => largeDetectFactor,
            SonarSizeClass.Huge   => hugeDetectFactor,
            _ => 1f
        };

        // Upper bound on how far any object can reflect, relative to base range: the largest
        // configured size factor × the largest reflectionStrength (slider max 2). Used as the
        // scan radius so the per-target detect-range filter is never starved.
        private float MaxReflectScanFactor =>
            Mathf.Max(tinyDetectFactor, smallDetectFactor, mediumDetectFactor, largeDetectFactor, hugeDetectFactor) * 2f;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();
            RegisterAction(pingAction);
        }

        private void Update()
        {
            // Manual fire on the ping button.
            if (PrimaryAction != null && PrimaryAction.WasPressedThisFrame())
                TryEmitPing();

            ProcessPendingReturns();
            PruneFadedContacts();
        }

        // -------------------------------------------------------
        // Emission
        // -------------------------------------------------------

        /**
         * Emits a sonar pulse if the sonar is unlocked and off cooldown.
         * Detects reflective targets in range and schedules their returns.
         * Returns false if the ping could not fire.
         */
        public bool TryEmitPing()
        {
            if (!CanPing) return false;

            _cooldownEnd = Time.time + ResolvedCooldown();
            Vector2 origin = RadiusOrigin;

            // Announce the outgoing pulse — VFX ring + emit cue.
            onPingEmitted?.Invoke();
            PingEmitted?.Invoke(origin);
            Sub?.Feedbacks?.Play(SubFeedbacks.SonarPingEmit, origin);

            ScheduleReturns(origin);
            return true;
        }

        /**
         * Scans for reflective targets around the origin and schedules a return for
         * each one within its own reflect range. The echo delay is the round-trip
         * travel time (out and back) at the current ping speed, so nearer contacts
         * mature first — the distance cue the player learns to read.
         */
        private void ScheduleReturns(Vector2 origin)
        {
            float range = ResolvedRange;
            float speed = ResolvedPingSpeed;
            _seenThisPing.Clear();

            // Scan generously, then filter per-target by that object's true reflect range.
            Collider2D[] hits = Physics2D.OverlapCircleAll(origin, range * MaxReflectScanFactor);
            for (int i = 0; i < hits.Length; i++)
            {
                // Resolve the target from anywhere in the collider's hierarchy, deduped
                // so a multi-collider entity returns a single echo.
                var target = hits[i].GetComponentInParent<SonarTarget>();
                if (target == null || target.Signature == null) continue;
                if (!_seenThisPing.Add(target)) continue;

                // Only objects whose size/strength reflect this far actually echo back —
                // the size mapping is this sonar's own Detection by Size config.
                Vector2 contactPos = target.ReflectionOrigin;
                float distance = Vector2.Distance(origin, contactPos);
                float maxReflect = range * SizeDetectFactor(target.Signature.sizeClass) * target.Signature.reflectionStrength;
                if (distance > maxReflect) continue;

                // Round-trip echo delay: out and back at the ping speed.
                float returnDelay = (2f * distance) / Mathf.Max(0.01f, speed);
                Vector2 dir = distance > 0.001f ? (contactPos - origin) / distance : Vector2.up;

                var contact = new SonarContact(target, contactPos, dir, distance, target.Signature, returnDelay);
                _pendingReturns.Add(new PendingReturn { contact = contact, dueTime = Time.time + returnDelay });
                ContactScheduled?.Invoke(contact);
            }
        }

        // -------------------------------------------------------
        // Returns
        // -------------------------------------------------------

        /**
         * Matures any scheduled returns whose delay has elapsed, raising ContactReturned
         * and playing the return ping for each. Fires PingResolved when the last return
         * of a pulse clears.
         */
        private void ProcessPendingReturns()
        {
            if (_pendingReturns.Count == 0) return;

            bool anyResolved = false;
            for (int i = _pendingReturns.Count - 1; i >= 0; i--)
            {
                if (Time.time < _pendingReturns[i].dueTime) continue;

                ResolveContact(_pendingReturns[i].contact);
                _pendingReturns.RemoveAt(i);
                anyResolved = true;
            }

            // The pulse is fully resolved the moment its last scheduled return clears.
            if (anyResolved && _pendingReturns.Count == 0)
                PingResolved?.Invoke();
        }

        /**
         * Publishes a single returned contact: adds it to the fading active set, raises
         * the events, and plays the return ping with intensity scaled by proximity
         * (closer = louder — the metal-detector cue). A signature-specific cue is used
         * when the contact carries one, otherwise the generic return ping.
         */
        private void ResolveContact(SonarContact contact)
        {
            _activeContacts.Add(contact);
            _activeExpiry.Add(Time.time + contactFadeDuration);

            onContactReturned?.Invoke(contact.WorldPosition);
            ContactReturned?.Invoke(contact);

            // Proximity → intensity: 1 at the sub, fading to 0 at the base range.
            float intensity = Mathf.Clamp01(1f - contact.Distance / Mathf.Max(0.01f, ResolvedRange));

            // A contact with its own signature cue "sounds" like itself; others use the generic ping.
            FeedbackId cue = contact.Signature != null && !contact.Signature.returnPingFeedback.IsEmpty
                ? contact.Signature.returnPingFeedback
                : SubFeedbacks.SonarReturn;
            Sub?.Feedbacks?.Play(cue, contact.WorldPosition, intensity);
        }

        /** Drops contacts from the active set once their fade window elapses. */
        private void PruneFadedContacts()
        {
            for (int i = _activeContacts.Count - 1; i >= 0; i--)
            {
                if (Time.time < _activeExpiry[i]) continue;
                _activeContacts.RemoveAt(i);
                _activeExpiry.RemoveAt(i);
            }
        }

        // -------------------------------------------------------
        // Stat Resolution
        // -------------------------------------------------------

        /** Cooldown after the SonarCooldown modifier. */
        private float ResolvedCooldown() => Sub?.Upgrades != null
            ? Sub.Upgrades.Stats.Resolve(SubStats.SonarCooldown, cooldown) : cooldown;

        // -------------------------------------------------------
        // Gizmos
        // -------------------------------------------------------

        /**
         * Draws the base reflect range and a line to each active contact, tinted by the
         * contact's signature color, so detection can be eyeballed in the scene view.
         */
        private void OnDrawGizmosSelected()
        {
            Vector2 origin = RadiusOrigin;

            // Base range ring.
            Gizmos.color = new Color(0.3f, 0.85f, 1f, 0.5f);
            Gizmos.DrawWireSphere(origin, Application.isPlaying ? ResolvedRange : baseRange);

            // Active contacts.
            if (!Application.isPlaying) return;
            for (int i = 0; i < _activeContacts.Count; i++)
            {
                var c = _activeContacts[i];
                Gizmos.color = c.Signature != null ? c.Signature.blipColor : Color.white;
                Gizmos.DrawLine(origin, c.WorldPosition);
                Gizmos.DrawWireSphere(c.WorldPosition, 0.35f);
            }
        }

        // -------------------------------------------------------
        // Editor Utilities
        // -------------------------------------------------------

#if UNITY_EDITOR
        [FoldoutGroup("Debug")]
        [Tooltip("Force a tier at runtime to audition each tier's presentation without granting upgrades.")]
        [SerializeField] private bool _debugOverrideTier;

        [FoldoutGroup("Debug"), ShowIf(nameof(_debugOverrideTier))]
        [SerializeField] private SonarTier _debugTier = SonarTier.Identify;

        /** Emits a pulse and logs how many contacts it scheduled. */
        [FoldoutGroup("Debug")]
        [Button("Fire Test Ping"), GUIColor(0.4f, 0.8f, 1f)]
        private void DebugFirePing()
        {
            if (!Application.isPlaying) { Debug.Log("[SonarSystem] Play mode only."); return; }
            int before = _pendingReturns.Count;
            bool fired = TryEmitPing();
            if (!fired) { Debug.Log($"[SonarSystem] Ping blocked (tier={CurrentTier}, cooldown={CooldownRemaining:0.0}s)."); return; }
            Debug.Log($"[SonarSystem] Pinged — {_pendingReturns.Count - before} contact(s) scheduled, tier={CurrentTier}.");
        }
#endif
    }
}
