# Core.Modulation — Environmental Modulation System

Generic runtime for translating raw game facts into coordinated atmosphere. Four layers:

```text
Raw signals (FloatSignal)            depth, timers, manual sliders...
        ↓ SignalContribution         normalize → curve → remap → blend
Semantic parameters (DirectorParameterDef + EnvironmentDirector)
        ↓ FloatRoute                 curve → output range
Outputs (ModulatedFloatTarget)       Light2D intensity, materials, ambience influence...
        + DirectorRule               discrete events off parameter thresholds
```

## Key classes

- **FloatSignal** (abstract) — reports a measurable fact, never interprets it. Concrete: `TransformDepthSignal` (Y below a surface line; defaults to main camera = the listener context), `ManualFloatSignal` (inspector slider for testing), `TimerSignal`.
- **DirectorParameterDef** (SO, `Submachina/Director/Parameter Def`) — authoring definition of a semantic parameter (Darkness, Dread...): range, base value, attack/release smoothing. Runtime values never live in the asset.
- **EnvironmentDirector** — per-scene evaluator (NOT a singleton; consumers use `EnvironmentDirector.FindFor(component)` — parents first, then scene, so multiple rigs can coexist). Composes contributions per parameter each Update in documented order: base → Add → Max → Min → Multiply → Override(priority) → clamp → attack/release exponential smoothing. `GetValue(def)` reads smoothed value; `AddModifier(...)` creates temporary envelope influences.
- **SignalContribution** — scene component mapping one signal into one parameter (input range → AnimationCurve → output range, blend mode + weight). Registers OnEnable / unregisters OnDisable, so influence lifetime tracks the component.
- **ParameterModifier** — code-created temporary influence with attack/hold/release envelope (hold < 0 = until `Release()`); the instance is its own handle. `ParameterModifierTrigger` is the inspector/UnityEvent-wireable wrapper.
- **FloatRoute** — continuous parameter → destination mapping with curve, output range and epsilon. Writes a `ModulatedFloatTarget.Baseline` and/or raises `onValueChanged(float)`.
- **ModulatedFloatTarget** (abstract) — composited destination so systems never fight over one property: `final = override ?? (Baseline + Additive) * Multiplier`. Director routes write Baseline; Feel/feedback effects write Additive/Multiplier; scripted sequences may Override. Concrete: `Light2DFloatTarget`, `MaterialFloatTarget` (MaterialPropertyBlock), `UnityEventFloatTarget`. `FloatTargetPulser` generates sine/noise pulses into a channel.
- **DirectorRule** — threshold event with hysteresis (trigger vs reset threshold), sustain duration, cooldown, probability, one-shot mode. Fires MMF_Player feedbacks + `onTriggered` UnityEvent. `Fire()` is public for debug panels.

## Interactions

- `Core.Audio` (AudioDirector) consumes parameters through `AmbienceInfluenceTarget` (a ModulatedFloatTarget that pushes ambience-layer influence) and exposes `StingerTimerSignal` back into the signal layer for pacing rules.
- Game-specific atmosphere components live in `Assets/Submachina/Scripts/Atmosphere` (flickers, skitters, encounters) and are driven by rules/routes wired in scenes.
- First real usage: the HorrorScene descent sequence (Darkness/Dread/Intensity parameters driven by camera depth).

## Conventions

- Odin attributes are always wrapped in `#if ODIN_INSPECTOR`.
- No runtime state in ScriptableObjects; no per-frame allocations; contributions/routes cache their director.
