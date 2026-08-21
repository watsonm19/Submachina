using Core.ProceduralAnimation;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Events;

namespace Submachina.Core
{
    /**
     * Deep-water ambush predator — nearly invisible in the dark, betrayed only by a
     * glowing lure bobbing enticingly above its head, until the player strays too close.
     *
     * Behavior loop:
     *   Lurk    — drifts near motionless around the spawn point. Body undulation is kept
     *             deliberately low so the creature barely reads as alive; the lure breathes
     *             a slow bob and glow pulse to draw the eye without giving anything away.
     *   Flare   — the instant the player enters strike range: a very brief glow flare and
     *             body flash telegraph the ambush. The strike direction locks immediately,
     *             not at the end of the flare — there is no time to react to a moving target.
     *   Lunge   — a single violent burst of speed along the locked direction. Short by design;
     *             this is a startle, not a chase.
     *   Chase   — a brief, relentless pursuit at full undulation following the initial burst.
     *   GiveUp  — the attack failed (or connected): the fish turns away, body goes limp, and
     *             the lure snuffs out to near-black. It "goes dark" for a beat before drifting
     *             back to Lurk, where the lure re-lights on a slow, distinct eased ramp.
     *
     * The lure's glow IS the horror instrument — everything else (body wave, flash, motion)
     * exists to make the contrast between "near-invisible" and "sudden burst" as sharp as
     * possible. Keep Lurk genuinely still; the stillness is what sells the scare.
     */
    public class AnglerfishEnemy : EnemyBase
    {
        // =====================
        // Detection
        // =====================

        [FoldoutGroup("Detection")]
        [Tooltip("Player within this range triggers the ambush (Flare -> Lunge). Deliberately small — " +
                 "this is a proximity scare, not a chase from afar.")]
        [SerializeField, Min(0.5f)] private float strikeRadius = 4f;

        // =====================
        // Lurk
        // =====================

        [FoldoutGroup("Lurk")]
        [Tooltip("Drift speed while lurking near the spawn point. Kept very low — near motionless.")]
        [SerializeField, Min(0f)] private float lurkSpeed = 0.15f;

        [FoldoutGroup("Lurk")]
        [Tooltip("Radius around the spawn point the fish drifts within while lurking.")]
        [SerializeField, Min(0.25f)] private float lurkRadius = 1.5f;

        [FoldoutGroup("Lurk")]
        [Tooltip("Body wave frequency multiplier while lurking. Kept LOW — the contrast against the burst is the scare.")]
        [SerializeField, Min(0f)] private float lurkBodyWaveFreqMultiplier = 0.25f;

        [FoldoutGroup("Lurk")]
        [Tooltip("Body wave amplitude multiplier while lurking. Kept LOW — near motionless.")]
        [SerializeField, Min(0f)] private float lurkBodyWaveAmpMultiplier = 0.12f;

        [FoldoutGroup("Lurk")]
        [Tooltip("Sine cycles per second for the lure's amplitude 'breathing' bob.")]
        [SerializeField, Min(0f)] private float lureBobSpeed = 0.35f;

        [FoldoutGroup("Lurk")]
        [Tooltip("How far the bob swings the lure's wave amplitude multiplier above/below its baseline.")]
        [SerializeField, Range(0f, 1f)] private float lureBobDepth = 0.5f;

        [FoldoutGroup("Lurk")]
        [Tooltip("Baseline lure wave amplitude multiplier the bob breathes around.")]
        [SerializeField, Min(0f)] private float lureBaseAmplitudeMultiplier = 1f;

        // =====================
        // Movement
        // =====================

        [FoldoutGroup("Movement")]
        [Tooltip("Steering responsiveness (per second) used for both the lurk drift and the chase pursuit. " +
                 "Higher = snappier turns; lower = big flowing arcs.")]
        [SerializeField, Range(0.5f, 20f)] private float steering = 5f;

        // =====================
        // Flare (telegraph)
        // =====================

        [FoldoutGroup("Flare")]
        [Tooltip("Duration of the telegraph before the burst. Very short by design — this is an ambush, " +
                 "not a wind-up the player can plan around.")]
        [SerializeField, Min(0.02f)] private float telegraphDuration = 0.15f;

        [FoldoutGroup("Flare")]
        [Tooltip("Peak _FlashAmount pushed into the body material during the telegraph flash.")]
        [SerializeField, Range(0f, 1f)] private float flareBodyFlashPeak = 0.65f;

        // =====================
        // Lunge
        // =====================

        [FoldoutGroup("Lunge")]
        [Tooltip("Burst speed at the moment of the lunge. Direction locks at the start of the Flare telegraph.")]
        [SerializeField, Min(1f)] private float lungeSpeed = 15f;

        [FoldoutGroup("Lunge")]
        [Tooltip("How long the straight-line burst holds before easing into the Chase state.")]
        [SerializeField, Min(0.05f)] private float lungeBurstDuration = 0.25f;

        [FoldoutGroup("Lunge")]
        [Tooltip("Contact damage dealt on a successful bite (before hull depth-vulnerability scaling).")]
        [SerializeField, Min(0)] private int biteDamage = 6;

        // =====================
        // Chase
        // =====================

        [FoldoutGroup("Chase")]
        [Tooltip("Pursuit speed following the initial burst.")]
        [SerializeField, Min(0f)] private float chaseSpeed = 7f;

        [FoldoutGroup("Chase")]
        [Tooltip("How long the chase lasts before giving up — relentless but brief.")]
        [SerializeField, Min(0.1f)] private float chaseDuration = 1.6f;

        [FoldoutGroup("Chase")]
        [Tooltip("Body wave frequency multiplier while chasing. Full-amplitude undulation — nothing left to hide.")]
        [SerializeField, Min(0f)] private float chaseBodyWaveFreqMultiplier = 1.6f;

        [FoldoutGroup("Chase")]
        [Tooltip("Body wave amplitude multiplier while chasing.")]
        [SerializeField, Min(0f)] private float chaseBodyWaveAmpMultiplier = 1.4f;

        // =====================
        // Give Up
        // =====================

        [FoldoutGroup("Give Up")]
        [Tooltip("How long the fish stays dark and limp after an attack ends before drifting back to Lurk.")]
        [SerializeField, Min(0.1f)] private float recoveryDim = 2.5f;

        [FoldoutGroup("Give Up")]
        [Tooltip("Eased ramp duration for the lure re-lighting once back in Lurk. A distinct, slow beat — " +
                 "never snaps back on.")]
        [SerializeField, Min(0.1f)] private float relightRampDuration = 2f;

        // =====================
        // Lure Glow
        // =====================

        [FoldoutGroup("Lure Glow")]
        [Tooltip("HDR color of the lure's bioluminescent glow. Drives _EmissionColor on the lure (and, " +
                 "at a fraction, the body).")]
        [SerializeField, ColorUsage(true, true)] private Color lureGlow = new Color(0.3f, 1f, 0.6f, 1f);

        [FoldoutGroup("Lure Glow")]
        [Tooltip("Idle glow intensity multiplier the lure breathes around while lurking.")]
        [SerializeField, Min(0f)] private float idleGlowIntensity = 1.2f;

        [FoldoutGroup("Lure Glow")]
        [Tooltip("Fraction of idle intensity the pulse swings above/below the baseline.")]
        [SerializeField, Range(0f, 1f)] private float pulseDepth = 0.25f;

        [FoldoutGroup("Lure Glow")]
        [Tooltip("Sine cycles per second for the idle glow pulse.")]
        [SerializeField, Min(0f)] private float pulseSpeed = 0.6f;

        [FoldoutGroup("Lure Glow")]
        [Tooltip("Peak intensity multiplier during the Flare telegraph — a brief, sharp spike.")]
        [SerializeField, Min(0f)] private float flareIntensity = 6f;

        [FoldoutGroup("Lure Glow")]
        [Tooltip("Near-black intensity multiplier the glow snuffs down to during GiveUp.")]
        [SerializeField, Min(0f)] private float darkIntensity = 0.02f;

        [FoldoutGroup("Lure Glow")]
        [Tooltip("Optional subtle body emission, expressed as a fraction of the lure's current intensity — " +
                 "the fish's own skin faintly catching its lure's light. 0 disables it.")]
        [SerializeField, Range(0f, 1f)] private float bodyGlowFraction = 0.12f;

        // =====================
        // Animation Coupling
        // =====================

        [FoldoutGroup("Animation Coupling")]
        [Tooltip("The fish body's chain spine. Must be wired explicitly — not auto-resolved, since this " +
                 "creature has two chains (body and lure) and auto-discovery could grab the wrong one.")]
        [SerializeField] private ChainSimulator body;

        [FoldoutGroup("Animation Coupling")]
        [Tooltip("Strip renderer for the body, used for the telegraph flash and the optional subtle body glow.")]
        [SerializeField] private ChainStripRenderer bodyRenderer;

        [FoldoutGroup("Animation Coupling")]
        [Tooltip("Short chain arcing up/forward from the head — its tip is the glowing lure bulb.")]
        [SerializeField] private ChainSimulator lure;

        [FoldoutGroup("Animation Coupling")]
        [Tooltip("Thin strip renderer for the lure — the glow is driven on this renderer's material.")]
        [SerializeField] private ChainStripRenderer lureRenderer;

        [FoldoutGroup("Animation Coupling")]
        [Tooltip("Optional explicit marker for the lure's glowing tip (e.g. for attaching a light or particle " +
                 "system). Safe to leave empty — the tip position is computed from the lure chain's last point.")]
        [SerializeField] private Transform lureTip;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired the instant the ambush telegraph begins.")]
        public UnityEvent onFlare;

        [FoldoutGroup("Events")]
        [Tooltip("Fired at the moment of the burst.")]
        public UnityEvent onLunge;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when a bite connects with a submarine.")]
        public UnityEvent onHitPlayer;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when the attack ends (hit or timeout) and the fish turns away.")]
        public UnityEvent onGiveUp;

        // =====================
        // State
        // =====================

        private enum AiState { Lurk, Flare, Lunge, Chase, GiveUp, Dead }

        private AiState _state = AiState.Lurk;
        private float _stateTimer;
        private Vector2 _lurkTarget;
        private Vector2 _lungeDirection;
        private bool _hasHitThisAttack;

        // Lure animation phases.
        private float _bobPhase;
        private float _pulsePhase;

        // Glow state: current smoothed intensity, plus an active re-light ramp timer (-1 = not relighting).
        private float _glowIntensity;
        private float _relightTimer = -1f;

        private MaterialPropertyBlock _mpb;
        private static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        protected override string CurrentState => _state.ToString();

        // Locked onto its target from the moment the ambush is telegraphed through to the end of the chase.
        protected override bool CanRetarget => _state != AiState.Lunge && _state != AiState.Chase;

        /** World position of the lure's glowing tip. Uses the explicit marker if assigned, else the lure chain's last point. */
        public Vector2 LureTipPosition =>
            lureTip != null
                ? (Vector2)lureTip.position
                : (lure != null && lure.Chain != null ? lure.GetPoint(lure.PointCount - 1) : (Vector2)transform.position);

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();
            _mpb = new MaterialPropertyBlock();
            _lurkTarget = transform.position;

            // Start already lit, as though it's been lurking a while before the player arrives.
            _glowIntensity = idleGlowIntensity;
        }

        // -------------------------------------------------------
        // AI
        // -------------------------------------------------------

        protected override void UpdateAI()
        {
            _stateTimer += Time.fixedDeltaTime;
            UpdateLureGlow(Time.fixedDeltaTime);

            switch (_state)
            {
                case AiState.Lurk: TickLurk(); break;
                case AiState.Flare: TickFlare(); break;
                case AiState.Lunge: TickLunge(); break;
                case AiState.Chase: TickChase(); break;
                case AiState.GiveUp: TickGiveUp(); break;
            }
        }

        /** Near-motionless drift near the spawn anchor; the lure breathes while the body barely stirs. */
        private void TickLurk()
        {
            // Ambush trigger — direction locks the instant the telegraph begins, not at its end.
            if (DistanceToPlayer() < strikeRadius)
            {
                _lungeDirection = DirectionToPlayer();
                Enter(AiState.Flare);
                return;
            }

            // Re-pick a wander point when reached (or on a lazy timeout) — tight and unhurried.
            if (((Vector2)transform.position - _lurkTarget).sqrMagnitude < 0.1f || _stateTimer > 8f)
            {
                _lurkTarget = (Vector2)SpawnPosition + Random.insideUnitCircle * lurkRadius;
                _stateTimer = 0f;
            }

            Steer((_lurkTarget - (Vector2)transform.position).normalized * lurkSpeed);

            // Lure bob: a slow sine breathing on the amplitude multiplier — a living, enticing wobble.
            // e.g. depth 0.5 at speed 0.35: the lure's wave amplitude swings ±50% roughly every 2.9 seconds.
            _bobPhase += Time.fixedDeltaTime * lureBobSpeed * Mathf.PI * 2f;
            if (lure != null)
                lure.WaveAmplitudeMultiplier = Mathf.Max(0f, lureBaseAmplitudeMultiplier + Mathf.Sin(_bobPhase) * lureBobDepth);
        }

        /** Brief telegraph: kill drift, flash the body, hold the already-locked direction. */
        private void TickFlare()
        {
            Rb.linearVelocity *= 0.5f; // rapid brace, not an instant stop — reads as coiling to spring

            float t = Mathf.Clamp01(_stateTimer / telegraphDuration);
            SetFlash(t * flareBodyFlashPeak);

            if (_stateTimer >= telegraphDuration) Enter(AiState.Lunge);
        }

        /** Violent straight-line burst along the locked direction — short by design. */
        private void TickLunge()
        {
            Rb.linearVelocity = _lungeDirection * lungeSpeed;
            if (_stateTimer >= lungeBurstDuration) Enter(AiState.Chase);
        }

        /** Brief, relentless pursuit at full undulation following the initial burst. */
        private void TickChase()
        {
            Steer(DirectionToPlayer() * chaseSpeed);
            if (_stateTimer >= chaseDuration) Enter(AiState.GiveUp);
        }

        /** Turns away, goes limp and dark, then drifts back toward Lurk. */
        private void TickGiveUp()
        {
            Rb.linearVelocity *= 0.9f; // coasts down as it turns away
            if (_stateTimer >= recoveryDim) Enter(AiState.Lurk);
        }

        /** Eases the Rigidbody toward a desired velocity — organic steering instead of instant snaps. */
        private void Steer(Vector2 desiredVelocity)
        {
            float t = 1f - Mathf.Exp(-steering * Time.fixedDeltaTime);
            Rb.linearVelocity = Vector2.Lerp(Rb.linearVelocity, desiredVelocity, t);
        }

        // -------------------------------------------------------
        // Transitions & animation coupling
        // -------------------------------------------------------

        /** Central state switch — resets timers and retunes the body/lure language per state. */
        private void Enter(AiState next)
        {
            AiState previous = _state;
            _state = next;
            _stateTimer = 0f;

            switch (next)
            {
                case AiState.Lurk:
                    SetBodyLanguage(freq: lurkBodyWaveFreqMultiplier, amp: lurkBodyWaveAmpMultiplier);
                    SetLureLanguage(freq: 1f, amp: lureBaseAmplitudeMultiplier); // TickLurk drives amp with the breathing sine from here
                    SetFlash(0f);
                    _lurkTarget = (Vector2)SpawnPosition + Random.insideUnitCircle * lurkRadius;

                    // Coming back from a failed attack, the lure re-lights on its own slow eased ramp
                    // instead of snapping straight back on. Any other arrival is already lit.
                    _relightTimer = previous == AiState.GiveUp ? 0f : -1f;
                    break;

                case AiState.Flare:
                    SetIntentIndicator(true);
                    onFlare?.Invoke();
                    break;

                case AiState.Lunge:
                    // Body goes near-rigid for the burst — a torpedo, not a swimmer.
                    SetBodyLanguage(freq: 0.4f, amp: 0.4f);
                    SetFlash(0f);
                    SetIntentIndicator(false);
                    _hasHitThisAttack = false;
                    onLunge?.Invoke();
                    break;

                case AiState.Chase:
                    SetBodyLanguage(freq: chaseBodyWaveFreqMultiplier, amp: chaseBodyWaveAmpMultiplier);
                    break;

                case AiState.GiveUp:
                    // Slack, over-wobbly body language layered under the chain's own ragdoll flop.
                    SetBodyLanguage(freq: 0.6f, amp: 1f);
                    SetLureLanguage(freq: 0.5f, amp: 0.3f);
                    SetIntentIndicator(false);
                    body?.Limp(0.5f);
                    onGiveUp?.Invoke();
                    break;
            }
        }

        /** Pushes wave multipliers into the body chain — the state machine's body-language channel. */
        private void SetBodyLanguage(float freq, float amp)
        {
            if (body == null) return;
            body.WaveFrequencyMultiplier = freq;
            body.WaveAmplitudeMultiplier = amp;
        }

        /** Pushes wave multipliers into the lure chain. */
        private void SetLureLanguage(float freq, float amp)
        {
            if (lure == null) return;
            lure.WaveFrequencyMultiplier = freq;
            lure.WaveAmplitudeMultiplier = amp;
        }

        /** Drives the material flash channel on the body through a property block (no material instancing). */
        private void SetFlash(float amount)
        {
            if (bodyRenderer == null || bodyRenderer.Renderer == null) return;
            bodyRenderer.Renderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(FlashAmountId, amount);
            bodyRenderer.Renderer.SetPropertyBlock(_mpb);
        }

        /**
         * Drives the lure's emission intensity every tick — the horror beat lives entirely in this
         * channel. Idle it breathes with a gentle sine pulse; the telegraph spikes it into a brief
         * flare; giving up snuffs it to near-black; and coming back out of a failed attack it
         * re-lights on a slow, distinct eased ramp rather than snapping back on.
         */
        private void UpdateLureGlow(float dt)
        {
            _pulsePhase += dt * pulseSpeed * Mathf.PI * 2f;
            float target;

            switch (_state)
            {
                case AiState.Lurk:
                    // Idle breathing: intensity oscillates +-pulseDepth around the idle baseline.
                    target = idleGlowIntensity * (1f + Mathf.Sin(_pulsePhase) * pulseDepth);

                    // Ease up from near-black across relightRampDuration instead of snapping back on.
                    if (_relightTimer >= 0f)
                    {
                        _relightTimer += dt;
                        float t = Mathf.Clamp01(_relightTimer / relightRampDuration);
                        float eased = t * t * (3f - 2f * t); // smoothstep
                        target = Mathf.Lerp(darkIntensity, target, eased);
                        if (t >= 1f) _relightTimer = -1f;
                    }
                    break;

                case AiState.Flare:
                    // Brief flare spike — fast smoothing below carries this to near-peak within the telegraph window.
                    target = flareIntensity;
                    break;

                case AiState.GiveUp:
                    // Snuffed out — eases down to near-black across the give-up window.
                    target = darkIntensity;
                    break;

                default: // Lunge, Chase — the ambush has already sprung; hold a flat idle glow.
                    target = idleGlowIntensity;
                    break;
            }

            // Exponential smoothing so every transition (including the flare spike and the give-up
            // snuff) reads as a fluid pulse of light rather than a hard cut. Flare smooths faster so
            // the spike is visible within its short window.
            float smoothing = _state == AiState.Flare ? 18f : 6f;
            _glowIntensity = Mathf.Lerp(_glowIntensity, target, 1f - Mathf.Exp(-smoothing * dt));

            SetLureGlow(_glowIntensity);
        }

        /** Drives _EmissionColor on the lure (and, at a fraction, the body) through property blocks. */
        private void SetLureGlow(float intensity)
        {
            if (lureRenderer != null && lureRenderer.Renderer != null)
            {
                lureRenderer.Renderer.GetPropertyBlock(_mpb);
                _mpb.SetColor(EmissionColorId, lureGlow * intensity);
                lureRenderer.Renderer.SetPropertyBlock(_mpb);
            }

            // Optional subtle skin glow — the body catching a fraction of the lure's bioluminescence.
            if (bodyGlowFraction > 0f && bodyRenderer != null && bodyRenderer.Renderer != null)
            {
                bodyRenderer.Renderer.GetPropertyBlock(_mpb);
                _mpb.SetColor(EmissionColorId, lureGlow * (intensity * bodyGlowFraction));
                bodyRenderer.Renderer.SetPropertyBlock(_mpb);
            }
        }

        // -------------------------------------------------------
        // Damage & death
        // -------------------------------------------------------

        private void OnCollisionEnter2D(Collision2D collision)
        {
            // Only the burst/chase bites, once per attack cycle, resolved against the submarine
            // actually struck (co-op safe — same pattern as RammingEnemy/EelEnemy).
            if ((_state != AiState.Lunge && _state != AiState.Chase) || _hasHitThisAttack) return;
            if (!collision.collider.CompareTag("Player")) return;

            var victim = collision.collider.GetComponentInParent<Submarine>();
            if (victim == null) return;

            _hasHitThisAttack = true;
            int damage = victim.Hull != null ? victim.Hull.EvaluateAttack(biteDamage) : biteDamage;
            victim.Health?.TakeDamage(damage);
            onHitPlayer?.Invoke();

            Enter(AiState.GiveUp);
        }

        protected override void OnDeath()
        {
            _state = AiState.Dead;
            base.OnDeath();

            // The corpse goes fully dark and slack — no more glow, no more life in either chain.
            SetBodyLanguage(freq: 0f, amp: 0f);
            SetLureLanguage(freq: 0f, amp: 0f);
            SetFlash(0f);
            SetLureGlow(0f);
            body?.Limp(2f);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, strikeRadius);
        }
    }
}
