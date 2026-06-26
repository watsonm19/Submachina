# Sonar Detection — World Side

The data model that makes world objects detectable by submarine sonar. The submarine-side scanner, tier gating, audio, and HUD live under `../../Submarine/` (see `SubSystems/context.md` → *Sonar* and `SubUI/context.md` → *SonarHud*).

## Concept

The sub emits a pulse; objects in range reflect an echo back. Each echo carries a **distance** (encoded as return delay), a **direction**, and a **"sonic signature"** (color / shape / sound) that identifies *what* the object is. How much of that the player can read is unlocked progressively (presence → direction → size → identity) via the upgrade system.

## Components

- **SonarSignature** (`SonarSignature.cs`) — a `ScriptableObject` describing how an archetype reads on sonar: `displayName` + `blipIcon` (revealed only at the Identify tier), `blipColor`, `sizeClass` (`SonarSizeClass` Tiny→Huge), an optional per-archetype `returnPingFeedback` cue, and `reflectionStrength`. One asset per archetype lives in `Assets/Submachina/Data/Sonar/` (Fish, Octopus, Mineral Deposit, Scrap, Air Pocket, Rock). `SizeRangeFactor` maps the size bucket to a reflect-range multiplier (Tiny 0.4 … Huge 1.6) — the "larger objects reflect from further" rule.

- **SonarTarget** (`SonarTarget.cs`) — the marker that makes any GameObject detectable. Drop it on an entity prefab and assign a `SonarSignature`. The scanner finds it via an `OverlapCircle` scan + `GetComponentInParent<SonarTarget>()` (so multi-collider entities resolve to one contact — the same idiom `PickupRangeDetector`/`DashRam` use). `MaxReflectRange(baseRange) = baseRange × SizeRangeFactor × reflectionStrength` is the furthest distance at which this object still echoes. An optional `reflectionPoint` overrides the reflect origin when the visual centre differs from the pivot.

## Why a component + SO, not an interface

Detectable entities share no base class (`EnemyBase`, `PassiveCreature`, `MiningResource`, `ScrapPickup`, `O2Pickup`, rocks). Rather than retrofit an `ISonarDetectable` onto all of them, a single `SonarTarget` component + a shared `SonarSignature` asset makes anything detectable with zero per-type code — designers author a signature once and drop the marker on each prefab.

## Authoring a detectable object

1. Add a **SonarTarget** to the entity prefab (it needs a Collider2D somewhere in its hierarchy for the scan to find it — most entities already have one).
2. Assign the matching **SonarSignature** asset from `Data/Sonar/`.
3. Tune `sizeClass` / `reflectionStrength` on the signature so big/hard things show up from further out.
