# Semantic Anchor (Mount-Point) Keys

This folder defines the **anchor key types and registry** used by the submarine's mount-point system — the visual-location mirror of `SubFeedbacks/`. Systems and feedback prefabs resolve a live `Transform` by `AnchorId` key via `Sub.Anchors.Get(key)`, instead of holding a Transform reference across prefab boundaries. The runtime router that maps keys to transforms lives in `SubSystems/SubmarineAnchorRouter.cs`; the marker that registers a transform is `SubSystems/SubmarineAnchor.cs`.

## Why it exists

A feedback packaged as its own prefab (or a swapped-in weapon module) often needs to spawn particles at a specific spot on the sub — the muzzle, the tail, the nose. It can't reference that Transform directly because the two live in different prefabs. Naming the location by a semantic key decouples them: change the key (or swap the prefab) to move the effect, with no gameplay-code change.

## Core types

- **AnchorId** (`AnchorId.cs`) — serializable struct wrapping a single packed int (category in the upper 16 bits, local value in the lower 16). A 1:1 mirror of `FeedbackId`: `IEquatable<AnchorId>` for dictionary use, `IsEmpty`, and a reflection-cached `ToString()` resolving to the field name.

- **SubAnchors** (`SubAnchors.cs` + `SubAnchors.*.cs`) — partial static class registering all anchor keys as `static readonly AnchorId` fields. The shell file holds `CategoryNames` (ID → display name); each category file defines a private category constant and its keys:
  - `SubAnchors.Hull.cs` (cat 1) — Center, Front, Tail, Top, Bottom
  - `SubAnchors.Weapon.cs` (cat 2) — Muzzle

- **UsesAnchorsAttribute** (`UsesAnchorsAttribute.cs`) — `[UsesAnchors(nameof(SubAnchors.X), ...)]` class-level attribute declaring which anchors a `SubmarineComponent` resolves. `nameof` gives compile-time validation. Pure metadata (consumed for inspector display, like `UsesFeedbacks`).

## Editor

- **AnchorIdDrawer** (`Editor/AnchorIdDrawer.cs`) — `OdinValueDrawer<AnchorId>` rendering a categorized dropdown (e.g. "Weapon/Muzzle") instead of a raw int. Reflects over `SubAnchors` fields on domain reload; includes a "(None)" sentinel for unset keys.

## Adding a new anchor / category

1. Add a `static readonly AnchorId` field to an existing `SubAnchors.*.cs`, or create a new category file with a unique `private const int YourCat = N;`.
2. For a new category, add its name to `SubAnchors.CategoryNames` in `SubAnchors.cs`.
3. Place a `SubmarineAnchor` on the target child transform and pick the new key — it self-registers at runtime.
