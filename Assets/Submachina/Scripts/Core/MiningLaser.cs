using UnityEngine;
using UnityEngine.InputSystem;
using MoreMountains.Feedbacks;
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
    public class MiningLaser : MonoBehaviour
    {
        // =====================
        // References
        // =====================

        [FoldoutGroup("References")]
        [Tooltip("TurretAim on the submarine's Turret child. Laser fires from the turret in its aim direction.")]
        [SerializeField] private TurretAim turretAim;

        [FoldoutGroup("References")]
        [Tooltip("The submarine's ManualBellowsPump. IsMining is toggled while the laser fires to increase air drain.")]
        [SerializeField] private ManualBellowsPump pump;

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
        // Feedbacks
        // =====================

        [FoldoutGroup("Feedbacks")]
        [Tooltip("MMF_Players played once when mining begins on a target (or switches targets). " +
                 "Stopped when the beam leaves the target or the button is released. " +
                 "Use looping feedbacks (e.g. looping sound, sustained screen shake) for continuous effects.")]
        [SerializeField] private MMF_Player[] miningFeedbacks;

        [FoldoutGroup("Feedbacks")]
        [Tooltip("MMF_Players played once when a resource is fully collected. " +
                 "Called with PlayFeedbacks(hitPoint, 1.0).")]
        [SerializeField] private MMF_Player[] collectFeedbacks;

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
        // State
        // =====================

        private MiningResource _currentTarget;
        private float _miningTimer;
        private float _miningDuration;
        private bool _miningFeedbacksPlaying;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            _miningDuration = miningDuration;
        }

        private void OnEnable()
        {
            if (mineAction != null) mineAction.action.Enable();
        }

        private void OnDisable()
        {
            if (mineAction != null) mineAction.action.Disable();
            StopLaser();
        }

        private void Update()
        {
            if (mineAction == null || turretAim == null || beamVFX == null) return;

            bool firing = mineAction.action.IsPressed()
                && (pump == null || pump.CurrentAirPressure > 0f);
            if (pump != null) pump.IsMining = firing;

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
         * If a MiningResource is hit, accumulates the mining timer and
         * shows impact VFX at the hit point. Switching to a different
         * resource resets progress — encourages committing to one node.
         */
        private void FireLaser()
        {
            beamVFX.Show();

            Vector2 origin = turretAim.transform.position;
            Vector2 direction = turretAim.AimDirection;

            // Cast along the aim direction; only hits Resource-layer colliders
            RaycastHit2D hit = Physics2D.Raycast(origin, direction, maxRange, miningLayer);

            Vector2 beamEnd = hit.collider != null
                ? hit.point
                : origin + direction * maxRange;

            // Try to get a MiningResource from whatever was hit
            MiningResource hitResource = hit.collider != null
                ? hit.collider.GetComponent<MiningResource>()
                : null;

            if (hitResource != null)
            {
                // Switched targets — reset progress on the previous one and restart feedbacks
                if (hitResource != _currentTarget)
                {
                    StopMiningFeedbacks();
                    _currentTarget?.SetMiningProgress(0f);
                    _currentTarget = hitResource;
                    _miningTimer = 0f;
                }

                // Accumulate and report progress
                _miningTimer += Time.deltaTime;
                float progress = Mathf.Clamp01(_miningTimer / _miningDuration);
                _currentTarget.SetMiningProgress(progress);

                // Beam with impact VFX — progress drives visual intensity
                beamVFX.SetBeam(origin, beamEnd, true, progress);

                // Start mining feedbacks once when we begin on a target
                if (!_miningFeedbacksPlaying)
                {
                    _miningFeedbacksPlaying = true;
                    for (int i = 0; i < miningFeedbacks.Length; i++)
                    {
                        if (miningFeedbacks[i] != null) miningFeedbacks[i].PlayFeedbacks(beamEnd, 1f);
                    }
                }

                // Collect when fully mined
                if (_miningTimer >= _miningDuration)
                {
                    StopMiningFeedbacks();

                    // Collection feedbacks — one-shot burst at full intensity
                    for (int i = 0; i < collectFeedbacks.Length; i++)
                    {
                        if (collectFeedbacks[i] != null) collectFeedbacks[i].PlayFeedbacks(beamEnd, 1f);
                    }

                    _currentTarget.Collect();
                    _currentTarget = null;
                    _miningTimer = 0f;
                }
            }
            else
            {
                // Beam is in air — show idle beam, reset any in-progress target
                ResetCurrentTarget();
                beamVFX.SetBeam(origin, beamEnd, false);
            }
        }

        /**
         * Hides the beam VFX and resets mining progress on the
         * current target so it returns to its un-mined appearance.
         */
        private void StopLaser()
        {
            if (beamVFX != null) beamVFX.Hide();
            ResetCurrentTarget();
        }

        private void ResetCurrentTarget()
        {
            if (_currentTarget == null) return;
            StopMiningFeedbacks();
            _currentTarget.SetMiningProgress(0f);
            _currentTarget = null;
            _miningTimer = 0f;
        }

        /**
         * Stops all mining feedbacks cleanly. Called on target switch,
         * beam release, or collection so pooled objects aren't left dangling.
         */
        private void StopMiningFeedbacks()
        {
            if (!_miningFeedbacksPlaying) return;
            _miningFeedbacksPlaying = false;

            for (int i = 0; i < miningFeedbacks.Length; i++)
            {
                if (miningFeedbacks[i] != null) miningFeedbacks[i].StopFeedbacks();
            }
        }
    }
}
