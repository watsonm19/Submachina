using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Cavitation Burst — a directional underwater surge for the submarine.
     *
     * Named after real submarine cavitation: when a propeller drives so hard
     * through water it creates vapor bubbles. The burst simulates that sudden
     * violent push through the water column.
     *
     * The underwater feel comes from three layered physics effects:
     *   1. A single Impulse force gives the initial burst of speed.
     *   2. Linear damping drops sharply during the burst so the sub slides
     *      through the water instead of stopping immediately.
     *   3. Damping is restored after the active phase, letting normal drag
     *      naturally bleed off the excess momentum.
     *
     * Direction priority:
     *   1. Current thrust input (where the player is steering)
     *   2. Current velocity direction (if no input but already moving)
     *   3. Aim direction from TurretAim (last resort when stationary)
     *
     * Setup:
     *   1. Add to the submarine root alongside SubmarinePhysicsController.
     *   2. Assign PhysicsController and TurretAim references.
     *   3. Create a "CavitationBurst" Button action in your Input Asset and assign it.
     */
    [RequireComponent(typeof(Rigidbody2D))]
    public class CavitationBurst : MonoBehaviour
    {
        // =====================
        // References
        // =====================

        [FoldoutGroup("References")]
        [Tooltip("SubmarinePhysicsController on this submarine — needed to bypass speed clamp during the burst.")]
        [SerializeField] private SubmarinePhysicsController physicsController;

        [FoldoutGroup("References")]
        [Tooltip("TurretAim child object — used as fallback direction when the sub is stationary.")]
        [SerializeField] private TurretAim turretAim;

        // =====================
        // Input
        // =====================

        [FoldoutGroup("Input")]
        [Tooltip("Button InputAction that triggers the burst.")]
        [SerializeField] private InputActionReference burstAction;

        // =====================
        // Burst Settings
        // =====================

        [FoldoutGroup("Burst")]
        [Tooltip("Impulse magnitude applied at the start of the burst. " +
                 "With submarine mass=3, a value of 12 adds ~4 m/s instantly.")]
        [SerializeField, Min(0f)] private float burstImpulse = 12f;

        [FoldoutGroup("Burst")]
        [Tooltip("Seconds the reduced damping stays active. Longer = the sub slides further before drag takes hold.")]
        [SerializeField, Min(0.05f)] private float burstDuration = 0.3f;

        [FoldoutGroup("Burst")]
        [Tooltip("Seconds before the player can burst again.")]
        [SerializeField, Min(0f)] private float burstCooldown = 1.5f;

        [FoldoutGroup("Burst")]
        [Tooltip("Linear damping during the burst phase. Much lower than normal so the impulse carries. " +
                 "Normal damping is restored automatically after burstDuration.")]
        [SerializeField, Min(0f)] private float burstDrag = 0.4f;

        // =====================
        // Visual Feedback
        // =====================

        [FoldoutGroup("Feedback")]
        [Tooltip("Sprite color flashed at the start of the burst to sell the propulsion kick.")]
        [SerializeField] private Color burstFlashColor = new Color(0.6f, 0.9f, 1f, 1f);

        [FoldoutGroup("Feedback")]
        [Tooltip("How long the flash color holds before returning to normal.")]
        [SerializeField, Min(0f)] private float flashDuration = 0.08f;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private bool IsBursting => _isBursting;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float CooldownRemaining => Mathf.Max(0f, _cooldownEnd - Time.time);

        // =====================
        // State
        // =====================

        private Rigidbody2D _rb;
        private SpriteRenderer _spriteRenderer;
        private Color _originalColor;
        private float _originalDrag;
        private float _cooldownEnd = -1f;
        private bool _isBursting;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _spriteRenderer = GetComponent<SpriteRenderer>();
        }

        private void Start()
        {
            // SubmarinePhysicsController sets linearDamping in its Awake,
            // so cache the correct value here in Start (after all Awakes run)
            _originalDrag = _rb.linearDamping;

            if (_spriteRenderer != null) _originalColor = _spriteRenderer.color;
        }

        private void OnEnable()
        {
            if (burstAction != null) burstAction.action.Enable();
        }

        private void OnDisable()
        {
            if (burstAction != null) burstAction.action.Disable();
        }

        private void Update()
        {
            if (burstAction != null && burstAction.action.WasPressedThisFrame())
                TryBurst();
        }

        // -------------------------------------------------------
        // Burst
        // -------------------------------------------------------

        private void TryBurst()
        {
            if (_isBursting || Time.time < _cooldownEnd) return;

            Vector2 dir = GetBurstDirection();
            if (dir.sqrMagnitude < 0.001f) return;

            StartCoroutine(BurstRoutine(dir.normalized));
        }

        /**
         * Resolves burst direction from available inputs.
         * Thrust input is preferred so the burst always goes where the player is steering.
         * Velocity fallback handles the "coasting but no input" case.
         * Aim direction is the last resort so a stationary sub still has a direction.
         */
        private Vector2 GetBurstDirection()
        {
            if (physicsController != null && physicsController.ThrustInput.sqrMagnitude > 0.01f)
                return physicsController.ThrustInput;

            if (_rb.linearVelocity.sqrMagnitude > 0.25f)
                return _rb.linearVelocity.normalized;

            if (turretAim != null)
                return turretAim.AimDirection;

            return Vector2.zero;
        }

        /**
         * Three-phase burst coroutine:
         *
         * Phase 1 — Burst (instant):
         *   Apply the impulse and drop damping so the sub punches through
         *   the water without friction killing the burst. Flash the sprite.
         *
         * Phase 2 — Slide (burstDuration seconds):
         *   The sub glides with reduced drag. The ocean current still pulls
         *   downward so the path curves naturally — preserving the underwater feel.
         *
         * Phase 3 — Recovery:
         *   Drag snaps back to normal. Excess momentum bleeds off over the
         *   next few physics steps for a natural deceleration.
         */
        private IEnumerator BurstRoutine(Vector2 direction)
        {
            _isBursting = true;
            if (physicsController != null) physicsController.IsDashing = true;

            // Phase 1 — burst
            _rb.linearDamping = burstDrag;
            _rb.AddForce(direction * burstImpulse, ForceMode2D.Impulse);

            if (_spriteRenderer != null) _spriteRenderer.color = burstFlashColor;
            yield return new WaitForSeconds(flashDuration);
            if (_spriteRenderer != null) _spriteRenderer.color = _originalColor;

            // Phase 2 — slide
            yield return new WaitForSeconds(burstDuration - flashDuration);

            // Phase 3 — recovery
            _rb.linearDamping = _originalDrag;
            if (physicsController != null) physicsController.IsDashing = false;

            _isBursting = false;
            _cooldownEnd = Time.time + burstCooldown;
        }

        // -------------------------------------------------------
        // Editor Utilities
        // -------------------------------------------------------

#if UNITY_EDITOR
        [FoldoutGroup("Debug")]
        [Button("Test Burst"), GUIColor(0.4f, 0.8f, 1f)]
        private void DebugBurst()
        {
            if (!Application.isPlaying) { Debug.Log("[CavitationBurst] Play mode only."); return; }
            TryBurst();
        }
#endif
    }
}
