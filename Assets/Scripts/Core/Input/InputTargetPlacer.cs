using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using Sirenix.OdinInspector;

namespace Gameplay.Pointer
{
    /**
     * Places this transform at a position source's location when an input
     * action fires. Designed as a composable bridge between input and the
     * follower system — the follower targets this object, and this object
     * only moves when the player commands it.
     *
     * Typical setup:
     *   PointerWorldTracker (always tracks cursor)
     *     ↓ positionSource
     *   InputTargetPlacer (stamps position on input)
     *     ↓ target
     *   SteeringFollower / SmoothFollower (chases the stamped position)
     *
     * The position source can be any Transform — mouse tracker, gamepad
     * cursor, AI waypoint, touch position, etc.
     */
    [AddComponentMenu("Gameplay/Input/Input Target Placer")]
    public class InputTargetPlacer : MonoBehaviour
    {
        // ── Input ───────────────────────────────────────────────────────────

        [TitleGroup("Input")]
        [SerializeField, Required]
        [Tooltip("The input action that triggers placement. Fires on the action's " +
                 "performed phase (button press, not release).")]
        InputActionReference inputAction;

        [TitleGroup("Input")]
        [SerializeField]
        [Tooltip("If true, continuously updates position while the input is held. " +
                 "If false, stamps the position once on press.")]
        bool continuousWhileHeld;

        // ── Position Source ─────────────────────────────────────────────────

        [TitleGroup("Position Source")]
        [SerializeField, Required]
        [Tooltip("The transform whose position is copied on input. " +
                 "Typically a PointerWorldTracker or other cursor proxy.")]
        Transform positionSource;

        [TitleGroup("Position Source")]
        [SerializeField]
        [Tooltip("If true, preserves this object's Z position instead of " +
                 "copying the source's Z. Useful for 2D setups.")]
        bool preserveZ = true;

        // ── Events ──────────────────────────────────────────────────────────

        [TitleGroup("Events")]
        [SerializeField]
        [Tooltip("Fired each time the input places (or re-places) the target.")]
        UnityEvent onTargetPlaced;

        // ── Runtime State ───────────────────────────────────────────────────

        [TitleGroup("Runtime State")]
        [ShowInInspector, ReadOnly, LabelText("Placed Position")]
        public Vector3 PlacedPosition { get; private set; }

        [TitleGroup("Runtime State")]
        [ShowInInspector, ReadOnly, LabelText("Is Held")]
        bool IsHeldDebug => _isHeld;

        // ── Internal ────────────────────────────────────────────────────────

        InputAction _action;
        bool _isHeld;

        // ── Lifecycle ───────────────────────────────────────────────────────

        void OnEnable()
        {
            if (inputAction == null) return;

            _action = inputAction.action;
            _action.Enable();
            _action.performed += OnPerformed;
            _action.canceled += OnCanceled;
        }

        void OnDisable()
        {
            if (_action == null) return;

            _action.performed -= OnPerformed;
            _action.canceled -= OnCanceled;
        }

        void Update()
        {
            // Continuous mode: re-stamp position every frame while held
            if (continuousWhileHeld && _isHeld) PlaceAtSource();
        }

        // ── Input Callbacks ─────────────────────────────────────────────────

        void OnPerformed(InputAction.CallbackContext ctx)
        {
            _isHeld = true;
            PlaceAtSource();
        }

        void OnCanceled(InputAction.CallbackContext ctx)
        {
            _isHeld = false;
        }

        // ── Placement ───────────────────────────────────────────────────────

        /** Copies the position source's world position to this transform. */
        void PlaceAtSource()
        {
            if (!positionSource) return;

            Vector3 pos = positionSource.position;
            if (preserveZ) pos.z = transform.position.z;

            transform.position = pos;
            PlacedPosition = pos;
            onTargetPlaced?.Invoke();
        }

        // ── Editor Controls ─────────────────────────────────────────────────

        [TitleGroup("Editor Controls", order: -1)]
        [InfoBox("All fields are live. Use the button below to test placement without input.")]

        // Manually trigger placement for testing.
        [TitleGroup("Editor Controls")]
        [Button("Place Now"), GUIColor(0.7f, 1f, 0.8f)]
        void PlaceNow()
        {
            PlaceAtSource();
        }
    }
}
