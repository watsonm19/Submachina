# Submachina.Core — Atmosphere Components

Game-specific pieces of the descent horror experience, built on `Core.Modulation` (see
`Assets/Scripts/Core/Modulation/context.md`) and wired to audio purely through UnityEvents
(no compile-time dependency on `Core.Audio`).

- **LightFlicker** — steps a `ModulatedFloatTarget`'s Multiplier channel with random values
  (dying-bulb effect), then restores neutral. `PlayFlicker()` random duration, `PlayFlicker(s)`
  explicit. Safe on disable.
- **SkitterSpawner** — occasionally darts a dark silhouette sprite across the camera view when a
  semantic parameter (Dread) is high enough; single pooled SpriteRenderer, `onSkitter` event for
  whoosh audio. `TrySkitterNow()` for testing.
- **WreckEncounter** — target-depth beat: `BeginEncounter()` activates + positions the wreck object
  ±horizontalOffset from the reference (main camera), then polls `Submarine.FindNearest` until one
  is within reachRadius → `onWreckReached` (once).
- **DescentFinale** — the big bang: `TriggerFinale()` (one-shot) waits bangDelaySeconds (timed so
  the 5s reverse riser peaks at the bang), plays feedbacks + `onBang`, forces the Darkness
  parameter to max forever (Max modifier, infinite hold), and fades the light target's Multiplier
  to 0 → `onBlackoutComplete`.
- **CameraShakeTrigger** — UnityEvent-wireable `MMCameraShakeEvent` firing for rules/finale
  without authoring an MMF_Player chain.

## HorrorScene descent sequence

Built/rebuilt via `Tools/Submachina/Build Descent Horror Sequence (active scene)`
(`Assets/Submachina/Scripts/Editor/DescentSequenceSceneBuilder.cs`) into a "Descent Direction"
hierarchy. Depth staging is derived from the scene's LevelBounds bottom so the sequence fits any
level: Darkness saturates at 80% of max depth, Dread runs 15%..78% (the traversable seabed sits
well above the bounds bottom, so the encounter trigger must be reachable before grounding out).
Flow (listener = main camera, depth = units below y=0):

```text
depth → Darkness (0..80% depth, eased, 0..0.85) → global light 0.30 → 0.015, deep ambience
depth → Dread    (15%..78% depth, linear, 0..1) → eerie ambience (0.15..0.7), bassy build
                                                  (0.45..0.9), base underwater fades 1 → 0.35
Dread rules: 0.35 flicker+bell (cd 22s) · 0.5 waterphone stinger (cd 55s) · 0.55 moans (cd 28s)
             0.82 JUMP SCARE (one-shot: scream stinger + 1.6s flicker + shake)
             0.97 sustained 2s → WreckEncounter.BeginEncounter (one-shot)
encounter → Intensity ramps (Add modifier, 25s attack) → red global light 0..0.8 + pulser,
            BuildSwells ambience 0..1, drone one-shot; skitters active from Dread ≥ 0.6
wreck reached → riser one-shot + finale (bang at 4.6s): explosion + shake + StopAllAmbience(2.5)
                + darkness ceiling + light multiplier → 0; ambience outputs/rules/skitters/red
                light disabled (output targets carry their own parameter bindings — no routes)
```

Repeating DirectorRules on monotonic depth use resetThreshold 2 (above param max) so they re-arm
immediately; cooldown + probability do the pacing. Legacy `Sound Manager` ambience/music sources
are disabled by the builder — the AudioDirector owns ambience in this scene.
