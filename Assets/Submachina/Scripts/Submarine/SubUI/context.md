# Submarine UI (HUD)

HUD components that visualize submarine state. See `../context.md` for the facade and `../SubSystems/context.md` for the systems that produce the data shown here.

## How the HUD reads data — `SubmarineObserver`

Every HUD element here is a **`SubmarineObserver`** (`SubmarineObserver.cs`) — the semantic counterpart to `SubmarineComponent`. A *component* IS part of the sub: it registers into the `Submarine` facade and provides function. An *observer* only WATCHES a sub and contributes nothing back, so it never registers and never appears in the facade's slots.

An observer resolves its `Sub` by walking up the hierarchy (`GetComponentInParent<Submarine>()`, with an optional explicit `submarineOverride`). Because each player's HUD lives inside its own sub's hierarchy (a per-sub "Player Canvas"), **two subs each get an independent, correctly-wired HUD with zero per-player asset duplication** — the core enabler for local multiplayer. Observers then poll their subsystem live off the facade (`Sub.O2`, `Sub.Health`, `Sub.Resources`, `Sub.Scrap`, `Sub.Pumps`), which tolerates Awake ordering and runtime module swaps.

> **History:** `HealthBar`/`O2Bar`/`ResourceBar` previously read shared `FloatVariable` atoms. That broke under multiplayer — both subs wrote the same global atom (last-writer-wins) — so they were migrated to facade polling. The subsystems (`O2System`, `Health`, `ResourceManager`) may still *write* their atoms; those writes are now unread by the HUD and are safe to remove once no other consumer remains.

## Components

All require `Image` (except `BellowsBar`/`ScrapDisplay`) and resolve their sub via `SubmarineObserver`.

- **HealthBar** — polls `Sub.Health.HealthPercent` and repaints only when it changes (cheap per-frame compare, no event wiring). Sets `fillAmount` plus a three-tier color lerp (`healthyColor` → `lowColor` → `criticalColor`) at `lowThreshold`/`criticalThreshold`.
- **O2Bar** — polls `Sub.O2` for `CurrentAirPressure`/`MaxAir`/`OriginalMaxAir`. Main fill is `CurrentAirPressure / OriginalMaxAir` with cyan→yellow→red coloring at `lowThreshold`; an optional second `capacityBar` Image tracks `MaxAir / OriginalMaxAir` to show max-capacity degradation.
- **ResourceBar** — polls `Sub.Resources` and fills `Clamp01(CurrentResources / CurrentThreshold)`, lerping `emptyColor`→`fullColor` toward the next level-up.
- **BellowsBar** — world-space charge bar that floats above the sub at `worldOffset`. Each frame it follows `Sub.Pumps.Active` (no cached pump reference), reading `ChargeProgress`, `IsInSweetSpot`, `SweetSpotMin/Max`, `IsAirLocked`, `IsOnCooldown`. Repositions the sweet-spot markers on pump hand-off, recolors for normal/sweet-spot/air-lock, and hides while no pump is engaged. Builds its bar/marker SpriteRenderers at runtime.
- **ScrapDisplay** — dot-row indicator. Polls `Sub.Scrap` for `MaxScrap`/`ScrapCount`. Rebuilds the dot layout (a `HorizontalLayoutGroup` of `Image` children) when `MaxScrap` changes (e.g. from an upgrade) and just re-skins filled/empty dots when `ScrapCount` changes.
