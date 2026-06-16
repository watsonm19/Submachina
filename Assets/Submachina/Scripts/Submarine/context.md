# Submarine Systems

This folder holds the **submarine facade**, its **config-driven assembly**, and the **semantic feedback system**. The actual subsystems, feedback definitions, and HUD live in subfolders:

- **`SubFeedbacks/context.md`** — the `FeedbackId` type, `SubFeedbacks` partial-class key registry, `UsesFeedbacksAttribute`, and the Odin editor drawer.
- **`SubSystems/context.md`** — every `SubmarineComponent` subsystem (O2, physics, turret, weapons, abilities, pumps, scrap/resources) plus the pump and feedback routers.
- **`SubUI/context.md`** — the HUD bars/displays.

## Facade pattern

Every subsystem extends **SubmarineComponent**, which auto-discovers its parent **Submarine** via `GetComponentInParent<Submarine>()` and registers itself in `Awake`. Subsystems then reach siblings through typed accessors on the facade — `Sub.O2`, `Sub.Physics`, `Sub.Turret`, `Sub.Resources`, `Sub.Scrap`, `Sub.Feedbacks`, `Sub.Pumps`, `Sub.Health` — with no hand-wired serialized references.

**Key types:**
- **Submarine** (`Submarine.cs`) — MonoBehaviour facade on the submarine root. Holds typed references to subsystems, populated by auto-registration (pattern-matched in `Register`/`Unregister`). Maintains `static List<Submarine> All` and `static FindNearest(worldPosition)` for external systems. `Build(SubmarineConfig)` assembles a full loadout from config.
- **SubmarineConfig** (`SubmarineConfig.cs`) — ScriptableObject (`Submachina/Submarine Config`) defining composition via prefab slots: core (`hullPrefab`, `o2SystemPrefab`, `propulsionPrefab`, `turretPrefab`) plus modular `weapons` / `abilities` / `utilities` lists. Consumed by `Submarine.Build()`; each prefab's `SubmarineComponent`s auto-register on instantiation.

**External entities** resolve submarines from context, never via singletons:
- Enemies find the nearest sub via `Submarine.FindNearest(position)` / `Submarine.All`.
- O2Pickups resolve the collecting sub from collision: `other.GetComponentInParent<Submarine>()`.
- MiningResources receive the collecting sub from `MiningLaser`'s `Collect(Sub)` call.

**Supports multiple submarines** (future local multiplayer) — discovery is by hierarchy and the static list, with no singletons.

## Semantic feedback system

Gameplay systems trigger juice (Feel/MMF) by **FeedbackId key**, never by direct `MMF_Player` reference. This keeps effects swappable in the inspector and stable against serialized-order changes. See `SubFeedbacks/context.md` for the type definitions and `SubSystems/context.md` for the router.

## Other components here

- **MiningBeamVFX** (`MiningBeamVFX.cs`) — Sci-Fi Arsenal 2D beam controller driven by `MiningLaser`. `SetBeam(start, end, hitting, miningProgress)` positions the beam, orients start/end VFX, scrolls the texture, and pulses width — scaling scroll/pulse with mining progress for an escalating energy-transfer feel. `Show()`/`Hide()` toggle visuals. Performs **no** raycasts itself (the laser does that).
