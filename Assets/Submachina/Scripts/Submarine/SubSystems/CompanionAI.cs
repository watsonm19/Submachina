using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * AI driver for a companion submarine.
     *
     * Drives movement via SubmarinePhysicsController.SetAIThrust, turret aim via
     * TurretAim.SetAIAimTarget, and mining via MiningLaser.SetAIMining — all using
     * the same AI override pattern so the companion benefits from the same physics
     * feel as the player without duplicating any movement code.
     *
     * State machine (priority high → low):
     *   SeekO2       — O2 below threshold and an O2 bubble is on-screen; navigate to it,
     *                  let the pump auto-start, call AICollect() at the sweet spot.
     *   MineResource — A MiningResource is visible on screen; navigate within laser range
     *                  and hold the beam until collected.
     *   Follow       — Default; fly to a position offset to one side of the player.
     *
     * Scan is viewport-filtered: only world objects within the camera view (plus padding)
     * are considered as targets. This prevents the companion chasing already-scrolled-past
     * objects above the screen in forced-scroll mode.
     *
     * Setup:
     *   1. Add to the companion submarine root alongside SubmarinePhysicsController,
     *      TurretAim, MiningLaser, O2System, O2PickupPump, and PickupRangeDetector.
     *   2. Optionally assign Player Sub — auto-found from Submarine.All at Start.
     *   3. Set Avoidance Layers to the Collision layer.
     *   4. Enable Allow Downward Thrust on the companion's SubmarinePhysicsController.
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
        [Tooltip("World-unit lateral offset from the player's position. " +
                 "Example: 3 = flies 3 units to the right of the player.")]
        [SerializeField, Min(0f)] private float sideOffset = 3f;

        [FoldoutGroup("Follow")]
        [Tooltip("Distance at which follow thrust reaches full magnitude. " +
                 "Example: 4 = full thrust when 4+ units away, proportionally less when closer.")]
        [SerializeField, Min(0.1f)] private float followSensitivity = 4f;

        // =====================
        // O2
        // =====================

        [FoldoutGroup("O2")]
        [Tooltip("O2 fill fraction (0–1) below which SeekO2 takes priority over mining. " +
                 "Example: 0.5 = seek a bubble when below 50% air.")]
        [SerializeField, Range(0f, 1f)] private float o2SeekThreshold = 0.5f;

        // =====================
        // Mining
        // =====================

        [FoldoutGroup("Mining")]
        [Tooltip("How often (seconds) to rescan for the nearest visible O2 bubble and mining " +
                 "resource. Lower = more responsive. 1 second is a good default.")]
        [SerializeField, Min(0.1f)] private float scanInterval = 1f;

        [FoldoutGroup("Mining")]
        [Tooltip("Distance from a resource at which the companion stops approaching and holds " +
                 "the beam. Should be less than MiningLaser's Max Range. " +
                 "Example: 4 = hovers 4 units away and fires from there.")]
        [SerializeField, Min(0.5f)] private float miningEngageDistance = 4f;

        [FoldoutGroup("Mining")]
        [Tooltip("Extra padding (in viewport fraction) beyond the screen edges to include when " +
                 "scanning for targets. 0.1 = 10% of the screen width/height of extra tolerance.")]
        [SerializeField, Range(0f, 0.5f)] private float viewportScanPadding = 0.1f;

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
        [Tooltip("How strongly obstacle avoidance deflects the follow thrust.")]
        [SerializeField, Range(0f, 1f)] private float avoidanceStrength = 0.75f;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private string CurrentState => _state.ToString();

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private Vector2 LiveThrust => _currentThrust;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private string O2Target => _targetO2 != null ? _targetO2.name : "None";

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private string ResourceTarget => _targetResource != null ? _targetResource.name : "None";

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float ResourceDistance => _targetResource != null
            ? Vector2.Distance(transform.position, _targetResource.transform.position)
            : -1f;

        // =====================
        // State
        // =====================

        private enum AIState { Follow, SeekO2, MineResource }
        private AIState _state = AIState.Follow;

        private Vector2 _currentThrust;
        private float _scanTimer;
        private O2Pickup _targetO2;
        private MiningResource _targetResource;
        private O2PickupPump _pump;
        private Camera _cam;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Start()
        {
            _cam = Camera.main;

            // Auto-find the player sub if not manually assigned
            if (playerSub == null)
                foreach (Submarine sub in Submarine.All)
                    if (sub != Sub) { playerSub = sub; break; }

            // Cache the pump — not in the Submarine facade, so get it from the sub root
            _pump = Sub != null
                ? Sub.GetComponentInChildren<O2PickupPump>()
                : GetComponentInChildren<O2PickupPump>();

            if (playerSub == null)
                Debug.LogWarning("[CompanionAI] No player submarine found in Submarine.All.", this);
            if (Sub?.Mining == null)
                Debug.LogWarning("[CompanionAI] MiningLaser not found on this sub — mining will not work.", this);
            if (_pump == null)
                Debug.LogWarning("[CompanionAI] O2PickupPump not found — AI O2 collection will not work.", this);
        }

        private void Update()
        {
            if (Sub?.Physics == null) return;

            // Periodic rescan for visible world objects
            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f) { ScanWorld(); _scanTimer = scanInterval; }

            UpdateState();
            _currentThrust = ComputeThrust();
            Sub.Physics.SetAIThrust(_currentThrust);
            UpdateMining();

            // While seeking O2, try to collect at the sweet spot each frame
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
         * Refreshes nearest visible O2 bubble and mining resource references.
         * Only considers objects inside the camera viewport (plus viewportScanPadding),
         * preventing the companion from chasing targets that have already scrolled off-screen.
         */
        private void ScanWorld()
        {
            Vector2 pos = transform.position;

            _targetO2 = FindNearestInView<O2Pickup>(pos);

            // Keep the current resource until it's collected (destroyed/disabled).
            // Don't drop it based on viewport — it may scroll toward the edge mid-mine
            // and the companion still needs to track it to stay within laser range.
            if (_targetResource == null || !_targetResource.gameObject.activeInHierarchy)
                _targetResource = FindNearestInView<MiningResource>(pos);
        }

        // -------------------------------------------------------
        // State Machine
        // -------------------------------------------------------

        /**
         * Evaluates state transitions each frame.
         *
         * SeekO2 triggers when air is critically low (below o2SeekThreshold) and a
         * visible bubble exists. MineResource activates whenever a resource target
         * is valid. Follow is the idle/default fallback.
         */
        private void UpdateState()
        {
            bool o2Low = Sub?.O2 != null
                && Sub.O2.MaxAir > 0f
                && Sub.O2.CurrentAirPressure / Sub.O2.MaxAir < o2SeekThreshold;

            if (o2Low && _targetO2 != null)  { _state = AIState.SeekO2;       return; }
            if (_targetResource != null)      { _state = AIState.MineResource; return; }
            _state = AIState.Follow;
        }

        // -------------------------------------------------------
        // Steering
        // -------------------------------------------------------

        /**
         * Blends the state-dependent navigation target with obstacle avoidance.
         * Uses a P-controller: thrust scales proportionally with distance up to
         * followSensitivity, then clamps at 1.
         *
         * Example: 6 units from target (sensitivity=4) → strength = 1.0 (full thrust).
         *          1 unit from target (sensitivity=4) → strength = 0.25 (gentle approach).
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
         * Returns the world position this AI should navigate toward in the current state.
         *
         * MineResource: always targets a point inside miningEngageDistance rather than
         * holding position. In forced-scroll mode the camera drifts the companion away
         * from world-space resources over time, so continuous thrust is needed to maintain
         * laser range even while the beam is active.
         *
         * Example: resource at (10, -5), engageDistance=4, holdFraction=0.7 →
         *   companion always steers toward (10-dir*2.8, -5), staying ~2.8 units from resource.
         *   P-controller damps the approach naturally as it gets close.
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
                    // Target 70% of engage distance from the resource — keeps the companion
                    // continuously thrusting to hold position against scroll drift
                    return (Vector2)_targetResource.transform.position
                           - toResource.normalized * (miningEngageDistance * 0.7f);

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

        // -------------------------------------------------------
        // Mining Control
        // -------------------------------------------------------

        /**
         * When in MineResource state and within engage range, aims the turret at the
         * resource and activates the laser. Clears both overrides otherwise.
         */
        private void UpdateMining()
        {
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

            Sub.Turret?.ClearAIAimTarget();
            Sub.Mining?.SetAIMining(false);
        }

        // -------------------------------------------------------
        // Obstacle Avoidance
        // -------------------------------------------------------

        /**
         * Casts three rays (center, left-spread, right-spread) in the movement direction.
         * Returns a lateral correction vector pushing away from detected obstacles.
         *
         * Example: left ray hits, right is clear → return +lateral (push right).
         *          all clear → return zero.
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
         * Returns the nearest MonoBehaviour of type T that is currently inside the
         * camera viewport (with viewportScanPadding), or null if none qualify.
         * Ignores objects that have already scrolled off-screen.
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

                float sqrDist = (pos - from).sqrMagnitude;
                if (sqrDist < bestSqrDist) { bestSqrDist = sqrDist; nearest = item; }
            }

            return nearest;
        }

        /** Returns true if worldPos falls within the camera viewport plus viewportScanPadding. */
        private bool IsInViewport(Vector2 worldPos)
        {
            if (_cam == null) return true; // fallback: no camera, accept everything
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

            // Navigation target — cyan line + sphere
            Vector2 navTarget = GetNavigationTarget(pos);
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.8f);
            Gizmos.DrawLine(pos, navTarget);
            Gizmos.DrawWireSphere(navTarget, 0.3f);

            // Mining engage radius — yellow ring around the resource
            if (_state == AIState.MineResource && _targetResource != null)
            {
                Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.6f);
                Gizmos.DrawWireSphere(_targetResource.transform.position, miningEngageDistance);
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
