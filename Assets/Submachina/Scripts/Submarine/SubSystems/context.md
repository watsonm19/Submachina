# Submarine Subsystems (SubmarineComponents)

These are the modular subsystems that make up a submarine. Each extends **SubmarineComponent** and is reached from siblings via the facade (`Sub.O2`, `Sub.Physics`, etc.). Most ship as prefabs under `Prefabs/SubSystems`, slotted into a `SubmarineConfig` and assembled by `Submarine.Build()`. See `../context.md` for the facade and feedback overview.

## Base class

- **SubmarineComponent** — abstract base. `Awake()` does `GetComponentInParent<Submarine>()` then `Sub.Register(this)`; `OnDestroy()` unregisters. Derived classes call `base.Awake()` first. This auto-registration is what enables **runtime swapping** (destroy old, instantiate new — the registry stays current) and upgrade-gated loadouts. An editor-only banner reads the component's `[UsesFeedbacks]` attribute and renders the feedback keys as chips.

## Routers (arbitration & decoupling)

- **SubmarineFeedbackRouter** (`Sub.Feedbacks`) — semantic feedback switchboard. Serialized `mappings` pair each `FeedbackId` key with one or more `MMF_Player`s; `Play(key, position, intensity)` does an O(1) lookup and plays all mapped players, `Stop(key)` halts looping ones. Every gameplay system routes juice through this instead of holding `MMF_Player` references. Editor button `AddMissingMappings()` auto-populates via reflection over `SubFeedbacks` fields. A custom Odin `FeedbackIdDrawer` shows a categorized dropdown for each key.
- **SubmarinePumpRouter** (`Sub.Pumps`) — arbitration for the shared pump input. Pumps self-`Register`/`Unregister` (in their OnEnable/OnDisable). `Active` returns the registered `ISweetSpotPump` with the highest `ControlPriority` whose `WantsControl` is true (first-registered breaks ties); `IsActive(pump)` is the per-pump check. No hand-wired references between pumps or bars.
- **SubmarineAnchorRouter** (`Sub.Anchors`) — semantic mount-point registry (the visual-location mirror of the feedback router). Maps each `AnchorId` key to the `Transform` of a `SubmarineAnchor` marker. `Get(key)` returns the transform (falling back to the sub root on a miss); `TryGet(key, out t)` skips the fallback. Markers self-`Register`/`Unregister`, and `Awake` back-fills any anchors already nested under the sub. See `../SubAnchors/context.md` for the key types.

## Anchors & feedback binding (decoupled VFX placement)

- **SubmarineAnchor** — a marker placed on a child transform (muzzle, nose, tail) holding an `AnchorId` key; self-registers with `Sub.Anchors` (OnEnable/OnDisable) and draws a Scene gizmo. `Key` and `Point` expose the key and its transform.
- **FeedbackAnchorBinder** — plain MonoBehaviour on a self-contained feedback prefab. On `Start` it resolves its `AnchorId` via `GetComponentInParent<Submarine>().Anchors` and re-points the player's `MMF_ParticlesInstantiation`: `Attach` parents + nests the particles so they follow the sub; `PositionOnly` spawns a one-shot at the anchor. Swapping the prefab or its key moves the effect without touching gameplay code (Mode A). Modules can instead pass `Sub.Anchors.Get(key).position` to `Sub.Feedbacks.Play` for dynamic points (Mode B).

## Core movement & aim

- **O2System** (`Sub.O2`) — authoritative air tank and single source of truth for air state: `CurrentAirPressure`, `MaxAir`/`OriginalMaxAir`, and `ActiveDecayRate` = `(baseDecayRate × exertionMult + miningExtra) × depthMultiplier`. Exertion flags `IsThrusting` (set by physics) and `IsMining` (set by the laser) raise drain; capacity decays over time floored at `minMaxCapacity`; air at zero bleeds `Health`. API: `AddAir`, `ConsumeAir`, `RestoreCapacity`, `RefillAir`; events `onO2Depleted`/`onO2Restored`. Writes the `currentO2`, `maxAirCapacity`, `originalMaxAir` atoms for the HUD; reads the optional `currentDepth` atom for depth scaling.
- **SubmarinePhysicsController** (`Sub.Physics`) — force-based (not velocity-set) Rigidbody2D controller. Reads `thrustAction`, applies lateral thrust / upward-only counter-thrust, and applies the `currentDescentSpeed` atom (from `CurrentManager`) as downward ocean current each FixedUpdate. Gravity is disabled. Exposes `ThrustInput`, `IsDashing` (set by `CavitationBurst` to bypass the speed clamp), and `FacingSign`; drives sprite facing/tilt in LateUpdate. Writes `Sub.O2.IsThrusting`.
- **TurretAim** (`Sub.Turret`) — dual-input aim. Prioritizes gamepad right-stick (`aimAction`, past `stickDeadzone`) over mouse fallback, with seamless device switching and last-direction hold when idle. Exposes `AimDirection`, read by `MiningLaser`, `PlayerAttack`, and `CavitationBurst`.

## Pump system (sweet-spot air mechanics)

All pumps implement **ISweetSpotPump** and bind the **same** pump InputAction; the `SubmarinePumpRouter.Active` pump owns the input and the shared `BellowsBar`. The interface exposes the read state the router/HUD need: `ChargeProgress`, `IsInSweetSpot`, `SweetSpotMin/Max`, `IsAirLocked`, `IsOnCooldown`, `CooldownRemaining`, plus `WantsControl` and `ControlPriority` for arbitration.

- **ManualBellowsPump** — the always-on baseline (`ControlPriority 0`, `WantsControl = enableManualPumping`). Hold-to-charge with a sweet-spot window; a Perfect/Weak release calls `Sub.O2.AddAir(...)`, overshoot vents for nothing. Anti-spam **Air Lock** after rapid presses, plus a post-Perfect cooldown. `IsActivePump` reads `Sub.Pumps.IsActive(this)` (defaults true if no router exists, so it runs solo); when not active it cancels any in-flight charge so it can't stick. Plays `PumpCharge`/`PumpPerfect`/`PumpWeak`/`AirLock`.
- **O2PickupPump** — contextual intake pump (`ControlPriority 10`, outranks manual). Runs a looping 0→1 charge; pressing while looping grades the collect by timing (sweet spot = full reward, otherwise weak) and routes air via `O2Pickup.Collect(Sub, multiplier)`. `WantsControl` while looping **or** a pickup is in range. With `autoActivateInRange` it starts on range-enter and stops quietly when the last pickup leaves; `requirePickupToStart` gates manual starts. No cooldown. Detects pickups via `Physics2D.OverlapCircleAll`; draws a procedural LineRenderer ring.

**Hand-off:** the intake pump takes over near bubbles and the manual pump resumes the instant it lets go — entirely through the router, no direct references. **Independence:** with only one pump registered, that pump is active whenever it wants control, so a bellows-only or intake-only sub behaves correctly.

**Scene wiring (Proto_Descent):** `O2System`, `SubmarineFeedbackRouter`, `SubmarinePumpRouter`, `ManualBellowsPump`, and `O2PickupPump` live on the submarine root, sharing one pump InputAction. A single `BellowsBar` child follows `Sub.Pumps.Active` — no per-pump bars or manual references.

## Weapons & abilities

- **MiningLaser** — continuous beam fired along `Sub.Turret.AimDirection` (`mineAction` hold). Gated by `Sub.O2.CurrentAirPressure > 0` and sets `Sub.O2.IsMining`. Raycasts on `miningLayer`; calls `MiningResource.SetMiningProgress(...)` each frame and `Collect(Sub)` at completion. Drives `MiningBeamVFX` and plays looping `MiningActive` / one-shot `MiningCollect`.
- **PlayerAttack** — directional melee cone. Gathers enemies in `attackRange` on `enemyLayer` via `OverlapCircleAll`, filters by dot product against `Sub.Turret.AimDirection` (`coneHalfAngle`), and calls each `Health.TakeDamage`. Events `onAttack`/`onDamageDealt`; plays `AttackSwing` at its `attackAnchor` (resolved via `Sub.Anchors`, default `Muzzle`); procedural LineRenderer arc visual.
- **CavitationBurst** — directional dash. Consumes `Sub.O2.ConsumeAir(airCost)` (gated on air), applies an impulse with reduced drag for `burstDuration`, and sets `IsDashing` so physics lets it exceed `maxSpeed`. Direction priority: thrust input → velocity → `Sub.Turret.AimDirection`. Events `onBurstStart`/`onBurstEnd`; plays `DashStart`/`DashEnd`.

## Economy

- **ScrapManager** (`Sub.Scrap`) — consumable scrap bank (earned from mining, spent to heal). `AddScrap()` (capped at `maxScrap`) from `MiningResource`; `useScrapAction` heals `Sub.Health` by `healPerScrap` when banked and below full. Events `onScrapAdded`/`onScrapFull`/`onScrapUsed`/`onNoScrap`/`onFullHealth`; plays the matching Scrap feedbacks. Read by `ScrapDisplay`.
- **ResourceManager** (`Sub.Resources`) — roguelite progression. `AddResources(amount)` (from `MiningResource`) accumulates toward `threshold = baseThreshold + level × thresholdIncrement`, carrying overflow and firing multiple level-ups per call. Events `onResourcesAdded`/`onLevelUp` (the latter drives the upgrade-draft UI). Writes the `currentResources` / `resourceThreshold` atoms for `ResourceBar`; plays `ResourcesAdded`/`LevelUp`.
