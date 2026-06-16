# Submarine UI (HUD)

HUD components that visualize submarine state. See `../context.md` for the facade and `../SubSystems/context.md` for the systems that produce the data shown here.

## How the HUD reads data

Two patterns are in use, and the choice is deliberate (Unity Atoms are used **for UI reads**, not as a universal decoupling layer):

1. **Atom-backed bars** — read `FloatVariable` atoms written by the subsystems, so the bar needs no reference to the sub. `HealthBar` is **event-driven** (subscribes to `currentHealth.Changed`); `O2Bar` and `ResourceBar` **poll** their atoms each `Update`.
2. **Facade-polled displays** — resolve the sub via the hierarchy and poll the subsystem directly. `BellowsBar` (a `SubmarineComponent`) follows `Sub.Pumps.Active`; `ScrapDisplay` (`GetComponentInParent<Submarine>`) polls `Sub.Scrap`. These poll because they mirror richer, non-scalar state that doesn't map cleanly to a single atom.

## Components

- **HealthBar** — requires `Image`. Subscribes to the `currentHealth` atom's `Changed` event (no polling, no sub reference) and sets `fillAmount` plus a three-tier color lerp (`healthyColor` → `lowColor` → `criticalColor`) at `lowThreshold`/`criticalThreshold`.
- **O2Bar** — requires `Image`. Polls `currentO2`, `maxAirCapacity`, and `originalMaxAir` atoms each frame. Main fill is `currentO2 / originalMaxAir` with cyan→yellow→red coloring at `lowThreshold`; an optional second `capacityBar` Image shows max-capacity degradation.
- **ResourceBar** — requires `Image`. Polls the `currentResources` / `resourceThreshold` atoms and fills `Clamp01(currentResources / resourceThreshold)`, lerping `emptyColor`→`fullColor` toward the next level-up.
- **BellowsBar** — world-space charge bar; a `SubmarineComponent` that floats above the sub at `worldOffset`. Each frame it follows `Sub.Pumps.Active` (no cached pump reference), reading `ChargeProgress`, `IsInSweetSpot`, `SweetSpotMin/Max`, `IsAirLocked`, `IsOnCooldown`. Repositions the sweet-spot markers on pump hand-off, recolors for normal/sweet-spot/air-lock, and hides while no pump is engaged. Builds its bar/marker SpriteRenderers at runtime.
- **ScrapDisplay** — dot-row indicator. Resolves the sub via `GetComponentInParent<Submarine>()` and polls `Sub.Scrap` for `MaxScrap`/`ScrapCount`. Rebuilds the dot layout (a `HorizontalLayoutGroup` of `Image` children) when `MaxScrap` changes (e.g. from an upgrade) and just re-skins filled/empty dots when `ScrapCount` changes.
