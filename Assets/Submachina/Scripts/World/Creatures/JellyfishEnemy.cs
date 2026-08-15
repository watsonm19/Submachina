using Core.ProceduralAnimation;
using DG.Tweening;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Events;

namespace Submachina.Core
{
    /**
     * Drifting jellyfish — a hypnotic bloom-dweller whose pulse animation IS its
     * propulsion. There is no separate "movement" system layered on top of the
     * body: the bell's contraction squash and the swimming impulse fire from the
     * exact same state transition, so the visual squeeze and the physical shove
     * are inherently synchronized.
     *
     * Pulse cycle (drives both animation and locomotion):
     *   Gather   — the bell slowly inflates (Squash widens/flattens) as it loads up.
     *   Contract — a quick squeeze (Squash narrows/tall) fires ONE impulse along the
     *              current heading — the moment the animation and the thrust are one.
     *   Coast    — the squeeze relaxes and drag bleeds off velocity as it glides,
     *              before the cycle repeats.
     *
     * Heading (informal Drift / Menace modes):
     *   Drift  — gentle Perlin-noise wander with a slight upward buoyancy trend.
     *   Menace — a submarine wandered within menaceRadius: the heading slowly eases
     *            toward it instead. The jellyfish doesn't chase so much as drift with
     *            intent — it stalks. Glow brightens as an extra danger telegraph.
     *
     * The rim ripples with a traveling wave + per-vertex noise every tick (stronger
     * during Contract), and the trailing tentacles get a brief amplitude/frequency
     * punch on the squeeze and relax during Coast — body language read from a
     * distance, same as the eel's wind-up coil.
     */
    public class JellyfishEnemy : EnemyBase
    {
        // =====================
        // Detection
        // =====================

        [FoldoutGroup("Detection")]
        [Tooltip("A submarine within this range eases the jellyfish into Menace mode — it slowly stalks instead of wandering.")]
        [SerializeField, Min(0.5f)] private float menaceRadius = 7f;

        // =====================
        // Drift
        // =====================

        [FoldoutGroup("Drift")]
        [Tooltip("How fast the wander heading randomly veers (radians/sec at full noise deflection). Low values read as slow, aimless drift.")]
        [SerializeField, Min(0f)] private float wanderTurnRate = 0.6f;

        [FoldoutGroup("Drift")]
        [Tooltip("Speed the underlying Perlin noise evolves — higher wanders more erratically, lower drifts more smoothly.")]
        [SerializeField, Min(0f)] private float wanderNoiseFrequency = 0.15f;

        [FoldoutGroup("Drift")]
        [Tooltip("Upward bias blended into the wander direction (0 = neutral drift, 1 = strong buoyant rise) — a bloom trends slowly upward.")]
        [SerializeField, Range(0f, 1f)] private float buoyancyBias = 0.25f;

        [FoldoutGroup("Drift")]
        [Tooltip("Exponential ease rate (per second) the current heading turns toward its desired direction — governs both wander veering and the slow stalk-turn toward a menaced player.")]
        [SerializeField, Range(0.05f, 5f)] private float headingEaseSpeed = 0.6f;

        // =====================
        // Pulse Cycle
        // =====================

        [FoldoutGroup("Pulse Cycle")]
        [Tooltip("Duration of the slow inflate before the squeeze.")]
        [SerializeField, Min(0.05f)] private float gatherDuration = 1.6f;

        [FoldoutGroup("Pulse Cycle")]
        [Tooltip("Duration of the quick contraction. The propulsion impulse fires the instant this phase begins.")]
        [SerializeField, Min(0.05f)] private float contractDuration = 0.25f;

        [FoldoutGroup("Pulse Cycle")]
        [Tooltip("Time spent coasting/gliding on drag after a contraction before the next gather begins — the rest interval between pulses.")]
        [SerializeField, Min(0.05f)] private float pulseInterval = 1.8f;

        [FoldoutGroup("Pulse Cycle")]
        [Tooltip("Impulse magnitude applied along the current heading the instant Contract begins.")]
        [SerializeField, Min(0f)] private float pulseImpulse = 3.5f;

        [FoldoutGroup("Pulse Cycle")]
        [Tooltip("Per-tick velocity multiplier while coasting (0-1). Closer to 1 = glides further before drag settles it.")]
        [SerializeField, Range(0f, 1f)] private float coastDamping = 0.985f;

        [FoldoutGroup("Pulse Cycle")]
        [Tooltip("Exponential ease rate (per second) the bell's Squash blends toward its current phase target.")]
        [SerializeField, Range(0.5f, 20f)] private float squashEaseSpeed = 4f;

        [FoldoutGroup("Pulse Cycle")]
        [Tooltip("Squash target during Gather — the bell widens and flattens as it slowly inflates.")]
        [SerializeField] private Vector2 gatherSquash = new Vector2(1.1f, 0.9f);

        [FoldoutGroup("Pulse Cycle")]
        [Tooltip("Squash target during Contract — the quick, narrow, tall squeeze that fires the impulse.")]
        [SerializeField] private Vector2 contractSquash = new Vector2(0.75f, 1.2f);

        [FoldoutGroup("Pulse Cycle")]
        [Tooltip("Squash target during Coast — the bell relaxes back toward neutral before the next inflate.")]
        [SerializeField] private Vector2 restSquash = new Vector2(1f, 1f);

        // =====================
        // Bell
        // =====================

        [FoldoutGroup("Bell")]
        [Tooltip("Bell mesh driven by this brain. Auto-resolves from children if empty.")]
        [SerializeField] private RadialMeshRenderer bell;

        [FoldoutGroup("Bell")]
        [Tooltip("Base rim wobble amplitude (world units) outside of Contract.")]
        [SerializeField, Range(0f, 0.2f)] private float rimWobbleAmplitude = 0.03f;

        [FoldoutGroup("Bell")]
        [Tooltip("Rim wobble amplitude (world units) during Contract — the squeeze ripples harder.")]
        [SerializeField, Range(0f, 0.2f)] private float rimWobbleAmplitudeContract = 0.08f;

        [FoldoutGroup("Bell")]
        [Tooltip("Traveling wave speed around the rim, in cycles per second.")]
        [SerializeField, Min(0f)] private float rimWobbleSpeed = 0.6f;

        [FoldoutGroup("Bell")]
        [Tooltip("Number of wave crests traveling around the rim at once.")]
        [SerializeField, Range(1, 6)] private int rimWobbleWaves = 2;

        [FoldoutGroup("Bell")]
        [Tooltip("Speed the per-vertex rim noise evolves — layered on top of the traveling wave so the ripple doesn't look mechanical.")]
        [SerializeField, Min(0f)] private float rimNoiseFrequency = 0.8f;

        // =====================
        // Tentacles
        // =====================

        [FoldoutGroup("Tentacles")]
        [Tooltip("Trailing tentacle chains (visual only, no colliders). Auto-resolves from children if empty.")]
        [SerializeField] private ChainSimulator[] tentacles;

        [FoldoutGroup("Tentacles")]
        [Tooltip("Wave amplitude multiplier tentacles ease toward outside of Contract — a relaxed, loose trail.")]
        [SerializeField, Min(0f)] private float tentacleIdleAmplitudeMultiplier = 1f;

        [FoldoutGroup("Tentacles")]
        [Tooltip("Wave frequency multiplier tentacles ease toward outside of Contract.")]
        [SerializeField, Min(0f)] private float tentacleIdleFrequencyMultiplier = 1f;

        [FoldoutGroup("Tentacles")]
        [Tooltip("Wave amplitude multiplier tentacles punch to during Contract — a brief, sharp flick of body language.")]
        [SerializeField, Min(0f)] private float tentacleContractAmplitudeMultiplier = 1.8f;

        [FoldoutGroup("Tentacles")]
        [Tooltip("Wave frequency multiplier tentacles punch to during Contract.")]
        [SerializeField, Min(0f)] private float tentacleContractFrequencyMultiplier = 1.6f;

        [FoldoutGroup("Tentacles")]
        [Tooltip("Exponential ease rate (per second) tentacle multipliers blend toward their target — avoids a jarring snap.")]
        [SerializeField, Range(0.5f, 20f)] private float tentacleEaseSpeed = 6f;

        // =====================
        // Glow
        // =====================

        [FoldoutGroup("Glow")]
        [Tooltip("HDR emission color pushed into the bell material via MaterialPropertyBlock (_EmissionColor).")]
        [SerializeField, ColorUsage(true, true)] private Color glowColor = new Color(0.4f, 0.9f, 1f);

        [FoldoutGroup("Glow")]
        [Tooltip("Baseline glow intensity, always present.")]
        [SerializeField, Min(0f)] private float baseGlowIntensity = 0.4f;

        [FoldoutGroup("Glow")]
        [Tooltip("Extra glow intensity added at the peak of Contract, pulse-synced with the squeeze.")]
        [SerializeField, Min(0f)] private float pulseGlowBoost = 1.2f;

        [FoldoutGroup("Glow")]
        [Tooltip("Extra glow intensity added continuously while in Menace mode — a danger telegraph.")]
        [SerializeField, Min(0f)] private float menaceGlowBoost = 0.8f;

        [FoldoutGroup("Glow")]
        [Tooltip("Exponential ease rate (per second) the pulse-glow envelope rises/falls toward Contract's peak.")]
        [SerializeField, Range(0.5f, 20f)] private float glowEaseSpeed = 8f;

        // =====================
        // Death
        // =====================

        [FoldoutGroup("Death")]
        [Tooltip("Squash the bell eases toward after death — a slack, deflated drift instead of a rigid pop.")]
        [SerializeField] private Vector2 deathSquash = new Vector2(1.15f, 0.85f);

        [FoldoutGroup("Death")]
        [Tooltip("Duration of the post-death squash ease.")]
        [SerializeField, Min(0.1f)] private float deathSquashEaseDuration = 2.5f;

        // =====================
        // Sting
        // =====================

        [FoldoutGroup("Sting")]
        [Tooltip("Contact damage dealt on touch (before hull depth-vulnerability scaling).")]
        [SerializeField, Min(0)] private int stingDamage = 2;

        [FoldoutGroup("Sting")]
        [Tooltip("Minimum time between stings against the same (or any) submarine.")]
        [SerializeField, Min(0.05f)] private float stingCooldown = 0.75f;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired at the instant of each contraction — wire feedbacks/audio here.")]
        public UnityEvent onPulse;

        [FoldoutGroup("Events")]
        [Tooltip("Fired whenever the sting connects with a submarine.")]
        public UnityEvent onSting;

        // =====================
        // State
        // =====================

        private enum PulseState { Gather, Contract, Coast, Dead }

        private PulseState _state = PulseState.Gather;
        private float _stateTimer;
        private bool _isMenace;
        private float _wanderAngle;
        private float _rimPhase;
        private float _glowPulseT;
        private float _noiseSeed;
        private Vector2 _heading = Vector2.up;
        private float _nextStingTime;
        private MaterialPropertyBlock _mpb;
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        protected override string CurrentState => $"{(_isMenace ? "Menace" : "Drift")} / {_state}";

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();
            if (bell == null) bell = GetComponentInChildren<RadialMeshRenderer>();
            if (tentacles == null || tentacles.Length == 0) tentacles = GetComponentsInChildren<ChainSimulator>();

            _mpb = new MaterialPropertyBlock();

            // Per-instance noise seed and phase offset so a bloom of jellyfish doesn't pulse in lockstep.
            _noiseSeed = Random.value * 1000f;
            _stateTimer = Random.Range(0f, gatherDuration);
            _wanderAngle = Random.value * Mathf.PI * 2f;
            _heading = new Vector2(Mathf.Cos(_wanderAngle), Mathf.Sin(_wanderAngle));
        }

        // -------------------------------------------------------
        // AI
        // -------------------------------------------------------

        protected override void UpdateAI()
        {
            _stateTimer += Time.fixedDeltaTime;

            UpdateHeading();

            switch (_state)
            {
                case PulseState.Gather: TickGather(); break;
                case PulseState.Contract: TickContract(); break;
                case PulseState.Coast: TickCoast(); break;
            }

            UpdateRimWobble();
            UpdateTentacleBodyLanguage();
            UpdateGlow();
        }

        /** Slow inflate — Squash eases wide/flat while loading up for the next squeeze. */
        private void TickGather()
        {
            EaseSquash(gatherSquash);
            if (_stateTimer >= gatherDuration) Enter(PulseState.Contract);
        }

        /** Quick squeeze — Squash eases narrow/tall; the propulsion impulse already fired on entry. */
        private void TickContract()
        {
            EaseSquash(contractSquash);
            if (_stateTimer >= contractDuration) Enter(PulseState.Coast);
        }

        /** Drag-driven glide — Squash relaxes toward neutral while velocity bleeds off. */
        private void TickCoast()
        {
            EaseSquash(restSquash);
            Rb.linearVelocity *= coastDamping;
            if (_stateTimer >= pulseInterval) Enter(PulseState.Gather);
        }

        /**
         * Wander/stalk heading: a Perlin-driven random walk with an upward buoyancy
         * trend (Drift), blended toward the player's direction when close enough to
         * menace (Menace). Eased rather than snapped, so turns read as slow and heavy.
         */
        private void UpdateHeading()
        {
            float noise = Mathf.PerlinNoise(Time.time * wanderNoiseFrequency, _noiseSeed) - 0.5f;
            _wanderAngle += noise * wanderTurnRate * Time.fixedDeltaTime;
            Vector2 wanderDir = (new Vector2(Mathf.Cos(_wanderAngle), Mathf.Sin(_wanderAngle)) + Vector2.up * buoyancyBias).normalized;

            _isMenace = DistanceToPlayer() < menaceRadius;
            Vector2 desired = _isMenace ? DirectionToPlayer() : wanderDir;

            float t = 1f - Mathf.Exp(-headingEaseSpeed * Time.fixedDeltaTime);
            _heading = Vector2.Lerp(_heading, desired, t).normalized;
        }

        /** Traveling sine + per-vertex noise into the bell rim — stronger during Contract. */
        private void UpdateRimWobble()
        {
            if (bell == null) return;

            float[] offsets = bell.RimOffsets;
            int n = offsets.Length;
            if (n == 0) return;

            _rimPhase += Time.fixedDeltaTime * rimWobbleSpeed * Mathf.PI * 2f;
            float amp = _state == PulseState.Contract ? rimWobbleAmplitudeContract : rimWobbleAmplitude;

            // e.g. rimWobbleWaves = 2 means two ripple crests chase each other around the rim
            // as _rimPhase advances — a lazy, organic pulse rather than a uniform inflate.
            for (int i = 0; i < n; i++)
            {
                float angle01 = i / (float)n;
                float wave = Mathf.Sin(_rimPhase + angle01 * rimWobbleWaves * Mathf.PI * 2f);
                float noise = (Mathf.PerlinNoise(angle01 * 7.3f, Time.time * rimNoiseFrequency + _noiseSeed) - 0.5f) * 2f;
                offsets[i] = (wave * 0.7f + noise * 0.3f) * amp;
            }
        }

        /** Punches tentacle wave multipliers up on Contract, relaxes them otherwise — eased, not snapped. */
        private void UpdateTentacleBodyLanguage()
        {
            if (tentacles == null || tentacles.Length == 0) return;

            bool punch = _state == PulseState.Contract;
            float targetAmp = punch ? tentacleContractAmplitudeMultiplier : tentacleIdleAmplitudeMultiplier;
            float targetFreq = punch ? tentacleContractFrequencyMultiplier : tentacleIdleFrequencyMultiplier;
            float t = 1f - Mathf.Exp(-tentacleEaseSpeed * Time.fixedDeltaTime);

            foreach (ChainSimulator tentacle in tentacles)
            {
                if (tentacle == null) continue;
                tentacle.WaveAmplitudeMultiplier = Mathf.Lerp(tentacle.WaveAmplitudeMultiplier, targetAmp, t);
                tentacle.WaveFrequencyMultiplier = Mathf.Lerp(tentacle.WaveFrequencyMultiplier, targetFreq, t);
            }
        }

        /** Drives the bell's HDR emission through a property block — baseline + pulse-synced + menace telegraph. */
        private void UpdateGlow()
        {
            if (bell == null || bell.Renderer == null) return;

            float targetPulse = _state == PulseState.Contract ? 1f : 0f;
            float t = 1f - Mathf.Exp(-glowEaseSpeed * Time.fixedDeltaTime);
            _glowPulseT = Mathf.Lerp(_glowPulseT, targetPulse, t);

            float intensity = baseGlowIntensity + _glowPulseT * pulseGlowBoost + (_isMenace ? menaceGlowBoost : 0f);
            SetGlow(glowColor * intensity);
        }

        /** Eases the bell's Squash toward a target — the shared channel between animation and propulsion state. */
        private void EaseSquash(Vector2 target)
        {
            if (bell == null) return;
            float t = 1f - Mathf.Exp(-squashEaseSpeed * Time.fixedDeltaTime);
            bell.Squash = Vector2.Lerp(bell.Squash, target, t);
        }

        /** Pushes an HDR color into the bell material's _EmissionColor via property block (no material instancing). */
        private void SetGlow(Color emission)
        {
            if (bell == null || bell.Renderer == null) return;
            bell.Renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(EmissionColorId, emission);
            bell.Renderer.SetPropertyBlock(_mpb);
        }

        // -------------------------------------------------------
        // Transitions
        // -------------------------------------------------------

        /** Central state switch — resets the phase timer and fires the propulsion impulse on Contract. */
        private void Enter(PulseState next)
        {
            _state = next;
            _stateTimer = 0f;

            if (next == PulseState.Contract)
            {
                // The impulse fires in the same instant the squeeze animation begins —
                // movement and animation are the same event, not two synced systems.
                Rb.AddForce(_heading * pulseImpulse, ForceMode2D.Impulse);
                onPulse?.Invoke();
            }
        }

        // -------------------------------------------------------
        // Damage & death
        // -------------------------------------------------------

        private void OnCollisionEnter2D(Collision2D collision)
        {
            // The sting is always live (not state-gated), only rate-limited by cooldown.
            if (Time.time < _nextStingTime) return;
            if (!collision.collider.CompareTag("Player")) return;

            Submarine victim = collision.collider.GetComponentInParent<Submarine>();
            if (victim == null) return;

            _nextStingTime = Time.time + stingCooldown;
            int damage = victim.Hull != null ? victim.Hull.EvaluateAttack(stingDamage) : stingDamage;
            victim.Health?.TakeDamage(damage);
            onSting?.Invoke();
        }

        protected override void OnDeath()
        {
            _state = PulseState.Dead;
            base.OnDeath();

            // Glow dies immediately — no more danger telegraph from a corpse.
            SetGlow(Color.black);

            // Tentacles go fully slack — no more wave drive.
            if (tentacles != null)
            {
                foreach (ChainSimulator tentacle in tentacles)
                {
                    if (tentacle == null) continue;
                    tentacle.WaveAmplitudeMultiplier = 0f;
                    tentacle.WaveFrequencyMultiplier = 0f;
                }
            }

            // The bell slackens over a few seconds — a dead jelly drifting, not popping flat.
            if (bell != null)
                DOTween.To(() => bell.Squash, s => bell.Squash = s, deathSquash, deathSquashEaseDuration);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.6f, 0.2f, 1f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, menaceRadius);
        }
    }
}
