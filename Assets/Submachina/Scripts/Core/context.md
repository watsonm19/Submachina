# Core Systems Architecture

## Submarine Facade Pattern

All submarine subsystems extend **SubmarineComponent** (base class) which auto-discovers its parent **Submarine** via `GetComponentInParent<Submarine>()` and registers itself on Awake. Subsystems access siblings through `Sub.O2`, `Sub.Physics`, `Sub.Turret`, `Sub.Resources`, etc.

**Key types:**
- **Submarine** (`Submarine.cs`) — MonoBehaviour facade on the submarine root. Holds typed references to subsystems populated by auto-registration. Maintains `static List<Submarine> All` for external systems. Has `Build(SubmarineConfig)` for config-driven assembly.
- **SubmarineComponent** (`SubmarineComponent.cs`) — Abstract base class. Derived classes override `Awake()` with `base.Awake()` first, then their init. Auto-registers/unregisters for runtime swapping.
- **SubmarineConfig** (`SubmarineConfig.cs`) — ScriptableObject defining a submarine's composition via prefab slots (core, weapons, abilities, utilities).

**External entities** resolve submarines from context:
- Enemies find the nearest sub via `Submarine.FindNearest(position)` or `Submarine.All`
- O2Pickups resolve the collecting sub from collision: `other.GetComponentInParent<Submarine>()`
- MiningResources receive the collecting sub from MiningLaser's `Collect(Sub)` call

**Supports multiple submarines** (future local multiplayer) — no singletons.

---

# Core — Air / O2 Pump System

## Components

- **O2System** (`Sub.O2`) — the sub's air tank and single source of truth for air state: current/max air pressure, passive decay (faster under thrust/mining), max-capacity decay, health bleed at zero air, and the HUD atom write. Pumps call `Sub.O2.AddAir()` when an action succeeds.
- **ManualBellowsPump** — the manual pump mechanic only (air state lives in O2System). A hold-and-release sweet-spot charge with anti-spam Air Lock; a Perfect/Weak release calls `Sub.O2.AddAir()`.
- **O2PickupPump** — the intake pump that gates O2 bubble collection. Runs a looping 0→1 charge bar; pressing the input while looping grades the collect by timing (sweet spot = full reward, otherwise weak). `O2Pickup.Collect(Sub, multiplier)` routes the air into `Sub.O2`.
- **O2Pickup** — the bubble collectible. Restores current air and max capacity on `Collect(multiplier)`. Contact collection is off by default; collection goes through O2PickupPump. `WorldChunk` injects the pump reference at spawn.
- **ISweetSpotPump** — shared read interface (`ChargeProgress`, sweet spot bounds, plus `WantsControl`/`ControlPriority` for arbitration) consumed by `SubmarinePumpRouter` and `BellowsBar`.
- **SubmarinePumpRouter** (`Sub.Pumps`) — central registry that decides the single *active* pump. Pumps self-register on enable; `Active` returns the highest-`ControlPriority` pump whose `WantsControl` is true. Mirrors `SubmarineFeedbackRouter`. No hand-wired references between pumps or bars.
- **BellowsBar** — world-space charge bar. Follows `Sub.Pumps.Active` each frame (no serialized pump reference), repositioning the sweet-spot markers on hand-off and showing only while the active pump is engaged.

## Input ownership (the pumps share one input action via the router)

All pumps bind the same pump InputAction. The `SubmarinePumpRouter.Active` pump owns it:

- **O2PickupPump** wants control while looping **or** a pickup is in range (`ControlPriority 10`). With `autoActivateInRange` on, its loop starts automatically on range-enter and stops quietly when the last pickup leaves.
- **ManualBellowsPump** is the always-on baseline (`ControlPriority 0`, `WantsControl` = `enableManualPumping`). It's active whenever no higher-priority pump wants control. `IsActivePump` reads `Sub.Pumps.IsActive(this)`; when not active it cancels any in-flight charge so it can't get stuck mid-cycle.

So the intake pump takes over near bubbles and the manual pump resumes the instant it lets go — entirely through the router, with no direct references.

## Independence (upgrade-gated single pumps)

Each pump works standalone: with only one pump registered, that pump is active whenever it wants control — so a sub that carries only the bellows pump, or only the intake pump, behaves correctly. With **no** router present at all, `ManualBellowsPump.IsActivePump` defaults to true so the bellows still runs solo. `autoActivateInRange`/`requirePickupToStart` still let O2PickupPump run fully manual.

## Scene wiring (Proto_Descent)

`O2System`, `SubmarineFeedbackRouter`, `SubmarinePumpRouter`, `ManualBellowsPump`, and `O2PickupPump` live on the submarine root, sharing one pump InputAction. A single `BellowsBar` child follows `Sub.Pumps.Active` — no per-pump bars or manual references.
