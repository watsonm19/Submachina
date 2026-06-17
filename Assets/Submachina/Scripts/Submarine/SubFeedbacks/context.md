# Semantic Feedback Keys

This folder defines the **feedback key types and registry** used by the submarine's semantic feedback system. Gameplay systems trigger juice by `FeedbackId` key via `Sub.Feedbacks.Play(key, position, intensity)` — they never hold direct `MMF_Player` references. The runtime router that maps keys to players lives in `SubSystems/SubmarineFeedbackRouter.cs`.

## Core types

- **FeedbackId** (`FeedbackId.cs`) — serializable struct wrapping a single packed int. A category ID (upper 16 bits) and local value (lower 16 bits) guarantee each category has its own 65,536-value namespace with no manual range management. Implements `IEquatable<FeedbackId>` for dictionary performance. `ToString()` resolves to the field name via a lazily-built reflection cache.

- **SubFeedbacks** (`SubFeedbacks.cs` + `SubFeedbacks.*.cs`) — partial static class registering all feedback keys as `static readonly FeedbackId` fields. The shell file holds `CategoryNames` (ID → display name); each category file defines a private category constant and its keys:
  - `SubFeedbacks.Mining.cs` (cat 1) — MiningActive, MiningCollect
  - `SubFeedbacks.Combat.cs` (cat 2) — AttackSwing, DashStart, DashEnd, TakeDamage, CollisionDamage, DashReady
  - `SubFeedbacks.Scrap.cs` (cat 3) — ScrapAdded, ScrapFull, ScrapUsed, NoScrap, FullHealth
  - `SubFeedbacks.Resources.cs` (cat 4) — ResourcesAdded, LevelUp
  - `SubFeedbacks.Pumps.cs` (cat 5) — PumpPerfect, PumpWeak, PumpCharge, AirLock

- **UsesFeedbacksAttribute** (`UsesFeedbacksAttribute.cs`) — `[UsesFeedbacks(nameof(SubFeedbacks.X), ...)]` class-level attribute declaring which keys a `SubmarineComponent` fires. `nameof` gives compile-time validation. Read by the `SubmarineComponent` inspector banner to render colored chips. Pure metadata.

## Editor

- **FeedbackIdDrawer** (`Editor/FeedbackIdDrawer.cs`) — `OdinValueDrawer<FeedbackId>` that renders a categorized dropdown (e.g. "Mining/MiningActive") instead of a raw int. Reflects over `SubFeedbacks` fields on domain reload; includes a "(None)" sentinel for unset keys.

## Adding a new category

1. Create `SubFeedbacks.YourCategory.cs` with a unique `private const int YourCat = N;` and `static readonly FeedbackId` fields.
2. Add the category name to `SubFeedbacks.CategoryNames` in `SubFeedbacks.cs`.
3. Open SubmarineFeedbackRouter in the Inspector and click **Add Missing Mappings** to populate entries.
