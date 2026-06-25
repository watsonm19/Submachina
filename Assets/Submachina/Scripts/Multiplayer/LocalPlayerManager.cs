using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Drop-in / drop-out manager for local multiplayer.
     *
     * Owns a pool of pre-placed submarines (each with a SubmarineInputModule) and, once
     * that pool is exhausted, can instantiate more from a player prefab up to a Max Players
     * cap. Wires physical controllers to them on the fly:
     *
     *   • Detects when an *unassigned* device produces a button press
     *     (InputSystem.onAnyButtonPress) and, if a free slot exists, freezes the
     *     game and opens the PlayerJoinOverlay so the player can pick a slot.
     *   • On confirm, enables that slot's submarine, drops it inside the current
     *     camera frame, pairs the device(s) to its input module, and registers it
     *     with the shared camera.
     *   • On device removal (InputSystem.onDeviceChange) the owning player's sub is
     *     disabled and its slot freed; the same device pressing again re-joins
     *     through the normal prompt.
     *
     * Keyboard + mouse are treated as one logical "keyboard player". Follows the
     * project's no-singleton rule — this is a plain scene object discovered by
     * reference, not a global. Major moments fire UnityEvents for Feel/MMF juice.
     */
    public class LocalPlayerManager : MonoBehaviour
    {
        /** One entry in the fixed player pool — a pre-placed (initially disabled) submarine. */
        [Serializable]
        public class PlayerSlot
        {
            [HorizontalGroup, LabelWidth(40)]
            public string label = "Player";

            [HorizontalGroup(width: 60), HideLabel]
            public Color color = Color.white;

            [HorizontalGroup, HideLabel]
            [Tooltip("The submarine root for this slot. Starts disabled; enabled when a player joins.")]
            public GameObject root;

            // Runtime-only
            [NonSerialized] public SubmarineInputModule module;
            [NonSerialized] public bool active;
            [NonSerialized] public bool spawned; // true when created at runtime from the player prefab (vs. pre-placed)
        }

        // =====================
        // Pool
        // =====================

        [BoxGroup("Pool")]
        [Tooltip("The fixed roster of player submarines. Use the button to auto-fill from SubmarineInputModules in the scene.")]
        [SerializeField] private List<PlayerSlot> slots = new();

        [BoxGroup("Pool")]
        [Tooltip("On Awake, replace the slot list with every SubmarineInputModule found in the scene (including disabled ones), " +
                 "so you never have to wire players by hand. Runs before the disable pass below.")]
        [SerializeField] private bool autoDiscoverSlotsOnAwake = true;

        [BoxGroup("Pool")]
        [Tooltip("Disable every slot's submarine on Awake so players must join in (recommended).")]
        [SerializeField] private bool disableSlotsOnAwake = true;

        [BoxGroup("Pool")]
        [AssetsOnly]
        [Tooltip("Optional submarine prefab (must have a SubmarineInputModule). When the pre-placed pool is full, a fresh " +
                 "instance is spawned for each new player up to Max Players. Leave empty to use only the pre-placed pool.")]
        [SerializeField] private GameObject playerPrefab;

        [BoxGroup("Pool")]
        [MinValue(1)]
        [Tooltip("Hard cap on simultaneously active players. Pre-placed slots are used first; prefab instances fill the rest.")]
        [SerializeField] private int maxPlayers = 4;

        // =====================
        // References
        // =====================

        [BoxGroup("References")]
        [Tooltip("Overlay shown while assigning a controller to a player.")]
        [SerializeField] private PlayerJoinOverlay joinOverlay;

        [BoxGroup("References")]
        [Tooltip("Shared camera rig used to frame players and to place drop-ins inside the view. Optional — falls back to Camera.main.")]
        [SerializeField] private MultiTargetCamera2D cameraRig;

        // =====================
        // Behaviour
        // =====================

        [BoxGroup("Behaviour")]
        [Tooltip("Freeze the game (Time.timeScale = 0) while the join overlay is open.")]
        [SerializeField] private bool freezeDuringAssignment = true;

        [BoxGroup("Behaviour")]
        [Tooltip("Horizontal distance from an existing player at which a drop-in appears (then clamped into the camera frame).")]
        [SerializeField] private float dropInOffset = 3f;

        [BoxGroup("Behaviour")]
        [Tooltip("When a prefab-spawned player drops out, destroy its submarine instead of keeping it disabled for reuse. " +
                 "Off (recommended) pools spawned subs so a re-join reuses the same instance without re-instantiating.")]
        [SerializeField] private bool destroySpawnedOnLeave = false;

        // =====================
        // Events (hook Feel/MMF here)
        // =====================

        [FoldoutGroup("Events")]
        public UnityEvent onJoinPromptOpened = new();
        [FoldoutGroup("Events")]
        public UnityEvent onJoinPromptClosed = new();
        [FoldoutGroup("Events")]
        public UnityEvent<int> onPlayerJoined = new();
        [FoldoutGroup("Events")]
        public UnityEvent<int> onPlayerLeft = new();

        // =====================
        // State
        // =====================

        private readonly Dictionary<int, PlayerSlot> _deviceToSlot = new();
        private readonly List<InputDevice> _pendingDevices = new();
        private List<PlayerJoinOverlay.SlotInfo> _offered = new(); // slot options shown in the current prompt (free pool + spawnable)
        private IDisposable _anyButtonSubscription;
        private bool _assigning;
        private float _prevTimeScale = 1f;

        [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
        private int ActivePlayers
        {
            get
            {
                int n = 0;
                for (int i = 0; i < slots.Count; i++) if (slots[i].active) n++;
                return n;
            }
        }

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        /**
         * Caches each slot's input module, stops it from self-assigning a device
         * (the manager owns assignment now), and disables the pool so players join in.
         */
        private void Awake()
        {
            // Optionally build the roster from the scene first, so players don't need manual wiring
            if (autoDiscoverSlotsOnAwake) DiscoverSlots();

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot.root == null) continue;

                // The manager drives device pairing — disable the module's enum auto-assign
                slot.module = slot.root.GetComponent<SubmarineInputModule>();
                if (slot.module != null) slot.module.AutoAssignOnAwake = false;

                // Start disabled so nothing is controllable until a controller joins
                if (disableSlotsOnAwake) slot.root.SetActive(false);
                slot.active = false;
            }
        }

        private void OnEnable()
        {
            // Listen for any unassigned controller wanting in, and for device add/remove
            _anyButtonSubscription = InputSystem.onAnyButtonPress.Call(OnAnyButtonPressed);
            InputSystem.onDeviceChange += OnDeviceChange;
        }

        private void OnDisable()
        {
            // Drop subscriptions and make sure we never leave the game frozen
            _anyButtonSubscription?.Dispose();
            _anyButtonSubscription = null;
            InputSystem.onDeviceChange -= OnDeviceChange;

            if (_assigning) EndAssignment();
        }

        // -------------------------------------------------------
        // Join detection
        // -------------------------------------------------------

        /**
         * Fired by the Input System whenever any button-like control is pressed.
         * Ignores presses from devices already paired to a player (that's gameplay)
         * and only reacts to a fresh keyboard/mouse/gamepad when a slot is free.
         */
        private void OnAnyButtonPressed(InputControl control)
        {
            // The overlay handles its own input while a prompt is open
            if (_assigning) return;

            var device = control?.device;
            if (device == null) return;

            // Only these device classes can drive a player
            bool joinable = device is Gamepad || device is Keyboard || device is Mouse;
            if (!joinable) return;

            // Already-paired device → normal gameplay input, not a join
            if (_deviceToSlot.ContainsKey(device.deviceId)) return;

            // No free slot and nothing left to spawn → ignore the press
            if (!CanAcceptNewPlayer()) return;

            BeginJoin(device);
        }

        /**
         * Opens the assignment flow for a new device: pairs keyboard+mouse together,
         * freezes the game, and shows the overlay listing every free slot.
         */
        private void BeginJoin(InputDevice trigger)
        {
            // Build the logical device set (keyboard and mouse act as one player)
            _pendingDevices.Clear();
            if (trigger is Keyboard || trigger is Mouse)
            {
                if (Keyboard.current != null) _pendingDevices.Add(Keyboard.current);
                if (Mouse.current != null) _pendingDevices.Add(Mouse.current);
            }
            else
            {
                _pendingDevices.Add(trigger);
            }

            // Freeze gameplay so the assignment isn't happening mid-action
            _prevTimeScale = Time.timeScale;
            if (freezeDuringAssignment) Time.timeScale = 0f;
            _assigning = true;

            // Hand the free slots to the overlay and wait for the player's choice
            // (cached so OnJoinConfirmed can recover the picked label/colour for a spawn).
            _offered = BuildFreeSlotInfos();
            if (joinOverlay != null)
                joinOverlay.Open(_offered, _pendingDevices, OnJoinConfirmed, OnJoinCancelled);
            else
                Debug.LogWarning("[LocalPlayerManager] No PlayerJoinOverlay assigned — cannot show join prompt.");

            onJoinPromptOpened.Invoke();
        }

        // -------------------------------------------------------
        // Join resolution
        // -------------------------------------------------------

        /** Overlay callback: the player picked a slot — resolve it (spawning if needed) and activate. */
        private void OnJoinConfirmed(int slotIndex)
        {
            // A non-negative index is a real pool slot; a negative one means "spawn a new prefab"
            PlayerSlot slot = ResolveConfirmedSlot(slotIndex);
            if (slot != null) ActivatePlayer(slot, _pendingDevices);

            EndAssignment();
            onPlayerJoined.Invoke(slot != null ? slots.IndexOf(slot) : slotIndex);
        }

        /**
         * Maps an overlay choice back to a concrete slot: a pre-placed/pooled slot for a
         * non-negative index, or a freshly instantiated prefab slot for a spawn sentinel
         * (negative index), recovering the label/colour the player picked from the offered list.
         */
        private PlayerSlot ResolveConfirmedSlot(int slotIndex)
        {
            // Existing pool slot chosen
            if (slotIndex >= 0 && slotIndex < slots.Count) return slots[slotIndex];

            // Spawn sentinel — find the matching offered entry and build a new slot from the prefab
            for (int i = 0; i < _offered.Count; i++)
                if (_offered[i].Index == slotIndex) return SpawnSlot(_offered[i]);
            return null;
        }

        /**
         * Instantiates the player prefab as a brand-new pool slot (a drop-in beyond the
         * pre-placed roster). Created disabled so AutoAssignOnAwake is cleared before the
         * sub's Awake runs — mirroring the pre-placed path — then ActivatePlayer positions,
         * enables, and pairs it. The slot is added to the pool so a later re-join reuses it.
         */
        private PlayerSlot SpawnSlot(PlayerJoinOverlay.SlotInfo info)
        {
            if (playerPrefab == null) return null;

            // Instantiate inactive so we can configure the module before any Awake/OnEnable fires
            bool prefabActive = playerPrefab.activeSelf;
            if (prefabActive) playerPrefab.SetActive(false);
            GameObject go = Instantiate(playerPrefab);
            if (prefabActive) playerPrefab.SetActive(true);

            // Name and register it as a real slot so drop-out/re-join can pool the instance
            go.name = string.IsNullOrEmpty(info.Label) ? $"Player {slots.Count + 1}" : info.Label;
            var slot = new PlayerSlot
            {
                label = go.name,
                color = info.Color,
                root = go,
                spawned = true
            };

            // The manager owns device pairing — stop the module self-assigning on enable
            slot.module = go.GetComponent<SubmarineInputModule>();
            if (slot.module != null) slot.module.AutoAssignOnAwake = false;

            slots.Add(slot);
            return slot;
        }

        /** Overlay callback: the player backed out — just unfreeze and close. */
        private void OnJoinCancelled()
        {
            EndAssignment();
        }

        /** Restores time scale and clears the pending state after the overlay closes. */
        private void EndAssignment()
        {
            if (joinOverlay != null && joinOverlay.IsOpen) joinOverlay.Close();

            if (freezeDuringAssignment) Time.timeScale = _prevTimeScale;
            _assigning = false;
            _pendingDevices.Clear();

            onJoinPromptClosed.Invoke();
        }

        /**
         * Enables a slot's submarine, places it inside the current camera frame,
         * pairs the device(s) to its input module, and registers it with the camera.
         * Order matters: position + SetActive (runs Awake) before AssignDevices so
         * the module's clone exists when we restrict its devices.
         */
        private void ActivatePlayer(PlayerSlot slot, List<InputDevice> devices)
        {
            if (slot.root == null) return;

            // Drop the sub inside the current view, next to an existing player if any
            slot.root.transform.position = ResolveSpawnPosition(slot);

            // Enable the GameObject (Awake builds the input clone) then pair devices
            slot.root.SetActive(true);
            if (slot.module == null) slot.module = slot.root.GetComponent<SubmarineInputModule>();
            slot.module?.AssignDevices(devices);

            // Frame the new player and remember which devices map to this slot
            if (cameraRig != null)
            {
                cameraRig.Register(slot.root.transform);
                cameraRig.SnapToTargets();
            }
            for (int i = 0; i < devices.Count; i++)
                _deviceToSlot[devices[i].deviceId] = slot;

            slot.active = true;
        }

        // -------------------------------------------------------
        // Drop-out
        // -------------------------------------------------------

        /**
         * Reacts to device hot-plugging. When a paired device is removed/disconnected,
         * its player drops out (sub disabled, slot freed). A removed *pending* device
         * cancels an in-progress join.
         */
        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (change != InputDeviceChange.Removed && change != InputDeviceChange.Disconnected) return;

            // If the device that triggered an open prompt vanished, abort the join
            if (_assigning && _pendingDevices.Contains(device))
            {
                OnJoinCancelled();
                return;
            }

            // Otherwise drop the player that owned it
            if (_deviceToSlot.TryGetValue(device.deviceId, out var slot))
                DeactivatePlayer(slot);
        }

        /**
         * Drops a player out: releases its devices, disables the submarine, removes it
         * from the camera, and frees the slot for a future re-join.
         */
        private void DeactivatePlayer(PlayerSlot slot)
        {
            // Release input and hide the sub
            slot.module?.Unassign();
            if (slot.root != null)
            {
                if (cameraRig != null) cameraRig.Unregister(slot.root.transform);
                slot.root.SetActive(false);
            }

            // Clear every device mapping that pointed at this slot
            var stale = new List<int>();
            foreach (var kv in _deviceToSlot)
                if (kv.Value == slot) stale.Add(kv.Key);
            for (int i = 0; i < stale.Count; i++)
                _deviceToSlot.Remove(stale[i]);

            slot.active = false;
            onPlayerLeft.Invoke(slots.IndexOf(slot));

            // Spawned subs are pooled (disabled) for reuse by default; optionally tear them down entirely
            if (slot.spawned && destroySpawnedOnLeave)
            {
                slots.Remove(slot);
                if (slot.root != null) Destroy(slot.root);
            }
        }

        // -------------------------------------------------------
        // Helpers
        // -------------------------------------------------------

        /** Returns the first inactive slot with a valid root, or null when the pool is full. */
        private PlayerSlot FindFreeSlot()
        {
            for (int i = 0; i < slots.Count; i++)
                if (!slots[i].active && slots[i].root != null) return slots[i];
            return null;
        }

        /**
         * True when another player can join: under the Max Players cap and either a free
         * pool slot exists or a prefab is available to spawn a fresh one.
         */
        private bool CanAcceptNewPlayer()
        {
            if (ActivePlayers >= maxPlayers) return false;
            return FindFreeSlot() != null || playerPrefab != null;
        }

        /**
         * Builds the overlay's selectable options: every free pool slot (tagged with its real
         * pool index), then — if a prefab is set — extra "spawn a new sub" options filling the
         * remaining capacity up to Max Players, each tagged with a negative sentinel index.
         */
        private List<PlayerJoinOverlay.SlotInfo> BuildFreeSlotInfos()
        {
            var list = new List<PlayerJoinOverlay.SlotInfo>();

            // Free pre-placed / previously-spawned pool slots, keyed by their real index
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].active || slots[i].root == null) continue;
                list.Add(new PlayerJoinOverlay.SlotInfo
                {
                    Index = i,
                    Label = string.IsNullOrEmpty(slots[i].label) ? $"Player {i + 1}" : slots[i].label,
                    Color = slots[i].color
                });
            }

            // Spawnable prefab options fill whatever capacity the free pool slots don't cover
            if (playerPrefab != null)
            {
                int remaining = maxPlayers - ActivePlayers - list.Count;
                for (int s = 0; s < remaining; s++)
                {
                    int ordinal = slots.Count + s; // colour/label continue past the existing roster
                    list.Add(new PlayerJoinOverlay.SlotInfo
                    {
                        Index = -(s + 1),                              // negative ⇒ spawn a new prefab on confirm
                        Label = $"Player {ordinal + 1}",
                        Color = Color.HSVToRGB((ordinal * 0.18f) % 1f, 0.7f, 1f)
                    });
                }
            }

            return list;
        }

        /**
         * Picks where a joining sub appears: offset from an existing active player so
         * they spawn side-by-side, then clamped into the camera frame so it's on screen.
         * With no active players yet, uses the camera centre.
         */
        private Vector3 ResolveSpawnPosition(PlayerSlot joining)
        {
            // Anchor on the first other active player if one exists
            Vector3 anchor = ViewCentre();
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] == joining || !slots[i].active || slots[i].root == null) continue;
                anchor = slots[i].root.transform.position + new Vector3(dropInOffset, 0f, 0f);
                break;
            }

            // Keep Z consistent with the joining sub and clamp inside the visible frame
            anchor.z = joining.root.transform.position.z;
            if (cameraRig != null) anchor = cameraRig.ClampIntoView(anchor);
            return anchor;
        }

        /** Best available view centre: the rig if present, else the main camera, else origin. */
        private Vector3 ViewCentre()
        {
            if (cameraRig != null) return cameraRig.ViewCentre;
            if (Camera.main != null)
            {
                var p = Camera.main.transform.position;
                return new Vector3(p.x, p.y, 0f);
            }
            return Vector3.zero;
        }

        /**
         * Rebuilds the slot list from every SubmarineInputModule in the scene
         * (including disabled ones), assigning default labels and spaced-out colours.
         * Runtime-safe — shared by the Awake auto-discover path and the editor button.
         */
        private void DiscoverSlots()
        {
            var modules = UnityEngine.Object.FindObjectsByType<SubmarineInputModule>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            slots.Clear();
            for (int i = 0; i < modules.Length; i++)
            {
                slots.Add(new PlayerSlot
                {
                    label = $"Player {i + 1}",
                    color = Color.HSVToRGB((i * 0.18f) % 1f, 0.7f, 1f),
                    root = modules[i].gameObject
                });
            }
        }

        // -------------------------------------------------------
        // Editor utilities
        // -------------------------------------------------------

#if UNITY_EDITOR
        /** Editor convenience: populate the slot list now so it's visible/serialized before play. */
        [BoxGroup("Pool")]
        [Button("Auto-Find Player Slots"), GUIColor(0.6f, 0.85f, 1f)]
        private void AutoFindSlots()
        {
            DiscoverSlots();
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
