# Upgrade System — Remaining Work

## Status: Foundation Complete

Phases 1-3 (foundation, component integration, draft pool) are implemented. The core system is functional — stat modifier upgrades can be granted/removed/toggled at runtime via the UpgradeManager's Odin debug panel.

---

## Phase 4: Behavioral Upgrades

### 4a. AutoPumpBehavior
- **File:** `Upgrades/Behaviors/AutoPumpBehavior.cs`
- **What:** When enabled, automatically operates the ManualBellowsPump — simulates press, waits for sweet spot, releases
- **How:** Implements `IUpgradeBehavior`. In Update(), checks if ManualBellowsPump is idle and is the active pump (via `Sub.Pumps`). Uses public `ChargeProgress`, `SweetSpotMin`, `SweetSpotMax` from ISweetSpotPump to time the release
- **Tuning:** Configurable timing jitter (always-perfect would feel robotic), optional delay between auto-pumps
- **Needs:** ManualBellowsPump needs a public method or the behavior needs to simulate input. Consider adding `SimulatePress()` / `SimulateRelease()` to ManualBellowsPump, or having the behavior directly call the pump's internal methods

### 4b. ConditionalDamageBoostBehavior
- **File:** `Upgrades/Behaviors/ConditionalDamageBoostBehavior.cs`
- **What:** After a perfect pump, temporarily boosts weapon damage
- **How:** Implements `IUpgradeBehavior`. Subscribes to `ManualBellowsPump.OnPerfectPump`. On trigger, pushes a temporary additive modifier to `SubStats.AttackDamage` via `Sub.Upgrades.Stats.Add()`. Removes after N seconds or after next attack (whichever comes first)
- **Tuning:** Bonus damage amount, duration, whether it stacks or refreshes

### 4c. AlternatingDashCostBehavior
- **File:** `Upgrades/Behaviors/AlternatingDashCostBehavior.cs`
- **What:** Every other dash is free (no O2 cost)
- **How:** Implements `IUpgradeBehavior`. Sets `CavitationBurst.CostOverride` delegate. Tracks a counter; odd dashes return 0 cost, even dashes pass through the resolved cost. Subscribes to `CavitationBurst.onBurstStart` to increment the counter
- **Note:** `CostOverride` delegate already added to CavitationBurst in Phase 2

---

## Phase 5: Draft UI (V1) — DONE

### Completed
- `SubUI/UpgradeDraftUI.cs` — SubmarineObserver that listens to `ResourceManager.onLevelUp`, builds a screen-space overlay Canvas at runtime, pauses the game, shows N choices as TMPro buttons with name/description/stat summary, grants on click, unpauses
- `Editor/UpgradeSetupWizard.cs` — menu item `Tools > Submachina > Setup Upgrade System` that creates 8 sample UpgradeDef assets + 1 UpgradeDraftPool, and wires UpgradeManager + UpgradeDraftUI onto all submarines in the scene

### Remaining polish (future)
- Card-style layout with icons, descriptions, level indicators
- Animation/juice on appear/select (MMF feedbacks)
- Sound effects via SubFeedbacks.UpgradeGranted
- Display current upgrade level and effect preview
- Support for "reroll" mechanic

---

## Phase 6: Upgrade ScriptableObject Assets

Created by `UpgradeSetupWizard` (menu: `Tools > Submachina > Setup Upgrade System`).
Assets live at `Assets/Submachina/Data/Upgrades/`.

### O2 Upgrades
- [x] IncreaseO2Capacity — `MaxAirPressure` additive +15, maxLevel 3
- [x] IncreasePumpSweetGain — `PerfectPumpAir` additive +5, maxLevel 3

### Combat Upgrades
- [x] IncreaseWeaponDamage — `AttackDamage` additive +1, maxLevel 3
- [ ] IncreaseWeaponRange — `AttackRange` additive +0.5, maxLevel 3
- [ ] IncreaseKnockback — `KnockbackForce` additive +2, maxLevel 3
- [ ] DecreaseWeaponCooldown — `AttackCooldown` multiplier -0.15, maxLevel 3

### Dash Upgrades
- [x] DecreaseDashCost — `DashAirCost` additive -3, maxLevel 3
- [x] DecreaseDashCooldown — `DashCooldown` additive -0.3, maxLevel 3
- [ ] IncreaseDashDistance — `DashImpulse` additive +3, maxLevel 2

### Movement Upgrades
- [x] FasterLateral — `LateralThrustForce` additive +2, maxLevel 3
- [x] FasterDescent — `CounterThrustForce` additive +3, maxLevel 3
- [ ] EfficientLateral — `LateralExertionMultiplier` multiplier -0.15, maxLevel 3
- [ ] EfficientDescent — `VerticalExertionMultiplier` multiplier -0.15, maxLevel 3

### Defense Upgrades
- [x] LessCollisionDamage — `CollisionDamagePerImpact` additive -1, maxLevel 1

### Pump Upgrades
- [ ] IncreaseMissGain — `WeakPumpAir` additive +3, maxLevel 3
- [ ] DecreasePumpCooldown — `PumpCooldown` multiplier -0.2, maxLevel 3
- [ ] MoreForgivingLockout — `SpamPressLimit` additive +1 AND `AirLockDuration` additive -0.5, maxLevel 2
- [ ] IncreaseIntakeEffectiveness — `IntakeSweetMultiplier` multiplier +0.25, maxLevel 3

### Behavioral Upgrades (need Phase 4 first)
- [ ] AutoPump — behaviorPrefab = AutoPumpBehavior
- [ ] DamageAfterPump — behaviorPrefab = ConditionalDamageBoostBehavior
- [ ] SecondDashFree — behaviorPrefab = AlternatingDashCostBehavior

---

## Phase 7: Component Swap Testing (future)

- Create a `CavitationBurstV2` variant component with hold-to-dash behavior
- Create an UpgradeDef with `swapPrefab` pointing to the V2 prefab
- Test stat inheritance across the swap
- Test toggle on/off restores original component

---

## Phase 8: Persistence & Polish (future)

- Save/load upgrade state across runs (if roguelite session persistence is needed)
- Upgrade removal on death / run end
- Upgrade synergy system (certain combos grant bonus effects)
- Visual indicators on the submarine for active upgrades
