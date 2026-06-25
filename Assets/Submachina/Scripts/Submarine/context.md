# Submarine Systems

This folder holds the **submarine facade**, its **config-driven assembly**, and the **semantic feedback system**. The actual subsystems, feedback definitions, and HUD live in subfolders:

- **`SubFeedbacks/context.md`** — the `FeedbackId` type, `SubFeedbacks` partial-class key registry, `UsesFeedbacksAttribute`, and the Odin editor drawer.
- **`SubAnchors/context.md`** — the `AnchorId` type, `SubAnchors` partial-class key registry, `UsesAnchorsAttribute`, and the Odin editor drawer (the **mount-point** mirror of `SubFeedbacks`).
- **`SubSystems/context.md`** — every `SubmarineComponent` subsystem (O2, physics, turret, weapons, abilities, pumps, scrap/resources) plus the pump, feedback, and anchor routers.
- **`SubUI/context.md`** — the HUD bars/displays.

## Facade pattern

Every subsystem extends **SubmarineComponent**, which auto-discovers its parent **Submarine** via `GetComponentInParent<Submarine>()` and registers itself in `Awake`. Subsystems then reach siblings through typed accessors on the facade — `Sub.O2`, `Sub.Physics`, `Sub.Turret`, `Sub.Resources`, `Sub.Scrap`, `Sub.Feedbacks`, `Sub.Pumps`, `Sub.Anchors`, `Sub.Health` — with no hand-wired serialized references.

**Key types:**
- **Submarine** (`Submarine.cs`) — MonoBehaviour facade on the submarine root. Holds typed references to subsystems, populated by auto-registration (pattern-matched in `Register`/`Unregister`). Maintains `static List<Submarine> All` and `static FindNearest(worldPosition)` for external systems. `Build(SubmarineConfig)` assembles a full loadout from config.
- **SubmarineConfig** (`SubmarineConfig.cs`) — ScriptableObject (`Submachina/Submarine Config`) defining composition via prefab slots: core (`hullPrefab`, `o2SystemPrefab`, `propulsionPrefab`, `turretPrefab`) plus modular `weapons` / `abilities` / `utilities` lists. Consumed by `Submarine.Build()`; each prefab's `SubmarineComponent`s auto-register on instantiation.

**External entities** resolve submarines from context, never via singletons:
- Enemies find the nearest sub via `Submarine.FindNearest(position)` / `Submarine.All`.
- O2Pickups resolve the collecting sub from collision: `other.GetComponentInParent<Submarine>()`.
- MiningResources receive the collecting sub from `MiningLaser`'s `Collect(Sub)` call.

**Supports multiple submarines** (local multiplayer) — discovery is by hierarchy and the static list, with no singletons.

## Local multiplayer input

**SubmarineInputModule** (`SubmarineInputModule.cs`) — MonoBehaviour on the submarine root, alongside `Submarine`. Provides per-player input isolation by cloning the shared `InputActionAsset` at runtime and restricting it to specific devices via `InputActionAsset.devices`. Runs at `[DefaultExecutionOrder(-100)]` so the clone is ready before any subsystem resolves actions.

**How it works:**
1. On Awake, clones the shared `PlayerControls` asset. If `AutoAssignOnAwake` is true (single-player / quick setup) it grabs devices matching one `DeviceMode`: keyboard+mouse, gamepad 1, or gamepad 2. The drop-in manager sets `AutoAssignOnAwake` false and assigns device **instances** explicitly instead.
2. Subsystems call `SubmarineComponent.ResolveAction(InputActionReference)` — when a module is present, this delegates to `SubmarineInputModule.FindAction(name)` which returns the per-player action from the clone. Without the module, the shared action is used directly (single-player, backward-compatible).
3. Each input component registers its action(s) via `InputSubmarineComponent.RegisterAction`, which handles enable/disable lifecycle automatically.

**Device-instance pairing (preferred):** `AssignDevices(IReadOnlyList<InputDevice>)` restricts the clone to exactly those devices, enables it, and re-resolves all child `InputSubmarineComponent`s — surviving gamepad reordering and supporting any number of pads. `Unassign()` releases the devices and disables input (drop-out). `OwnsDevice(device/id)` and `AssignedDevices` let the manager route gameplay vs. join presses. `HasMouse` checks the paired device set. `Reassign(DeviceMode)` remains for the enum path.

**Drop-in / drop-out system:** the runtime join flow (controller detection, slot assignment UI, shared framing camera) lives in `../Multiplayer/` — see that folder's `context.md`. `SubmarineInputModule` is the per-player input primitive it drives.

**Scene setup for local multiplayer:**
- Place the player submarine instances in the scene, each with `SubmarineInputModule` + the shared `PlayerControls` asset.
- For managed drop-in/out, add a `LocalPlayerManager` (`../Multiplayer/`) — it disables the subs and assigns devices on join. For a fixed 2-player setup, leave `AutoAssignOnAwake` on and set the `DeviceMode` per sub instead.
- No `PlayerInput` component, no control schemes, no `PlayerInputManager` needed.

**Limitations:** Mouse aiming is exclusive to the player that owns the mouse (`HasMouseInput` gates `TurretAim`'s mouse path); one keyboard = one player. The camera is a single shared frame-all view (`../Multiplayer/MultiTargetCamera2D`), not splitscreen. UI is not yet per-player.

## Semantic feedback system

Gameplay systems trigger juice (Feel/MMF) by **FeedbackId key**, never by direct `MMF_Player` reference. This keeps effects swappable in the inspector and stable against serialized-order changes. See `SubFeedbacks/context.md` for the type definitions and `SubSystems/context.md` for the router.

## Semantic anchor (mount-point) system

The **third semantic router**, alongside feedbacks and pumps. It solves the cross-prefab coupling problem: a feedback or module that needs to spawn an effect at a specific visual spot on the sub (muzzle, tail, nose) can't hold a Transform reference across prefab boundaries. Instead, it resolves the live Transform by an **AnchorId key** via `Sub.Anchors.Get(key)`.

- **AnchorId / SubAnchors** (`SubAnchors/`) — a 1:1 mirror of `FeedbackId` / `SubFeedbacks`: a packed-int value key plus a partial-class registry of named keys (`SubAnchors.Muzzle`, `Front`, `Tail`, ...), with the same Odin dropdown drawer.
- **SubmarineAnchor** — a marker component placed on a child transform; self-registers with the router (like pumps) so module swaps stay current.
- **SubmarineAnchorRouter** (`Sub.Anchors`) — maps `AnchorId → Transform`; `Get` falls back to the sub root on a miss so effects never land at world origin.

Two ways a location reaches an effect, both built on this registry:
- **Feedback prefab self-binds** — a `FeedbackAnchorBinder` on a feedback prefab resolves its anchor key at runtime and re-points its `MMF_ParticlesInstantiation` (attach-and-follow, or spawn-at-position). Swapping the prefab or its key moves the effect; gameplay code is untouched.
- **Module passes position** — a module resolves `Sub.Anchors.Get(anchor).position` and passes it to `Sub.Feedbacks.Play(key, position)` (as `PlayerAttack` does for the swing, and `MiningLaser` does with its beam endpoint).

## Other components here

- **MiningBeamVFX** (`MiningBeamVFX.cs`) — Sci-Fi Arsenal 2D beam controller driven by `MiningLaser`. `SetBeam(start, end, hitting, miningProgress)` positions the beam, orients start/end VFX, scrolls the texture, and pulses width — scaling scroll/pulse with mining progress for an escalating energy-transfer feel. `Show()`/`Hide()` toggle visuals. Performs **no** raycasts itself (the laser does that).
