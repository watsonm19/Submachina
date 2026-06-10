# Rendering — Underwater Distortion

A fullscreen post-processing effect that makes the 2D scene read as a **side-view, submerged
("looking through the water")** environment: gentle distortion, light-bending refraction,
artificial underwater light (god rays + caustics), and two kinds of interactive disturbance —
concentric ripples and propulsion turbulence wakes.

## Pieces

- **`Assets/Shaders/UnderwaterDistortion.shader`** (`Submachina/UnderwaterDistortion`)
  Fullscreen Blit shader. Does three things per pixel:
  1. **Distort** — sums UV displacement from ambient flow + ripples + wakes, then re-samples
     the scene (`_BlitTexture`) with a **chromatic split** (R/B offset along the displacement)
     for the light-bending look.
  2. **Light** — adds **god-ray shafts** (fully procedural 1-D-noise beams from the surface,
     with Sway / Shimmer / Distort controls so they wave, twinkle and bend with the water;
     procedural on purpose so they can't inherit a texture's directional streaks and band) and **caustics**
     (dual-layer `min()` of a cell/voronoi texture, luminance-masked so the sparkle lands on
     lit objects rather than open water). The caustics are pushed through an animated domain
     **warp** (Caustic Warp) so the web morphs/pulses in place, and bent by the scene
     displacement (Caustic Distort) so they ride the same wobble as the water — without these
     they read as a flat decal sliding across the screen.
  3. **Tint** — optional subtle deep-water color grade.
  Material properties: `_UD_NoiseTex` (tiling noise for ambient/wake/god-rays) and
  `_UD_CausticTex` (cell/voronoi for caustics). Everything else arrives as global uniforms.

- **`UnderwaterDistortionController.cs`** (`Core.Rendering`, `[ExecuteAlways]` singleton)
  The sole writer of the shader globals. Owns ambient/refraction/god-ray/caustic/tint settings
  plus two pools — **ripples (16)** and **wakes (8)**. Each frame it projects each disturbance's
  world position to viewport UV, computes its envelope, and uploads everything via
  `Shader.SetGlobal*`. Odin test buttons + an edit-mode **manual time override** for
  deterministic `EditorCapture` stills.

- **`DistortionRippleBus.cs`** (`Core.Rendering`, static) — `Emit(pos, strength, frequency,
  speed, lifetime)`. Concentric expanding ripple (surface-style splash / impact).

- **`DistortionWakeBus.cs`** (`Core.Rendering`, static) — `Emit(pos, dir, strength, length,
  frequency, lifetime)`. Elongated turbulence plume trailing a travel direction (propulsion).

- **`Gameplay/SpeedRippleEmitter.cs`** — emits a ripple when an object exceeds a speed threshold.
- **`Gameplay/PropulsionWakeEmitter.cs`** — streams wakes along an object's velocity while it
  moves fast (the propulsion-trail counterpart).

## Render wiring (already set up)

Injected by URP's built-in **Full Screen Pass Renderer Feature** on
`Assets/Settings/Renderer2D.asset` → material `Assets/Shaders/UnderwaterDistortion.mat`,
injection **After Rendering Post Processing**, **Fetch Color Buffer = ON**. No custom
render-pipeline C#. `JDTestScene` has an `UnderwaterDistortion` GameObject (controller);
`Player_Torch` carries both example emitters.

Default source textures: `_UD_NoiseTex` = Feel `MMFlowNoise`, `_UD_CausticTex` = Feel
`MMCellNoise` (swap for `MMVoronoiNoise`, `MMCloudsNoise`, etc. in the material inspector).

## World anchoring (sense of travel)

All the ambient patterns originally sampled in pure screen-space UV, so they rode along with
the camera and the player never felt like they were moving. The **World Anchoring** group on the
controller fixes that: the camera's world position is converted to viewport-height UV units
(`_UD_WorldOffset`) and added to each pattern's sample coordinates, scaled by a per-feature
anchor strength (`_UD_WorldAnchor`):

- **0** = screen-locked (the original in-place behavior — still available per feature).
- **1** = pinned to the world: the pattern scrolls past at exactly travel speed, as if painted
  on the water. Tiling textures make the scroll seamless/infinite.
- **between** = parallax — the pattern reads as a more distant water layer; **>1** = foreground.

Per feature: `ambientWorldAnchor` (default 0.35, partial parallax keeps some in-place wobble),
`causticWorldAnchor` (default 1 — swimming through a stationary light field is the strongest
motion cue), `godRayWorldAnchor` (default 0.6 — anchors beam placement and shimmer phase, but
the surface-entry brightness gradient deliberately stays screen-space so light always enters
from the top of the view). Ripples and wakes were already world-anchored (projected from world
positions every frame) and are unaffected.

Note: anchoring follows the **camera**, not the player object — correct as long as the camera
follows the Submarine. Float precision in the shader stays clean to roughly tens of thousands
of world units of descent; revisit (e.g. wrap the offset) if runs ever go deeper.

## How to use

- **Tune the look** on the controller: Ambient Flow, Refraction (chromatic), God Rays, Caustics,
  Deep Tint. Each light feature has an intensity that goes to 0 = off. Master `globalEnable`
  gates the whole effect.
- **Trigger from code:**
  - ripple: `DistortionRippleBus.Emit(pos, strength, frequency, speed, lifetime)`
  - wake:   `DistortionWakeBus.Emit(pos, travelDir, strength, length, frequency, lifetime)`
- **Verify in-editor:** enable Manual Time Override, hit a test button (Emit Test Ripple / Emit
  Test Wake) at a low time, then scrub Manual Time up and `EditorCapture.Capture(...)`.

## Gotchas

- GPU arrays always upload at full length (16 ripples / 8 wakes) so Unity doesn't cache a shorter
  length — a known `SetGlobalVectorArray` pitfall.
- Caustics show faintly in open water via the Open Water floor; set it to 0 to keep them only on
  lit surfaces.
- Effects apply to everything rendered before post-processing. A screen-space/overlay UI canvas
  won't be distorted (usually desired).

## Ideas / backlog (not yet built)

- Drifting **particulate/motes** ("marine snow") — world-anchored particles at 2-3 parallax
  depths; the single strongest motion cue and a perfect complement to world anchoring.
- **Velocity-driven flow bias** — push the ambient noise scroll opposite the player's velocity
  (smoothed) so the water visibly streams past when under propulsion.
- A **water surface line** near the top with brighter caustics + god-ray origin there.
- **Depth-driven grading**: darken + shift the deep tint as the player descends.
- **Bubble emitter** (sprites) for stronger interactivity on impacts/propulsion.
- Tie ambient amplitude / chromatic to player turbulence or nearby currents.
