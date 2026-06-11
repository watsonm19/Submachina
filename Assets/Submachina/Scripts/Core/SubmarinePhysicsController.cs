using UnityEngine;
using UnityEngine.InputSystem;
using UnityAtoms.BaseAtoms;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Physics-driven player controller for the submarine.
     *
     * Simulates a heavy submersible with realistic momentum and inertia by
     * applying forces to a Rigidbody2D rather than manipulating velocity directly.
     * Unity gravity is disabled — the ocean current is simulated explicitly each
     * FixedUpdate using the currentDescentSpeed Atom, so all systems share a
     * single authoritative current value.
     *
     * Player input produces two kinds of thrust:
     *   - Lateral: left/right force for trench navigation
     *   - Counter-thrust: upward-only force to fight the descent current
     *     (downward player input is intentionally ignored — the current already
     *      handles downward pull, and stacking it breaks control feel)
     *
     * Wire-up:
     *   1. Add this component to the submarine root (with Rigidbody2D).
     *   2. Assign the currentDescentSpeed FloatVariable (owned by CurrentManager).
     *   3. Assign a ThrustAction InputActionReference pointing to a Vector2 action
     *      (e.g., "Move" from your Input Action Asset — WASD/left-stick).
     */
    [RequireComponent(typeof(Rigidbody2D))]
    public class SubmarinePhysicsController : MonoBehaviour
    {
        // =====================
        // Thrust Settings
        // =====================

        [FoldoutGroup("Thrust")]
        [Tooltip("Force applied laterally per physics tick. Higher = snappier side-to-side response.")]
        [SerializeField, Min(0f)] private float lateralThrustForce = 12f;

        [FoldoutGroup("Thrust")]
        [Tooltip("Force applied upward when the player counter-thrusts against the current.")]
        [SerializeField, Min(0f)] private float counterThrustForce = 18f;

        [FoldoutGroup("Thrust")]
        [Tooltip("Scales the Atom's current speed into a Rigidbody force. " +
                 "Example: speed=5, multiplier=1.5 → 7.5 N downward per FixedUpdate.")]
        [SerializeField, Min(0f)] private float currentForceMultiplier = 1.5f;

        // =====================
        // Physics Feel
        // =====================

        [FoldoutGroup("Physics Feel")]
        [Tooltip("Linear damping on the Rigidbody. Higher values kill momentum faster — tune for the 'heavy sub' feel.")]
        [SerializeField, Min(0f)] private float linearDrag = 2.5f;

        [FoldoutGroup("Physics Feel")]
        [Tooltip("Angular drag prevents unwanted spin from asymmetric force application.")]
        [SerializeField, Min(0f)] private float angularDrag = 5f;

        [FoldoutGroup("Physics Feel")]
        [Tooltip("Rigidbody mass. Higher = slower to accelerate but more momentum when moving.")]
        [SerializeField, Min(0.1f)] private float mass = 3f;

        [FoldoutGroup("Physics Feel")]
        [Tooltip("Hard cap on speed in any direction. Prevents runaway momentum from compounding forces.")]
        [SerializeField, Min(0f)] private float maxSpeed = 15f;

        // =====================
        // World Bounds
        // =====================

        [FoldoutGroup("World Bounds")]
        [Tooltip("Maximum horizontal distance from X=0 the submarine can travel. " +
                 "Set to 0 to disable. Replace with physical walls when real cave geometry exists.")]
        [SerializeField, Min(0f)] private float horizontalBoundary = 500f;

        // =====================
        // Atoms
        // =====================

        [FoldoutGroup("Atoms")]
        [Tooltip("Shared FloatVariable owned by CurrentManager. Read each FixedUpdate to drive the downward force.")]
        [SerializeField] private FloatVariable currentDescentSpeed;

        // =====================
        // Input
        // =====================

        [FoldoutGroup("Input")]
        [Tooltip("Vector2 InputAction: X = lateral thrust, Y = counter-thrust (upward only). " +
                 "Assign from your Input Action Asset (e.g., the 'Move' action).")]
        [SerializeField] private InputActionReference thrustAction;

        // =====================
        // Debug / Read-Only State
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private Vector2 CurrentVelocity => _rb != null ? _rb.linearVelocity : Vector2.zero;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float ActiveCurrentForce => currentDescentSpeed != null
            ? currentDescentSpeed.Value * currentForceMultiplier
            : 0f;

        // =====================
        // Public State
        // =====================

        /** Raw thrust input this frame — read by SubmarineDash for dash direction. */
        public Vector2 ThrustInput => _thrustInput;

        /**
         * Set true by SubmarineDash during a burst to bypass the speed clamp.
         * Without this, ClampSpeed immediately cancels the impulse on the same frame.
         */
        public bool IsDashing { get; set; }

        // =====================
        // Internals
        // =====================

        private Rigidbody2D _rb;
        private Vector2 _thrustInput;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();

            // Apply feel settings to the Rigidbody at startup so the Inspector
            // values stay as the single source of truth (not the Rigidbody component fields)
            _rb.mass = mass;
            _rb.linearDamping = linearDrag;
            _rb.angularDamping = angularDrag;
            _rb.gravityScale = 0f;
            _rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        }

        private void OnEnable()
        {
            if (thrustAction != null) thrustAction.action.Enable();
        }

        private void OnDisable()
        {
            if (thrustAction != null) thrustAction.action.Disable();
        }

        private void Update()
        {
            // Cache input each frame; physics application deferred to FixedUpdate
            _thrustInput = thrustAction != null
                ? thrustAction.action.ReadValue<Vector2>()
                : Vector2.zero;
        }

        private void FixedUpdate()
        {
            ApplyCurrentForce();
            ApplyPlayerThrust();
            ClampSpeed();
            ClampHorizontalBounds();
        }

        // -------------------------------------------------------
        // Physics
        // -------------------------------------------------------

        /**
         * Applies the downward environmental current as a continuous force.
         * Reading from the Atom means CurrentManager can shift the speed at any
         * time and this controller responds automatically without coupling.
         *
         * Force mode is ForceMode2D.Force (mass-scaled, per-frame) so heavier
         * submarines naturally resist the current more than lighter ones would.
         */
        private void ApplyCurrentForce()
        {
            if (currentDescentSpeed == null) return;

            float magnitude = currentDescentSpeed.Value * currentForceMultiplier;
            _rb.AddForce(Vector2.down * magnitude, ForceMode2D.Force);
        }

        /**
         * Translates raw player input into Rigidbody forces.
         *
         * Lateral (X): bidirectional — full left/right thrust.
         * Vertical (Y): upward-only counter-thrust — negative Y is clamped to zero.
         *   Allowing downward player input on top of the current makes the sub
         *   feel uncontrollable; the current already handles the downward pull.
         *
         * Example: input=(1, 0.8) → lateral=(12, 0), vertical=(0, 14.4)
         */
        private void ApplyPlayerThrust()
        {
            Vector2 lateral = new Vector2(_thrustInput.x * lateralThrustForce, 0f);

            float verticalInput = Mathf.Max(0f, _thrustInput.y);
            Vector2 vertical = new Vector2(0f, verticalInput * counterThrustForce);

            _rb.AddForce(lateral + vertical, ForceMode2D.Force);
        }

        /**
         * Enforces maxSpeed by clamping the velocity magnitude each FixedUpdate.
         * Comparison uses sqrMagnitude to avoid a sqrt when the cap isn't breached.
         */
        private void ClampSpeed()
        {
            // Skip during a dash — the burst needs to exceed normal max speed briefly
            if (IsDashing) return;
            if (_rb.linearVelocity.sqrMagnitude > maxSpeed * maxSpeed)
                _rb.linearVelocity = _rb.linearVelocity.normalized * maxSpeed;
        }

        /**
         * Prevents the submarine from travelling beyond horizontalBoundary units
         * from the world center (X=0). When the limit is reached, horizontal
         * velocity is zeroed so the sub doesn't press against an invisible wall.
         * Remove this once real cave wall geometry is in place.
         */
        private void ClampHorizontalBounds()
        {
            if (horizontalBoundary <= 0f) return;

            Vector2 pos = _rb.position;
            if (Mathf.Abs(pos.x) <= horizontalBoundary) return;

            pos.x = Mathf.Clamp(pos.x, -horizontalBoundary, horizontalBoundary);
            _rb.position = pos;
            _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
        }

        // -------------------------------------------------------
        // Editor Utilities
        // -------------------------------------------------------

#if UNITY_EDITOR
        [FoldoutGroup("Debug")]
        [Button("Kill Velocity"), GUIColor(1f, 0.5f, 0.2f)]
        private void DebugKillVelocity()
        {
            if (!Application.isPlaying) { Debug.Log("[Submarine] Only available in Play mode."); return; }
            _rb.linearVelocity = Vector2.zero;
            _rb.angularVelocity = 0f;
            Debug.Log("[Submarine] Velocity zeroed.");
        }

        [FoldoutGroup("Debug")]
        [Button("Burst Upward"), GUIColor(0.4f, 0.8f, 1f)]
        private void DebugBurstUp()
        {
            if (!Application.isPlaying) { Debug.Log("[Submarine] Only available in Play mode."); return; }
            _rb.AddForce(Vector2.up * counterThrustForce * 5f, ForceMode2D.Impulse);
        }
#endif
    }
}
