# Feature Plan — Hub, Missions, Resources, Depth Progression

Working document for the automated implementation run driven from `big-plan.md`.
Where big-plan.md was ideation, this doc records the **concrete decisions**. Update as phases land.

## Core loop (target)

```
HUB (surface station)
 ├─ Review banked resources & sub rating (max safe depth, cargo, O2)
 ├─ Buy permanent upgrades with typed resources
 ├─ Pick loadout (exclusive slots — can't have it all)
 ├─ Pick 1 of 3 generated missions (long-range scanner report)
 └─ LAUNCH → Mission scene
      ├─ Descend, mine typed resources into limited cargo hold
      ├─ Complete objective (retrieve / neutralize / research)
      ├─ Survive pressure + impacts + O2 + creatures
      └─ Return to surface → EXTRACT → bank cargo → back to HUB
Death = lose unbanked cargo, keep permanent upgrades → back to HUB.
```

The existing in-run 3-choice draft (`ResourceManager.onLevelUp` → `UpgradeDraftUI`) **stays** as
temporary in-run "field mods" (lost at mission end). Permanent progression moves to the hub shop.
This keeps a working system working and gives runs an in-run power curve; revisit later if redundant.

## Decisions at a glance

| Area | Decision |
|---|---|
| Resource types | 5 thematic deep-sea resources (below), ScriptableObject identity assets |
| Cargo | New `CargoHold` subsystem (`Sub.Cargo`), typed counts, unit capacity, contributes mass |
| Hull | New `HullSystem` (`Sub.Hull`); Integrity = existing `Health` percent; Resistance = Strength × Integrity |
| Depth gating | Rated depth derives from hull strength — missions demand depth, hull upgrades unlock it |
| O2 progression | Reuse `SubStats.MaxAirPressure` stat; hub-purchasable tank tiers; tanks add mass |
| Ballast | New `BallastTank` subsystem (`Sub.Ballast`); flood/blow + pump-destination toggles |
| Mass | `SubmarinePhysicsController.RegisterMass(source, kg)` aggregation; contributors push in |
| Persistence | JSON `PlayerProfile` at `persistentDataPath`, static `ProfileService` (not a scene singleton) |
| Missions | `MissionSpec` (plain serializable) from `MissionGenerator`; authored `MissionTemplate` SOs |
| Hub | New uGUI scene `Hub.unity`: Outfitting / Missions / Launch panels |
| Mission scene | New `Mission_Descent.unity` duplicated from `Ore testing.unity` + mission rig |
| Scene handoff | Static `MissionContext` carries MissionSpec + loadout across scene load |

## Resource types (concrete)

Real deep-sea-mining flavored, each tied to an upgrade domain so different missions justify
different targets. Identity = `ResourceType` ScriptableObject (name, tint, icon, unit mass, description,
depth band hint for UI). Assets in `Assets/Submachina/Data/Resources/`.

| Type | Look / reuses art | Depth band | Upgrade domain |
|---|---|---|---|
| **Ferrite Nodules** | dull grey-brown nodules (CopperResource tint) | 0–40% | Hull & structure |
| **Vent Brass** | golden sulfide (Glitter Metal / gold nugget) | 20–60% | Thrusters, tools, machinery |
| **Clathrate Ice** | pale cyan crystal (crystal art retint) | 30–70% | O2, ballast, consumables |
| **Luminite** | green glowing crystal (GreenCrystal) | 50–85% | Sensors, sonar, lighting |
| **Abyssite** | violet crystal (RockPinkCrystals retint) | 70–100% | Depth capability, top-tier |

Bands are % of mission target depth ranges, expressed via existing `SpawnRuleData.DepthRange` +
`prevalenceByDepth` — the spawn system already supports this; we add per-type rule assets.

`MiningResource` changes: add `ResourceType resourceType` + `int units` (default 1). `Collect(sub)`
keeps `sub.Resources.AddResources(resourceValue)` (in-run XP draft) **and** adds
`sub.Cargo.Add(resourceType, units)`. If cargo is full → still awards XP, fires `onCargoRejected`
feedback (mining never hard-blocks; you just can't bank more).

## Hull / pressure model (concrete math)

New `HullSystem : SubmarineComponent` (`Sub.Hull`), prefab under `Prefabs/SubSystems/`.

- `HullStrength` — stat-resolved (`SubStats.HullStrength`), base **120**.
- `Integrity` — `Sub.Health.HealthPercent` (0..1). Health stays the single damage store; scrap-heal,
  HitFlash, HealthBar all keep working untouched.
- `HullResistance = HullStrength × Integrity`
- `PressureLoad = depth_m × pressurePerMeter (1.0) × PressureLoadMult` (`SubStats.PressureLoadMult`,
  base 1; "pressure reinforcement" upgrades lower it)
- `StructuralReserve = HullResistance − PressureLoad` (clamped ≥ 0; HUD + creak feedback driver)
- **Rated depth** = `HullStrength × PressureLoadMult⁻¹ × safetyFactor (0.8)` → shown in hub, gates missions.
- Impacts: `CollisionDamage` is reworked to compute `ImpactLoad = impactSpeed × impactLoadScale (12) ×
  ImpactLoadMult` (`SubStats.ImpactLoadMult`, "impact reinforcement" lowers it).
  `overload = PressureLoad + ImpactLoad − HullResistance`; if `overload > 0` →
  `Health.TakeDamage(ceil(overload × overloadToDamage (0.5)))`. Below-margin impacts do **zero** damage
  (shallow = forgiving; deep = the same bump cracks you — exactly the big-plan intent).
- Pressure-only cascade: while `PressureLoad > HullResistance`, continuous damage
  `excess × cascadeDamageRate (0.25) HP/s` (fractional accumulator like O2 bleed). Falling integrity
  lowers resistance → cascade accelerates → emergency ascent or die.
- Events/feedbacks: `onReserveLow`, `onOverload(float)`, `onCrushZoneEntered/Exited`;
  new FeedbackIds: `HullCreak`, `HullOverload`, `CrushZone` (loop). Pairs beautifully with the
  Environment Director horror layer.

## Ballast / buoyancy (concrete — v2, reworked per feedback 2026-08-10)

`BallastTank : InputSubmarineComponent` (`Sub.Ballast`). **Air-based fill on a shared, conserved
O2 pool** (v1's water-fill + separate air costs replaced after playtest feedback).

- `AirFraction` 0..1 — how much of the tank holds AIR (rest floods with water). Air = lift.
- **Gear shifter, three modes** (`BallastMode`): Empty (sink at full speed) / Neutral (hover) /
  Full (rise ~as fast as you fall). One step per press: shift-down C / dpad-down, shift-up
  X / dpad-up. The tank eases `AirFraction` toward the commanded target at `shiftRate` (0.35/s).
- **Shared O2 economy**: filling draws `tankAirCapacity (30)` × fraction from `Sub.O2`
  (stalls when the reserve is dry); venting returns the air; overflow past a full main tank
  spawns real `O2Bubble` pickups at the sub (`o2PerVentedBubble (5)` each, banked across
  sub-bubble amounts) — air is conserved, reclaim it by coming back.
- **Buoyancy tuning** (piecewise-linear vs `referenceCurrentForce (7.5)` = average current force):
  air 0 → 0 lift; air neutral (0.5) → +reference (cancels the current, hover);
  air 1 → +2×reference (rise speed ≈ fall speed). Verified in play: full tank floats the sub
  to the surface. Water mass (`floodedMass × (1−air)`) registers with the physics aggregation.
- **Cargo interplay**: a heavy hold can outweigh full lift (the lift still assists thrust);
  `CargoHold`'s jettison control (hold B / dpad-right, `dumpRate` 2 units/s, heaviest type
  first) spawns re-collectible `CargoPickup` parcels so the sub can never be truly stuck.
- **Pump destination** (V / dpad-left): destination Ballast routes intake-pump bubbles and
  manual-pump air into the TANK (`AddAirToBallast` — auto-promotes the gear to keep the gift,
  overflow banks to the main reserve). Destination O2Reserve = pumps behave classically.
- No `BallastTank` in the loadout → everything behaves as before (pumps null-check).
- FeedbackIds: `BallastBlow` (fill loop), `BallastFlood` (vent loop), `BallastFull`,
  `BallastEmpty`, `BallastShift` (gear click), `PumpDestinationToggled`.

## Mass & inertia (concrete)

`SubmarinePhysicsController` gains `RegisterMass(object source, float extraMass)` /
`UnregisterMass(source)`; recomputes `rb.mass = baseMass + Σ`. Contributors: `CargoHold`
(Σ units × type unit mass), `BallastTank` (water), O2 tank tier upgrades (flat adds via a small
`MassContribution` behavior prefab). Heavier = same thrust force, slower acceleration — no extra
handling code needed. `maxSpeed` stays; inertia does the feel work.

## Persistence (concrete)

- `PlayerProfile` (plain class, `[Serializable]`): `Dictionary<string,int> bankedResources`
  (keyed by ResourceType asset name), `List<OwnedUpgrade> {defName, level}`, `List<string> loadoutSelections`
  (slot→choice), `int missionsCompleted`, `float deepestDepth`.
- `ProfileService` (static, `Assets/Submachina/Scripts/Meta/`): `Load()`, `Save()`, JSON via
  `JsonUtility` at `Application.persistentDataPath/submachina_profile.json`. Not a MonoBehaviour;
  no singleton GameObject. Upgrade defs resolved by name through a `UpgradeCatalog` SO listing all
  purchasable defs (needed anyway for the shop).
- Applying to a sub: `LoadoutApplier` component in the mission scene, on the sub — reads
  profile + `MissionContext`, `Grant`s owned/selected upgrades on `Start`.

## Missions (concrete)

- `MissionType { Retrieval, Neutralize, Research }`.
- `MissionSpec` (plain `[Serializable]`): type, seed, `targetDepth`, `currentStrength` (0–2),
  `o2Richness` (0.5–1.5 spawn multiplier), `hazardCreatureLevel`, per-type resource abundance
  (parallel arrays: type name + multiplier), reward resource bonus, display name + flavor.
- `MissionGenerator` (static): takes profile rated-depth → emits 3 specs around it
  (one comfortable ~70% rated, one at rated, one stretch ~130% — risk/reward), seeded rng, picks type,
  rolls properties, weights resource abundance toward the depth band's native types.
- **Scanner report** = the spec's display data with per-property confidence noise (e.g. "Vent Brass:
  RICH", "Hostiles: large contact detected"). Rendered on hub mission cards.
- Runtime: `MissionController` (scene object, no singleton) in `Mission_Descent.unity`:
  - applies spec: sets `CurrentManager` tier/boost from `currentStrength`, scales `SpawnProfile.globalDensityMultiplier`
    clones per resource-rule via multipliers, o2Richness scales the O2 rule density.
  - objective handling v1: **Retrieval fully implemented** — spawns `MissionCargo` prefab near
    `targetDepth` (SonarTarget so it pings; auto-collect on proximity), then "return to surface".
    Neutralize = spawn boosted `RammerEnemy` variant at depth, track its `Health.onDeath`, harvest drop.
    Research = spawn N `ResearchTarget`s; proximity-dwell to scan. (Neutralize/Research land after
    Retrieval works end-to-end.)
  - Extraction: sub above `extractionY` (near surface) + objective done → `onMissionComplete` →
    summary → `ProfileService` banks `Sub.Cargo` contents (+ reward bonus) → load Hub.
  - Death (`Health.onDeath`): cargo lost, profile keeps permanents → summary → Hub.

## Hub (concrete)

`Hub.unity` — pure uGUI scene (no water/world v1), authored canvas (not runtime-built; the runtime-built
`UpgradeDraftUI` stays only for the in-run draft).

Panels (single canvas, tabbed):
1. **Dock** — banked resources (typed, with icons/tints), sub stats: rated depth, cargo capacity,
   O2 max, hull strength. Money-free: resources ARE the currencies.
2. **Outfitting** — grid of `ShopEntry`s from `UpgradeCatalog`: cost per `{ResourceType, amount}`[],
   buy → deduct + add to profile. Prereq-chained tiers reuse `UpgradeDef.prerequisites`.
3. **Loadout** — exclusive slot groups (`LoadoutSlotDef` SO: slot name + allowed choices):
   - Computerized system (1): Sonar / Spotlight rig / (Weapons, Flare later)
   - Hull feature (1): Ballast tank / Double O2 / Impact reinforcement / Pressure reinforcement
   - Tools (2): Mining beam / (Salvage hook, Harpoon later — beam is the only tool today)
   - Traversal (1): Dash (CavitationBurst) / (Turbo, Ram later)
   Owned-but-unselected items are inert that mission. Selection saved to profile.
4. **Missions** — 3 generated cards w/ scanner reports; pick → confirm → `MissionContext.Launch(spec)`.

`UpgradeCatalog` + `ShopEntry` + `LoadoutSlotDef` assets under `Assets/Submachina/Data/Meta/`.

## New scenes & build settings

- `Assets/Scenes/Hub.unity` — new.
- `Assets/Scenes/Mission_Descent.unity` — duplicate of `Ore testing.unity` (cleanest full gameplay
  scene), plus: `MissionController`, `LoadoutApplier` on the sub, extraction zone, new resource prefabs
  in spawn rules. HorrorScene remains untouched as the atmosphere showcase.
- Register Hub + Mission_Descent in Build Settings (fixes the dangling SampleScene entry).

## Implementation phases (= task list)

1. **Resource types + cargo** — `ResourceType`, `CargoHold`, `MiningResource` rework, typed prefab
   variants, spawn rule assets, cargo HUD, `SubStats.CargoCapacity`.
2. **Hull/pressure** — `HullSystem`, `CollisionDamage` rework, StatIds, feedback keys, HUD reserve bar.
3. **Ballast** — `BallastTank`, pump destination toggles, input actions, feedbacks.
4. **Mass/inertia** — `RegisterMass` aggregation + contributors.
5. **Persistence** — `PlayerProfile`, `ProfileService`, `UpgradeCatalog`, `LoadoutApplier`.
6. **Missions** — `MissionSpec/Generator/Context`, `MissionController`, retrieval objective, extraction.
7. **Hub** — scene, panels, shop, loadout slots, mission cards, launch/return flow.
8. **Integration** — end-to-end loop, build settings, context.md updates, big-plan.md checkboxes.

Ordering rationale: sub-side systems first (testable standalone in existing scenes), meta layer after,
so the hub has real stats/costs to display by the time it exists.

## Status (2026-08-09) — all phases implemented, loop verified end-to-end

A play-mode smoke test validated the full loop: hub renders → buy Reinforced Hull (wallet 50→40,
level 0→1) → mission launch → LoadoutApplier grants the purchase (RatedDepth 96→128, matching the
math exactly) → objective spawns at target depth → cargo Add() raises Rigidbody mass (3.0→3.65 for
5 Vent Brass) → objective + extraction completes the mission → cargo + reward banked → hub debrief
shows "Mission successful", updated wallet and stats. One bug found and fixed: mission scenes with
the sub authored inactive (drop-in join flow) broke MissionController — it now defers init until a
sub registers and auto-activates the sub when no LocalPlayerManager owns the scene;
Mission_Descent's inherited LocalPlayerManager is disabled (single-player loop).

Known gaps / follow-ups:
- Ballast tank is authored ACTIVE on the sub prefab (dev-friendly); flip the child inactive to
  hard-enforce the Hull Feature loadout slot once the hub loop is the main path.
- Neutralize missions spawn `RammerEnemy` via the same code path as the validated types but
  haven't been play-tested to the kill.
- `hazardLevel` is generated/displayed but not yet applied to spawns; per-resource abundance is
  scanner-forecast-only (derives from depth bands — honest, but missions don't yet boost spawns).
- Tools / Traversal / drones / turbo loadout slots deferred until those systems exist.
- Hub UI is placeholder V1 (runtime-built uGUI, same tier as UpgradeDraftUI).
- `HubStats` mirrors HullSystem/CargoHold/O2System base constants — keep in sync.
- Testing note: `EditorCapture` cannot capture ScreenSpaceOverlay canvases (renders black);
  verify hub UI via TMP text dumps or a Game View grab.

## Deliberately deferred

- Turbo / terrain ram / salvage hook / harpoon / flare / drones (slot lists reserve space; entries land later).
- Hidden-creature & fast-creature hazard archetypes (hazard field exists in spec; only creature level used v1).
- Grappling/anchor gear for currents (current strength affects missions v1 via CurrentManager only).
- O2 capacity *decay* mechanics (docs mention it; code doesn't have it — not resurrecting).
- Per-mission terrain obstacles beyond existing spawn rules.
