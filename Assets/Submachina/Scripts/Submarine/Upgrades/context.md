# Upgrade System

Per-submarine upgrade system supporting four upgrade types:

## Upgrade Types

1. **Stat Modifiers** — Additive/multiplicative tweaks to numerical stats via `StatModifierTable`. Components query `Sub.Upgrades.Stats.Resolve(StatId, baseValue)` through thin accessor properties.
2. **Behavioral Add-Ons** — Prefabs instantiated as children of the sub, implementing `IUpgradeBehavior`. Add new logic without replacing existing components (e.g. auto-pump, conditional damage boost).
3. **Component Swaps** — Replace an entire `SubmarineComponent` with a variant prefab. The original is deactivated (not destroyed) for reversibility. Stat modifiers carry over automatically because they're stored by `StatId` in the `UpgradeManager`, not on the component.
4. **Hierarchy Toggles** — Switch existing objects already in the sub hierarchy on/off — no prefab spawning. An object is tagged with an `UpgradeFeature` (identity ScriptableObject) via an `UpgradeToggleTarget` marker; `UpgradeDef.toggles` lists `{feature, setActive}` entries. While the upgrade is active, every marker matching a feature is driven to the requested state, then restored to its authored state when removed/disabled. Matched by object reference (rename-safe, typo-proof). Use this when the content already lives in the prefab and just needs enabling (e.g. the standalone DashRam prefab).

## Key Classes

- **StatId** — Packed-int identifier for upgradeable stats (mirrors `FeedbackId` pattern)
- **SubStats** — Partial class registry of all stat keys, organized by category
- **StatModifierTable** — Stores stacked modifiers. Formula: `(base + additives) * (1 + multiplierDeltas)`
- **UpgradeDef** — ScriptableObject defining a single upgrade (stat mods, behavior prefab, swap prefab, hierarchy toggles, prerequisites)
- **UpgradeManager** — `SubmarineComponent` on each sub. Owns the `StatModifierTable`. API: `Grant`, `Remove`, `SetEnabled`, `GetLevel`, `IsActive`. Hierarchy toggles are **reference-counted** per feature (`_featureCounts`): an object stays on while any active upgrade wants it on (ON wins ties over OFF), and reverts to its authored state only when no upgrade references the feature
- **UpgradeFeature** — Identity-only ScriptableObject (`Submachina/Upgrade Feature`). One asset per toggleable feature; the asset *is* the ID, matched by reference
- **UpgradeToggleTarget** — Marker `MonoBehaviour` on any hierarchy object, tagging it with one `UpgradeFeature`. Captures its authored active state on demand (works on objects that start disabled, which never run `Awake`) and exposes `onActivated`/`onDeactivated` UnityEvents
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
