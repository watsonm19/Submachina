using UnityEngine;
using UnityEngine.InputSystem;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Fires a mining laser beam toward the mouse cursor while the mine button is held.
     *
     * If the beam hits a MiningResource node, a progress timer accumulates.
     * When the timer reaches miningDuration the resource is collected.
     * Moving the beam off-target or releasing the button resets the timer.
     *
     * The beam visual is driven by a MiningBeamVFX component on a child object,
     * which renders the Sci-Fi Arsenal static beam with a line renderer, emitter
     * particles at the turret tip, and impact sparks at the hit point.
     *
     * Setup:
     *   1. Add to the submarine root alongside SubmarinePhysicsController.
     *   2. Assign TurretAim (the Turret child object).
     *   3. Assign Beam VFX (MiningBeamVFX on the beam child of the turret).
     *   4. Create a "Mine" action (Button, hold behavior) in your Input Asset — assign it here.
     *   5. Set Mining Layer to the "Resource" layer.
     */
    [UsesFeedbacks(nameof(SubFeedbacks.MiningActive), nameof(SubFeedbacks.MiningCollect), nameof(SubFeedbacks.RockBreak))]
    public class MiningLaser : InputSubmarineComponent
    {
        // =====================
        // References
        // =====================

        [FoldoutGroup("References")]
        [Tooltip("MiningBeamVFX on the beam child object. Drives the Sci-Fi Arsenal beam visuals.")]
        [SerializeField] private MiningBeamVFX beamVFX;

        // =====================
        // Input
        // =====================

        [FoldoutGroup("Input")]
        [Tooltip("Hold InputAction that fires the mining laser. Create a 'Mine' Button action and assign it here.")]
        [SerializeField] private InputActionReference mineAction;

        // =====================
        // Mining Settings
        // =====================

        [FoldoutGroup("Mining")]
        [Tooltip("Maximum distance the laser can reach.")]
        [SerializeField, Min(0.5f)] private float maxRange = 6f;

        [FoldoutGroup("Mining")]
        [Tooltip("Seconds of sustained beam contact needed to collect a resource.")]
        [SerializeField, Min(0.1f)] private float miningDuration = 2f;

        [FoldoutGroup("Mining")]
        [Tooltip("Layer containing mining resource colliders. Create a 'Resource' layer and assign it here.")]
        [SerializeField] private LayerMask miningLayer;

        // =====================
        // Obstacle Settings
        // =====================

        [FoldoutGroup("Obstacles")]
        [Tooltip("Layer containing RockObstacle colliders. The laser will break rocks on this layer. " +
                 "Set the RockObstacle prefab to this layer and add it here.")]
        [SerializeField] private LayerMask obstacleLayer;

        [FoldoutGroup("Obstacles")]
        [Tooltip("Seconds of sustained beam contact needed to break a rock obstacle. " +
                 "Longer than miningDuration is recommended — rocks should feel harder to clear.")]
        [SerializeField, Min(0.1f)] private float rockBreakDuration = 3f;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float MiningTimer => _miningTimer;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float MiningProgress => _miningDuration > 0f ? _miningTimer / _miningDuration : 0f;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private bool HasTarget => _currentTarget != null;

        // =====================
        // Upgrade Accessors
        // =====================

        private float MineDurationMod => Sub?.Upgrades?.Stats.Resolve(SubStats.MiningDuration, miningDuration) ?? miningDuration;
        private float LaserRangeMod   => Sub?.Upgrades?.Stats.Resolve(SubStats.MiningRange, maxRange) ?? maxRange;

        // =====================
        // State
        // =====================

        private MiningResource _currentTarget;
        private float _miningTimer;
        private float _miningDuration;

        private RockObstacle _currentRock;
        private float _rockTimer;

        private bool _miningFeedbacksPlaying;

        // AI override: null = use player input; true/false = AI controls firing
        private bool? _aiMining;

        /**
         * When set, overrides player input to fire (true) or suppress (false) the laser.
         * CompanionAI sets this when it is close enough to a resource and ready to mine.
         * Clear to null to restore player control.
         */
        public void SetAIMining(bool active) => _aiMining = active;
        public void ClearAIMining()          => _aiMining = null;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();
            _miningDuration = MineDurationMod;
            RegisterAction(mineAction);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            StopLaser();
        }

        private void Update()
        {
            // Allow AI-controlled firing even when no player action is wired up
            if (Sub?.Turret == null || beamVFX == null) return;

            bool firing = _aiMining.HasValue
                ? _aiMining.Value
                : (PrimaryAction != null && PrimaryAction.IsPressed());

            if (firing)
                FireLaser();
            else
                StopLaser();
        }

        // -------------------------------------------------------
        // Laser Logic
        // -------------------------------------------------------

        /**
         * Fires the beam from the turret in its aim direction.
         *
         * Casts against both the mining layer (MiningResource) and the obstacle
         * layer (RockObstacle) in a single pass. The first solid hit determines
         * behavior: collect a resource, break a rock, or show an idle beam.
         * Switching between targets (resource→rock or vice versa) always resets
         * the other in-progress timer so progress doesn't carry over.
         */
        private void FireLaser()
        {
            beamVFX.Show();

            Vector2 origin    = Sub.Turret.transform.position;
            Vector2 direction = Sub.Turret.AimDirection;
            float   range     = LaserRangeMod;

            // Single cast — hits whichever (resource or obstacle) is closer
            RaycastHit2D hit = Physics2D.Raycast(origin, direction, range, miningLayer | obstacleLayer);

            Vector2 beamEnd = hit.collider != null ? hit.point : origin + direction * range;

            // Identify what was hit — resource check first; rock only if resource not found
            MiningResource hitResource = hit.collider?.GetComponent<MiningResource>();
            RockObstacle   hitRock     = hitResource == null && hit.collider != null
                ? hit.collider.GetComponent<RockObstacle>()
                : null;

            if (hitResource != null)
            {
                // Beam moved from a rock to a resource — reset the rock
                ResetCurrentRock();

                // Switched resource targets — reset progress on the previous one
                if (hitResource != _currentTarget)
                {
                    StopMiningFeedbacks();
                    _currentTarget?.SetMiningProgress(0f);
                    _currentTarget = hitResource;
                    _miningTimer   = 0f;
                }

                // Accumulate and report progress
                _miningTimer += Time.deltaTime;
                float progress = Mathf.Clamp01(_miningTimer / _miningDuration);
                _currentTarget.SetMiningProgress(progress);

                beamVFX.SetBeam(origin, beamEnd, true, progress);

                if (!_miningFeedbacksPlaying)
                {
                    _miningFeedbacksPlaying = true;
                    Sub?.Feedbacks?.Play(SubFeedbacks.MiningActive, beamEnd);
                }

                if (_miningTimer >= _miningDuration)
                {
                    StopMiningFeedbacks();
                    Sub?.Feedbacks?.Play(SubFeedbacks.MiningCollect, beamEnd);
                    _currentTarget.Collect(Sub);
                    _currentTarget = null;
                    _miningTimer   = 0f;
                }
            }
            else if (hitRock != null)
            {
                // Beam moved from a resource to a rock — reset the resource
                ResetCurrentTarget();

                // Switched rock targets — reset progress on the previous one
                if (hitRock != _currentRock)
                {
                    StopMiningFeedbacks();
                    _currentRock?.ResetProgress();
                    _currentRock = hitRock;
                    _rockTimer   = 0f;
                }

                // Accumulate and report progress; visual intensity matches mining resources
                _rockTimer += Time.deltaTime;
                float progress = Mathf.Clamp01(_rockTimer / rockBreakDuration);
                _currentRock.SetProgress(progress);

                beamVFX.SetBeam(origin, beamEnd, true, progress);

                if (!_miningFeedbacksPlaying)
                {
                    _miningFeedbacksPlaying = true;
                    Sub?.Feedbacks?.Play(SubFeedbacks.MiningActive, beamEnd);
                }

                if (_rockTimer >= rockBreakDuration)
                {
                    StopMiningFeedbacks();
                    Sub?.Feedbacks?.Play(SubFeedbacks.RockBreak, beamEnd);
                    _currentRock.Break();
                    _currentRock = null;
                    _rockTimer   = 0f;
                }
            }
            else
            {
                // Beam is in air — idle beam, reset any in-progress target or rock
                ResetCurrentTarget();
                ResetCurrentRock();
                beamVFX.SetBeam(origin, beamEnd, false);
            }
        }

        /**
         * Hides the beam VFX and resets any in-progress mining or rock-breaking state.
         */
        private void StopLaser()
        {
            if (beamVFX != null) beamVFX.Hide();
            ResetCurrentTarget();
            ResetCurrentRock();
        }

        private void ResetCurrentTarget()
        {
            if (_currentTarget == null) return;
            StopMiningFeedbacks();
            _currentTarget.SetMiningProgress(0f);
            _currentTarget = null;
            _miningTimer   = 0f;
        }

        private void ResetCurrentRock()
        {
            if (_currentRock == null) return;
            StopMiningFeedbacks();
            _currentRock.ResetProgress();
            _currentRock = null;
            _rockTimer   = 0f;
        }

        /**
         * Stops all mining feedbacks cleanly. Called on target switch,
         * beam release, or collection so pooled objects aren't left dangling.
         */
        private void StopMiningFeedbacks()
        {
            if (!_miningFeedbacksPlaying) return;
            _miningFeedbacksPlaying = false;
            Sub?.Feedbacks?.Stop(SubFeedbacks.MiningActive);
        }
    }
}
