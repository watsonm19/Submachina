using Core.ProceduralAnimation;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Events;

namespace Submachina.Core
{
    /**
     * Abyssal eel — a sinuous procedural chaser built on the ChainSimulator spine.
     *
     * Behavior loop:
     *   Lurk    — serpentine drift around the spawn point, slow and unbothered.
     *   Hunt    — player detected: weaving approach (direct pursuit + perpendicular
     *             sine weave), body undulation ramps up with speed automatically.
     *   WindUp  — in strike range: brief coil telegraph, body flashes, wave frequency
     *             spikes while velocity drops — the classic "wind up before the bite".
     *   Strike  — straight-line lunge at locked direction; deals contact damage once.
     *   Recover — drifts loose and floppy after the lunge; the vulnerability window.
     *
     * The animation IS the tell: undulation frequency and amplitude are driven by
     * the state machine through the ChainSimulator multipliers, so players read the
     * eel's intent from its body language before the lunge comes.
     */
    public class EelEnemy : EnemyBase
    {
        // =====================
        // Detection
        // =====================

        [FoldoutGroup("Detection")]
        [Tooltip("Player within this range wakes the eel into Hunt.")]
        [SerializeField, Min(1f)] private float detectionRadius = 9f;

        [FoldoutGroup("Detection")]
        [Tooltip("Player beyond this range during Hunt sends the eel back to Lurk (hysteresis band above detection).")]
        [SerializeField, Min(1f)] private float loseInterestRadius = 14f;

        // =====================
        // Movement
        // =====================

        [FoldoutGroup("Movement")]
        [Tooltip("Cruise speed while lurking near the spawn point.")]
        [SerializeField, Min(0f)] private float lurkSpeed = 1.2f;

        [FoldoutGroup("Movement")]
        [Tooltip("Radius around the spawn point the eel patrols while lurking.")]
        [SerializeField, Min(0.5f)] private float lurkRadius = 4f;

        [FoldoutGroup("Movement")]
        [Tooltip("Pursuit speed while hunting.")]
        [SerializeField, Min(0f)] private float huntSpeed = 3.5f;

        [FoldoutGroup("Movement")]
        [Tooltip("Sideways weave amplitude while hunting (world units) — makes the approach serpentine instead of beeline.")]
        [SerializeField, Min(0f)] private float weaveAmplitude = 1.5f;

        [FoldoutGroup("Movement")]
        [Tooltip("Weave oscillations per second while hunting.")]
        [SerializeField, Min(0f)] private float weaveFrequency = 0.8f;

        [FoldoutGroup("Movement")]
        [Tooltip("Steering responsiveness (per second). Higher = snappier turns; lower = big flowing arcs.")]
        [SerializeField, Range(0.5f, 20f)] private float steering = 4f;

        // =====================
        // Strike
        // =====================

        [FoldoutGroup("Strike")]
        [Tooltip("Hunt range at which the eel commits to a strike.")]
        [SerializeField, Min(0.5f)] private float strikeRange = 3.5f;

        [FoldoutGroup("Strike")]
        [Tooltip("Coil telegraph duration before the lunge — the player's dodge window.")]
        [SerializeField, Min(0.05f)] private float windUpDuration = 0.4f;

        [FoldoutGroup("Strike")]
        [Tooltip("Lunge speed. Direction locks at the end of WindUp, so a moving player can dodge it.")]
        [SerializeField, Min(1f)] private float strikeSpeed = 13f;

        [FoldoutGroup("Strike")]
        [Tooltip("Lunge duration before entering Recover.")]
        [SerializeField, Min(0.05f)] private float strikeDuration = 0.45f;

        [FoldoutGroup("Strike")]
        [Tooltip("Contact damage dealt while striking (before hull depth-vulnerability scaling).")]
        [SerializeField, Min(0)] private int strikeDamage = 4;

        [FoldoutGroup("Strike")]
        [Tooltip("Loose drift after a strike — the eel's vulnerable/readable recovery window.")]
        [SerializeField, Min(0.1f)] private float recoverDuration = 1.4f;

        // =====================
        // Animation Coupling
        // =====================

        [FoldoutGroup("Animation Coupling")]
        [Tooltip("Body chain driven by this brain. Auto-resolves from children if empty.")]
        [SerializeField] private ChainSimulator body;

        [FoldoutGroup("Animation Coupling")]
        [Tooltip("Strip renderer used for the wind-up flash. Auto-resolves from children if empty.")]
        [SerializeField] private ChainStripRenderer bodyRenderer;

        [FoldoutGroup("Animation Coupling")]
        [Tooltip("Wave frequency multiplier at the peak of the wind-up coil.")]
        [SerializeField, Min(1f)] private float windUpWaveBoost = 3f;

        [FoldoutGroup("Animation Coupling")]
        [Tooltip("Peak _FlashAmount pushed into the body material during wind-up.")]
        [SerializeField, Range(0f, 1f)] private float windUpFlashPeak = 0.55f;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired when the wind-up telegraph begins — wire feedbacks/audio here.")]
        public UnityEvent onWindUp;

        [FoldoutGroup("Events")]
        [Tooltip("Fired at the moment of the lunge.")]
        public UnityEvent onStrike;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when the strike connects with a submarine.")]
        public UnityEvent onHitPlayer;

        // =====================
        // State
        // =====================

        private enum AiState { Lurk, Hunt, WindUp, Strike, Recover, Dead }

        private AiState _state = AiState.Lurk;
        private float _stateTimer;
        private float _weavePhase;
        private Vector2 _strikeDirection;
        private Vector2 _lurkTarget;
        private bool _hasHitThisStrike;
        private MaterialPropertyBlock _mpb;
        private static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");

        protected override string CurrentState => _state.ToString();

        // Lock the target mid-commitment so the coil/lunge finishes on the telegraphed victim.
        protected override bool CanRetarget => _state != AiState.WindUp && _state != AiState.Strike;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();
            if (body == null) body = GetComponentInChildren<ChainSimulator>();
            if (bodyRenderer == null) bodyRenderer = GetComponentInChildren<ChainStripRenderer>();
            _mpb = new MaterialPropertyBlock();
            _lurkTarget = transform.position;
        }

        // -------------------------------------------------------
        // AI
        // -------------------------------------------------------

        protected override void UpdateAI()
        {
            _stateTimer += Time.fixedDeltaTime;

            switch (_state)
            {
                case AiState.Lurk: TickLurk(); break;
                case AiState.Hunt: TickHunt(); break;
                case AiState.WindUp: TickWindUp(); break;
                case AiState.Strike: TickStrike(); break;
                case AiState.Recover: TickRecover(); break;
            }
        }

        /** Serpentine drift between random points around the spawn anchor. */
        private void TickLurk()
        {
            // Wake up when a submarine wanders close.
            if (DistanceToPlayer() < detectionRadius) { Enter(AiState.Hunt); return; }

            // Re-pick a wander point when reached (or on a lazy timeout).
            if (((Vector2)transform.position - _lurkTarget).sqrMagnitude < 0.6f || _stateTimer > 6f)
            {
                _lurkTarget = (Vector2)SpawnPosition + Random.insideUnitCircle * lurkRadius;
                _stateTimer = 0f;
            }

            Steer((_lurkTarget - (Vector2)transform.position).normalized * lurkSpeed);
        }

        /** Weaving pursuit — direct chase plus a perpendicular sine sweep. */
        private void TickHunt()
        {
            float dist = DistanceToPlayer();
            if (dist > loseInterestRadius) { Enter(AiState.Lurk); return; }
            if (dist < strikeRange) { Enter(AiState.WindUp); return; }

            _weavePhase += Time.fixedDeltaTime * weaveFrequency * Mathf.PI * 2f;
            Vector2 toPlayer = DirectionToPlayer();
            Vector2 side = new Vector2(-toPlayer.y, toPlayer.x);

            // e.g. amplitude 1.5 at frequency 0.8: the eel sweeps ±1.5 units across
            // its approach line roughly every 1.25 seconds — snake, not torpedo.
            Vector2 desired = (toPlayer * huntSpeed + side * (Mathf.Sin(_weavePhase) * weaveAmplitude));
            Steer(Vector2.ClampMagnitude(desired, huntSpeed * 1.3f));
        }

        /** Coil telegraph: kill velocity, spike the body wave, flash toward the peak. */
        private void TickWindUp()
        {
            Rb.linearVelocity *= 0.82f; // rapid but not instant stop — reads as bracing

            float t = Mathf.Clamp01(_stateTimer / windUpDuration);
            SetFlash(t * windUpFlashPeak);

            if (_stateTimer >= windUpDuration)
            {
                // Direction locks NOW — dodging during the coil works.
                _strikeDirection = DirectionToPlayer();
                Enter(AiState.Strike);
                onStrike?.Invoke();
            }
        }

        /** Straight lunge along the locked direction. */
        private void TickStrike()
        {
            Rb.linearVelocity = _strikeDirection * strikeSpeed;
            if (_stateTimer >= strikeDuration) Enter(AiState.Recover);
        }

        /** Loose floppy drift — readable vulnerability window, then back to hunting. */
        private void TickRecover()
        {
            Rb.linearVelocity *= 0.95f;
            if (_stateTimer >= recoverDuration)
                Enter(DistanceToPlayer() < loseInterestRadius ? AiState.Hunt : AiState.Lurk);
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

        /** Central state switch — resets timers and retunes the body language per state. */
        private void Enter(AiState next)
        {
            _state = next;
            _stateTimer = 0f;

            switch (next)
            {
                case AiState.Lurk:
                    SetBodyLanguage(freq: 1f, amp: 1f); SetFlash(0f);
                    _lurkTarget = (Vector2)SpawnPosition + Random.insideUnitCircle * lurkRadius;
                    break;
                case AiState.Hunt:
                    SetBodyLanguage(freq: 1.4f, amp: 1.2f); SetFlash(0f);
                    break;
                case AiState.WindUp:
                    SetBodyLanguage(freq: windUpWaveBoost, amp: 1.6f);
                    SetIntentIndicator(true);
                    onWindUp?.Invoke();
                    break;
                case AiState.Strike:
                    // Body goes near-rigid during the lunge — a straight loosed arrow.
                    SetBodyLanguage(freq: 0.4f, amp: 0.4f); SetFlash(0f);
                    SetIntentIndicator(false);
                    _hasHitThisStrike = false;
                    break;
                case AiState.Recover:
                    SetBodyLanguage(freq: 0.7f, amp: 1.8f); // slack, over-wobbly = spent
                    break;
            }
        }

        /** Pushes wave multipliers into the chain — the state machine's body-language channel. */
        private void SetBodyLanguage(float freq, float amp)
        {
            if (body == null) return;
            body.WaveFrequencyMultiplier = freq;
            body.WaveAmplitudeMultiplier = amp;
        }

        /** Drives the material flash channel through a property block (no material instancing). */
        private void SetFlash(float amount)
        {
            if (bodyRenderer == null || bodyRenderer.Renderer == null) return;
            bodyRenderer.Renderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(FlashAmountId, amount);
            bodyRenderer.Renderer.SetPropertyBlock(_mpb);
        }

        // -------------------------------------------------------
        // Damage & death
        // -------------------------------------------------------

        private void OnCollisionEnter2D(Collision2D collision)
        {
            // Only the lunge bites, once per strike, resolved against the submarine
            // actually struck (co-op safe — same pattern as RammingEnemy).
            if (_state != AiState.Strike || _hasHitThisStrike) return;
            if (!collision.collider.CompareTag("Player")) return;

            var victim = collision.collider.GetComponentInParent<Submarine>();
            if (victim == null) return;

            _hasHitThisStrike = true;
            int damage = victim.Hull != null ? victim.Hull.EvaluateAttack(strikeDamage) : strikeDamage;
            victim.Health?.TakeDamage(damage);
            onHitPlayer?.Invoke();

            Enter(AiState.Recover);
        }

        protected override void OnDeath()
        {
            _state = AiState.Dead;
            base.OnDeath();

            // The corpse goes fully slack — no wave drive, pure trailing rope.
            SetBodyLanguage(freq: 0f, amp: 0f);
            SetFlash(0f);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, detectionRadius);
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, strikeRange);
        }
    }
}
