using Core.ProceduralAnimation;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Events;

namespace Submachina.Core
{
    /**
     * Jet-propelled ambusher — the showpiece predator built on RadialMeshRenderer
     * (mantle) + ChainSimulator (tentacles), with chromatophore flicker and an
     * ink-cloud panic escape.
     *
     * Behavior loop:
     *   Lurk       — out of detection range: slow aimless drift near the spawn point.
     *   Hover      — player detected: holds preferredRange with a gentle strafing
     *                wobble, easing closer if too far and backing off if too close.
     *   Aim        — attack telegraph: velocity bleeds to zero, the mantle inflates,
     *                and chromatophores flicker _FlashAmount 0 → peak on fast Perlin
     *                noise — the signature "tensing up to fire" tell.
     *   Jet        — direction locks (aimed THROUGH the player, ramming-dash style),
     *                the mantle squeezes long and the tentacles' wave amplitude spikes
     *                then trails off as the burst burns out. Contact damage once per jet.
     *   Recover    — short loose drift, then back to Hover.
     *   InkEscape  — panic response: triggered by dropping below a health fraction or
     *                immediately after landing a jet hit (both gated by an internal
     *                cooldown so it can't chain-trigger). Ink cloud plays, the squid
     *                jets AWAY from the player, and the mantle strobes a hard dark
     *                flash, then settles back into Hover.
     *
     * The mantle/tentacle animation channels ARE the tell — players read the aim
     * telegraph and the recoil of a jet from the body language, same philosophy as
     * EelEnemy's wind-up.
     */
    public class SquidEnemy : EnemyBase
    {
        // =====================
        // Detection
        // =====================

        [FoldoutGroup("Detection")]
        [Tooltip("Player within this range wakes the squid from Lurk into Hover.")]
        [SerializeField, Min(1f)] private float detectionRadius = 8f;

        [FoldoutGroup("Detection")]
        [Tooltip("Player beyond this range during Hover sends the squid back to Lurk (hysteresis band above detection).")]
        [SerializeField, Min(1f)] private float loseInterestRadius = 13f;

        // =====================
        // Movement
        // =====================

        [FoldoutGroup("Movement")]
        [Tooltip("Cruise speed while lurking near the spawn point.")]
        [SerializeField, Min(0f)] private float lurkSpeed = 1f;

        [FoldoutGroup("Movement")]
        [Tooltip("Radius around the spawn point the squid drifts within while lurking.")]
        [SerializeField, Min(0.5f)] private float lurkRadius = 3f;

        [FoldoutGroup("Movement")]
        [Tooltip("Approach/back-off speed while holding station in Hover.")]
        [SerializeField, Min(0f)] private float hoverSpeed = 2.5f;

        [FoldoutGroup("Movement")]
        [Tooltip("Distance from the player the squid tries to hold while hovering.")]
        [SerializeField, Min(0.5f)] private float preferredRange = 5f;

        [FoldoutGroup("Movement")]
        [Tooltip("Deadzone around preferredRange: inside this band the squid neither approaches nor backs off, and (off cooldown) commits to Aim.")]
        [SerializeField, Min(0.1f)] private float rangeTolerance = 1f;

        [FoldoutGroup("Movement")]
        [Tooltip("Sideways strafe amplitude (world units) layered over the hover hold — keeps station-keeping from looking frozen.")]
        [SerializeField, Min(0f)] private float strafeAmplitude = 1f;

        [FoldoutGroup("Movement")]
        [Tooltip("Strafe oscillations per second while hovering.")]
        [SerializeField, Min(0f)] private float strafeFrequency = 0.5f;

        [FoldoutGroup("Movement")]
        [Tooltip("Steering responsiveness (per second). Higher = snappier turns; lower = big flowing arcs.")]
        [SerializeField, Range(0.5f, 20f)] private float steering = 5f;

        [FoldoutGroup("Movement")]
        [Tooltip("How fast visualRoot slerps to face the current travel direction (per second).")]
        [SerializeField, Range(0.5f, 20f)] private float visualTurnSpeed = 6f;

        // =====================
        // Attack
        // =====================

        [FoldoutGroup("Attack")]
        [Tooltip("Telegraph duration before the jet fires — the player's dodge window.")]
        [SerializeField, Min(0.05f)] private float aimDuration = 0.6f;

        [FoldoutGroup("Attack")]
        [Tooltip("Minimum time between the end of one jet attempt and the next Aim.")]
        [SerializeField, Min(0f)] private float attackCooldown = 3f;

        [FoldoutGroup("Attack")]
        [Tooltip("Jet burst speed. Direction locks at the end of Aim, so a moving player can dodge it.")]
        [SerializeField, Min(1f)] private float jetSpeed = 16f;

        [FoldoutGroup("Attack")]
        [Tooltip("How long the jet burst lasts before easing into Recover (if it didn't already connect).")]
        [SerializeField, Min(0.05f)] private float jetDuration = 0.35f;

        [FoldoutGroup("Attack")]
        [Tooltip("Contact damage dealt on a successful jet hit (before hull depth-vulnerability scaling).")]
        [SerializeField, Min(0)] private int jetDamage = 5;

        [FoldoutGroup("Attack")]
        [Tooltip("Loose drift after a jet (miss) before returning to Hover.")]
        [SerializeField, Min(0.1f)] private float recoverDuration = 1.2f;

        // =====================
        // Mantle
        // =====================

        [FoldoutGroup("Mantle")]
        [Tooltip("Mantle Squash target at the peak of the Aim telegraph — inflated and wide.")]
        [SerializeField] private Vector2 aimSquash = new Vector2(1.15f, 0.9f);

        [FoldoutGroup("Mantle")]
        [Tooltip("Mantle Squash target during the Jet burst and the InkEscape flee — long and squeezed.")]
        [SerializeField] private Vector2 jetSquash = new Vector2(0.7f, 1.25f);

        [FoldoutGroup("Mantle")]
        [Tooltip("How fast the mantle Squash eases back toward neutral (per second) in Recover/OnDeath.")]
        [SerializeField, Range(0.5f, 20f)] private float squashEaseSpeed = 6f;

        [FoldoutGroup("Mantle")]
        [Tooltip("Chromatophore flicker speed (Perlin noise sample rate) during Aim.")]
        [SerializeField, Min(0f)] private float flickerFrequency = 14f;

        [FoldoutGroup("Mantle")]
        [Tooltip("Peak _FlashAmount the chromatophore flicker ramps toward as the Aim telegraph completes.")]
        [SerializeField, Range(0f, 1f)] private float flickerPeak = 0.6f;

        [FoldoutGroup("Mantle")]
        [Tooltip("Idle breathing Squash sine amplitude (Lurk/Hover) so the mantle never looks static.")]
        [SerializeField, Range(0f, 0.2f)] private float breatheAmplitude = 0.04f;

        [FoldoutGroup("Mantle")]
        [Tooltip("Idle breathing cycles per second.")]
        [SerializeField, Min(0f)] private float breatheFrequency = 0.5f;

        // =====================
        // Tentacles
        // =====================

        [FoldoutGroup("Tentacles")]
        [Tooltip("Peak WaveAmplitudeMultiplier pushed into every tentacle the instant the jet fires.")]
        [SerializeField, Min(1f)] private float jetTentacleSpike = 2.5f;

        [FoldoutGroup("Tentacles")]
        [Tooltip("How fast the tentacle amplitude spike trails back down to baseline (per second) during Jet.")]
        [SerializeField, Range(0.5f, 20f)] private float tentacleTrailSpeed = 4f;

        // =====================
        // Ink Escape
        // =====================

        [FoldoutGroup("Ink Escape")]
        [Tooltip("Own health fraction (0-1) below which taking damage can trigger InkEscape.")]
        [SerializeField, Range(0f, 1f)] private float inkHealthFraction = 0.3f;

        [FoldoutGroup("Ink Escape")]
        [Tooltip("Minimum time between InkEscape triggers, so a flurry of hits can't chain-trigger it.")]
        [SerializeField, Min(0f)] private float inkCooldown = 4f;

        [FoldoutGroup("Ink Escape")]
        [Tooltip("Flee speed while jetting away from the player during InkEscape.")]
        [SerializeField, Min(0f)] private float fleeSpeed = 10f;

        [FoldoutGroup("Ink Escape")]
        [Tooltip("How long the flee burst lasts before settling back into Hover.")]
        [SerializeField, Min(0.05f)] private float fleeDuration = 0.8f;

        // =====================
        // Animation Coupling
        // =====================

        [FoldoutGroup("Animation Coupling")]
        [Tooltip("Child holding the mantle + tentacle anchors. Rotated toward travel direction each frame; the root Rigidbody2D itself never rotates.")]
        [SerializeField] private Transform visualRoot;

        [FoldoutGroup("Animation Coupling")]
        [Tooltip("Mantle mesh driven by this brain. Auto-resolves from children if empty.")]
        [SerializeField] private RadialMeshRenderer mantle;

        [FoldoutGroup("Animation Coupling")]
        [Tooltip("Tentacle chains driven by this brain. Auto-resolves from children if empty.")]
        [SerializeField] private ChainSimulator[] tentacles;

        [FoldoutGroup("Animation Coupling")]
        [Tooltip("Ink cloud particles played on InkEscape (and a weak puff on death). May be left empty.")]
        [SerializeField] private ParticleSystem inkParticles;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired when the Aim telegraph begins — wire feedbacks/audio here.")]
        public UnityEvent onAim;

        [FoldoutGroup("Events")]
        [Tooltip("Fired at the moment the jet fires.")]
        public UnityEvent onJet;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when InkEscape triggers.")]
        public UnityEvent onInk;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when a jet connects with a submarine.")]
        public UnityEvent onHitPlayer;

        // =====================
        // State
        // =====================

        private enum AiState { Lurk, Hover, Aim, Jet, Recover, InkEscape, Dead }

        private AiState _state = AiState.Lurk;
        private float _stateTimer;
        private float _strafePhase;
        private float _flickerSeed;
        private Vector2 _lurkTarget;
        private Vector2 _jetDirection;
        private Vector2 _fleeDirection;
        private bool _hasHitThisJet;
        private float _nextAttackTime;
        private float _nextInkReadyTime;
        private Health _health;
        private MaterialPropertyBlock _mpb;
        private static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");

        protected override string CurrentState => _state.ToString();

        // Lock the target mid-commitment so the telegraph/jet finishes on the telegraphed victim.
        protected override bool CanRetarget => _state != AiState.Aim && _state != AiState.Jet;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();
            if (mantle == null) mantle = GetComponentInChildren<RadialMeshRenderer>();
            if (tentacles == null || tentacles.Length == 0) tentacles = GetComponentsInChildren<ChainSimulator>();
            _mpb = new MaterialPropertyBlock();
            _flickerSeed = Random.value * 1000f;
            _lurkTarget = transform.position;
        }

        protected override void Start()
        {
            base.Start();

            // Listen for our own damage so a critically wounded squid can bolt into
            // InkEscape regardless of what the state machine is currently doing.
            _health = GetComponent<Health>();
            if (_health != null) _health.onDamaged.AddListener(OnDamaged);
        }

        private void Update()
        {
            // Cosmetic-only, so it runs on the render tick rather than the fixed AI tick.
            UpdateVisualRotation();
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
                case AiState.Hover: TickHover(); break;
                case AiState.Aim: TickAim(); break;
                case AiState.Jet: TickJet(); break;
                case AiState.Recover: TickRecover(); break;
                case AiState.InkEscape: TickInkEscape(); break;
            }
        }

        /** Aimless drift between random points around the spawn anchor. */
        private void TickLurk()
        {
            if (DistanceToPlayer() < detectionRadius) { Enter(AiState.Hover); return; }

            // Re-pick a wander point when reached (or on a lazy timeout).
            if (((Vector2)transform.position - _lurkTarget).sqrMagnitude < 0.6f || _stateTimer > 6f)
            {
                _lurkTarget = (Vector2)SpawnPosition + Random.insideUnitCircle * lurkRadius;
                _stateTimer = 0f;
            }

            Steer((_lurkTarget - (Vector2)transform.position).normalized * lurkSpeed);
            ApplyBreathing();
        }

        /** Holds preferredRange from the player with a strafing wobble; commits to Aim once settled and off cooldown. */
        private void TickHover()
        {
            float dist = DistanceToPlayer();
            if (dist > loseInterestRadius) { Enter(AiState.Lurk); return; }

            // Comfortably at range and off cooldown — commit to the telegraph.
            if (Mathf.Abs(dist - preferredRange) < rangeTolerance && Time.time >= _nextAttackTime)
            {
                Enter(AiState.Aim);
                return;
            }

            Vector2 toPlayer = DirectionToPlayer();
            Vector2 desired = Vector2.zero;

            // Approach if too far, back off if too close, hold station inside the deadzone.
            if (dist > preferredRange + rangeTolerance) desired = toPlayer * hoverSpeed;
            else if (dist < preferredRange - rangeTolerance) desired = -toPlayer * hoverSpeed;

            // Gentle perpendicular wobble layered on top so the hold never looks frozen.
            _strafePhase += Time.fixedDeltaTime * strafeFrequency * Mathf.PI * 2f;
            Vector2 side = new Vector2(-toPlayer.y, toPlayer.x);
            desired += side * (Mathf.Sin(_strafePhase) * strafeAmplitude);

            Steer(desired);
            ApplyBreathing();
        }

        /** Telegraph: velocity bleeds to zero, mantle inflates, chromatophores flicker toward the peak. */
        private void TickAim()
        {
            Steer(Vector2.zero);

            float t = Mathf.Clamp01(_stateTimer / aimDuration);
            if (mantle != null) mantle.Squash = Vector2.Lerp(Vector2.one, aimSquash, t);

            // Fast Perlin flicker envelope-ramped from 0 up to the peak as the telegraph builds.
            // e.g. flickerFrequency 14 samples the noise fast enough to read as a jittery
            // chromatophore shimmer rather than a smooth fade.
            float flicker = Mathf.PerlinNoise(Time.time * flickerFrequency, _flickerSeed) * flickerPeak * t;
            SetFlash(flicker);

            if (_stateTimer >= aimDuration)
            {
                // Direction locks now, aimed THROUGH the player's current position — a dash-past ram, not a stop-and-poke.
                _jetDirection = DirectionToPlayer();
                Enter(AiState.Jet);
                onJet?.Invoke();
            }
        }

        /** Locked-direction burst. Tentacle amplitude spikes on entry and trails off as the burst burns out. */
        private void TickJet()
        {
            Rb.linearVelocity = _jetDirection * jetSpeed;

            float trailT = 1f - Mathf.Exp(-tentacleTrailSpeed * Time.fixedDeltaTime);
            if (tentacles != null)
            {
                for (int i = 0; i < tentacles.Length; i++)
                {
                    if (tentacles[i] == null) continue;
                    tentacles[i].WaveAmplitudeMultiplier = Mathf.Lerp(tentacles[i].WaveAmplitudeMultiplier, 1f, trailT);
                }
            }

            if (_stateTimer >= jetDuration) Enter(AiState.Recover);
        }

        /** Loose drift after a miss, easing the mantle back toward neutral, then back to Hover. */
        private void TickRecover()
        {
            Rb.linearVelocity *= 0.92f;
            if (mantle != null)
                mantle.Squash = Vector2.Lerp(mantle.Squash, Vector2.one, 1f - Mathf.Exp(-squashEaseSpeed * Time.fixedDeltaTime));

            if (_stateTimer >= recoverDuration) Enter(AiState.Hover);
        }

        /** Panic flee: locked-away-direction burst with a hard strobing dark flash, then back to Hover. */
        private void TickInkEscape()
        {
            Rb.linearVelocity = _fleeDirection * fleeSpeed;

            // FlashColor is set dark on the material, so slamming _FlashAmount hard (with a
            // fast pulse on top) reads as the ink cloud strobing across the mantle.
            float pulse = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.time * 16f));
            SetFlash(pulse);

            if (_stateTimer >= fleeDuration) Enter(AiState.Hover);
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

        /** Central state switch — resets timers and retunes mantle/tentacle body language per state. */
        private void Enter(AiState next)
        {
            _state = next;
            _stateTimer = 0f;

            switch (next)
            {
                case AiState.Lurk:
                    SetTentacleWave(freq: 0.8f, amp: 0.8f);
                    SetFlash(0f);
                    _lurkTarget = (Vector2)SpawnPosition + Random.insideUnitCircle * lurkRadius;
                    break;

                case AiState.Hover:
                    SetTentacleWave(freq: 1.2f, amp: 1f);
                    SetFlash(0f);
                    if (mantle != null) mantle.Squash = Vector2.one;
                    break;

                case AiState.Aim:
                    SetTentacleWave(freq: 1.8f, amp: 1.3f); // tension builds through the telegraph
                    SetIntentIndicator(true);
                    onAim?.Invoke();
                    break;

                case AiState.Jet:
                    SetIntentIndicator(false);
                    if (mantle != null) mantle.Squash = jetSquash;
                    SetTentacleAmplitude(jetTentacleSpike);
                    _hasHitThisJet = false;
                    _nextAttackTime = Time.time + attackCooldown;
                    break;

                case AiState.Recover:
                    SetTentacleWave(freq: 0.9f, amp: 1.4f); // loose, over-wobbly = spent
                    break;

                case AiState.InkEscape:
                    _fleeDirection = DirectionAwayFromPlayer();
                    if (mantle != null) mantle.Squash = jetSquash;
                    SetTentacleWave(freq: 2f, amp: 1.5f); // frantic
                    SetIntentIndicator(false);
                    inkParticles?.Play();
                    onInk?.Invoke();
                    break;
            }
        }

        /** Attempts to bolt into InkEscape, gated by an internal cooldown so it can't chain-trigger every hit. Returns whether it actually triggered. */
        private bool TryTriggerInkEscape()
        {
            if (_state == AiState.Dead || _state == AiState.InkEscape) return false;
            if (Time.time < _nextInkReadyTime) return false;

            _nextInkReadyTime = Time.time + inkCooldown;
            Enter(AiState.InkEscape);
            return true;
        }

        /** Health.onDamaged listener — bolts into InkEscape once HP drops below the configured fraction. */
        private void OnDamaged(int amount)
        {
            if (_state == AiState.Dead) return;
            if (_health != null && _health.HealthPercent < inkHealthFraction)
                TryTriggerInkEscape();
        }

        /** Pushes uniform wave multipliers into every tentacle — the state machine's body-language channel. */
        private void SetTentacleWave(float freq, float amp)
        {
            if (tentacles == null) return;
            for (int i = 0; i < tentacles.Length; i++)
            {
                if (tentacles[i] == null) continue;
                tentacles[i].WaveFrequencyMultiplier = freq;
                tentacles[i].WaveAmplitudeMultiplier = amp;
            }
        }

        /** Sets just the amplitude channel on every tentacle — used for the Jet-launch spike. */
        private void SetTentacleAmplitude(float amp)
        {
            if (tentacles == null) return;
            for (int i = 0; i < tentacles.Length; i++)
                if (tentacles[i] != null) tentacles[i].WaveAmplitudeMultiplier = amp;
        }

        /** Subtle idle Squash sine (Lurk/Hover) so the mantle never looks static at rest. */
        private void ApplyBreathing()
        {
            if (mantle == null) return;
            float s = 1f + Mathf.Sin(Time.time * breatheFrequency * Mathf.PI * 2f) * breatheAmplitude;
            mantle.Squash = new Vector2(s, s);
        }

        /** Drives the material flash channel through a property block (no material instancing). */
        private void SetFlash(float amount)
        {
            if (mantle == null || mantle.Renderer == null) return;
            mantle.Renderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(FlashAmountId, amount);
            mantle.Renderer.SetPropertyBlock(_mpb);
        }

        /**
         * Smoothly turns visualRoot to face the current travel direction. The root
         * Rigidbody2D itself never rotates (EnemyBase freezes it while alive) — this
         * is purely cosmetic.
         *
         * Orientation convention: squid swim backwards, mantle-first, so visualRoot's
         * local +X axis is defined as the MANTLE TIP and is pointed AWAY from velocity
         * (tentacles/arms lead the charge on the -X side). Keep prefab art oriented to
         * match: mantle mesh on the +X side of visualRoot, tentacle anchors on -X.
         */
        private void UpdateVisualRotation()
        {
            if (visualRoot == null) return;

            Vector2 vel = Rb.linearVelocity;
            if (vel.sqrMagnitude < 0.09f) return; // ~0.3 units/sec floor — hold last heading near-rest

            Vector2 mantleFacing = -vel.normalized;
            float targetAngle = Mathf.Atan2(mantleFacing.y, mantleFacing.x) * Mathf.Rad2Deg;
            Quaternion targetRotation = Quaternion.Euler(0f, 0f, targetAngle);

            float t = 1f - Mathf.Exp(-visualTurnSpeed * Time.deltaTime);
            visualRoot.rotation = Quaternion.Slerp(visualRoot.rotation, targetRotation, t);
        }

        // -------------------------------------------------------
        // Damage & death
        // -------------------------------------------------------

        private void OnCollisionEnter2D(Collision2D collision)
        {
            // Only the jet bites, once per burst, resolved against the submarine actually struck.
            if (_state != AiState.Jet || _hasHitThisJet) return;
            if (!collision.collider.CompareTag("Player")) return;

            var victim = collision.collider.GetComponentInParent<Submarine>();
            if (victim == null) return;

            _hasHitThisJet = true;
            int damage = victim.Hull != null ? victim.Hull.EvaluateAttack(jetDamage) : jetDamage;
            victim.Health?.TakeDamage(damage);
            onHitPlayer?.Invoke();

            // A successful ram is itself an ink trigger (subject to the shared cooldown);
            // otherwise settle into the normal miss recovery.
            if (!TryTriggerInkEscape()) Enter(AiState.Recover);
        }

        protected override void OnDeath()
        {
            _state = AiState.Dead;
            base.OnDeath();

            // Slacken every animation channel — the corpse drifts inert.
            SetTentacleWave(freq: 0f, amp: 0f);
            if (mantle != null) mantle.Squash = Vector2.one;
            SetFlash(0f);

            // One last weak ink puff as it goes under.
            inkParticles?.Play();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.6f, 0.2f, 1f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, detectionRadius);
            Gizmos.color = new Color(1f, 0.3f, 0.6f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, preferredRange);
        }
    }
}
