# Core.Audio — AudioDirector Layer

Runtime audio orchestration sitting under the environmental modulation system (see
`Assets/Scripts/Core/Modulation/context.md`). No AudioMixer required: volumes are computed
directly on AudioSources the director owns. Not a singleton — resolve via `AudioDirector.FindFor()`.

## Pieces

- **AudioDirector** (MonoBehaviour) — owns three voice groups:
  - *Ambience*: one persistent looping AudioSource per `AmbienceLayerDef`, created lazily and
    playing silently from first reference. Volume = `influenceCurve(maxInfluence) × baseVolume ×
    masterAmbienceVolume × duckMultiplier`, moved linearly at 1/fadeIn / 1/fadeOut per second.
    Multiple influence sources per layer combine via MAX (`SetAmbienceInfluence(def, key, v)`).
    `StopAmbience`/`StopAllAmbience(fade)` force-fade and stop voices (a fresh influence resumes).
  - *One-shots*: pooled AudioSources (grows to a cap, then steals the oldest). `PlayOneShot(def)`
    2D, `PlayOneShotAt(def, pos)` spatial. Per-def cooldowns + Random/ShuffleBag clip selection
    (bag state lives in the director, keyed by def — never in the SO).
  - *Stingers*: `PlayStinger(def)` gated by per-def AND per-category AND global cooldowns; on play
    runs a duck envelope (attack/hold/release) that scales all ambience via duckMultiplier.
    `TriggerStinger`/`TriggerOneShot` are void wrappers for UnityEvent wiring.
- **AmbienceLayerDef / AudioOneShotDef / AudioStingerDef** (SOs, `Submachina/Audio/...` menus) —
  pure authoring data; all runtime state (cooldowns, bags, volumes) lives in the director.
- **AmbienceInfluenceTarget** (`ModulatedFloatTarget`) — bridge from the modulation system: its
  built-in parameter binding drives Baseline and the composited value becomes one ambience
  layer's influence. Pushes influence 0 on disable so layers fade out when their driver goes away.
- **StingerTimerSignal** (`FloatSignal`) — exposes seconds-since-last-stinger (optionally per
  category) back to the modulation layer for pacing rules.

## Conventions

- Defs are created under `Assets/Submachina/Data/Audio/{Ambience,OneShots,Stingers}`.
- `SecondsSinceAnyStinger` clamps to 99999 when nothing has played.
- Debug: `BuildDebugReadout()` (also surfaced as an Odin readonly inspector string).

First real usage: the HorrorScene descent sequence — built/rebuilt by
`Tools/Submachina/Build Descent Horror Sequence (active scene)`
(`Assets/Submachina/Scripts/Editor/DescentSequenceSceneBuilder.cs`).
