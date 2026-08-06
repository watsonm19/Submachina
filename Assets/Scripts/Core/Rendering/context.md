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

## Particles (marine snow + bubbles)

Fully procedural, in-shader (hash-grid cells, no textures, no GameObjects), world-anchored
through the same offset so they scroll with travel:

- **Marine snow** (`UD_Motes`, 3 layers) — soft twinkling motes at three parallax depths.
  The far layer barely scrolls (anchor `moteFarAnchor`, default 0.35) and is smaller/denser/
  dimmer; the near layer overshoots the world (`moteNearAnchor`, default 1.25 > 1) so it reads
  as **foreground passing the camera** — this near/far spread is what produces the 3D feel.
  Motes drift slowly through the water (`moteDrift`, default a gentle sink + slight current)
  and **brighten where they cross the god-ray shafts** (`moteGodRayBoost`) like dust in a
  sunbeam, tying the particulate to the lighting.
- **Bubbles** (`UD_Bubbles`, 2 layers) — sparse rim-lit circles with a specular glint
  (reads as a glassy sphere), rising (`bubbleRiseSpeed`) and wobbling side-to-side as they go.
  Keep `bubbleDensity` low so they read as individuals.

Both are additive light drawn over the scene (a fullscreen pass can't occlude behind sprites);
at the tuned default intensities this is imperceptible, and dim far layers read as distant.

## Scene-light gating (Self Light — true darkness)

Every added feature is additive, so by default it *creates* light and still glows in a
pitch-black scene. The **Scene Lighting** group + per-feature **Self Light** sliders fix that:

- Per pixel the shader estimates `sceneLight = saturate(globalLightLevel + analytic light
  pool + sceneLuma × Luma Light Gain)`.
- Each feature's contribution is scaled by `lerp(sceneLight, 1, selfLight)`:
  **1 = emissive** (old behavior — god rays default here, they ARE sunlight),
  **0 = only visible where the scene is actually lit**. Defaults: god rays 1, caustics 0.3,
  motes 0.2, bubbles 0.1.
- The **global level** auto-reads the scene's Global Light2D (intensity × color luminance,
  auto-found on enable; none found = treated as fully lit) or can be driven manually via
  `overrideGlobalLight` + `globalLightLevel` (e.g. from the environment director).
- **Spot/point lights**: add a `DistortionLightSource` component next to any Light2D and it
  self-registers (`DistortionLightRegistry`, same decoupled pattern as the ripple bus —
  prefab-spawned sub spotlights just work). The controller uploads up to **8** on-screen
  lights per frame (position, radius, cone half-angle cosines, intensity) and the shader
  computes an analytic quadratic-falloff cone — so gated features appear inside a headlight
  beam even over empty black water, where the luma term has nothing to catch.
- **God rays count as light for particles** (`godRayLightGain`): motes/bubbles sparkle inside
  a shaft even in otherwise dark water — and this uses the *gated* ray brightness, so if the
  rays themselves are darkened, so is their reveal.
- Debug readouts in Testing & Debug: `CurrentGlobalLightLevel`, `RegisteredLightCount`.

## Flow bias (experimental, default OFF)

`flowBiasEnabled` + `flowBiasStrength` on the controller: while the camera travels, extra
scroll is integrated from its smoothed velocity, so the water streams past **faster** than
1:1 anchoring — an exaggerated slipstream. Implemented purely C#-side as an addition to
`_UD_WorldOffset`, so every feature inherits it through its own World Anchor (anchor 0 =
immune). Play-mode only; eases back to zero when disabled, so it's safe to toggle live.
Debug: `CurrentFlowBias` readout + "Reset Flow Bias" button in Testing & Debug.

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

- **Depth-driven grading** — darken / shift the deep tint and fade god rays as the player
  descends (descent-depth progression, distinct from the parallax depth already built).
- **Sprite-layer parallax** — actual background sprite layers (rock walls, silhouettes of
  distant fauna) moving at fractional camera speed; pairs with the shader parallax.
- A **water surface line** near the top with brighter caustics + god-ray origin there.
- **Depth-driven grading**: darken + shift the deep tint as the player descends.
- **Bubble emitter** (sprites) for stronger interactivity on impacts/propulsion.
- Tie ambient amplitude / chromatic to player turbulence or nearby currents.

# Rendering — Spline Fill Terrain

Spline-outlined 2D terrain meshes with first-class seamless texturing, replacing the
SpriteShapeRenderer's second-class tiling fill. The SpriteShapeController is kept purely as
the **spline editor**; the visual is a generated mesh on a child object.

## Pieces

- **`SplineFillMeshBuilder.cs`** (`Core.Rendering`, `[ExecuteAlways]`, on a child with
  MeshFilter + MeshRenderer) — samples the closed spline, insets an **edge band** ring,
  ear-clips the interior, and bakes: planar tiling UVs (local or world space,
  `uvTilesPerUnit`), and per-vertex **edge data in TEXCOORD1** (xy = outward direction,
  z = 0 at the outline → 1 inside). Auto-rebuilds in the editor on spline/settings changes
  (editor-only polling; stripped from builds). `Bake To Asset` freezes the mesh into a
  `.asset` (re-bakes overwrite in place, prefab refs survive) — baked objects skip ALL
  generation and the builder/controller can be deleted. Live meshes are per-instance;
  baked meshes are shared — bake anything instantiated repeatedly.

- **`Assets/Submachina/Shaders/SplineFillLitSpecular.shader`** — MeshRenderer sibling of
  `SpriteLitSpecular` (both share `SpecularLitCore.hlsl`: the global specular lights,
  surface-normal modes, and the whole glint pipeline). Fill albedo/normal/specmask are
  material texture slots sampled with the mesh UVs (`_MainTex_ST` tiling/offset works).
  Edge-band effects driven by TEXCOORD1: `_EdgeDarken`/`_EdgeColor` (multiply inner-glow
  rim, applied before specular so bevel glints survive), `_EdgeBevel` (bends normals
  outward in BOTH the light-buffer pass and the specular — edges round under lights),
  `_EdgeAlphaFade` (rim melts to transparent), shaped by `_EdgeWidth`/`_EdgeFalloff`.
  The normal map and spec mask also have their own `_NormalMap_ST`/`_SpecMask_ST`
  transforms RELATIVE to the fill UV ((1,0) = follow the fill), plus per-map Stamp Once
  modes that window the map to a single placement — outside it the normal reads flat and
  the mask reads `_SpecMaskOnceBg` (match it to the stamp's border colour; the
  one-glowy-spot-at-a-position setup for reused decal-sized mask graphics).

- **`SplineFillOverride.cs`** (`Core.Rendering`) — per-object overrides over ONE shared
  material via MaterialPropertyBlock: texture set, fill tiling/offset, the edge-band look,
  and a whole-object `tint` (the SpriteRenderer.color equivalent; the specular glint colour
  is separate — dim it in step via SpecularController.ApplyTint). Free in this project:
  every specular renderer already carries a property block. Compose-safe with
  SpecularController (both read-modify-write the block).

## Gotchas

- Keep the edge band width below the shape's tightest feature radius or the inset ring can
  fold over itself (miter limit softens, doesn't eliminate).
- Property blocks live on the NATIVE renderer: they survive domain reloads and can't remove
  single properties. To clear stale overrides: `renderer.SetPropertyBlock(null)` then
  re-`Apply()` every block-writing component on that renderer.
- MeshRenderer sorting layer/order is script-only — the builder exposes it.

# Rendering — 2D Shadow Caster Self-Healing

URP's `ShadowCaster2D` never serializes its shadow mesh (`m_ShadowMesh.m_Mesh` is always
`{fileID: 0}`); it is regenerated only when the component detects a *change* against its
serialized "previous" values or its internal `m_ForceShadowMeshRebuild` flag is set. A
cleanly-serialized caster therefore detects no change after scene load, domain reload, or
`Instantiate()`, keeps an empty mesh, and silently casts no shadow. Sprite reimports (e.g.
editing a custom outline) can also strand a caster with an empty mesh — and the first
rebuild after a provider re-initializes can itself come up empty, needing a second pass.

## Pieces

- **`ShadowCaster2DRefresher.cs`** (`Core.Rendering`, runtime) — sets the internal rebuild
  flag via reflection. Drop ONE on any scene object (a manager is fine): with
  `refreshEntireScene` (default on) it heals every caster in the loaded scenes at Start.
  Spawners can call `RefreshHierarchy(root)` right after `Instantiate` for a targeted fix.
  Also exposes `ForceRebuild(caster)` and `HasShadowGeometry(caster)` for the editor side.

- **`Core/Editor/ShadowCaster2DEditorRefresher.cs`** (`Core.Editor`, `[InitializeOnLoad]`)
  — edit-mode auto-heal: refreshes all casters on scene open, after any texture reimport
  (via `AssetPostprocessor`), and on demand via **Tools/Custom/Refresh 2D Shadows**.
  Drives each caster's public `Update()` directly and synchronously — deliberately NOT
  `EditorApplication.QueuePlayerLoopUpdate()`, which would tick every other ExecuteAlways
  script in the scene (chunk spawners etc.). Verifies geometry after each pass and retries
  stragglers up to 3 passes (some empties are legitimate, e.g. no sprite assigned).

## Gotchas

- The reflection targets `m_ForceShadowMeshRebuild` (URP 17.x). If a URP upgrade renames it,
  a one-time warning fires and the code falls back to nudging `trimEdge`.
- Domain reload (script recompile) wipes rebuilt meshes for casters whose serialized state
  is self-consistent — use the menu item if shadows vanish after a compile.
