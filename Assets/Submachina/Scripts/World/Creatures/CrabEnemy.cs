using Core.ProceduralAnimation;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Events;

namespace Submachina.Core
{
    /**
     * Armored scuttler — the first walking creature, built on IKLeg +
     * LegGaitController instead of chains. It lives on terrain: the body rides a
     * height spring above whatever ground is below, the gait controller walks the
     * legs, and two dedicated claw IKLegs (driven directly by this brain, not the
     * gait) do the talking.
     *
     * Behavior loop:
     *   Settle  — no ground below: falling. Legs paddle (gait's airborne swim),
     *             the body lilts to one side, and a close player can still be
     *             lunged at mid-fall.
     *   Patrol  — grounded side-shuffle between spawn ± patrolRange, claws idling
     *             with a lazy sway.
     *   Chase   — player in range while grounded: scuttles toward them, claws
     *             raised in threat posture.
     *   WindUp  — in snap range: claws cock back and the shell flashes — the tell.
     *   Snap    — a short forward hop with claws punched toward the target;
     *             contact damage lands once during the window.
     *   Recover — claws droop, shuffle resumes after a beat.
     *
     * Cliffs: a probe ahead of travel spots drops. Whether the crab is willing to
     * leave its platform is a probabilistic roll (cliffLeapChance: 0 = never,
     * 1 = always), cached for a few seconds so he commits to a decision. When he
     * DOES go over, he never just walks off — he exits with the snap-hop lunge.
     *
     * The claws ARE the state display: idle sway → raised threat → cocked →
     * punched. No UI needed to read this creature.
     */
    public class CrabEnemy : EnemyBase
    {
        // =====================
        // Detection
        // =====================

        [FoldoutGroup("Detection")]
        [Tooltip("Player within this range switches the crab from Patrol to Chase.")]
        [SerializeField, Min(1f)] private float detectionRadius = 7f;

        [FoldoutGroup("Detection")]
        [Tooltip("Player beyond this range during Chase drops back to Patrol.")]
        [SerializeField, Min(1f)] private float loseInterestRadius = 11f;

        [FoldoutGroup("Detection")]
        [Tooltip("Range at which the crab commits to a claw snap.")]
        [SerializeField, Min(0.5f)] private float snapRange = 2.4f;

        // =====================
        // Movement
        // =====================

        [FoldoutGroup("Movement")]
        [Tooltip("Sideways shuffle speed while patrolling.")]
        [SerializeField, Min(0f)] private float scuttleSpeed = 1.5f;

        [FoldoutGroup("Movement")]
        [Tooltip("Shuffle speed while chasing.")]
        [SerializeField, Min(0f)] private float chaseSpeed = 3f;

        [FoldoutGroup("Movement")]
        [Tooltip("Horizontal patrol distance either side of the spawn point.")]
        [SerializeField, Min(0.5f)] private float patrolRange = 4f;

        [FoldoutGroup("Movement")]
        [Tooltip("Height the body rides above the ground surface.")]
        [SerializeField, Min(0.1f)] private float hoverHeight = 0.6f;

        [FoldoutGroup("Movement")]
        [Tooltip("Spring strength pulling the body toward its hover height (per second).")]
        [SerializeField, Range(0.5f, 20f)] private float heightSpring = 6f;

        [FoldoutGroup("Movement")]
        [Tooltip("Sink speed while airborne (no ground below) — crabs are not swimmers.")]
        [SerializeField, Min(0f)] private float sinkSpeed = 1.1f;

        [FoldoutGroup("Movement")]
        [Tooltip("The crab only re-aims its shuffle when the player is more than this far to one side of it. Inside the band (e.g. a sub hovering directly overhead) it holds its ground instead of dithering left-right every frame.")]
        [SerializeField, Min(0f)] private float overheadDeadzone = 0.8f;

        [FoldoutGroup("Movement")]
        [Tooltip("Layers that count as ground for the body height probe (match the gait controller's mask).")]
        [SerializeField] private LayerMask groundMask = ~0;

        [FoldoutGroup("Movement")]
        [Tooltip("How far below the body to look for ground before deciding we're airborne.")]
        [SerializeField, Min(0.5f)] private float groundProbeDepth = 2.5f;

        // =====================
        // Cliffs & Falling
        // =====================

        [FoldoutGroup("Cliffs & Falling")]
        [Tooltip("Chance the crab is willing to leave its platform when it meets an edge: 0 = never jumps off, 1 = always does, 0.25 = mostly stays but occasionally commits. When he goes, he goes with the lunge attack — never a plain walk-off.")]
        [SerializeField, Range(0f, 1f)] private float cliffLeapChance = 0.25f;

        [FoldoutGroup("Cliffs & Falling")]
        [Tooltip("When on, the leap roll only happens while chasing a player — a patrolling crab treats every edge as a wall. Off = he may also wander off ledges for no reason.")]
        [SerializeField] private bool leapOnlyWhenChasing = true;

        [FoldoutGroup("Cliffs & Falling")]
        [Tooltip("How far ahead of travel the ground probe checks for a drop.")]
        [SerializeField, Min(0.1f)] private float cliffProbeDistance = 0.9f;

        [FoldoutGroup("Cliffs & Falling")]
        [Tooltip("The edge decision is cached this long before re-rolling — so a crab held at an edge (e.g. player waiting below) occasionally changes its mind.")]
        [SerializeField, Min(0.5f)] private float cliffRerollInterval = 4f;

        [FoldoutGroup("Cliffs & Falling")]
        [Tooltip("Upward bias of the cliff-exit lunge direction — higher arcs the hop before the fall.")]
        [SerializeField, Range(0f, 1f)] private float leapUpBias = 0.45f;

        [FoldoutGroup("Cliffs & Falling")]
        [Tooltip("While falling, a player inside this range can be lunged at mid-air (snap cooldown still applies).")]
        [SerializeField, Min(0.5f)] private float airLungeRange = 3f;

        [FoldoutGroup("Cliffs & Falling")]
        [Tooltip("How far the body rolls toward the lilt side while falling, in degrees.")]
        [SerializeField, Range(0f, 30f)] private float liltAngleDegrees = 9f;

        [FoldoutGroup("Cliffs & Falling")]
        [Tooltip("Sideways drift speed while falling — the lazy 'crab swim' slide.")]
        [SerializeField, Min(0f)] private float liltDriftSpeed = 0.5f;

        // =====================
        // Snap Attack
        // =====================

        [FoldoutGroup("Snap Attack")]
        [Tooltip("Claw cock-back telegraph duration — the player's dodge window.")]
        [SerializeField, Min(0.05f)] private float windUpDuration = 0.4f;

        [FoldoutGroup("Snap Attack")]
        [Tooltip("Forward hop speed during the snap.")]
        [SerializeField, Min(0f)] private float snapHopSpeed = 6f;

        [FoldoutGroup("Snap Attack")]
        [Tooltip("Duration of the snap damage window.")]
        [SerializeField, Min(0.05f)] private float snapDuration = 0.3f;

        [FoldoutGroup("Snap Attack")]
        [Tooltip("Contact damage dealt during the snap (before hull depth-vulnerability scaling).")]
        [SerializeField, Min(0)] private int snapDamage = 3;

        [FoldoutGroup("Snap Attack")]
        [Tooltip("Recovery pause after a snap before the next decision.")]
        [SerializeField, Min(0.1f)] private float recoverDuration = 0.9f;

        [FoldoutGroup("Snap Attack")]
        [Tooltip("Minimum time between snap attempts.")]
        [SerializeField, Min(0f)] private float snapCooldown = 2f;

        // =====================
        // Animation Coupling
        // =====================

        [FoldoutGroup("Animation Coupling")]
        [Tooltip("Walking-leg gait controller. Auto-resolves from this object if empty.")]
        [SerializeField] private LegGaitController gait;

        [FoldoutGroup("Animation Coupling")]
        [Tooltip("The two claw legs (index 0 = left/-X, 1 = right/+X), driven directly by this brain — keep them OUT of the gait controller's legs list.")]
        [SerializeField] private IKLeg[] claws;

        [FoldoutGroup("Animation Coupling")]
        [Tooltip("Shell mesh used for the wind-up flash. Auto-resolves from children if empty.")]
        [SerializeField] private RadialMeshRenderer shell;

        [FoldoutGroup("Animation Coupling")]
        [Tooltip("Shell flash telegraph during the wind-up — the brightening flicker just before a snap. " +
                 "Turn off for a silent tell: the claw cock-back and the intent indicator still read the attack.")]
        [SerializeField] private bool shellFlashTelegraph = true;

        [FoldoutGroup("Animation Coupling")]
        [Tooltip("Claw rest pose relative to the body: x = outward from center, y = height.")]
        [SerializeField] private Vector2 clawRestPose = new Vector2(0.75f, 0.05f);

        [FoldoutGroup("Animation Coupling")]
        [Tooltip("How high the claws raise (added to rest y) in the Chase threat posture.")]
        [SerializeField, Min(0f)] private float clawThreatRaise = 0.35f;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired when the claw cock-back telegraph begins.")]
        public UnityEvent onWindUp;

        [FoldoutGroup("Events")]
        [Tooltip("Fired at the snap itself.")]
        public UnityEvent onSnap;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when the snap connects with a submarine.")]
        public UnityEvent onHitPlayer;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when the crab commits to leaping off a cliff edge.")]
        public UnityEvent onCliffLeap;

        // =====================
        // State
        // =====================

        private enum AiState { Settle, Patrol, Chase, WindUp, Snap, Recover, Dead }

        private AiState _state = AiState.Settle;
        private float _stateTimer;
        private float _clawPhase;
        private int _patrolDir = 1;
        private Vector2 _snapDirection;
        private bool _hasHitThisSnap;
        private float _nextSnapTime;
        private bool _grounded;
        private float _groundY;
        private int _chaseDir = 1;
        private bool _cliffWillLeap;
        private float _cliffDecisionTime = -999f;
        private bool _cliffLeap;
        private Vector2 _leapDirection;
        private int _liltDir = 1;
        private float _tilt;
        private MaterialPropertyBlock _mpb;
        private float _flashAmount = -1f; // last value pushed to the shell; -1 forces the first write
        private static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");

        protected override string CurrentState => _state.ToString();

        protected override bool CanRetarget => _state != AiState.WindUp && _state != AiState.Snap;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();
            if (gait == null) gait = GetComponent<LegGaitController>();
            if (shell == null) shell = GetComponentInChildren<RadialMeshRenderer>();
            _mpb = new MaterialPropertyBlock();
            _patrolDir = Random.value < 0.5f ? -1 : 1;
        }

        // -------------------------------------------------------
        // AI
        // -------------------------------------------------------

        protected override void UpdateAI()
        {
            _stateTimer += Time.fixedDeltaTime;
            ProbeGround();

            switch (_state)
            {
                case AiState.Settle: TickSettle(); break;
                case AiState.Patrol: TickPatrol(); break;
                case AiState.Chase: TickChase(); break;
                case AiState.WindUp: TickWindUp(); break;
                case AiState.Snap: TickSnap(); break;
                case AiState.Recover: TickRecover(); break;
            }

            UpdateClaws();
            UpdateTilt();
        }

        /** Falling: paddle-drift down, lilting to one side — and lunge at a close player mid-air. */
        private void TickSettle()
        {
            if (_grounded) { Enter(AiState.Patrol); return; }

            // Mid-fall opportunism: a player inside air-lunge range gets attacked
            // without waiting to land (normal snap cooldown gates it).
            if (Time.time >= _nextSnapTime && DistanceToPlayer() < airLungeRange)
            {
                BeginWindUp(false, Vector2.zero);
                return;
            }

            FallDrift();
        }

        /** Grounded side-shuffle between spawn ± patrolRange. */
        private void TickPatrol()
        {
            if (!_grounded) { Enter(AiState.Settle); return; }
            if (DistanceToPlayer() < detectionRadius) { Enter(AiState.Chase); return; }

            // A drop ahead: patrolling crabs only consider leaving if the wander
            // leap is enabled — by default an idle crab treats every edge as a wall.
            if (EdgeAhead(_patrolDir))
            {
                if (!leapOnlyWhenChasing && Time.time >= _nextSnapTime && WillLeapOffCliff())
                {
                    LeapOffCliff(new Vector2(_patrolDir, leapUpBias));
                    return;
                }
                _patrolDir = -_patrolDir;
            }

            // Turn around at the patrol edges (or when the shuffle stalls against a wall).
            float dx = transform.position.x - SpawnPosition.x;
            if (Mathf.Abs(dx) > patrolRange && Mathf.Sign(dx) == _patrolDir) _patrolDir = -_patrolDir;

            Move(_patrolDir * scuttleSpeed);
        }

        /** Scuttles toward the player, claws raised. */
        private void TickChase()
        {
            if (!_grounded) { Enter(AiState.Settle); return; }
            float dist = DistanceToPlayer();
            if (dist > loseInterestRadius) { Enter(AiState.Patrol); return; }

            if (dist < snapRange && Time.time >= _nextSnapTime) { BeginWindUp(false, Vector2.zero); return; }

            // Deadband steering: only re-aim when the player is meaningfully off to
            // one side, so a sub hovering directly overhead doesn't flip the shuffle
            // direction every frame (the vibrate). Inside the band: hold ground.
            float dx = Player != null ? Player.position.x - transform.position.x : 0f;
            if (Mathf.Abs(dx) > overheadDeadzone) _chaseDir = (int)Mathf.Sign(dx);
            else { Move(0f); return; }

            // A drop between us and the player: only cross it if the dice say so —
            // otherwise hold the edge in threat posture rather than shuffle into the void.
            if (EdgeAhead(_chaseDir))
            {
                if (Time.time >= _nextSnapTime && WillLeapOffCliff())
                {
                    LeapOffCliff(new Vector2(_chaseDir, leapUpBias));
                    return;
                }
                Move(0f);
                return;
            }

            Move(_chaseDir * chaseSpeed);
        }

        /** Claw cock-back: shuffle stops (or the fall continues), shell flashes toward the peak. */
        private void TickWindUp()
        {
            // A mid-air wind-up keeps falling and telegraphs faster; grounded plants and cocks.
            float duration = _grounded ? windUpDuration : windUpDuration * 0.55f;
            if (_grounded) Move(0f); else FallDrift();
            SetFlash(Mathf.Clamp01(_stateTimer / duration) * 0.5f);

            if (_stateTimer >= duration)
            {
                // Cliff exits lunge along the stored leap direction; combat snaps track the player.
                _snapDirection = _cliffLeap ? _leapDirection : DirectionToPlayer();
                Enter(AiState.Snap);
                onSnap?.Invoke();
            }
        }

        /** Short forward hop with the claws punched out — the damage window. */
        private void TickSnap()
        {
            Rb.linearVelocity = _snapDirection * snapHopSpeed;
            if (_stateTimer >= snapDuration) Enter(AiState.Recover);
        }

        /** Post-snap pause, then re-decide — or keep falling if the snap left us airborne. */
        private void TickRecover()
        {
            if (!_grounded) { Enter(AiState.Settle); return; }
            Move(0f);
            if (_stateTimer >= recoverDuration)
                Enter(DistanceToPlayer() < loseInterestRadius ? AiState.Chase : AiState.Patrol);
        }

        // -------------------------------------------------------
        // Locomotion
        // -------------------------------------------------------

        /**
         * Grounded locomotion: horizontal shuffle plus a vertical spring toward
         * hoverHeight above the probed ground — the gait controller reads the
         * resulting body motion and does the actual stepping.
         */
        private void Move(float vx)
        {
            float targetY = _groundY + hoverHeight;
            float vy = Mathf.Clamp((targetY - transform.position.y) * heightSpring, -3f, 3f);
            Rb.linearVelocity = new Vector2(vx, vy);
        }

        /** Falling motion: steady sink with an eased sideways drift toward the lilt side. */
        private void FallDrift()
        {
            float vx = Mathf.Lerp(Rb.linearVelocity.x, _liltDir * liltDriftSpeed, 1f - Mathf.Exp(-2.5f * Time.fixedDeltaTime));
            Rb.linearVelocity = new Vector2(vx, -sinkSpeed);
        }

        /** Single downward probe for the body — leg placement probes are the gait controller's. */
        private void ProbeGround()
        {
            RaycastHit2D hit = Physics2D.Raycast(transform.position, Vector2.down, hoverHeight + groundProbeDepth, groundMask);
            _grounded = hit.collider != null;
            if (_grounded) _groundY = hit.point.y;
        }

        // -------------------------------------------------------
        // Cliffs
        // -------------------------------------------------------

        /** True when the ground probe ahead of travel direction dir finds nothing — a drop is there. */
        private bool EdgeAhead(float dir)
        {
            Vector2 origin = (Vector2)transform.position + new Vector2(Mathf.Sign(dir) * cliffProbeDistance, 0f);
            return Physics2D.Raycast(origin, Vector2.down, hoverHeight + groundProbeDepth, groundMask).collider == null;
        }

        /**
         * The probabilistic edge decision, cached for cliffRerollInterval so the
         * crab commits: e.g. chance 0.25 means at any given edge encounter he
         * usually turns back, but roughly one visit in four he takes the plunge.
         */
        private bool WillLeapOffCliff()
        {
            if (Time.time - _cliffDecisionTime > cliffRerollInterval)
            {
                _cliffWillLeap = Random.value < cliffLeapChance;
                _cliffDecisionTime = Time.time;
            }
            return _cliffWillLeap;
        }

        /** Commit to leaving the platform — always via the lunge attack, never a plain walk-off. */
        private void LeapOffCliff(Vector2 dir)
        {
            onCliffLeap?.Invoke();
            BeginWindUp(true, dir.normalized);
        }

        /** Enter WindUp, recording whether this is a cliff exit (fixed direction) or a player snap. */
        private void BeginWindUp(bool cliffLeap, Vector2 leapDir)
        {
            _cliffLeap = cliffLeap;
            _leapDirection = leapDir;
            Enter(AiState.WindUp);
        }

        // -------------------------------------------------------
        // Claws — the state display
        // -------------------------------------------------------

        /** Drives both claw foot targets per state: lazy sway, threat raise, cock-back, punch, fall-paddle. */
        private void UpdateClaws()
        {
            if (claws == null) return;
            _clawPhase += Time.fixedDeltaTime * 2f;

            for (int i = 0; i < claws.Length; i++)
            {
                if (claws[i] == null) continue;
                float side = i == 0 ? -1f : 1f;
                float sway = Mathf.Sin(_clawPhase + i * 1.7f) * 0.08f;
                Vector2 local;

                switch (_state)
                {
                    case AiState.Settle:
                        // Falling: a slow breaststroke circle (opposite phases) so he reads alive, not dead.
                        float ph = _clawPhase * 1.2f + i * Mathf.PI;
                        local = new Vector2(side * (clawRestPose.x + Mathf.Cos(ph) * 0.18f),
                                            clawRestPose.y + 0.15f + Mathf.Sin(ph) * 0.18f);
                        break;
                    case AiState.Chase:
                        // Threat posture: raised and spread.
                        local = new Vector2(side * (clawRestPose.x + 0.1f), clawRestPose.y + clawThreatRaise + sway);
                        break;
                    case AiState.WindUp:
                        // Cocked: pulled in tight against the shell.
                        local = new Vector2(side * (clawRestPose.x * 0.45f), clawRestPose.y + clawThreatRaise * 0.6f);
                        break;
                    case AiState.Snap:
                        // Punched along the snap direction, both claws together.
                        local = (Vector2)transform.InverseTransformDirection(_snapDirection) * (claws[i].TotalReach * 0.95f);
                        break;
                    case AiState.Dead:
                        local = new Vector2(side * clawRestPose.x, clawRestPose.y - 0.3f);
                        break;
                    default:
                        local = new Vector2(side * clawRestPose.x, clawRestPose.y + sway);
                        break;
                }

                claws[i].FootTarget = transform.TransformPoint(local);
            }
        }

        // -------------------------------------------------------
        // Body tilt — the falling lilt
        // -------------------------------------------------------

        /** Airborne the body rolls toward the lilt side with a slow rock; grounded it eases upright. */
        private void UpdateTilt()
        {
            float target = !_grounded && _state != AiState.Dead
                ? _liltDir * (liltAngleDegrees + Mathf.Sin(Time.time * 1.6f) * 4f)
                : 0f;
            _tilt = Mathf.Lerp(_tilt, target, 1f - Mathf.Exp(-3f * Time.fixedDeltaTime));
            transform.rotation = Quaternion.Euler(0f, 0f, _tilt);
        }

        // -------------------------------------------------------
        // Transitions
        // -------------------------------------------------------

        /** Central state switch. */
        private void Enter(AiState next)
        {
            _state = next;
            _stateTimer = 0f;

            switch (next)
            {
                case AiState.Settle:
                    // Lean into whichever way we're already drifting (else a coin flip).
                    _liltDir = Mathf.Abs(Rb.linearVelocity.x) > 0.3f
                        ? (int)Mathf.Sign(Rb.linearVelocity.x)
                        : (Random.value < 0.5f ? -1 : 1);
                    break;
                case AiState.Patrol:
                    SetFlash(0f);
                    break;
                case AiState.WindUp:
                    SetIntentIndicator(true);
                    onWindUp?.Invoke();
                    break;
                case AiState.Snap:
                    SetIntentIndicator(false);
                    SetFlash(0f);
                    _hasHitThisSnap = false;
                    _cliffLeap = false;
                    _nextSnapTime = Time.time + snapCooldown;
                    break;
            }
        }

        /**
         * Shell flash via property block (no material instancing). With the
         * telegraph switched off every request collapses to 0, so the shell is
         * driven flat rather than left stuck at whatever it flashed to last.
         * Repeat values are skipped — TickWindUp calls this every fixed frame.
         */
        private void SetFlash(float amount)
        {
            if (!shellFlashTelegraph) amount = 0f;
            if (shell == null || shell.Renderer == null) return;
            if (Mathf.Approximately(_flashAmount, amount)) return;

            _flashAmount = amount;
            shell.Renderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(FlashAmountId, amount);
            shell.Renderer.SetPropertyBlock(_mpb);
        }

        /** Toggling the telegraph off mid-play clears any flash already on the shell. */
        private void OnValidate()
        {
            if (!Application.isPlaying || shellFlashTelegraph) return;
            SetFlash(0f);
        }

        // -------------------------------------------------------
        // Damage & death
        // -------------------------------------------------------

        private void OnCollisionEnter2D(Collision2D collision)
        {
            // Only the snap pinches, once per attack, resolved against the sub actually struck.
            if (_state != AiState.Snap || _hasHitThisSnap) return;
            if (!collision.collider.CompareTag("Player")) return;

            var victim = collision.collider.GetComponentInParent<Submarine>();
            if (victim == null) return;

            _hasHitThisSnap = true;
            int damage = victim.Hull != null ? victim.Hull.EvaluateAttack(snapDamage) : snapDamage;
            victim.Health?.TakeDamage(damage);
            onHitPlayer?.Invoke();

            Enter(AiState.Recover);
        }

        protected override void OnDeath()
        {
            _state = AiState.Dead;
            base.OnDeath();

            // Legs stop stepping (gait off), claws droop, corpse settles under its own weight.
            if (gait != null) gait.enabled = false;
            SetFlash(0f);
            UpdateClaws();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, detectionRadius);
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, snapRange);

            // Cliff probes: the two rays that decide "drop ahead".
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.6f);
            foreach (float d in new[] { -1f, 1f })
            {
                Vector3 o = transform.position + new Vector3(d * cliffProbeDistance, 0f, 0f);
                Gizmos.DrawLine(o, o + Vector3.down * (hoverHeight + groundProbeDepth));
            }
        }
    }
}
