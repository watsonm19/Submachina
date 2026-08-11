# Meta Layer — Hub, Missions, Persistence

The between-missions layer: what survives a run, how missions are offered and
resolved, and the hub where progression is spent. Design rationale and concrete
numbers live in `dev-docs/feature-plan.md` at the repo root.

## Core loop

Hub (buy upgrades, pick loadout, accept a mission) → `MissionContext.Launch(spec)`
→ `Mission_Descent` scene (descend, mine typed resources into `Sub.Cargo`,
complete the objective, return above `extractionY`) → cargo + reward banked to
the profile → back to the hub. Death loses unbanked cargo; permanent upgrades
persist.

## Persistence

- **PlayerProfile** — serializable POCO: resource wallet (keyed by
  `ResourceType.Key`), owned upgrades (`UpgradeDef` name + level), loadout
  selections (`"slot:def"` strings), mission stats. No Unity references in the
  JSON.
- **ProfileService** — static, no singleton GameObject. Load-on-demand from
  `persistentDataPath/submachina_profile.json`, write-through saves. Keyed
  helpers: wallet (`GetResource`/`AddResource`/`TrySpend` — atomic multi-line
  costs), upgrades (`GetUpgradeLevel`/`SetUpgradeLevel`), loadout
  (`GetLoadoutChoices`/`IsLoadoutChoice`/`ToggleLoadoutChoice` with pick-limit
  eviction), `BankCargo(CargoHold)`, `RecordMission`.

## Shop & loadout data

- **UpgradeCatalog** (SO, `Data/Meta/UpgradeCatalog.asset`) — the shop stock
  (`ShopEntry`: def + `ResourceCost[]` + `costGrowth` per owned level) plus the
  loadout slot list. Doubles as the name→`UpgradeDef` resolver for persistence.
- **LoadoutSlotDef** (SO, `Data/Meta/Slot_*.asset`) — an exclusive slot
  ("Hull Feature", `maxPicks`, choices). Owned-but-unpicked choices are inert
  for the mission.
- **LoadoutApplier** — MonoBehaviour on the sub root in mission scenes. In
  `Start` (post-Awake registration) grants owned upgrades through
  `Sub.Upgrades.Grant`, once per level: non-slot purchases always, slot choices
  only when picked. The "Ballast Tank" choice works via the existing
  hierarchy-toggle upgrade kind (`BallastTankFeature` + `UpgradeToggleTarget`
  marker on the sub's BallastTank child — authored ACTIVE for now so test
  scenes keep ballast; flip the child inactive to enforce the restriction).
- Assets are (re)built idempotently by `Tools/Submachina/Build Meta Content`
  (`Editor/MetaContentBuilder.cs`) — tune numbers there and re-run.

## Missions (`Missions/`)

- **MissionSpec** — plain serializable offer: type (Retrieval / Neutralize /
  Research), targetDepth, currentStrength, o2Richness, hazardLevel, scanner
  `forecast` (per-resource abundance → RICH/DETECTED/TRACE grades), reward.
- **MissionGenerator** — static; emits 3 offers per hub visit around the sub's
  rated depth (≈70% / 100% / 130% — the stretch card is the depth-progression
  carrot). The forecast is a PROMISE, not a payout: **MissionResourceRule**
  (`World/Spawning/`, a `SpawnRule` subclass in the `MissionProfile` used by
  `Mission_Descent`'s ChunkSpawner) expands `MissionContext.Current.forecast`
  into concrete per-type spawn rules at chunk-generation time — forecasted
  types spawn at forecasted abundance inside their native depth bands
  (TRACE ≈ occasional single nodes, RICH ≈ dense), unlisted types don't exist
  in that level, and there is no completion reward: mined → cargo hold →
  extracted is the only way resources bank. No active mission → a moderate
  fallback mix so sandbox play still has ore. This is also the composition
  hook for future biome packs (any `SpawnRule` subclass can contribute
  multiple runtime rule datas via the virtual `Rules` property).
- **MissionContext** — static scene hand-off (`Launch`, `ReturnToHub`,
  `Current`, last-result flags). Scenes played directly see null and fall back
  to a debug spec.
- **MissionController** — scene object in `Mission_Descent`: applies
  environment (`CurrentManager.AddSpeedBoost`, o2Richness → O2 decay stat mod),
  spawns the objective at targetDepth (`MissionCargoPod` / hostile w/
  `Health.onDeath` / N `ResearchSite`s), tracks completion + deepest depth,
  extraction above `extractionY` banks `Sub.Cargo` + reward and returns to hub;
  sub death fails the mission. UnityEvents for all majors + an OnGUI debug overlay.
  **Single-player vs join flow:** init defers until a `Submarine` registers.
  With an ACTIVE `LocalPlayerManager` the join flow owns sub activation and
  camera registration. With it disabled/absent (the intended single-player
  mission setup), the controller wakes the sub itself AND wires the camera —
  registering all subs with a live `MultiTargetCamera2D`, or else enabling and
  retargeting the dormant `CameraFollow` on the main camera (scenes authored
  for the join flow ship it disabled, and the multi-cam rig often sits on the
  disabled manager object).
- **MissionCargo** — retrieval pod: latches to the touching sub (parents +
  `RegisterMass` haul weight), fires `onRetrieved`.
- **ResearchTarget** — dwell-scan site (progress pauses, not resets, when the
  sub leaves the radius), fires `onScanned`.

## Hub (`Hub/`)

- **HubScreenController** — runtime-built uGUI hub screen (placeholder V1, same
  philosophy as `UpgradeDraftUI`): wallet header, Outfitting (buy from catalog),
  Loadout (slot picks), Missions (3 scanner-report cards → Launch).
- **HubStats** — computes rated depth / cargo / O2 for display from the profile
  alone (mirrors `HullSystem`'s formula — keep in sync if hull constants change).

## Scenes

`Tools/Submachina/Build Meta Scenes` (`Editor/MetaSceneBuilder.cs`) builds/updates
`Assets/Scenes/Hub.unity` (camera + event system + hub controller) and
`Assets/Scenes/Mission_Descent.unity` (Ore testing clone + mission rig +
LoadoutApplier on each sub) and registers both in Build Settings (hub first).

## Interactions

- `CargoHold` (`Sub.Cargo`) is the in-run side of the wallet; `MiningResource`
  deposits typed units, `MissionController` banks them via `ProfileService`.
- The in-run 3-choice draft (`ResourceManager.onLevelUp` → `UpgradeDraftUI`)
  is unchanged and intentionally separate: temporary in-run field mods vs the
  hub's permanent purchases.
