using UnityEngine;
using UnityEngine.InputSystem;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Rotates this GameObject's +X axis toward the player's aim direction each frame.
     *
     * Supports two input modes simultaneously:
     *   - Gamepad right stick: when pushed past the deadzone, the stick direction
     *     is used directly as the aim vector.
     *   - Mouse fallback: when the stick is idle, the turret points from its world
     *     position toward the mouse cursor in world space.
     *
     * The gamepad check takes priority so switching between devices feels seamless.
     * AimDirection is always a normalized world-space vector pointing from the
     * turret toward the current aim target.
     *
     * Setup:
     *   1. In your Input Asset, add a Vector2 Value action named "Aim".
     *   2. Bind it to <Gamepad>/rightStick.
     *   3. Assign the action reference to Aim Action below.
     *   Mouse fallback requires no extra setup — it reads Mouse.current automatically.
     */
    public class TurretAim : MonoBehaviour
    {
        // =====================
        // Input
        // =====================

        [FoldoutGroup("Input")]
        [Tooltip("Vector2 action bound to the gamepad right stick. " +
                 "When this stick is pushed past the deadzone it overrides mouse aim.")]
        [SerializeField] private InputActionReference aimAction;

        [FoldoutGroup("Input")]
        [Tooltip("Minimum stick magnitude to be treated as intentional input. " +
                 "Keeps the turret from twitching on stick drift.")]
        [SerializeField, Range(0.05f, 0.5f)] private float stickDeadzone = 0.2f;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private string AimSource => _gamepadMode ? "Gamepad" : "Mouse";

        // =====================
        // Public State
        // =====================

        /** World-space normalized direction this turret is currently aiming. */
        public Vector2 AimDirection { get; private set; } = Vector2.right;

        // =====================
        // State
        // =====================

        private Camera _camera;
        // True = gamepad is the active device; turret holds last stick direction when idle.
        // False = mouse is active; turret always follows the cursor.
        private bool _gamepadMode;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            _camera = Camera.main;
        }

        private void OnEnable()
        {
            if (aimAction != null) aimAction.action.Enable();
        }

        private void OnDisable()
        {
            if (aimAction != null) aimAction.action.Disable();
        }

        private void Update()
        {
            UpdateActiveDevice();
            ApplyAim();
        }

        // -------------------------------------------------------
        // Input Resolution
        // -------------------------------------------------------

        /**
         * Detects which device is actively being used and switches modes.
         *
         * Any stick movement past the deadzone → gamepad mode.
         * Any mouse movement → mouse mode.
         * Devices don't interfere with each other when idle — whichever
         * last moved owns the aim until the other one moves again.
         */
        private void UpdateActiveDevice()
        {
            // Gamepad stick pushed → enter gamepad mode
            if (aimAction != null)
            {
                Vector2 stick = aimAction.action.ReadValue<Vector2>();
                if (stick.magnitude > stickDeadzone)
                {
                    _gamepadMode = true;
                    return;
                }
            }

            // Mouse moved → enter mouse mode
            if (Mouse.current != null && Mouse.current.delta.ReadValue().sqrMagnitude > 0.5f)
                _gamepadMode = false;
        }

        /**
         * Applies the aim direction for the current active device.
         *
         * Gamepad mode: use the stick direction; when stick is idle hold the
         * last known direction (turret stays put rather than snapping to mouse).
         * Mouse mode: always track the cursor in world space.
         */
        private void ApplyAim()
        {
            Vector2 dir = _gamepadMode ? GetStickDirection() : GetMouseDirection();

            // Zero means no input or no device — hold last direction
            if (dir.sqrMagnitude < 0.001f) return;

            AimDirection = dir.normalized;

            // Atan2(y, x) converts direction to Z-rotation: (1,0)→0°, (0,1)→90°, etc.
            float angle = Mathf.Atan2(AimDirection.y, AimDirection.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        private Vector2 GetStickDirection()
        {
            if (aimAction == null) return Vector2.zero;
            return aimAction.action.ReadValue<Vector2>();
        }

        /**
         * Converts mouse screen position to a world-space direction vector
         * pointing from the turret toward the cursor.
         */
        private Vector2 GetMouseDirection()
        {
            if (_camera == null || Mouse.current == null) return Vector2.zero;

            Vector2 screenPos = Mouse.current.position.ReadValue();
            Vector3 mouseWorld = _camera.ScreenToWorldPoint(
                new Vector3(screenPos.x, screenPos.y, Mathf.Abs(_camera.transform.position.z)));
            mouseWorld.z = 0f;

            return (Vector2)mouseWorld - (Vector2)transform.position;
        }
    }
}
