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
     * Movement is two independent, experiment-friendly layers:
     *
     *   Posture (Posture foldout) — how the body orients:
     *     FixedPosture — the body holds neutralPostureAngle no matter which way it
     *                    travels; only the pulse-synced bob (cosmetic vertical
     *                    offset on visualRoot) and sway (rotational wobble) move it.
     *     FaceTravel   — the head (headAngle defines which local direction that is)
     *                    steers to point along the travel intent, like an airplane.
     *
     *   Propulsion (Propulsion foldout) — how velocity is produced. propulsionBlend
     *   tweens continuously between the two strategies (the PropulsionBlend
     *   property is exposed for runtime tweening/DOTween experiments):
     *     0 = Glide — plain eased acceleration toward the desired velocity, with
     *         continuous steering.
     *     1 = Pulse — a four-beat swim cycle on a single clock (pulseFrequency):
     *         gather (mantle inflates, tentacles still and physically pull in via
     *         ChainSimulator.LengthMultiplier) → pause (holds the loaded pose) →
     *         burst (the kick applied as real acceleration while the mantle
     *         squeezes and the arms whip back out; onPulse fires) → coast (drag).
     *         Steering is gated to chosen beats (steerDuring) — aim on the gather,
     *         commit on the push. Bob and sway ride the same clock so the whole
     *         body moves on one rhythm.
     *
     * Behavior loop (unchanged):
     *   Lurk → Hover → Aim (telegraph + chromatophore flicker) → Jet (locked ram)
     *   → Recover, with InkEscape as the panic response. Jet/InkEscape set velocity
     *   directly and can aim the body fully along the burst (burstsFaceTravel).
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
        [Tooltip("Steering responsiveness (per second) of the glide component and the posture intent. Higher = snappier; lower = big flowing arcs.")]
        [SerializeField, Range(0.5f, 20f)] private float steering = 5f;

        // =====================
        // Posture
        // =====================

        public enum PostureMode
        {
            /** Hold neutralPostureAngle regardless of travel direction — only bob/sway move the body. */
            FixedPosture,
            /** Point the head (headAngle) along the travel intent — steers like an airplane. */
            FaceTravel,
        }

        [FoldoutGroup("Posture")]
        [Tooltip("FixedPosture: hold the neutral angle no matter where it swims (bob/sway only). FaceTravel: steer the head along the travel direction like an airplane.")]
        [SerializeField, EnumToggleButtons] private PostureMode postureMode = PostureMode.FixedPosture;

        [FoldoutGroup("Posture")]
        [Tooltip("visualRoot Z rotation (degrees) of the resting/held posture. 0 = your art's authored upright pose.")]
        [SerializeField, Range(-180f, 180f)] private float neutralPostureAngle = 0f;

        [FoldoutGroup("Posture")]
        [Tooltip("Which local direction is the HEAD when visualRoot is unrotated, in degrees (0 = +X/right, 90 = +Y/up). FaceTravel aims this axis along travel. If your art is authored head-up, leave at 90.")]
        [SerializeField, Range(-180f, 180f)] private float headAngle = 90f;

        [FoldoutGroup("Posture")]
        [Tooltip("Aim the body fully along the actual velocity during Jet and InkEscape bursts, even in FixedPosture mode. FaceTravel bursts always aim along the motion.")]
        [SerializeField] private bool burstsFaceTravel = true;

        [FoldoutGroup("Posture")]
        [Tooltip("How fast visualRoot rotates toward its posture target (per second).")]
        [SerializeField, Range(0.5f, 20f)] private float visualTurnSpeed = 6f;

        [FoldoutGroup("Posture")]
        [Tooltip("Rotational sway (± degrees) riding the pulse clock while swimming — the side-to-side body wobble. 0 = off.")]
        [SerializeField, Range(0f, 45f)] private float swayDegrees = 8f;

        [FoldoutGroup("Posture")]
        [Tooltip("Vertical bob amplitude (world units) riding the pulse clock while swimming — a cosmetic offset on visualRoot, so it never fights the physics or the posture. 0 = off.")]
        [SerializeField, Min(0f)] private float bobAmplitude = 0.15f;

        // =====================
        // Propulsion
        // =====================

        [FoldoutGroup("Propulsion")]
        [Tooltip("Tween between propulsion styles: 0 = pure glide (smooth eased acceleration), 1 = pure pulse (rhythmic swim kicks + coast drag). Mid values mix both.")]
        [SerializeField, Range(0f, 1f)] private float propulsionBlend = 1f;

        [FoldoutGroup("Propulsion")]
        [Tooltip("Swim pulses per second — the ONE rhythm clock. Kicks, tentacle retract/push, mantle inflate/squeeze, bob and sway all ride this frequency.")]
        [SerializeField, Min(0.05f)] private float pulseFrequency = 0.9f;

        [FoldoutGroup("Propulsion")]
        [Tooltip("Fraction of each cycle spent GATHERING — mantle inflates, tentacles still their wave and physically pull in toward the body.")]
        [SerializeField, Range(0.05f, 0.9f)] private float gatherFraction = 0.35f;

        [FoldoutGroup("Propulsion")]
        [Tooltip("Fraction of each cycle spent PAUSED between gather and burst — holding the loaded pose for a beat of stillness before the push. 0 = none.")]
        [SerializeField, Range(0f, 0.5f)] private float pauseFraction = 0.1f;

        [FoldoutGroup("Propulsion")]
        [Tooltip("Fraction of each cycle spent BURSTING — the kick is applied as real acceleration across this whole beat while the mantle squeezes and the tentacles whip back out. The remainder of the cycle is the coast. (If gather+pause+burst exceed 1 they are squeezed proportionally.)")]
        [SerializeField, Range(0.05f, 0.9f)] private float burstFraction = 0.2f;

        [FoldoutGroup("Propulsion")]
        [Tooltip("Which beats of the cycle steering stays live in at full pulse — Gather-only means the squid aims during the wind-up, then commits to the push like a locked trajectory. Pure glide (blend 0) always steers continuously; mid blends mix the two.")]
        [SerializeField, EnumToggleButtons] private PulseBeatMask steerDuring = PulseBeatMask.Gather;

        [FoldoutGroup("Propulsion")]
        [Tooltip("Total velocity gained across each burst beat, as a multiple of the aim speed. >1 makes each pulse surge past the target speed, then coastDrag bleeds it back — averaging near the desired speed.")]
        [SerializeField, Min(0f)] private float pulseKick = 1.8f;

        [FoldoutGroup("Propulsion")]
        [Tooltip("Exponential drag (per second) faded in with the blend. Higher = each kick dies faster and the motion reads more staccato.")]
        [SerializeField, Min(0f)] private float coastDrag = 1.2f;

        [FoldoutGroup("Propulsion")]
        [Tooltip("Mantle Squash reached by the end of the gather beat and held through the pause — inflated, loaded up.")]
        [SerializeField] private Vector2 pulseGatherSquash = new Vector2(1.12f, 0.9f);

        [FoldoutGroup("Propulsion")]
        [Tooltip("Mantle Squash reached by the end of the burst beat — the squeeze that fires the pulse. Relaxes back to neutral across the coast.")]
        [SerializeField] private Vector2 pulseBurstSquash = new Vector2(0.78f, 1.18f);

        [FoldoutGroup("Propulsion")]
        [Tooltip("How tightly the mantle tracks the pulse squash curve (per second). The curve itself is already smooth — this mostly softens handoffs when states change mid-cycle. High = crisp beats, low = dreamy lag.")]
        [SerializeField, Range(1f, 30f)] private float pulseSquashSharpness = 14f;

        [FoldoutGroup("Propulsion")]
        [Tooltip("Fraction of tentacle LENGTH physically pulled in toward the body by the end of the gather — the real retract, via ChainSimulator.LengthMultiplier. 0 = off, 0.5 = arms drawn to half length; the burst shoves them back out.")]
        [SerializeField, Range(0f, 0.9f)] private float pulseTentacleLengthRetract = 0.5f;

        [FoldoutGroup("Propulsion")]
        [Tooltip("Tentacle wave-amplitude multiplier (relative to the state baseline) at the end of the gather — stills the undulation while drawn in. A subtle layer next to the length retract above.")]
        [SerializeField, Range(0f, 1f)] private float pulseTentacleRetract = 0.35f;

        [FoldoutGroup("Propulsion")]
        [Tooltip("Tentacle wave-amplitude multiplier (relative to the state baseline) at the end of the burst — the wave whip of the push-out. Trails home across the coast.")]
        [SerializeField, Min(1f)] private float pulseTentacleBurst = 2.2f;

        // =====================
        // Tentacle Reach
        // =====================

        [FoldoutGroup("Tentacle Reach")]
        [Tooltip("Peak drift force (world units/sec at the tip) throwing the tentacles toward the reach target during the enabled beats. 0 = off. Lives alongside the length retract — combine or alternate them freely. NOT scaled by the propulsion blend, so it can be explored in glide mode too.")]
        [SerializeField, Min(0f)] private float reachStrength = 5f;

        [FoldoutGroup("Tentacle Reach")]
        [Tooltip("Beats of the pulse cycle the reach force is live in. Burst = thrown with the push (classic strike-reach); Gather instead reaches during the wind-up; combine for a sustained grasp.")]
        [SerializeField, EnumToggleButtons] private PulseBeatMask reachDuring = PulseBeatMask.Burst;

        [FoldoutGroup("Tentacle Reach")]
        [Tooltip("What the tentacles reach toward — the travel aim, or straight at the player (falls back to aim while lurking).")]
        [SerializeField, EnumToggleButtons] private ReachTarget reachTarget = ReachTarget.Player;

        [FoldoutGroup("Tentacle Reach")]
        [Tooltip("Envelope within each active beat: 0 = flat sustained push for the whole beat, 1 = a sharp flick at the beat's start that fully decays by its end.")]
        [SerializeField, Range(0f, 1f)] private float reachFlick = 0.6f;

        [FoldoutGroup("Tentacle Reach")]
        [Tooltip("Tip weighting exponent along each tentacle (ChainSimulator.ExternalForceTipPower). 1 = linear head→tip ramp; 2-3 concentrates the throw in the tips so the body trails behind — more 'reaching fingers'.")]
        [SerializeField, Range(0.5f, 4f)] private float reachTipBias = 2f;

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
        [Tooltip("How fast the mantle Squash eases toward its current target (per second) — shared by the pulse cycle, Recover, and OnDeath.")]
        [SerializeField, Range(0.5f, 20f)] private float squashEaseSpeed = 6f;

        [FoldoutGroup("Mantle")]
        [Tooltip("Master switch for the built-in chromatophore _FlashAmount flicker during Aim — turn off to drive your own telegraph effects via onAim/onAimCharging events instead.")]
        [SerializeField] private bool enableAimFlicker = true;

        [FoldoutGroup("Mantle")]
        [Tooltip("Chromatophore flicker speed (Perlin noise sample rate) during Aim.")]
        [SerializeField, Min(0f)] private float flickerFrequency = 14f;

        [FoldoutGroup("Mantle")]
        [Tooltip("Peak _FlashAmount the chromatophore flicker ramps toward as the Aim telegraph completes.")]
        [SerializeField, Range(0f, 1f)] private float flickerPeak = 0.6f;

        [FoldoutGroup("Mantle")]
        [Tooltip("Idle breathing Squash sine amplitude — what the mantle does when the propulsion blend sits near glide (it crossfades into the pulse inflate/squeeze as the blend rises).")]
        [SerializeField, Range(0f, 0.2f)] private float breatheAmplitude = 0.04f;

        [FoldoutGroup("Mantle")]
        [Tooltip("Idle breathing cycles per second.")]
        [SerializeField, Min(0f)] private float breatheFrequency = 0.5f;

        // =====================
        // Tentacles
        // =====================

        [FoldoutGroup("Tentacles")]
        [Tooltip("Peak WaveAmplitudeMultiplier pushed into every tentacle the instant the attack jet fires.")]
        [SerializeField, Min(1f)] private float jetTentacleSpike = 2.5f;

        [FoldoutGroup("Tentacles")]
        [Tooltip("How fast tentacle amplitude eases toward its current target (per second) — shared by the jet-spike trail-off and the pulse retract/push cycle.")]
        [SerializeField, Range(0.5f, 20f)] private float tentacleEaseSpeed = 4f;

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
        [Tooltip("Child holding the mantle + tentacle anchors. Rotated/bobbed by the posture layer each frame; the root Rigidbody2D itself never rotates.")]
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
        [Tooltip("Fired at the instant of every swim pulse kick — wire bubble puffs / subtle swim audio here.")]
        public UnityEvent onPulse;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when the Aim telegraph begins — wire feedbacks/audio here.")]
        public UnityEvent onAim;

        [FoldoutGroup("Events")]
        [Tooltip("Fired every AI tick during the Aim telegraph with normalized progress 0→1 — drive custom charge-up effects (light intensity, MMF players, audio pitch) here.")]
        public UnityEvent<float> onAimCharging;

        [FoldoutGroup("Events")]
        [Tooltip("Fired at the moment the jet fires.")]
        public UnityEvent onJet;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when the jet burst ends for any reason — burnt out, connected with the player, or interrupted by InkEscape.")]
        public UnityEvent onJetComplete;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when InkEscape triggers.")]
        public UnityEvent onInk;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when a jet connects with a submarine.")]
        public UnityEvent onHitPlayer;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when post-jet recovery finishes and the squid settles back into Hover — good for resetting any wired-up telegraph effects.")]
        public UnityEvent onRecovered;

        // =====================
        // State
        // =====================

        private enum AiState { Lurk, Hover, Aim, Jet, Recover, InkEscape, Dead }

        /** Beats of the swim-pulse cycle, in order. Coast is the remainder after gather+pause+burst. */
        private enum PulseBeat { Gather, Pause, Burst, Coast }

        /**
         * Beat mask for gating actions to parts of the pulse cycle — the shared idiom
         * for the growing per-beat action system (steering, tentacle reach, ...).
         * Flags, combine freely.
         */
        [System.Flags]
        public enum PulseBeatMask { Gather = 1, Pause = 2, Burst = 4, Coast = 8 }

        /** What the tentacle reach gesture is thrown toward. */
        public enum ReachTarget
        {
            /** The current travel aim — reaches where the squid is going. */
            Aim,
            /** Straight at the engaged submarine regardless of travel — the menacing option. Falls back to Aim while lurking (nobody in sensory range). */
            Player,
        }

        private AiState _state = AiState.Lurk;
        private float _stateTimer;
        private float _strafePhase;
        private float _pulsePhase;          // 0..1 swim-rhythm clock: [0, gatherFraction) = gather, kick fires at gatherFraction, rest = coast
        private float _bobWeight;           // eased 0..1 — fades the cosmetic bob in while swimming, out otherwise
        private float _flickerSeed;
        private Vector2 _lurkTarget;
        private Vector2 _jetDirection;
        private Vector2 _fleeDirection;
        private Vector2 _steeringIntent;    // smoothed travel intent — what FaceTravel posture faces; kicks/drag never touch it
        private Vector3 _visualBasePos;     // authored visualRoot local position the bob offsets from
        private float _tentacleBaseAmp = 1f;    // per-state baseline the pulse retract/push multiplies
        private bool _hasHitThisJet;
        private float _nextAttackTime;
        private float _nextInkReadyTime;
        private Health _health;
        private MaterialPropertyBlock _mpb;
        private static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");

        protected override string CurrentState => _state.ToString();

        // Lock the target mid-commitment so the telegraph/jet finishes on the telegraphed victim.
        protected override bool CanRetarget => _state != AiState.Aim && _state != AiState.Jet;

        /** Runtime access for tweening glide↔pulse (e.g. DOTween.To on this property). */
        public float PropulsionBlend { get => propulsionBlend; set => propulsionBlend = Mathf.Clamp01(value); }

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();

            // Posture is dead without a visual root — auto-resolve the conventional
            // "Visual" child and complain loudly rather than failing silently.
            if (visualRoot == null) visualRoot = transform.Find("Visual");
            if (visualRoot == null) Debug.LogWarning($"{name}: SquidEnemy has no visualRoot assigned (and no child named 'Visual') — posture, bob and sway will not run.", this);
            else _visualBasePos = visualRoot.localPosition;

            if (mantle == null) mantle = GetComponentInChildren<RadialMeshRenderer>();
            if (tentacles == null || tentacles.Length == 0) tentacles = GetComponentsInChildren<ChainSimulator>();
            _mpb = new MaterialPropertyBlock();
            _flickerSeed = Random.value * 1000f;
            _lurkTarget = transform.position;

            // Random clock phases so multiple squids don't pulse/sway in lockstep.
            _strafePhase = Random.value * Mathf.PI * 2f;
            _pulsePhase = Random.value;
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
            UpdateVisualPosture();
        }

        // -------------------------------------------------------
        // AI
        // -------------------------------------------------------

        protected override void UpdateAI()
        {
            _stateTimer += Time.fixedDeltaTime;
            _strafePhase += Time.fixedDeltaTime * strafeFrequency * Mathf.PI * 2f;

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

            Swim((_lurkTarget - (Vector2)transform.position).normalized * lurkSpeed);
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
            Vector2 side = new Vector2(-toPlayer.y, toPlayer.x);
            desired += side * (Mathf.Sin(_strafePhase) * strafeAmplitude);

            Swim(desired);
        }

        /** Telegraph: velocity bleeds to zero, mantle inflates, chromatophores flicker toward the peak. */
        private void TickAim()
        {
            BleedToStop();

            float t = Mathf.Clamp01(_stateTimer / aimDuration);
            if (mantle != null) mantle.Squash = Vector2.Lerp(Vector2.one, aimSquash, t);
            onAimCharging?.Invoke(t);

            // Fast Perlin flicker envelope-ramped from 0 up to the peak as the telegraph builds.
            // e.g. flickerFrequency 14 samples the noise fast enough to read as a jittery
            // chromatophore shimmer rather than a smooth fade. Optional — turn off Enable Aim
            // Flicker to drive custom telegraph effects through onAimCharging instead.
            if (enableAimFlicker)
            {
                float flicker = Mathf.PerlinNoise(Time.time * flickerFrequency, _flickerSeed) * flickerPeak * t;
                SetFlash(flicker);
            }

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
            EaseTentacleAmplitude(_tentacleBaseAmp);
            if (_stateTimer >= jetDuration) Enter(AiState.Recover);
        }

        /** Loose drift after a miss, easing the mantle back toward neutral, then back to Hover. */
        private void TickRecover()
        {
            Rb.linearVelocity *= 0.92f;
            _steeringIntent *= 0.92f; // intent decays with the drift so posture settles toward neutral too
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

        // -------------------------------------------------------
        // Propulsion
        // -------------------------------------------------------

        /**
         * Shared swim propulsion for Lurk/Hover — realizes the state's desired
         * velocity through the glide↔pulse blend:
         *   glide component — eases the Rigidbody toward the desired velocity,
         *                     its gain faded OUT as the blend rises;
         *   pulse component — a four-beat cycle (gather → pause → burst → coast)
         *                     where the kick is applied as real acceleration across
         *                     the burst beat, and coast drag bleeds it back off.
         * Steering (the aim the kick pushes along, and what posture faces) is
         * phase-gated by steerDuring at full pulse — e.g. Gather-only means the
         * squid aims during the wind-up, then commits.
         */
        private void Swim(Vector2 desiredVelocity)
        {
            float dt = Time.fixedDeltaTime;

            // Advance the rhythm clock and resolve which beat of the cycle we're in.
            float prevPhase = _pulsePhase;
            _pulsePhase += dt * pulseFrequency;
            if (_pulsePhase >= 1f) _pulsePhase -= 1f;
            PulseBeat beat = ResolveBeat(_pulsePhase, out float beatT);

            // onPulse marks the burst's rising edge — the moment the push begins.
            if (beat == PulseBeat.Burst && ResolveBeat(prevPhase, out float _prevT) != PulseBeat.Burst && propulsionBlend > 0f)
                onPulse?.Invoke();

            // Steering gate: glide steers continuously; full pulse only updates its
            // aim during the beats enabled in steerDuring, freezing it otherwise.
            bool steerNow = (steerDuring & ToBeatMask(beat)) != 0;
            float gate = Mathf.Lerp(1f, steerNow ? 1f : 0f, propulsionBlend);
            float intentT = 1f - Mathf.Exp(-steering * gate * dt);
            _steeringIntent = Vector2.Lerp(_steeringIntent, desiredVelocity, intentT);

            // Glide: eased acceleration toward the target, weakening as blend → 1.
            float glideT = 1f - Mathf.Exp(-steering * (1f - propulsionBlend) * dt);
            Rb.linearVelocity = Vector2.Lerp(Rb.linearVelocity, desiredVelocity, glideT);

            // Pulse thrust: the kick spread evenly across the burst beat as real
            // acceleration along the (possibly frozen) aim — e.g. hoverSpeed 2.5 ×
            // pulseKick 1.8 gains ~4.5 u/s over the burst, then drag bleeds it off.
            if (beat == PulseBeat.Burst && propulsionBlend > 0f)
            {
                float aimSpeed = _steeringIntent.magnitude;
                GetBeatFractions(out float _g, out float _p, out float b);
                float burstDuration = b / pulseFrequency;
                if (aimSpeed > 0.05f && burstDuration > 0f)
                    Rb.linearVelocity += _steeringIntent.normalized * (aimSpeed * pulseKick * propulsionBlend * dt / burstDuration);
            }

            // Pulse coast: exponential drag bleeds each kick off, strengthening as blend → 1.
            Rb.linearVelocity *= Mathf.Exp(-coastDrag * propulsionBlend * dt);

            ApplySwimBodyLanguage(beat, beatT);
            ApplyTentacleReach(beat, beatT);
        }

        /**
         * Normalized beat fractions — squeezed proportionally if gather+pause+burst
         * exceed the whole cycle, so the math never breaks while sliders are dragged.
         */
        private void GetBeatFractions(out float g, out float p, out float b)
        {
            g = gatherFraction; p = pauseFraction; b = burstFraction;
            float total = g + p + b;
            if (total > 1f) { g /= total; p /= total; b /= total; }
        }

        /** Maps a clock value (0..1) to its beat plus normalized progress 0→1 within that beat. */
        private PulseBeat ResolveBeat(float phase, out float beatT)
        {
            GetBeatFractions(out float g, out float p, out float b);
            if (phase < g) { beatT = phase / g; return PulseBeat.Gather; }
            phase -= g;
            if (phase < p) { beatT = phase / p; return PulseBeat.Pause; }
            phase -= p;
            if (phase < b) { beatT = phase / b; return PulseBeat.Burst; }
            phase -= b;
            float coast = 1f - g - p - b;
            beatT = coast > 0f ? phase / coast : 1f;
            return PulseBeat.Coast;
        }

        /** PulseBeatMask flag for a beat — the enums share ordering, so it's a bit shift. */
        private static PulseBeatMask ToBeatMask(PulseBeat beat) => (PulseBeatMask)(1 << (int)beat);

        /**
         * Drives mantle squash, tentacle wave, and tentacle length from the pulse
         * clock as one continuous piecewise curve — every beat smoothsteps from
         * where the previous beat ended, so nothing ever snaps:
         *   gather — neutral → loaded (mantle inflates, arms still and pull in)
         *   pause  — hold the loaded pose
         *   burst  — loaded → fired (mantle squeezes, arms whip and re-extend)
         *   coast  — fired → neutral
         * All modulation is scaled by propulsionBlend and crossfaded with the idle
         * breathing, so gliding squids keep the old soft look.
         */
        private void ApplySwimBodyLanguage(PulseBeat beat, float beatT)
        {
            float dt = Time.fixedDeltaTime;
            float ease = Mathf.SmoothStep(0f, 1f, beatT);

            // Mantle: piecewise squash curve, crossfaded with breathing by the blend.
            if (mantle != null)
            {
                Vector2 pulseSquash;
                switch (beat)
                {
                    case PulseBeat.Gather: pulseSquash = Vector2.Lerp(Vector2.one, pulseGatherSquash, ease); break;
                    case PulseBeat.Pause: pulseSquash = pulseGatherSquash; break;
                    case PulseBeat.Burst: pulseSquash = Vector2.Lerp(pulseGatherSquash, pulseBurstSquash, ease); break;
                    default: pulseSquash = Vector2.Lerp(pulseBurstSquash, Vector2.one, ease); break;
                }

                float s = 1f + Mathf.Sin(Time.time * breatheFrequency * Mathf.PI * 2f) * breatheAmplitude;
                Vector2 target = Vector2.Lerp(new Vector2(s, s), pulseSquash, propulsionBlend);
                mantle.Squash = Vector2.Lerp(mantle.Squash, target, 1f - Mathf.Exp(-pulseSquashSharpness * dt));
            }

            // Tentacle wave amplitude: stilled through the gather, whipping on the burst, home across the coast.
            float wave;
            switch (beat)
            {
                case PulseBeat.Gather: wave = Mathf.Lerp(1f, pulseTentacleRetract, ease); break;
                case PulseBeat.Pause: wave = pulseTentacleRetract; break;
                case PulseBeat.Burst: wave = Mathf.Lerp(pulseTentacleRetract, pulseTentacleBurst, ease); break;
                default: wave = Mathf.Lerp(pulseTentacleBurst, 1f, ease); break;
            }
            EaseTentacleAmplitude(_tentacleBaseAmp * Mathf.Lerp(1f, wave, propulsionBlend));

            // Tentacle length: the REAL pull-in. Segment lengths shrink through the
            // gather and shove back out across the burst — the chain solver drags the
            // points, so the re-extension IS the visible push of each kick.
            float retracted = 1f - pulseTentacleLengthRetract;
            float length;
            switch (beat)
            {
                case PulseBeat.Gather: length = Mathf.Lerp(1f, retracted, ease); break;
                case PulseBeat.Pause: length = retracted; break;
                case PulseBeat.Burst: length = Mathf.Lerp(retracted, 1f, ease); break;
                default: length = 1f; break;
            }
            SetTentacleLength(Mathf.Lerp(1f, length, propulsionBlend));
        }

        /**
         * Throws the tentacle tips toward the reach target during the enabled beats —
         * a tip-weighted drift force fed into each ChainSimulator, which its constraint
         * solve turns into a physical reach: tips lead, bodies trail after them.
         * Deliberately NOT scaled by propulsionBlend (reachStrength is the master
         * knob), so the gesture can be explored in glide mode too.
         */
        private void ApplyTentacleReach(PulseBeat beat, float beatT)
        {
            Vector2 force = Vector2.zero;

            if (reachStrength > 0f && (reachDuring & ToBeatMask(beat)) != 0)
            {
                // Flick envelope: flat sustained hold blended toward a sharp spike that
                // decays across the beat — e.g. reachFlick 1 is dead by the beat's end.
                float envelope = Mathf.Lerp(1f, (1f - beatT) * (1f - beatT), reachFlick);

                // Player-directed reach only makes sense while engaged; lurking falls back to the aim.
                bool atPlayer = reachTarget == ReachTarget.Player && _state != AiState.Lurk;
                Vector2 dir = atPlayer ? DirectionToPlayer()
                    : _steeringIntent.sqrMagnitude > 0.01f ? _steeringIntent.normalized : Vector2.zero;

                force = dir * (reachStrength * envelope);
            }

            SetTentacleReachForce(force);
        }

        /** Eases the Rigidbody and the posture intent to a stop — used by the Aim telegraph. */
        private void BleedToStop()
        {
            float t = 1f - Mathf.Exp(-steering * Time.fixedDeltaTime);
            _steeringIntent = Vector2.Lerp(_steeringIntent, Vector2.zero, t);
            Rb.linearVelocity = Vector2.Lerp(Rb.linearVelocity, Vector2.zero, t);
        }

        // -------------------------------------------------------
        // Transitions & animation coupling
        // -------------------------------------------------------

        /** Central state switch — resets timers and retunes mantle/tentacle body language per state. */
        private void Enter(AiState next)
        {
            var prev = _state;
            _state = next;
            _stateTimer = 0f;

            // Phase-exit events for wiring: the jet ending (for any reason) and recovery completing.
            if (prev == AiState.Jet && next != AiState.Jet) onJetComplete?.Invoke();
            if (prev == AiState.Recover && next == AiState.Hover) onRecovered?.Invoke();

            switch (next)
            {
                case AiState.Lurk:
                    NotifyPursuitEnd(); // gave up the chase (or never started one)
                    SetTentacleWave(freq: 0.8f, amp: 0.8f);
                    SetFlash(0f);
                    _lurkTarget = (Vector2)SpawnPosition + Random.insideUnitCircle * lurkRadius;
                    break;

                case AiState.Hover:
                    NotifyPursuitStart(); // no-op on re-entry from Recover/InkEscape while already engaged
                    SetTentacleWave(freq: 1.2f, amp: 1f);
                    SetFlash(0f);
                    if (mantle != null) mantle.Squash = Vector2.one;
                    break;

                case AiState.Aim:
                    SetTentacleWave(freq: 1.8f, amp: 1.3f); // tension builds through the telegraph
                    ResetTentaclePulseChannels(); // release any mid-gather retract/reach — Aim has its own body language
                    SetIntentIndicator(true);
                    onAim?.Invoke();
                    break;

                case AiState.Jet:
                    SetIntentIndicator(false);
                    if (mantle != null) mantle.Squash = jetSquash;
                    SetTentacleAmplitude(jetTentacleSpike);
                    ResetTentaclePulseChannels();
                    _hasHitThisJet = false;
                    _nextAttackTime = Time.time + attackCooldown;
                    break;

                case AiState.Recover:
                    SetTentacleWave(freq: 0.9f, amp: 1.4f); // loose, over-wobbly = spent
                    ResetTentaclePulseChannels();
                    break;

                case AiState.InkEscape:
                    _fleeDirection = DirectionAwayFromPlayer();
                    if (mantle != null) mantle.Squash = jetSquash;
                    SetTentacleWave(freq: 2f, amp: 1.5f); // frantic
                    ResetTentaclePulseChannels();
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

        /**
         * Sets the per-state tentacle baseline — the state machine's body-language
         * channel. The pulse cycle's retract/push modulates RELATIVE to this
         * baseline, so state moods and swim rhythm compose instead of fighting.
         */
        private void SetTentacleWave(float freq, float amp)
        {
            _tentacleBaseAmp = amp;
            if (tentacles == null) return;
            for (int i = 0; i < tentacles.Length; i++)
            {
                if (tentacles[i] == null) continue;
                tentacles[i].WaveFrequencyMultiplier = freq;
                tentacles[i].WaveAmplitudeMultiplier = amp;
            }
        }

        /** Pushes a segment-length multiplier into every tentacle — the physical retract channel. */
        private void SetTentacleLength(float multiplier)
        {
            if (tentacles == null) return;
            for (int i = 0; i < tentacles.Length; i++)
                if (tentacles[i] != null) tentacles[i].LengthMultiplier = multiplier;
        }

        /** Pushes the reach force + tip bias into every tentacle — the gesture channel. */
        private void SetTentacleReachForce(Vector2 force)
        {
            if (tentacles == null) return;
            for (int i = 0; i < tentacles.Length; i++)
            {
                if (tentacles[i] == null) continue;
                tentacles[i].ExternalForce = force;
                tentacles[i].ExternalForceTipPower = reachTipBias;
            }
        }

        /** Releases every pulse-driven tentacle channel (length retract, reach force) — called when leaving the swim states, whose per-tick curves would otherwise leave a mid-gesture pose frozen. */
        private void ResetTentaclePulseChannels()
        {
            SetTentacleLength(1f);
            SetTentacleReachForce(Vector2.zero);
        }

        /** Snaps the amplitude channel on every tentacle — used for kick push-outs and the jet-launch spike. */
        private void SetTentacleAmplitude(float amp)
        {
            if (tentacles == null) return;
            for (int i = 0; i < tentacles.Length; i++)
                if (tentacles[i] != null) tentacles[i].WaveAmplitudeMultiplier = amp;
        }

        /** Eases every tentacle's amplitude toward a target at tentacleEaseSpeed — snapped spikes decay through this. */
        private void EaseTentacleAmplitude(float target)
        {
            if (tentacles == null) return;
            float t = 1f - Mathf.Exp(-tentacleEaseSpeed * Time.fixedDeltaTime);
            for (int i = 0; i < tentacles.Length; i++)
            {
                if (tentacles[i] == null) continue;
                tentacles[i].WaveAmplitudeMultiplier = Mathf.Lerp(tentacles[i].WaveAmplitudeMultiplier, target, t);
            }
        }

        /** Drives the material flash channel through a property block (no material instancing). */
        private void SetFlash(float amount)
        {
            if (mantle == null || mantle.Renderer == null) return;
            mantle.Renderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(FlashAmountId, amount);
            mantle.Renderer.SetPropertyBlock(_mpb);
        }

        // -------------------------------------------------------
        // Posture
        // -------------------------------------------------------

        /**
         * Posture driver — rotation and the cosmetic bob offset on visualRoot.
         * FixedPosture holds neutralPostureAngle (plus pulse-synced sway);
         * FaceTravel points the head (headAngle) along the smoothed travel intent.
         * Jet/InkEscape bursts aim fully along the actual velocity when
         * burstsFaceTravel allows. The root Rigidbody2D itself never rotates
         * (EnemyBase freezes it while alive) — purely cosmetic.
         *
         * Angle convention: headAngle is the world angle the head points at when
         * visualRoot is unrotated (art authored head-up → 90). To face travel angle
         * A, the required Z rotation is A - headAngle.
         */
        private void UpdateVisualPosture()
        {
            if (visualRoot == null) return;

            bool swimming = _state == AiState.Lurk || _state == AiState.Hover;

            // Cosmetic vertical bob riding the pulse clock, faded out when not swimming.
            // Applied to visualRoot's local position so it never fights physics or posture.
            _bobWeight = Mathf.Lerp(_bobWeight, swimming ? 1f : 0f, 1f - Mathf.Exp(-3f * Time.deltaTime));
            visualRoot.localPosition = _visualBasePos + Vector3.up * (Mathf.Sin(_pulsePhase * Mathf.PI * 2f) * bobAmplitude * _bobWeight);

            // Pulse-synced rotational sway, only while actually swimming.
            float sway = swimming ? Mathf.Sin(_pulsePhase * Mathf.PI * 2f) * swayDegrees : 0f;

            // Committed bursts read best aimed dead along the actual motion.
            bool burst = _state == AiState.Jet || _state == AiState.InkEscape;
            if (burst && (burstsFaceTravel || postureMode == PostureMode.FaceTravel))
            {
                Vector2 vel = Rb.linearVelocity;
                if (vel.sqrMagnitude > 0.09f)
                {
                    RotateToward(Mathf.Atan2(vel.y, vel.x) * Mathf.Rad2Deg - headAngle, visualTurnSpeed);
                    return;
                }
            }

            // FaceTravel: head along the smoothed intent (falls through to neutral near rest).
            if (postureMode == PostureMode.FaceTravel && _steeringIntent.sqrMagnitude > 0.09f)
            {
                float travelAngle = Mathf.Atan2(_steeringIntent.y, _steeringIntent.x) * Mathf.Rad2Deg;
                RotateToward(travelAngle - headAngle + sway, visualTurnSpeed);
                return;
            }

            // FixedPosture (and FaceTravel at rest): hold the neutral posture, swaying on the rhythm.
            RotateToward(neutralPostureAngle + sway, visualTurnSpeed);
        }

        /** Exponentially eases visualRoot's Z rotation toward a target angle along the shortest arc. */
        private void RotateToward(float targetAngle, float speed)
        {
            float t = 1f - Mathf.Exp(-speed * Time.deltaTime);
            float next = Mathf.LerpAngle(visualRoot.eulerAngles.z, targetAngle, t);
            visualRoot.rotation = Quaternion.Euler(0f, 0f, next);
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

            // Slacken every animation channel — the corpse drifts inert at full length.
            SetTentacleWave(freq: 0f, amp: 0f);
            ResetTentaclePulseChannels();
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
