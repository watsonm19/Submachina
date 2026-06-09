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
     * The beam is rendered as a LineRenderer. Assign a URP-compatible material
     * (Particles/Unlit or similar) to make it visible in the Game view.
     *
     * Setup:
     *   1. Add to the submarine root alongside SubmarinePhysicsController.
     *   2. Assign TurretAim (the Turret child object).
     *   3. Create a "Mine" action (Button, hold behavior) in your Input Asset — assign it here.
     *   4. Set Mining Layer to the "Resource" layer.
     *   5. Add a "Resource" layer in Project Settings → Tags & Layers.
     *      Set the CopperResource prefab to that layer.
     *   6. Assign Laser Material — a URP Unlit/Particle material works well.
     */
    public class MiningLaser : MonoBehaviour
    {
        // =====================
        // References
        // =====================

        [FoldoutGroup("References")]
        [Tooltip("TurretAim on the submarine's Turret child. Laser fires from the turret in its aim direction.")]
        [SerializeField] private TurretAim turretAim;

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
        // Laser Visual
        // =====================

        [FoldoutGroup("Laser")]
        [Tooltip("Width of the laser beam in world units.")]
        [SerializeField, Min(0.01f)] private float laserWidth = 0.05f;

        [FoldoutGroup("Laser")]
        [Tooltip("Color of the laser beam when not on a resource target.")]
        [SerializeField] private Color idleBeamColor = new Color(0.2f, 1f, 0.8f, 0.8f);

        [FoldoutGroup("Laser")]
        [Tooltip("Color of the laser beam when actively mining a resource.")]
        [SerializeField] private Color miningBeamColor = new Color(1f, 0.9f, 0.2f, 1f);

        [FoldoutGroup("Laser")]
        [Tooltip("Material for the laser LineRenderer. Assign a URP Unlit/Particle material.")]
        [SerializeField] private Material laserMaterial;

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

        private LineRenderer _laserLine;
        private MiningResource _currentTarget;
        private float _miningTimer;
        private float _miningDuration; // cached to survive target changes

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            _miningDuration = miningDuration;
            BuildLaserRenderer();
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
            if (mineAction == null || turretAim == null) return;

            if (mineAction.action.IsPressed())
                FireLaser();
            else
                StopLaser();
        }

        // -------------------------------------------------------
        // Laser Logic
        // -------------------------------------------------------

        /**
         * Fires the beam from the turret in its aim direction.
         * If a MiningResource is hit, accumulates the mining timer.
         * Switching to a different resource resets the timer — encourages
         * committing to one node at a time.
         */
        private void FireLaser()
        {
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
                // Switched targets — reset progress on the previous one
                if (hitResource != _currentTarget)
                {
                    _currentTarget?.SetMiningProgress(0f);
                    _currentTarget = hitResource;
                    _miningTimer = 0f;
                }

                // Accumulate and report progress
                _miningTimer += Time.deltaTime;
                float progress = Mathf.Clamp01(_miningTimer / _miningDuration);
                _currentTarget.SetMiningProgress(progress);

                SetLaser(origin, beamEnd, miningBeamColor);

                // Collect when fully mined
                if (_miningTimer >= _miningDuration)
                {
                    _currentTarget.Collect();
                    _currentTarget = null;
                    _miningTimer = 0f;
                }
            }
            else
            {
                // Beam is in air — show idle beam, reset any in-progress target
                ResetCurrentTarget();
                SetLaser(origin, beamEnd, idleBeamColor);
            }
        }

        /**
         * Disables the laser visual and resets mining progress on the
         * current target so it returns to its un-mined appearance.
         */
        private void StopLaser()
        {
            _laserLine.enabled = false;
            ResetCurrentTarget();
        }

        private void ResetCurrentTarget()
        {
            if (_currentTarget == null) return;
            _currentTarget.SetMiningProgress(0f);
            _currentTarget = null;
            _miningTimer = 0f;
        }

        // -------------------------------------------------------
        // Renderer
        // -------------------------------------------------------

        private void BuildLaserRenderer()
        {
            _laserLine = gameObject.AddComponent<LineRenderer>();
            _laserLine.positionCount = 2;
            _laserLine.useWorldSpace = true;
            _laserLine.startWidth = laserWidth;
            _laserLine.endWidth = laserWidth * 0.3f; // slight taper toward the tip
            _laserLine.startColor = idleBeamColor;
            _laserLine.endColor = idleBeamColor;
            _laserLine.sortingOrder = 6;
            _laserLine.enabled = false;

            if (laserMaterial != null)
                _laserLine.material = laserMaterial;
        }

        private void SetLaser(Vector2 start, Vector2 end, Color color)
        {
            _laserLine.enabled = true;
            _laserLine.SetPosition(0, start);
            _laserLine.SetPosition(1, end);
            _laserLine.startColor = color;
            _laserLine.endColor = new Color(color.r, color.g, color.b, color.a * 0.3f);
        }
    }
}
