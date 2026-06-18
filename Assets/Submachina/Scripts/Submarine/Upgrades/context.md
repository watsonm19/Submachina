# Upgrade System

Per-submarine upgrade system supporting three upgrade types:

## Upgrade Types

1. **Stat Modifiers** — Additive/multiplicative tweaks to numerical stats via `StatModifierTable`. Components query `Sub.Upgrades.Stats.Resolve(StatId, baseValue)` through thin accessor properties.
2. **Behavioral Add-Ons** — Prefabs instantiated as children of the sub, implementing `IUpgradeBehavior`. Add new logic without replacing existing components (e.g. auto-pump, conditional damage boost).
3. **Component Swaps** — Replace an entire `SubmarineComponent` with a variant prefab. The original is deactivated (not destroyed) for reversibility. Stat modifiers carry over automatically because they're stored by `StatId` in the `UpgradeManager`, not on the component.

## Key Classes

- **StatId** — Packed-int identifier for upgradeable stats (mirrors `FeedbackId` pattern)
- **SubStats** — Partial class registry of all stat keys, organized by category
- **StatModifierTable** — Stores stacked modifiers. Formula: `(base + additives) * (1 + multiplierDeltas)`
- **UpgradeDef** — ScriptableObject defining a single upgrade (stat mods, behavior prefab, swap prefab, prerequisites)
- **UpgradeManager** — `SubmarineComponent` on each sub. Owns the `StatModifierTable`. API: `Grant`, `Remove`, `SetEnabled`, `GetLevel`, `IsActive`
- **UpgradeInstance** — Runtime state per granted upgrade (level, enabled, spawned GO refs)
- **IUpgradeBehavior** — Interface for behavioral upgrade lifecycle (`OnUpgradeEnabled`, `OnUpgradeDisabled`)
- **UpgradeDraftPool** — ScriptableObject holding the set of upgrades available for selection on level-up

## Component Integration Pattern

Each upgradeable component adds thin private accessor properties:

```csharp
private float BurstCooldownMod => Sub?.Upgrades?.Stats.Resolve(SubStats.DashCooldown, burstCooldown) ?? burstCooldown;
```

The serialized field stays as the designer-tuned base value; the accessor resolves modifiers with null-safe fallback.

## Interactions

- **ResourceManager** — `onLevelUp` triggers the draft UI to present upgrade choices
- **Submarine Facade** — `Upgrades` slot registered via standard `SubmarineComponent` pattern
- **SubFeedbacks** — Category 6 (Upgrades): `UpgradeGranted`, `UpgradeMaxed`
- **All subsystem components** — Query `Sub.Upgrades.Stats` for modified stat values
