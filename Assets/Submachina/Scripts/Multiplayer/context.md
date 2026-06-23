# Local Multiplayer (drop-in / drop-out)

This folder holds the **runtime join system** for couch co-op: detecting unassigned
controllers, assigning them to players, framing everyone on one screen, and handling
players coming and going. It builds on the per-player input isolation in
`../Submarine/SubmarineInputModule.cs` (see `../Submarine/context.md` → *Local multiplayer input*).

The whole system follows the project's **no-singleton** rule — these are plain scene
objects wired by reference, not globals.

## Pieces

- **LocalPlayerManager** (`LocalPlayerManager.cs`) — the orchestrator. Owns a fixed
  **pool** of pre-placed submarines (`PlayerSlot { label, color, root }`, each carrying a
  `SubmarineInputModule`). On `Awake` it disables every slot and flips each module's
  `AutoAssignOnAwake` off so the manager — not the enum — owns device pairing.
  - **Join detection:** subscribes to `InputSystem.onAnyButtonPress` (an
    `IObservable<InputControl>`; `.Call(...)` lives in `UnityEngine.InputSystem.Utilities`).
    A press from a device that no slot owns, while a slot is free, triggers a join.
    Presses from already-paired devices fall through as normal gameplay.
  - **Assign flow:** `BeginJoin` pairs keyboard+mouse as one logical player, freezes the
    game (`Time.timeScale = 0`, configurable), and opens the overlay with the free slots.
    On confirm, `ActivatePlayer` positions the sub in-frame, `SetActive(true)` (which runs
    the module's `Awake` so its clone exists), then calls `module.AssignDevices(...)`,
    registers the sub with the camera, and maps each `device.deviceId → slot`.
  - **Drop-out:** `InputSystem.onDeviceChange` (Removed/Disconnected) finds the owning slot
    by device id, calls `module.Unassign()`, disables the sub, unregisters it from the
    camera, and frees the slot. A removed device that was mid-join cancels the prompt.
    Re-joining is just another button press — bindings are **not** persisted across a drop,
    by design (a returning controller is offered assignment again).
  - **Events** (`onJoinPromptOpened/Closed`, `onPlayerJoined/Left(int)`) for Feel/MMF juice.
  - Editor: `[Button] Auto-Find Player Slots` fills the pool from every
    `SubmarineInputModule` in the scene (including disabled ones).

- **PlayerJoinOverlay** (`PlayerJoinOverlay.cs`) — the assignment UI. Built **from code**
  on first use (Canvas + TMP + an `EventSystem` with `InputSystemUIInputModule` if none
  exists), so it's drop-in; colours and font sizes are exposed for restyling. `Open(slots,
  devices, onConfirm, onCancel)` lists each free slot as a button. The **joining device**
  drives selection (D-pad/stick/arrows to move the highlight, south/Start/Enter/Space to
  confirm, east/Esc to cancel); the mouse can also click a slot. All timing uses
  **unscaled time** because the game is frozen while it's open. UnityEvents fire at each
  step (`onOpened`, `onHighlightChanged`, `onConfirmed`, `onCancelled`, `onClosed`).

- **MultiTargetCamera2D** (`MultiTargetCamera2D.cs`) — the shared 2D camera. Follows the
  **centroid** of all registered players and eases `orthographicSize` to **fit** them with
  padding, clamped to `[minSize, maxSize]`; one player behaves like a normal follow cam,
  several pull the view back. `Register`/`Unregister` are called by the manager on
  join/drop. `ClampIntoView(worldPos, padding)` is what keeps a drop-in **inside the frame**
  — the manager offsets a joiner beside an existing player, then clamps. Coexists with the
  single-target `Core/CameraFollow`; use one or the other. Runs in `LateUpdate` on unscaled
  time so framing keeps working during the freeze.

## Scene setup

1. Place the player submarines in the scene (each with `Submarine` + `SubmarineInputModule`
   + the shared `PlayerControls` asset). They can be left enabled in edit mode; the manager
   disables them on `Awake`.
2. Add an empty `LocalPlayerManager`, press **Auto-Find Player Slots**, set per-slot
   labels/colours, and assign the overlay + camera references.
3. Add `PlayerJoinOverlay` anywhere (it builds its own canvas).
4. Put `MultiTargetCamera2D` on the gameplay camera (orthographic) instead of / alongside
   `CameraFollow`.
5. Press Play and tap any key / gamepad button to join.

## Notes & limits

- Mouse aiming remains exclusive to the player who owns the mouse (`SubmarineInputModule.HasMouse`
  now checks the paired device set, not just the enum). Two keyboard-only players aren't
  supported (one keyboard = one player); add more gamepads for more players.
- The pool is fixed-size — max players = slot count. A full pool ignores further join presses.
- Camera is a single shared view (frame-all). Splitscreen is not implemented.
