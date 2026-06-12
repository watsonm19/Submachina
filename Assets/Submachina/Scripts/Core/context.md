# Core — Air / O2 Pump System

## Components

- **ManualBellowsPump** — the sub's air tank *and* the manual pump mechanic. Owns current/max air pressure, passive decay (faster under thrust/mining), max-capacity decay, health bleed at zero air, and the HUD atom write. Manual pumping is a hold-and-release sweet-spot charge with anti-spam Air Lock.
- **O2PickupPump** — the intake pump that gates O2 bubble collection. Runs a looping 0→1 charge bar; pressing the input while looping grades the collect by timing (sweet spot = full reward, otherwise weak). Routes air into ManualBellowsPump via `O2Pickup.Collect()`.
- **O2Pickup** — the bubble collectible. Restores current air and max capacity on `Collect(multiplier)`. Contact collection is off by default; collection goes through O2PickupPump. `WorldChunk` injects the pump reference at spawn.
- **ISweetSpotPump** — shared read interface (`ChargeProgress`, sweet spot bounds, etc.) consumed by `BellowsBar` so one HUD bar can display either pump.

## Input ownership (the two pumps share one input action)

Both pumps bind the same pump InputAction. Ownership rules:

- **O2 pickup in range** → O2PickupPump owns the input. With `autoActivateInRange` on, its loop starts automatically when a pickup enters range and stops quietly (no reward/penalty) when the last one leaves. The player presses the input to time the collect.
- **No pickup in range** → ManualBellowsPump owns the input and works as normal. O2PickupPump refuses to start while nothing is in range (`requirePickupToStart`).
- ManualBellowsPump suppresses its input via `IsBlockedByIntakePump` (intake pump looping OR pickup in its range), cancelling any in-flight charge so it can't get stuck mid-cycle.

## Independence

The coupling is opt-in on both sides: leave `ManualBellowsPump.intakePump` unassigned and the bellows runs standalone; turn off `autoActivateInRange`/`requirePickupToStart` and O2PickupPump is fully manual. Either component operates alone.

## Scene wiring (Proto_Descent)

Both components live on the submarine root GameObject, sharing one pump InputAction. `intakePump` is assigned, `enableManualPumping` is on, and both activation flags are enabled.
