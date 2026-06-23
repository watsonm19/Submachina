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
     * InputActionAsset at runtime and restricts it to a specific set of
     * physical devices. Subsystems resolve their actions through
     * SubmarineComponent.ResolveAction, which delegates here when present.
     *
     * Two ways to assign devices:
     *   1. Device-instance pairing (preferred for drop-in/out) — LocalPlayerManager
     *      calls AssignDevices(...) with the exact InputDevices a player owns.
     *      This survives gamepad reordering and supports any number of pads.
     *   2. DeviceMode enum (single-player / quick editor setup) — when
     *      autoAssignOnAwake is true the module grabs devices matching the
     *      chosen mode on Awake, exactly like the original behaviour.
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

        [Tooltip("When true, the module grabs devices matching Device Mode on Awake (single-player / quick setup). " +
                 "LocalPlayerManager sets this false so it can assign exact device instances on join.")]
        [SerializeField]
        private bool autoAssignOnAwake = true;

        [Tooltip("Which physical device(s) this player uses in the enum-based path. Change at runtime and press Apply to hot-swap.")]
        [SerializeField, OnValueChanged("OnDeviceModeChanged"), ShowIf("autoAssignOnAwake")]
        private DeviceMode deviceMode = DeviceMode.KeyboardAndMouse;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private string PairedDevices => _assignedDevices.Count > 0
            ? string.Join(", ", _assignedDevices.ConvertAll(d => d.displayName))
            : "None";

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private bool IsActive => _ownedActions != null && _ownedActions.enabled;

        // =====================
        // State
        // =====================

        private InputActionAsset _ownedActions;
        private readonly List<InputDevice> _assignedDevices = new();

        // =====================
        // Public API — accessors
        // =====================

        /** True when this player has a mouse paired (enables mouse aiming). */
        public bool HasMouse => _assignedDevices.Count > 0
            ? _assignedDevices.Exists(d => d is Mouse)
            : deviceMode == DeviceMode.KeyboardAndMouse;

        /** The devices currently paired to this player. Empty when unassigned. */
        public IReadOnlyList<InputDevice> AssignedDevices => _assignedDevices;

        /** When true the module self-assigns from DeviceMode on Awake; LocalPlayerManager sets this false. */
        public bool AutoAssignOnAwake
        {
            get => autoAssignOnAwake;
            set => autoAssignOnAwake = value;
        }

        /** True if the given device is one this player owns — used to route gameplay vs. join presses. */
        public bool OwnsDevice(InputDevice device)
        {
            return device != null && _assignedDevices.Contains(device);
        }

        /** Id-based variant, used when matching a removed device against its (now stale) instance. */
        public bool OwnsDevice(int deviceId)
        {
            for (int i = 0; i < _assignedDevices.Count; i++)
                if (_assignedDevices[i].deviceId == deviceId) return true;
            return false;
        }

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            // Always have a clone ready so subsystems can resolve actions immediately
            EnsureClone();

            // Single-player / quick-setup path: grab devices for the chosen mode now
            if (autoAssignOnAwake)
                AssignFromDeviceMode();
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
        // Public API — action resolution
        // -------------------------------------------------------

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
        // Public API — device assignment
        // -------------------------------------------------------

        /**
         * Pairs this player to an explicit set of devices (the drop-in path).
         * Restricts the cloned asset to exactly those devices, enables it, and
         * notifies all child input components to re-resolve their actions.
         * Pass keyboard + mouse together for a "keyboard player".
         */
        public void AssignDevices(IReadOnlyList<InputDevice> devices)
        {
            EnsureClone();

            // Record the new device set
            _assignedDevices.Clear();
            if (devices != null)
            {
                for (int i = 0; i < devices.Count; i++)
                    if (devices[i] != null) _assignedDevices.Add(devices[i]);
            }

            // Restrict input to just these devices and (re)enable
            _ownedActions.devices = new ReadOnlyArray<InputDevice>(_assignedDevices.ToArray());
            _ownedActions.Enable();

            // Tell every input subsystem under this sub to re-resolve against the fresh device set
            RebindChildren();
        }

        /**
         * Releases all devices and disables this player's input (the drop-out path).
         * The clone is kept so a later AssignDevices re-binds without re-cloning.
         */
        public void Unassign()
        {
            _assignedDevices.Clear();

            if (_ownedActions != null)
            {
                _ownedActions.devices = new ReadOnlyArray<InputDevice>(System.Array.Empty<InputDevice>());
                _ownedActions.Disable();
            }
        }

        /**
         * Switches this submarine to a DeviceMode preset at runtime (enum path).
         * Kept for single-player and quick editor hot-swapping.
         */
        public void Reassign(DeviceMode newMode)
        {
            deviceMode = newMode;
            AssignFromDeviceMode();
        }

        // -------------------------------------------------------
        // Internals
        // -------------------------------------------------------

        /** Lazily clones the shared asset so this submarine has independent actions. */
        private void EnsureClone()
        {
            if (_ownedActions != null) return;
            _ownedActions = Instantiate(sharedActionAsset);
        }

        /** Resolves the DeviceMode preset to concrete devices and assigns them. */
        private void AssignFromDeviceMode()
        {
            var devices = new List<InputDevice>();

            switch (deviceMode)
            {
                // Keyboard + mouse act as one logical player
                case DeviceMode.KeyboardAndMouse:
                    if (Keyboard.current != null) devices.Add(Keyboard.current);
                    if (Mouse.current != null) devices.Add(Mouse.current);
                    break;

                // First connected gamepad
                case DeviceMode.Gamepad1:
                    if (Gamepad.all.Count > 0) devices.Add(Gamepad.all[0]);
                    else Debug.LogWarning($"[SubmarineInputModule] No gamepad found for {deviceMode}");
                    break;

                // Second connected gamepad
                case DeviceMode.Gamepad2:
                    if (Gamepad.all.Count > 1) devices.Add(Gamepad.all[1]);
                    else Debug.LogWarning($"[SubmarineInputModule] Not enough gamepads for {deviceMode}");
                    break;
            }

            AssignDevices(devices);
        }

        /** Notifies all input components under this submarine to re-resolve their actions. */
        private void RebindChildren()
        {
            var inputComponents = GetComponentsInChildren<InputSubmarineComponent>(true);
            for (int i = 0; i < inputComponents.Length; i++)
                inputComponents[i].RebindActions();
        }

        /** Called by Odin [OnValueChanged] when deviceMode is changed in the inspector at runtime. */
        private void OnDeviceModeChanged()
        {
            if (!Application.isPlaying || _ownedActions == null) return;
            AssignFromDeviceMode();
        }
    }
}
