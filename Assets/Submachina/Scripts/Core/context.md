# Core Systems

`Scripts/Core` is the namespace for **generic, cross-cutting systems** — pacing, camera, world current, damage reaction, and shared data assets. Project-specific gameplay lives elsewhere; see the sibling context docs:

- **`Scripts/Submarine/context.md`** — the submarine facade, config-driven assembly, and the semantic feedback system.
- **`Scripts/Submarine/SubSystems/context.md`** — all `SubmarineComponent` subsystems (O2, physics, weapons, pumps, scrap/resources) and their routers.
- **`Scripts/Submarine/SubUI/context.md`** — the HUD bars/displays and the "Atoms for UI reads" pattern.
- **`Scripts/World/context.md`** — procedural world generation and world entities (rocks, resources, O2 bubbles, enemies).

## Cross-system data flow via Unity Atoms

Core systems publish shared state through Unity Atoms `FloatVariable`s rather than direct references, so consumers (UI, difficulty, gameplay) read the atom instead of the producer:

- **`currentDepth`** — written by `DepthTracker`, read by `ActManager` (depth bonus), `LevelConfig` zone queries, `O2System` (depth-scaled decay), and depth-aware visuals.
- **`currentDescentSpeed`** — written by `CurrentManager`, read by `SubmarinePhysicsController` (ocean current force), parallax, and UI.

## Components

- **ActManager** — owns the run's act countdown. When the timer expires it fires `onBossSpawn`; reaching a depth threshold (read from the `currentDepth` atom) before expiry fires `onDepthBonusEarned`. `CompleteAct()` (called by the boss on death) advances the act; `onActStarted`/`onFinalBoss` round out the lifecycle. Odin play-mode buttons for skip/award/complete.
- **ActTimerHUD** — read-only consumer of `ActManager` (`RemainingTime`, `ActDuration`, `Act`). Renders MM:SS + act label and lerps text color from `normalColor` to `urgentColor` as the act nears expiry (`urgencyThreshold`).
- **CameraFollow** — LateUpdate smooth-follow of a target Transform (the sub) with offset, optional X-lock and top clamp. `SnapToTarget()` and `SetTarget()` for instant repositioning / runtime retargeting. Pure transform driver — no atoms or events.
- **CurrentManager** — single source of truth for ocean descent speed. Computes `TargetSpeed` from a progression tier (`AdvanceTier`/`SetTier`) plus temporary boosts (`AddSpeedBoost`/`ResetBoosts`), and writes the `currentDescentSpeed` atom each frame (smoothed or instant). Driven by public calls, not events.
- **DepthTracker** — a `SubmarineComponent` (lives here because depth is a generic concept). Converts the sub's world Y (via `Sub.Physics`) into metres-below-surface and writes the `currentDepth` atom each frame. Surface is `surfaceY` (Y=0), descent is negative Y; depth is clamped at 0. A `DepthTracker` prefab ships under `Prefabs/SubSystems`.
- **CollisionDamage** — a `SubmarineComponent` requiring `Health`. On `OnCollisionEnter2D` above `minImpactSpeed` (filtered by `collisionLayers`, with a `damageCooldown`), it applies `damagePerImpact` and fires `onCollisionDamage(impactSpeed)`. Plays `SubFeedbacks.CollisionDamage` via `Sub.Feedbacks` (declared with `[UsesFeedbacks]`).
- **HitFlash** — auto-wired (`GetComponent` for `Health` + `SpriteRenderer`) white-flash on damage. Subscribes to `Health.onHealthChanged`, captures and restores the original color so it composes with other tints (e.g. `EnemyController` state colors). Coroutine-guarded against rapid re-hits.
- **LevelConfig** — per-level ScriptableObject (`Submachina/Level Config`) defining trench shape (`TotalDepth`, `HalfWidth`, exit-gate Y) and normalized zone boundaries with per-zone spawn budgets via the nested `ZoneConfig`. `GetZone(depth)` / `GetZoneConfig(...)` classify depth into `ZoneType { Shallow, Midnight, Abyss }`. Read by `ChunkSpawner`/`WorldChunk`.
- **SpriteMaskInstanceIsolator** — requires `SpriteMask`. Confines each mask instance to its own prefab instance via sorting-order banding (`bandStride`/`maxSlots`), so pooled masked VFX don't reveal each other's particles. Claims a band from a static slot counter once in Awake (pooling-safe).
