using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using Sirenix.OdinInspector;

namespace Gameplay.Pointer
{
    /**
     * Tracks the pointer position in world space and moves this transform
     * to match every frame. Publishes position data to PointerWorldBus so
     * any subscriber (including prefab-based PointerWorldListeners) can
     * receive pointer positions without scene references.
     *
     * Also usable as a composable target provider — attach to an empty
     * GameObject and assign it as the target for SmoothFollower or any
     * other follow system.
     *
     * Emits three subscribable events (both UnityEvent and PointerWorldBus):
     *   onPositionUpdated  — world position every frame (continuous tracking)
     *   onPrimaryInput     — world position when the primary action fires (e.g. left click)
     *   onSecondaryInput   — world position when the secondary action fires (e.g. right click)
     *
     * Assumes a 2D camera looking down the Z axis. The worldZ parameter
     * controls the depth plane the cursor is projected onto.
     */
    [AddComponentMenu("Gameplay/Input/Pointer World Tracker")]
    public class PointerWorldTracker : MonoBehaviour
    {
        // ── Configuration ───────────────────────────────────────────────────

        [TitleGroup("Pointer World Tracker")]
        [InfoBox("Moves this transform to the pointer's world position each frame.\n" +
                 "Use as a follow target for SmoothFollower or any system that tracks a Transform.\n\n" +
                 "Subscribable events emit world-space Vector3 for position tracking, " +
                 "primary input (left click), and secondary input (right click).")]

        [TitleGroup("Pointer World Tracker")]
        [SerializeField, Tooltip("Hide the OS mouse cursor while this component is active.")]
        bool hideCursor = true;

        [TitleGroup("Pointer World Tracker")]
        [SerializeField, Tooltip("Camera for screen-to-world conversion. Falls back to Camera.main if empty.")]
        Camera targetCamera;

        [TitleGroup("Pointer World Tracker")]
        [SerializeField, Tooltip("World-space Z depth for the projected cursor position.")]
        float worldZ = 0f;

        // ── Input References ────────────────────────────────────────────────

        [TitleGroup("Input")]
        [SerializeField, Required]
        [Tooltip("Pointer position action — typically bound to <Pointer>/position. " +
                 "Reads a Vector2 screen position each frame.")]
        InputActionReference pointerPositionAction;

        [TitleGroup("Input")]
        [SerializeField]
        [Tooltip("Primary input action (e.g. left click). Emits onPrimaryInput with " +
                 "the current world position when performed.")]
        InputActionReference primaryInputAction;

        [TitleGroup("Input")]
        [SerializeField]
        [Tooltip("Secondary input action (e.g. right click). Emits onSecondaryInput with " +
                 "the current world position when performed.")]
        InputActionReference secondaryInputAction;

        // ── Events ──────────────────────────────────────────────────────────

        [TitleGroup("Events")]
        [SerializeField, Tooltip("Fired every frame with the updated world position.")]
        public UnityEvent<Vector3> onPositionUpdated;

        [TitleGroup("Events")]
        [SerializeField, Tooltip("Fired when the primary input action is performed (e.g. left click).")]
        public UnityEvent<Vector3> onPrimaryInput;

        [TitleGroup("Events")]
        [SerializeField, Tooltip("Fired when the secondary input action is performed (e.g. right click).")]
        public UnityEvent<Vector3> onSecondaryInput;

        // ── Debug ───────────────────────────────────────────────────────────

        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly]
        public Vector3 WorldPosition { get; private set; }

        // ── Internal ────────────────────────────────────────────────────────

        Camera _cam;
        InputAction _pointerAction;
        InputAction _primaryAction;
        InputAction _secondaryAction;

        // ── Lifecycle ───────────────────────────────────────────────────────

        void Awake()
        {
            _cam = targetCamera ? targetCamera : Camera.main;
        }

        void OnEnable()
        {
            if (hideCursor) Cursor.visible = false;

            // Bind pointer position action — required for screen-to-world conversion.
            if (pointerPositionAction != null)
            {
                _pointerAction = pointerPositionAction.action;
                _pointerAction.Enable();
            }

            // Bind primary input action (e.g. left click).
            if (primaryInputAction != null)
            {
                _primaryAction = primaryInputAction.action;
                _primaryAction.Enable();
                _primaryAction.performed += OnPrimaryPerformed;
                _primaryAction.canceled += OnPrimaryCanceled;
            }

            // Bind secondary input action (e.g. right click).
            if (secondaryInputAction != null)
            {
                _secondaryAction = secondaryInputAction.action;
                _secondaryAction.Enable();
                _secondaryAction.performed += OnSecondaryPerformed;
                _secondaryAction.canceled += OnSecondaryCanceled;
            }
        }

        void OnDisable()
        {
            Cursor.visible = true;

            // Unsubscribe input callbacks.
            if (_primaryAction != null)
            {
                _primaryAction.performed -= OnPrimaryPerformed;
                _primaryAction.canceled -= OnPrimaryCanceled;
            }
            if (_secondaryAction != null)
            {
                _secondaryAction.performed -= OnSecondaryPerformed;
                _secondaryAction.canceled -= OnSecondaryCanceled;
            }
        }

        void Update()
        {
            if (_pointerAction == null) return;

            // Read the pointer's screen position from the Input System action.
            // e.g. <Pointer>/position yields a Vector2 in screen pixels.
            Vector2 screenPos2D = _pointerAction.ReadValue<Vector2>();

            // Project the screen-space pointer position onto the configured Z plane.
            // The Z component of ScreenToWorldPoint controls how far from the
            // camera we sample — e.g. camera at z=-10, worldZ=0 → distance = 10.
            float cameraDistance = Mathf.Abs(_cam.transform.position.z - worldZ);
            Vector3 screenPos = new Vector3(screenPos2D.x, screenPos2D.y, cameraDistance);
            Vector3 worldPos = _cam.ScreenToWorldPoint(screenPos);
            worldPos.z = worldZ;

            // Update held state before broadcasting so continuous listeners
            // see the current frame's input when OnPositionUpdated fires.
            PointerWorldBus.IsPrimaryHeld = _primaryAction?.IsPressed() ?? false;
            PointerWorldBus.IsSecondaryHeld = _secondaryAction?.IsPressed() ?? false;

            // Update tracked position and broadcast.
            WorldPosition = worldPos;
            transform.position = worldPos;
            onPositionUpdated?.Invoke(worldPos);
            PointerWorldBus.PublishPosition(worldPos);
        }

        // ── Input Callbacks ─────────────────────────────────────────────────

        /** Broadcasts the current world position when the primary input fires. */
        void OnPrimaryPerformed(InputAction.CallbackContext ctx)
        {
            onPrimaryInput?.Invoke(WorldPosition);
            PointerWorldBus.PublishPrimary(WorldPosition);
        }

        /** Broadcasts the current world position when the secondary input fires. */
        void OnSecondaryPerformed(InputAction.CallbackContext ctx)
        {
            onSecondaryInput?.Invoke(WorldPosition);
            PointerWorldBus.PublishSecondary(WorldPosition);
        }

        /** Broadcasts the current world position when the primary input is released. */
        void OnPrimaryCanceled(InputAction.CallbackContext ctx)
        {
            PointerWorldBus.PublishPrimaryReleased(WorldPosition);
        }

        /** Broadcasts the current world position when the secondary input is released. */
        void OnSecondaryCanceled(InputAction.CallbackContext ctx)
        {
            PointerWorldBus.PublishSecondaryReleased(WorldPosition);
        }
    }
}
