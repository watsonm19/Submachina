using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Per-player input isolation for local multiplayer.
     *
     * Lives on the submarine root alongside Submarine. Clones the shared
     * InputActionAsset at runtime and restricts it to specific devices
     * (keyboard+mouse, gamepad 1, gamepad 2, etc.). Subsystems resolve
     * their actions through SubmarineComponent.ResolveAction, which
     * delegates here when a module is present.
     *
     * Without this component, subsystems use the shared InputActionReference
     * directly — standard single-player behaviour, fully backward-compatible.
     */
    [DefaultExecutionOrder(-100)]
    public class SubmarineInputModule : MonoBehaviour
    {
        public enum DeviceMode
        {
            [LabelText("Keyboard + Mouse")]
            KeyboardAndMouse,

            [LabelText("Gamepad 1")]
            Gamepad1,

            [LabelText("Gamepad 2")]
            Gamepad2
        }

        // =====================
        // Configuration
        // =====================

        [Tooltip("The shared PlayerControls InputActionAsset. Each player gets an independent clone at runtime.")]
        [SerializeField, Required]
        private InputActionAsset sharedActionAsset;

        [Tooltip("Which physical device(s) this player uses.")]
        [SerializeField]
        private DeviceMode deviceMode = DeviceMode.KeyboardAndMouse;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private string PairedDevices => _ownedActions?.devices != null
            ? string.Join(", ", _ownedActions.devices)
            : "None";

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private bool IsActive => _ownedActions != null && _ownedActions.enabled;

        // =====================
        // State
        // =====================

        private InputActionAsset _ownedActions;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            // Clone the shared asset so this submarine has independent actions
            _ownedActions = Instantiate(sharedActionAsset);

            // Restrict input to the assigned device(s)
            AssignDevices();

            _ownedActions.Enable();
        }

        private void OnDestroy()
        {
            if (_ownedActions != null)
            {
                _ownedActions.Disable();
                Destroy(_ownedActions);
            }
        }

        // -------------------------------------------------------
        // Public API
        // -------------------------------------------------------

        /** True when this player's device set includes a mouse (i.e. keyboard+mouse mode). */
        public bool HasMouse => deviceMode == DeviceMode.KeyboardAndMouse;

        /**
         * Returns the per-player InputAction matching the given name.
         * Called by SubmarineComponent.ResolveAction to swap shared
         * references for player-local ones.
         */
        public InputAction FindAction(string actionName)
        {
            return _ownedActions?.FindAction(actionName);
        }

        // -------------------------------------------------------
        // Device Assignment
        // -------------------------------------------------------

        private void AssignDevices()
        {
            switch (deviceMode)
            {
                case DeviceMode.KeyboardAndMouse:
                    var kbmDevices = new List<InputDevice>();
                    if (Keyboard.current != null) kbmDevices.Add(Keyboard.current);
                    if (Mouse.current != null) kbmDevices.Add(Mouse.current);
                    _ownedActions.devices = new ReadOnlyArray<InputDevice>(kbmDevices.ToArray());
                    break;

                case DeviceMode.Gamepad1:
                    if (Gamepad.all.Count > 0)
                        _ownedActions.devices = new ReadOnlyArray<InputDevice>(new[] { Gamepad.all[0] });
                    else
                        Debug.LogWarning($"[SubmarineInputModule] No gamepad found for {deviceMode}");
                    break;

                case DeviceMode.Gamepad2:
                    if (Gamepad.all.Count > 1)
                        _ownedActions.devices = new ReadOnlyArray<InputDevice>(new[] { Gamepad.all[1] });
                    else
                        Debug.LogWarning($"[SubmarineInputModule] Not enough gamepads for {deviceMode}");
                    break;
            }
        }
    }
}
