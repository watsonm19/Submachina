using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /** Player-issued order that overrides the companion's default AI behaviour. */
    public enum CompanionCommand { Mine, Guard, Collect }

    /**
     * AI driver for a companion submarine.
     *
     * Drives movement via SubmarinePhysicsController.SetAIThrust, turret aim via
     * TurretAim.SetAIAimTarget, and laser via MiningLaser.SetAIMining — all using
     * the same AI override pattern so the companion uses the same physics feel as
     * the player without duplicating movement code.
     *
     * Commands (issued by CompanionCommandSystem on the player sub):
     *   Mine    — seeks and collects resources; shadows the player when none are available.
     *   Guard   — interposes between player and nearest enemy; uses PlayerAttack to hit them.
     *   Collect — navigates to O2 bubbles and collects them into the PLAYER's O2 tank.
     *
     * The companion has no O2 system of its own — its only survivability concern is health,
     * repaired via tether. Collected O2 is routed to playerSub via O2PickupPump.OverrideCollectTarget.
     *
     * Setup:
     *   1. Add to the companion submarine root alongside SubmarinePhysicsController,
     *      TurretAim, MiningLaser, O2PickupPump, and PickupRangeDetector.
     *      Do NOT add O2System — the companion has no air tank.
     *   2. Optionally assign Player Sub — auto-found from Submarine.All at Start.
     *   3. Set Avoidance Layers to the Collision layer.
     *   4. Enable Allow Downward Thrust on the companion's SubmarinePhysicsController.
     *   5. Add CompanionCommandSystem to the player sub and assign input actions.
     */
    public class CompanionAI : SubmarineComponent
    {
        // =====================
        // Follow
        // =====================

        [FoldoutGroup("Follow")]
        [Tooltip("The submarine to follow. Auto-detected from Submarine.All at Start if left empty.")]
        [SerializeField] private Submarine playerSub;

        [FoldoutGroup("Follow")]
        [Tooltip("Which side of the player to fly on.")]
        [SerializeField] private bool preferRightSide = true;

        [FoldoutGroup("Follow")]
        [Tooltip("World-unit lateral offset from the player in Mine/Collect mode. " +
                 "Example: 3 = flies 3 units to the right of the player.")]
        [SerializeField, Min(0f)] private float sideOffset = 3f;

        [FoldoutGroup("Follow")]
        [Tooltip("Distance at which follow thrust reaches full magnitude.")]
        [SerializeField, Min(0.1f)] private float followSensitivity = 4f;

        // =====================
        // Mining
        // =====================

        [FoldoutGroup("Mining")]
        [Tooltip("How often (seconds) to rescan for visible targets.")]
        [SerializeField, Min(0.1f)] private float scanInterval = 1f;

        [FoldoutGroup("Mining")]
        [Tooltip("Distance at which the companion holds position and fires the mining laser. " +
                 "Should be less than MiningLaser's Max Range.")]
        [SerializeField, Min(0.5f)] private float miningEngageDistance = 4f;

        [FoldoutGroup("Mining")]
        [Tooltip("Viewport padding fraction for target scanning. 0.1 = 10% beyond screen edges.")]
        [SerializeField, Range(0f, 0.5f)] private float viewportScanPadding = 0.1f;

        [FoldoutGroup("Mining")]
        [Tooltip("Max world-unit height above the companion to consider a resource worth chasing. " +
                 "Prevents locking onto nodes that will scroll off before they can be reached.")]
        [SerializeField, Min(0f)] private float maxChaseHeight = 4f;

        // =====================
        // Guard
        // =====================

        [FoldoutGroup("Guard")]
        [Tooltip("World units from the player the companion positions itself toward the threat. " +
                 "Example: 3 = sits 3 units in front of the player in the enemy's direction.")]
        [SerializeField, Min(0f)] private float guardInterposeDist = 3f;

        // =====================
        // Avoidance
        // =====================

        [FoldoutGroup("Avoidance")]
        [Tooltip("Length of each obstacle-detection ray (world units).")]
        [SerializeField, Min(0.5f)] private float avoidanceRayLength = 3f;

        [FoldoutGroup("Avoidance")]
        [Tooltip("Angular spread of the side rays from the center ray (degrees).")]
        [SerializeField, Range(10f, 60f)] private float raySpreadDegrees = 30f;

        [FoldoutGroup("Avoidance")]
        [Tooltip("Layers that count as solid obstacles for avoidance raycasts.")]
        [SerializeField] private LayerMask avoidanceLayers;

        [FoldoutGroup("Avoidance")]
        [Tooltip("How strongly obstacle avoidance deflects the steering thrust.")]
        [SerializeField, Range(0f, 1f)] private float avoidanceStrength = 0.75f;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired whenever the active command changes. Wire to HUD to display the current mode.")]
        public UnityEvent<CompanionCommand> OnCommandChanged;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private string ActiveCommand => _command.ToString();

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private string CurrentState => _state.ToString();

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private Vector2 LiveThrust => _currentThrust;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private string O2Target => _targetO2 != null ? _targetO2.name : "None (Collect command only)";

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private string ResourceTarget => _targetResource != null ? _targetResource.name : "None";

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float ResourceDistance => _targetResource != null
            ? Vector2.Distance(transform.position, _targetResource.transform.position) : -1f;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private string EnemyTarget => _nearestEnemy != null ? _nearestEnemy.name : "None";

        // =====================
        // State
        // =====================

        private enum AIState { Follow, SeekO2, MineResource, Guard }
        private AIState _state = AIState.Follow;

        private CompanionCommand _command = CompanionCommand.Mine;

        /** Read by CompanionCommandSystem to display the active mode in debug. */
        public CompanionCommand CurrentCommand => _command;

        private Vector2 _currentThrust;
        private float _scanTimer;
        private O2Pickup _targetO2;
        private MiningResource _targetResource;
        private EnemyBase _nearestEnemy;
        private O2PickupPump _pump;
        private Camera _cam;

        // -------------------------------------------------------
        // Public API
        // -------------------------------------------------------

        /**
         * Issues a new command to the companion. Called by CompanionCommandSystem
         * on the player sub. Triggers an immediate world rescan so the new behaviour
         * takes effect within the same frame rather than waiting for the scan timer.
         */
        public void SetCommand(CompanionCommand command)
        {
            if (_command == command) return;
            _command = command;
            ScanWorld();
            _scanTimer = scanInterval;
            OnCommandChanged?.Invoke(_command);
        }

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Start()
        {
            _cam = Camera.main;

            if (playerSub == null)
                foreach (Submarine sub in Submarine.All)
                    if (sub != Sub) { playerSub = sub; break; }

            _pump = Sub != null
                ? Sub.GetComponentInChildren<O2PickupPump>()
                : GetComponentInChildren<O2PickupPump>();

            // Route collected O2 to the player's tank — companion has no O2System of its own
            if (_pump != null && playerSub != null)
                _pump.OverrideCollectTarget = playerSub;

            if (playerSub == null)
                Debug.LogWarning("[CompanionAI] No player submarine found in Submarine.All.", this);
            if (Sub?.Mining == null)
                Debug.LogWarning("[CompanionAI] MiningLaser not found — mining will not work.", this);
            if (_pump == null)
                Debug.LogWarning("[CompanionAI] O2PickupPump not found — Collect command will not work.", this);
        }

        private void Update()
        {
            if (Sub?.Physics == null) return;

            // Periodic world scan
            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f) { ScanWorld(); _scanTimer = scanInterval; }

            // Immediate rescan when active resource is gone or no longer reachable
            if (_state == AIState.MineResource && ShouldAbandonResource())
            {
                _targetResource = null;
                ScanWorld();
                _scanTimer = scanInterval;
            }

            UpdateState();
            _currentThrust = ComputeThrust();
            Sub.Physics.SetAIThrust(_currentThrust);
            UpdateWeapons();

            if (_state == AIState.SeekO2) _pump?.AICollect();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            Sub?.Physics?.ClearAIThrust();
            Sub?.Turret?.ClearAIAimTarget();
            Sub?.Mining?.ClearAIMining();
        }

        // -------------------------------------------------------
        // World Scan
        // -------------------------------------------------------

        /**
         * Refreshes nearest visible O2 bubble, mining resource, and enemy.
         * All results are viewport-filtered to avoid targeting already-scrolled-past objects.
         */
        private void ScanWorld()
        {
            Vector2 pos = transform.position;

            _targetO2 = FindNearestInView<O2Pickup>(pos);
            _nearestEnemy = FindNearestInView<EnemyBase>(pos);

            // Keep the current resource until it's destroyed — don't drop based on viewport
            // so mid-mine tracking continues even as the node drifts toward the screen edge
            if (_targetResource == null || !_targetResource.gameObject.activeInHierarchy)
                _targetResource = FindNearestInView<MiningResource>(pos);
        }

        // -------------------------------------------------------
        // State Machine
        // -------------------------------------------------------

        /**
         * Resolves the active AI state from the current command and world conditions.
         *
         * The companion has no O2 system — there is no survival O2 override.
         *
         * Command → state mapping:
         *   Guard   → Guard
         *   Collect → SeekO2 if a bubble is visible; routes air to playerSub on collect
         *   Mine    → MineResource if a target is valid, else Follow
         */
        private void UpdateState()
        {
            switch (_command)
            {
                case CompanionCommand.Guard:
                    _state = AIState.Guard;
                    return;

                case CompanionCommand.Collect:
                    _state = _targetO2 != null ? AIState.SeekO2 : AIState.Follow;
                    return;

                case CompanionCommand.Mine:
                default:
                    _state = _targetResource != null ? AIState.MineResource : AIState.Follow;
                    return;
            }
        }

        // -------------------------------------------------------
        // Steering
        // -------------------------------------------------------

        /**
         * Blends state-dependent navigation toward a target with obstacle avoidance.
         * P-controller: thrust scales with distance up to followSensitivity, then clamps at 1.
         */
        private Vector2 ComputeThrust()
        {
            Vector2 myPos = transform.position;
            Vector2 targetPos = GetNavigationTarget(myPos);
            Vector2 toTarget = targetPos - myPos;
            float dist = toTarget.magnitude;

            Vector2 followThrust = Vector2.zero;
            if (dist > 0.2f)
            {
                float strength = Mathf.Clamp01(dist / followSensitivity);
                followThrust = (toTarget / dist) * strength;
            }

            Vector2 moveDir = followThrust.sqrMagnitude > 0.01f ? followThrust.normalized : Vector2.down;
            Vector2 avoidance = ComputeAvoidance(moveDir);

            return Vector2.ClampMagnitude(followThrust + avoidance * avoidanceStrength, 1f);
        }

        /**
         * Returns the world position to navigate toward for the current state.
         *
         * MineResource: steers to 70% of engage distance from the resource so continuous
         * thrust counteracts forced-scroll drift.
         *
         * Guard: interposes between player and nearest enemy at guardInterposeDist.
         * If no enemy is visible, shadows the player at a tighter offset.
         *
         * Example (Guard): player at (0,0), enemy at (10,0), guardInterposeDist=3
         *   → companion targets (3, 0), blocking the enemy's approach vector.
         */
        private Vector2 GetNavigationTarget(Vector2 myPos)
        {
            switch (_state)
            {
                case AIState.SeekO2:
                    return _targetO2 != null
                        ? (Vector2)_targetO2.transform.position
                        : GetFollowTarget();

                case AIState.MineResource when _targetResource != null:
                    Vector2 toResource = (Vector2)_targetResource.transform.position - myPos;
                    if (toResource.sqrMagnitude < 0.01f) return myPos;
                    return (Vector2)_targetResource.transform.position
                           - toResource.normalized * (miningEngageDistance * 0.7f);

                case AIState.Guard:
                    return GetGuardTarget();

                default:
                    return GetFollowTarget();
            }
        }

        private Vector2 GetFollowTarget()
        {
            if (playerSub == null) return transform.position;
            float xSign = preferRightSide ? 1f : -1f;
            return (Vector2)playerSub.transform.position + Vector2.right * (sideOffset * xSign);
        }

        /**
         * Guard navigation target: a point between the player and the nearest enemy.
         * Positions the companion as a physical shield at guardInterposeDist from the player.
         * Falls back to a tighter follow offset when no enemy is in range.
         */
        private Vector2 GetGuardTarget()
        {
            if (playerSub == null) return transform.position;

            Vector2 playerPos = playerSub.transform.position;

            if (_nearestEnemy == null)
            {
                // No threat — shadow the player more closely than default follow
                float xSign = preferRightSide ? 1f : -1f;
                return playerPos + Vector2.right * (sideOffset * 0.5f * xSign);
            }

            // Interpose: step guardInterposeDist units from the player toward the enemy
            Vector2 toEnemy = ((Vector2)_nearestEnemy.transform.position - playerPos).normalized;
            return playerPos + toEnemy * guardInterposeDist;
        }

        // -------------------------------------------------------
        // Laser Control
        // -------------------------------------------------------

        /**
         * Controls the active weapon each frame based on the current AI state.
         *
         * MineResource: aims turret at resource, fires mining laser when within engage range.
         * Guard:        aims turret at nearest enemy, calls PlayerAttack.AIAttack() each frame
         *               (the attack's internal cooldown controls the actual swing rate).
         * All others:   clears all overrides so weapons are idle.
         */
        private void UpdateWeapons()
        {
            // Mining
            if (_state == AIState.MineResource && _targetResource != null)
            {
                float dist = Vector2.Distance(transform.position, _targetResource.transform.position);
                if (dist <= miningEngageDistance)
                {
                    Sub.Turret?.SetAIAimTarget(_targetResource.transform);
                    Sub.Mining?.SetAIMining(true);
                    return;
                }
            }
            Sub.Mining?.SetAIMining(false);

            // Guard attack — PlayerAttack.AIAttack respects its own cooldown internally
            if (_state == AIState.Guard && _nearestEnemy != null)
            {
                Sub.Turret?.SetAIAimTarget(_nearestEnemy.transform);
                Sub.Attack?.AIAttack();
                return;
            }

            Sub.Turret?.ClearAIAimTarget();
        }

        // -------------------------------------------------------
        // Obstacle Avoidance
        // -------------------------------------------------------

        /**
         * Casts three rays (center, left-spread, right-spread) in the movement direction.
         * Returns a lateral correction vector pushing away from detected obstacles.
         */
        private Vector2 ComputeAvoidance(Vector2 moveDir)
        {
            Vector2 pos = transform.position;
            float spread = raySpreadDegrees * Mathf.Deg2Rad;
            Vector2 leftDir  = RotateVector(moveDir, -spread);
            Vector2 rightDir = RotateVector(moveDir,  spread);

            bool hitCenter = Physics2D.Raycast(pos, moveDir,   avoidanceRayLength, avoidanceLayers);
            bool hitLeft   = Physics2D.Raycast(pos, leftDir,   avoidanceRayLength, avoidanceLayers);
            bool hitRight  = Physics2D.Raycast(pos, rightDir,  avoidanceRayLength, avoidanceLayers);

            if (!hitCenter && !hitLeft && !hitRight) return Vector2.zero;

            Vector2 lateral = new Vector2(moveDir.y, -moveDir.x);
            if (hitLeft  && !hitRight) return  lateral;
            if (hitRight && !hitLeft)  return -lateral;
            return lateral * (preferRightSide ? 1f : -1f);
        }

        // -------------------------------------------------------
        // Utilities
        // -------------------------------------------------------

        /**
         * True when the active mining target should be abandoned.
         * Drops destroyed/collected nodes immediately, and drops nodes that have scrolled
         * above maxChaseHeight while still outside laser range (unreachable before off-screen).
         */
        private bool ShouldAbandonResource()
        {
            if (_targetResource == null || !_targetResource.gameObject.activeInHierarchy)
                return true;

            float dist = Vector2.Distance(transform.position, _targetResource.transform.position);
            if (dist <= miningEngageDistance) return false;

            float heightAbove = _targetResource.transform.position.y - transform.position.y;
            return heightAbove > maxChaseHeight;
        }

        /**
         * Finds the nearest MonoBehaviour of type T within the camera viewport
         * (plus viewportScanPadding) and not more than maxChaseHeight above the companion.
         */
        private T FindNearestInView<T>(Vector2 from) where T : MonoBehaviour
        {
            T[] items = FindObjectsByType<T>(FindObjectsSortMode.None);
            T nearest = null;
            float bestSqrDist = float.MaxValue;

            foreach (T item in items)
            {
                Vector2 pos = item.transform.position;
                if (!IsInViewport(pos)) continue;
                if (pos.y - from.y > maxChaseHeight) continue;

                float sqrDist = (pos - from).sqrMagnitude;
                if (sqrDist < bestSqrDist) { bestSqrDist = sqrDist; nearest = item; }
            }

            return nearest;
        }

        private bool IsInViewport(Vector2 worldPos)
        {
            if (_cam == null) return true;
            Vector3 vp = _cam.WorldToViewportPoint(worldPos);
            float p = viewportScanPadding;
            return vp.x >= -p && vp.x <= 1f + p && vp.y >= -p && vp.y <= 1f + p;
        }

        private static Vector2 RotateVector(Vector2 v, float angleRad)
        {
            float cos = Mathf.Cos(angleRad);
            float sin = Mathf.Sin(angleRad);
            return new Vector2(cos * v.x - sin * v.y, sin * v.x + cos * v.y);
        }

        // -------------------------------------------------------
        // Editor Gizmos
        // -------------------------------------------------------

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Vector2 pos = transform.position;

            // Navigation target — cyan
            Vector2 navTarget = GetNavigationTarget(pos);
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.8f);
            Gizmos.DrawLine(pos, navTarget);
            Gizmos.DrawWireSphere(navTarget, 0.3f);

            // Mining engage radius — yellow
            if (_state == AIState.MineResource && _targetResource != null)
            {
                Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.6f);
                Gizmos.DrawWireSphere(_targetResource.transform.position, miningEngageDistance);
            }

            // Guard: show the interpose target and a line to the threat
            if (_state == AIState.Guard && _nearestEnemy != null)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
                Gizmos.DrawWireSphere(GetGuardTarget(), 0.4f);
                Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.4f);
                Gizmos.DrawLine(transform.position, _nearestEnemy.transform.position);
            }

            // Avoidance rays — green (clear) or red (hit)
            if (_currentThrust.sqrMagnitude > 0.01f)
            {
                Vector2 dir  = _currentThrust.normalized;
                float spread = raySpreadDegrees * Mathf.Deg2Rad;
                DrawAvoidanceRay(pos, dir,                        avoidanceRayLength);
                DrawAvoidanceRay(pos, RotateVector(dir, -spread), avoidanceRayLength);
                DrawAvoidanceRay(pos, RotateVector(dir,  spread), avoidanceRayLength);
            }
        }

        private void DrawAvoidanceRay(Vector2 origin, Vector2 dir, float length)
        {
            bool hit = Physics2D.Raycast(origin, dir, length, avoidanceLayers);
            Gizmos.color = hit ? new Color(1f, 0.3f, 0.3f, 0.8f) : new Color(0.3f, 1f, 0.5f, 0.5f);
            Gizmos.DrawRay(origin, dir * length);
        }
#endif
    }
}
