# Submachina — Current Game State Overview

*Snapshot as of 2026-09-02. Purpose: a complete inventory of what exists today — features, mechanics, systems, content — to guide decisions about what's missing and what can be leveraged. Each item is deliberately brief; see the per-folder `context.md` files for depth.*

---

## Core Loop (implemented end-to-end)

```
HUB → buy permanent upgrades → pick loadout → accept 1 of 3 generated missions
  → Mission_Descent: descend, mine typed resources into cargo, complete objective
  → survive pressure / O2 / impacts / creatures → return above extraction depth
  → bank cargo + reward → back to HUB. Death = lose unbanked cargo, keep permanents.
```

The full loop is verified working (smoke-tested). Hub UI is placeholder V1 (runtime-built uGUI).

---

## The Player Submarine

Built on a **facade + auto-registering component** architecture (`Sub.O2`, `Sub.Physics`, etc.) — subsystems are modular prefabs, swappable at runtime, assembled via `SubmarineConfig`. No singletons; supports multiple subs.

### Movement & physics
- **Force-based thrust controller** — lateral thrust + upward counter-thrust; constant downward ocean current (tiered + boostable via `CurrentManager`); sprite facing/tilt.
- **Mass aggregation** — cargo, ballast water, and hauled pods add real Rigidbody mass; heavy = sluggish.
- **Cavitation Burst (dash)** — air-cost directional dash with cooldown-ready signal.
- **Turret aim** — dual-input (right stick priority, mouse fallback), feeds laser/attack/dash.

### Air (O2) — the central survival resource
- **O2System** — air pressure drains over time, faster while thrusting/mining, scaled by depth; max capacity decays; empty = health bleed.
- **Sweet-spot pump minigame** — hold-to-charge manual bellows pump with perfect/weak timing windows, anti-spam air-lock, cooldown.
- **O2 pickup pump** — contextual intake pump that outranks the manual pump near air bubbles (looping timing grade); router-arbitrated hand-off between pumps.
- **Pump destinations** — pumped air routes to O2 reserve / Ballast / Hull (cycle key); sweet-spot-only for the fancy destinations.

### Depth-progression systems
- **HullSystem** — rated depth fixed by loadout; past it, ramping pressure-strain damage; depth also scales enemy damage taken; impact overload model; pump-air-to-hull for temporary depth headroom at an HP cost; warning creaks.
- **BallastTank** — 3-position gear shifter (Empty/Neutral/Full) on a conserved shared O2 economy (filling draws from the reserve, venting returns it, overflow spawns real bubbles).
- **CargoHold** — typed cargo with unit capacity and mass; jettison (heaviest-first) spawns re-collectible sinking parcels — the anti-stuck valve against ballast/cargo interplay.

### Tools & combat
- **Mining laser** — continuous aimed beam, air-gated, with escalating beam VFX; mines resource nodes and breaks rocks.
- **Melee attack** — aimed cone attack (procedural arc visual).
- **DashRam** — front shield hitbox active while dashing (loadout toggle upgrade).
- Scrap-to-heal: banked scrap consumable heals the sub.

### Sonar (full progression feature)
- Manual cooldown ping; echoes return sooner for closer contacts; per-archetype **sonic signatures** (size class, reflection strength, color/icon/sound, "ripple voice").
- **Four upgrade tiers**: Presence → Direction → Size → Identify, gating both the compass-ring HUD blips and the diegetic screen-space **echo ripples** (identity read through wave rhythm/tint/character).
- Any world object is detectable via a drop-on `SonarTarget` marker + signature asset (6 signatures authored).

### Health & damage
- Generic combat pipeline: `HitData → HitReceiver` (gates) → `Health` + `Knockback2D` (control-window knockback that stuns velocity-driven AI; death launches).
- Collision damage above impact speed threshold (routed through hull model when present); hit flash.

---

## Stats, Upgrades & Progression

- **Stat system** — packed-ID stat keys in 11 categories (Movement, O2, Pumps, Mining, Combat, Dash, Defense, Sonar, Hull, Cargo, Ballast); stacked additive/multiplicative modifier table; components resolve via null-safe accessors.
- **Four upgrade kinds** — stat modifiers, behavioral add-on prefabs, whole-component swaps, hierarchy toggles (reference-counted feature flags).
- **Two progression tracks, intentionally separate:**
  - **In-run draft** — resource XP → level-up → 3-choice upgrade draft UI (temporary field mods, roguelite style).
  - **Hub shop** — permanent purchases with typed resources, cost growth per level, prerequisite chains.
- **Loadout slots** — exclusive-choice slots (Computerized: sonar tiers…; Hull Feature: ballast tank / double O2 / reinforcements); owned-but-unpicked = inert.
- **~24 upgrade assets authored** — hull/pressure/impact reinforcement, O2 capacity, pump gains, dash cost/cooldown, sonar tier ladder + range/cooldown, weapon damage, thrust speeds, cargo expansion, dash-ram, ballast unlock.
- Debug panel + setup wizard for upgrade testing.

## Resources & Economy

- **5 typed deep-sea resources** (SO identities with tint, unit mass, native depth band): Ferrite Nodules, Vent Brass*, Clathrate Ice, Luminite, Abyssite (*asset currently named OpalEbonite). Each maps to an upgrade domain.
- **Mining nodes** award in-run XP + typed cargo units + chance of physical scrap drop.
- **Scrap** — capped consumable bank (heal); **cargo** banks to the persistent wallet only on successful extraction.
- **Ore clusters** — procedural rock+ore scatter clusters (deterministic from chunk seed, bad-luck protection on value rolls), incl. a "hidden beam" variant.

## Enemies & Creatures

All procedural creatures are **physics bodies with state-machine brains that speak through body language** — solver-driven bodies (chains, radial blobs, IK legs) where telegraphs are posture/flash/wave changes, not UI.

- **Eel** — sinuous chaser: lurk → weaving hunt → coil telegraph → rigid lunge → wobbly vulnerable recovery.
- **Jellyfish** — pulse propulsion (animation IS movement), buoyant Perlin wander, glow telegraph, contact sting; animated bell rim + tentacles. New **JellyfishPod** group prefab.
- **Squid** — jet ambusher: chromatophore flicker telegraph, dash-through attack, ink-cloud escape.
- **Crab** — walking creature: spring-ride over raycast ground, four IK-leg gait, claw body-language states, snap-hop attack, personality-dialed cliff leaping, mid-air "crab swim" + air lunge.
- **Anglerfish** — deep-water ambush horror: near-invisible body, glowing bobbing lure, flare telegraph → violent lunge → gives up and re-lights slowly.
- **Fish school** — ambient boids (single-controller, no per-fish physics), scatter/regroup near subs, pseudo-depth 2.5D fakery. Not an enemy.
- **Legacy simple enemies** — RammerEnemy / SeaCreature / PassiveCreature (older `EnemyController` chase-lunge AI; still spawnable, drop O2 bubbles on death).
- **Distance culling** on all creatures (chunks never despawn, so AI/sim/renderers suspend beyond ~50 units).
- Per-creature spawn rule assets exist but are opted into profiles manually.

## Environment & World Generation

- **Chunk-based procedural world** — persistent grid of chunks around the camera, deterministic per-cell seeds (reproducible worlds), never despawned.
- **Data-driven spawning** — `SpawnProfile`/`SpawnRule` SO assets: depth ranges + prevalence curves, count models (incl. exact fractional expected-count), placement strategies (scatter / wall-protrusion / center band), spacing, post-spawn configurators, per-rule mission gates. 6 profiles + ~18 rules authored.
- **Level shape** — `LevelConfig` trench (depth/width/exit gate) + `LevelBounds` camera clamping; `WorldBoundary` scrolling side walls.
- **World entities** — breakable rocks (laser or impact), mineable ore nodes, O2 bubbles, scrap pickups, cargo parcels.
- **Parallax** — multi-layer BG/FG with exact backdrop-fit math for bounded levels, infinite tiling layers, deterministic layer-space decor spawning.
- **Rock/ore art pipeline** — `TerrainObjectGenerator`: an in-editor procedural sprite baker (silhouette families, surface noise/cracks/strata, paint layers, stamped decal layers with relief, crystal prisms/druse, baked normals + spec masks, presets, live lit preview) fed by an AI (Nano Banana) material/decal generation pipeline with auto-import.

## Atmosphere, Horror & Audio (WIP showcase — HorrorScene)

- **Modulation system (generic)** — signals (depth, timers) → semantic parameters (Darkness, Dread, Intensity) with blend modes + smoothing → modulated outputs (lights, materials, ambience) + threshold-fired `DirectorRule` events (hysteresis, cooldown modes, probability). Live dataflow **Director Graph** editor window.
- **AudioDirector (generic)** — code-driven ambience layers (influence-mixed, MAX-combined), pooled one-shots, cooldown-gated stingers with ducking. No AudioMixer dependency. 5 ambience + 6 one-shot + 2 stinger defs authored.
- **Scripted descent horror sequence** (rebuildable via editor tool, depth-staged to any level): darkness saturation, dread-driven eerie/bass ambience, paced scares (light flicker + bell, waterphone stinger, creature moans), a one-shot **jump scare**, dark **silhouette skitters** across the camera, a **wreck encounter** beat, and a **finale** (riser → bang → permanent blackout).
- Building blocks: LightFlicker, SkitterSpawner, WreckEncounter, DescentFinale, CameraShakeTrigger.

## Presentation & Rendering

- **Underwater distortion post-process** — fullscreen water wobble + chromatic refraction, procedural god rays, warped caustics, deep tint; pooled interactive **ripples** (speed-triggered, sonar echoes) and **propulsion wakes**.
- **2D specular shader family** — one shared HLSL core behind sprite + generated-mesh shaders: GPU Blinn-Phong glints driven by the *actual* scene lights (headlamp cone-gated, zero per-sprite CPU, bloom-ready), normal maps, procedural form shapes, outline / rim emission / hit flash. `SpecularController` is the universal per-instance driver.
- **Procedural animation toolkit (generic)** — follow-the-leader chain solver with wave/sway drivers, tapered ribbon + sprite-run renderers, deformable radial blob mesh (squash/rim-wobble, sprite-silhouette baking, deform-pivot anchors so eyes/fins ride the deformation), 2-bone IK legs + stepping gait controller, limp/freeze hit reactions.
- **Feel/MMF juice everywhere via semantic routing** — gameplay fires `FeedbackId` keys (12 categories) through a router; **anchor** keys resolve mount-point transforms (muzzle/tail/…) across prefab boundaries; a feedback event bus lets self-contained prefabs react (e.g. dash-ready light).
- Floating text pool, PSB layer baker, sprite blur, camera shake, misc VFX prefabs (beam laser set, nova bursts, creature damage/death feedbacks).

## Meta Layer (Hub, Missions, Persistence)

- **Persistence** — JSON player profile (typed wallet, owned upgrades, loadout picks, mission stats) via a static write-through `ProfileService`.
- **Mission generator** — 3 offers per hub visit around the sub's rated depth (70% / 100% / 130% stretch), typed (Retrieval / Neutralize / Research), with an honest **scanner forecast**: forecasted resources actually spawn at forecasted abundance in their depth bands; nothing else exists in that level.
- **Mission flags** (Infested, MineralRich, …) — biome-ish site traits that can gate/scale any spawn rule or swap the whole spawn profile; plumbing done, generator doesn't set them yet.
- **Objectives** — Retrieval (latch pod, haul its mass home — fully tested), Neutralize (kill spawned hostile — untested to the kill), Research (dwell-scan N sites). Extraction banks cargo; death fails.
- **Hub screen** — wallet, shop, loadout picks, mission cards → launch (placeholder runtime uGUI).
- Idempotent editor builders for meta content and both scenes.

## Local Multiplayer (couch co-op)

- **Drop-in/drop-out** — press any button to join; slot pool of pre-placed subs; keyboard+mouse or N gamepads as logical players; drop-out on disconnect; join overlay UI built from code.
- **Per-player input isolation** — cloned input asset restricted to paired device instances.
- **Shared frame-all camera** — centroid follow + zoom-to-fit (no splitscreen); per-sub HUD via hierarchy-scoped observers (no per-player asset duplication).

## HUD & UI

Health bar, O2 bar (+ capacity-decay overlay), resource/XP bar, world-space pump charge bar with sweet-spot markers, scrap dots, cargo display, ballast bar, hull reserve bar, depth gauge ("142m / 160m"), pressure strain bar, vulnerability gauge, sonar compass HUD, act timer, upgrade draft UI, floating text, generic atom-bound TMP text binders, controls canvas.

## Scenes

| Scene | Role |
|---|---|
| `Hub.unity` | Meta hub (generated) |
| `Mission_Descent.unity` | The mission gameplay scene (main loop) |
| `Ore testing.unity` | Full gameplay sandbox (Mission_Descent's parent) |
| `HorrorScene.unity` | Atmosphere/horror descent showcase |
| `Proto_Descent Juiced` / `_2` | Earlier gameplay prototypes |
| `JDTestScene`, `Blur scene` | Effect/test scenes |

## Notable Editor Tooling

Director Graph window · Terrain Object Generator (+ AI art batch + import processor) · descent-sequence scene builder · meta content/scene builders · spawn profile test-spawn buttons · upgrade setup wizard + debug panel · EditorCapture (deterministic screenshots) · Odin test buttons throughout (fake sonar contacts, test limp, fire rules, audition stingers…).

---

## Known Gaps / WIP (recorded, not speculative)

- Hub UI is placeholder; mission flags generated nowhere yet; `hazardLevel` displayed but unused; Neutralize untested.
- Only one biome/level; zones (Shallow/Midnight/Abyss) exist as config but spawn budgets are deprecated.
- Loadout slot lists reserve space for tools/traversal that don't exist yet (salvage hook, harpoon, turbo, flare, drones).
- Horror direction system exists only as the scripted HorrorScene sequence — not yet integrated into missions.
- Splitscreen, per-player UI polish, and hidden/fast-creature hazard archetypes deferred.
- Legacy 3D combat scripts and old input scripts flagged safe-to-delete.
