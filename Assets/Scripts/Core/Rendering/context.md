# Rendering — Underwater Distortion

A fullscreen post-processing effect that warps the rendered 2D scene to look like it's
viewed through water: a gentle whole-screen undulation plus localized, interactive ripples
triggered at world points by gameplay events.

## Pieces

- **`Assets/Shaders/UnderwaterDistortion.shader`** (`Submachina/UnderwaterDistortion`)
  Fullscreen Blit shader. Samples the rendered scene (`_BlitTexture`) at a UV offset built
  from (1) *ambient flow* — two scrolling sine layers blended with an optional tiling noise
  texture (`_UD_NoiseTex`) — and (2) *ripple flow* — expanding radial waves read from two
  global Vector4 arrays. Edge-fades the displacement so warped UVs never sample off-screen.
  All inputs are **global uniforms** (no surfaced material properties).

- **`UnderwaterDistortionController.cs`** (`Core.Rendering`, `[ExecuteAlways]` singleton)
  The only writer of the shader globals. Owns the ambient settings and a fixed pool of 16
  ripples. Each frame it converts ripple world positions to viewport UV, computes their
  expand/fade envelope, and uploads everything via `Shader.SetGlobal*`. Has Odin test buttons
  and an edit-mode **manual time override** for deterministic `EditorCapture` stills.

- **`DistortionRippleBus.cs`** (`Core.Rendering`, static)
  Decoupled pub/sub (mirrors `Core.Input.PointerWorldBus`). Gameplay calls
  `DistortionRippleBus.Emit(worldPos, strength, frequency, speed, lifetime)`; the controller
  is the sole subscriber. This is the integration point for any event that should ripple the
  water (dashes, impacts, explosions, fast movement).

- **`Gameplay/SpeedRippleEmitter.cs`** (`Gameplay`)
  Example emitter: watches an object's speed (Rigidbody2D velocity or transform delta) and
  emits a strength-scaled ripple through the bus when it exceeds a threshold (with a cooldown).

## Render wiring (already set up in the project)

The effect is injected by URP's built-in **Full Screen Pass Renderer Feature**, added to
`Assets/Settings/Renderer2D.asset`, pointed at `Assets/Shaders/UnderwaterDistortion.mat`,
at injection point **After Rendering Post Processing**, with **Fetch Color Buffer = ON**
(required — the shader reads `_BlitTexture`). No custom render-pipeline C# is involved; the
built-in feature handles both compatibility-mode and Render Graph paths.

`JDTestScene` contains an `UnderwaterDistortion` GameObject holding the controller, and the
`Player_Torch` object carries an example `SpeedRippleEmitter`.

## How to use

- **Ambient look:** tune Ambient Flow (amplitude/scale/speed) on the controller. To use the
  textured mode, assign a tiling noise to `_UD_NoiseTex` on the material and raise Noise Blend.
- **Trigger a ripple from code:** `DistortionRippleBus.Emit(pos, strength, frequency, speed, lifetime)`.
- **Verify visually in-editor:** enable Manual Time Override, raise Ambient Amplitude, emit a
  test ripple at time 0, then scrub Manual Time upward and call
  `Core.Editor.EditorCapture.Capture(...)` to see the ring expand frame-by-frame.

## Gotchas

- The GPU arrays are always uploaded at full length (16) so Unity doesn't cache a shorter
  array length — a known `SetGlobalVectorArray` pitfall.
- If the warp ever appears on only half the screen, check the feature's Pass Index (URP 2D quirk).
