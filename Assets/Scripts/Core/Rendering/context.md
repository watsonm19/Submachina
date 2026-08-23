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
  speed, lifetime)`. Concentric expanding ripple (surface-style splash / impact). Crest
  pattern is anchored to the moving ring (anti-strobing for fast rings), so `speed` is the
  crests' drift RELATIVE to the ring — 0 rides the ring, negative trails behind. The 7-arg
  `RippleRequest` overload adds per-ripple `expansionSpeed` / `ringWidth` (viewport units)
  that override the controller's global `ringExpansionSpeed` / `ringFalloff` when > 0 —
  CPU-side only, no shader change (used by `SonarReturnRipples` to speed-match echo waves).
  The 9-arg overload adds identity extras carried by a third GPU array (`_UD_RippleC`):
  `tint` (rgb × a = additive glow riding the ring band with its own lifetime envelope —
  `rippleTintFadePower` on the controller tunes how long colour survives vs. the wave's
  displacement fade — added AFTER the deep grade so it stays visible in darkness; master
  `rippleTintGain` on the controller)
  and `chromaticBoost` (extra R/B split on that ring only — metallic glint; 1 = neutral).

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

- **Per-object overrides** — the texture set / fill tiling / per-map STs / stamp-once
  windows and the whole-object tint live in `SpecularController`'s "Mesh Textures"
  foldout (Submachina.Core — the universal surface driver); the edge-band look lives in
  **`EdgeBandOverride.cs`** (`Core.Rendering`, this folder) whose presence on the
  renderer is the opt-in. Both compose-safe via read-modify-write property blocks.
  (The old `SplineFillOverride` component was removed after all scenes were ported.)

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

## Specular light/controller family (moved from Assets/Submachina/Scripts/Core)

Namespace `Core.Rendering`. Generic pieces of the 2D specular pipeline: emitter side
(`SpecularLight2D` + `SpecularLight2DManager`), receiver side (`SpecularController`,
`SpriteShapeSpecularController`), the `ITintReceiver` tint contract (implemented by
`SpecularController`), and `IChildRenderersChangedListener` — the contract renderer
DRIVERS listen on: procedural spawners that create/destroy/re-material child renderers
(e.g. ChainSpriteRenderer segments) broadcast it upward so cached renderer lists and
property-block baselines re-sync automatically. Gameplay subclasses (OreSpecularController)
live in Submachina.

- **SpecularLight2D / SpecularLight2DManager** — generic GPU specular-glint lighting for sprites using the `Submachina/2D/SpriteLitSpecular` shader. Drop `SpecularLight2D` on a `Light2D` (e.g. the sub's torch) to mark it as a glint driver; the auto-created `SpecularLight2DManager` singleton packs all active drivers into global shader uniforms (`_SpecLightCount`, `_SpecLightA/B`, cap `MAX_SPEC_LIGHTS`=4) once per frame in `LateUpdate`. The shader then computes distance-falloff × cone-gate × Blinn-Phong specular per-pixel against the **real** lights — no per-sprite CPU, zero lag, local-multiplayer ready (contributions summed), cone taken from the `Light2D`'s own inner/outer angles. Replaced the old per-tick `OverlapCircle` illuminator.
  - **Per-light falloff curve** — each `SpecularLight2D` can shape its glint's distance falloff with a manual `AnimationCurve` (`useFalloffCurve` + `falloffCurve`, X = normalized distance 0→reach, Y = strength), defaulting to the old linear ramp. The manager bakes each active light into one **row** of a shared `128×MAX` `RHalf` LUT (`_SpecFalloffLUT`) and the shader samples it branchlessly (linear lights just hold a linear ramp). Rows are rebaked (and the texture `Apply()`-ed) **only when a slot's light or its curve version changes** — steady state adds no CPU beyond the existing packing.
- **SpecularController** — the receiver side of the above: a per-instance driver you drop on any shiny sprite that should glint. Writes the per-instance look ONCE via a `MaterialPropertyBlock` (colour/tightness/resting intensity, `illuminationResponse`→`_LightResponse`, idle-shimmer params) then **disables its own `Update`** — idle sprites cost zero CPU; the GPU adds the light-driven glint. The only per-frame work is transient flares: the generic `Pulse(amount)` one-shot (decays via `pulseDecay`), which wakes the component and drives the additive `_SpecBoost`, then sleeps. Subclasses add sustained/extra contributions via two hooks — `ComposeBoost()` (extra additive boost) and `IsIdle()` (stay awake while active) — plus `Wake()`; `World/OreSpecularController` is the ore subclass that layers a mining glow on top.
  - **Animation (in-shader, zero CPU)** — the "Animation" foldout drives continuous modulation computed entirely in the shader from `_Time` (the controller just writes the params once). Two independent channels, each with a selectable waveform (`ModWaveform`: `Sine` smooth pulse / `PingPong` triangle / `Noise` smooth random flicker):
    - **Intensity modulation** (`animate`) — `modTarget` (→ `_ShimmerMode`) picks what it drives: `ScaleBase` (legacy ±fraction of resting intensity, needs base > 0), `Additive` (absolute units on top of base — flickers even at base 0 / fully unlit), `ScaleLight` (dark stays dark; once a real light hits, the modulation scales the lit glint — "shimmer only when illuminated"), `ScaleBaseAndLight` (both).
    - **Direction wobble** (`animateDirection`) — rotates the glint direction ± `dirWobbleDegrees` over time (→ `_DirWobble`), applied to BOTH the baseline glint dir and each real light's specular dir (cone gate/falloff stay physical), so highlights slide across the surface like light through water — works unlit and lit.
  - **Surface normal / procedural glints** — the glint's *shape* comes from a surface normal, chosen by `normalSource`:
    - `SpriteNormalMap` — the sprite's own `_NormalMap` secondary texture (bespoke relief, e.g. the baked ore nuggets).
    - `NormalTexture` — an **explicit normal map dropped on the component** (`normalTexture` field), bound per-instance via the MPB to the shader's `_NormalTex` and sampled in the sprite's local 0..1 UV. Lets you give any sprite a bespoke normal **without wiring up secondary-texture import settings**, and it stays correct on atlased sprites. `normalTextureTiling`/`normalTextureOffset` (→ `_NormalTexST`) scale and position the map to fit the sprite (tiling <1 enlarges the stamp; set the texture's Wrap to Clamp for a single stamp). Import the texture as **Default, sRGB off** (straight RGB), same as the baked normals — *not* Unity's "Normal map" type.
    - `Dome / Bevel / Ripples / Radial / Facets` — **procedural patterns generated in-shader from the sprite UV, no authored texture**, so any generic sprite can glint (round bulge / rim only / wavy bands / concentric rings / sparkly cells), tuned by `normalStrength` + `normalFrequency`.
    - `AlbedoHeight` — **derives the normal from the albedo treated as a height map**, the trick Laigter / Material Maker use: central-difference the texture's luminance over a texel neighbourhood and turn the height gradient into surface slope (`n = normalize(-dh/du, -dh/dv, 1)`). Real relief that follows the actual art, with no authored map and no bake step. `heightTapRadius` is the tap distance in **texels** (1 = finest detail, larger reads broader forms and suppresses noise); `heightStrength` is the gradient→slope gain, and going **negative inverts** the relief (which way round looks right depends purely on how the texture was painted).
      - **Three levers that decide whether this looks good or looks like noise.** The raw 1-texel difference is maximally high-frequency, which reads as gritty and picks up albedo detail that isn't shape:
        - `heightBlur` (→ `_HeightBlur`) — the mip level the *broad* gradient is read from, and the main cure for grittiness. Each step doubles both the mip and the tap radius so the kernel keeps covering the same texture area, meaning it **removes** fine structure rather than just softening it. Uses the hardware mip chain as a free box blur (no extra taps) — which **requires mipmaps**; sprites often ship with "Generate Mip Maps" off, and the controller shows a warning InfoBox when that's the case.
        - `heightDetail` (→ `_HeightDetail`) — how much of the crisp LOD-0 gradient is mixed back over that broad form. 0 = pure shape, 1 = all the texture's detail. Skipped (four taps saved) when blur is 0.
        - `heightCompress` (→ `_HeightCompress`) — soft knee on the slope, `g /= 1 + |g|k`. Hard albedo edges (outlines, paint boundaries, speckles) produce extreme gradients that read as cliffs while genuine shading produces small ones, so compressing the top end suppresses *specifically* the detail that shouldn't be there instead of flattening everything.
      - **Cost** is four extra taps of a texture the shader already samples, one or two texels away, so they hit the same cache lines — cheap (eight when detail-mixing). The thing to watch is overdraw, since this is a transparent-queue shader, not the taps.
      - **Inherent limitation**: albedo is not height, so the technique cannot separate *pigment* from *shape* — dark paint on a flat surface becomes a dent. Great on rock/bark/rubble/corrosion where value variation *is* the relief; bad on flat-lit graphic art. Same failure mode as the desktop tools.
      - `_HeightTexel` (written by the controller from the sprite's texture) carries `1/width, 1/height`. It is deliberately **not** named `_MainTex_TexelSize`: the 2D SRP Batcher rejects any material carrying a `_TexelSize`/`_ST` property, so the magic name silently unbatches every sprite using the shader. The shader floors the tap offset at one screen pixel's worth of UV, which both covers an unset value (MeshRenderers, spline fills) and stops heavy magnification from collapsing all four taps into one texel.
    - `normalStrength` also acts as a **depth multiplier for the two texture modes** (`SpriteNormalMap`/`NormalTexture`): it scales the sampled normal's XY vs Z (1 = as authored, >1 = deeper relief, <1 = flatter) — affects the specular normal only, not URP's diffuse lighting.
    - `diffuseUsesNormalMode` (→ `_DiffFromMode`) — **the switch that makes the non-texture modes read as real depth.** By default the `NormalsRendering` pass (which fills URP's 2D light buffer) samples only `_NormalMap`, so every procedural/AlbedoHeight mode drives the **specular alone** and the surface still looks flat under an ordinary `Light2D` — the single biggest reason those modes don't match an authored normal texture. Turned on, that pass calls the same `ComputeSurfaceNormal` the lit pass does, so the derived relief is lit by every Light2D. It needs `worldPos` in the normals pass's varyings (added, for the world-space modes) and `SpecularLitCore.hlsl` included there. Off by default because enabling it changes the look of anything already relying on specular-only procedural relief; no cost when off.
    The controller writes a per-sprite `_NormalUVRect` (from `sprite.textureRect`) so both the override texture and the procedural patterns stay centered on atlased sprites. A ready-made generic material `Materials/SpriteSpecular.mat` (white, no normal texture) is the drop-in for non-ore sprites; needs the scene's Bloom + a `SpecularLight2D` to show.
  - **Per-pixel specular mask** — the shader also samples an optional `_SpecMask` **Secondary Texture** (RGB, linear/sRGB-off import) that **tints AND scales** ALL specular (baseline + boost + light-driven) per pixel, so one sprite can mix shiny areas with dull rock (~0.2 grey) **and crystals can glint their own baked colour** (e.g. purple amethyst flashes purple under a white torch). Composes with `_SpecColor` (the mask multiplies it). Defaults to white → sprites without a mask are unchanged; grayscale masks behave exactly as before. Baked automatically by the `TerrainObjectGenerator` (see `World/context.md`) — rock/specks use `rockSpecColor`, crystals their own per-crystal colour (or a flat override), decals a per-layer `specColor`.
  - **Light-following relief (in the "Surface Normal" foldout)** — makes the `SpecularLight2D`s *sculpt* the surface rather than only glint off it. Both terms are gated per light by the same `falloff * cone` as the glint, so they vanish where no beam reaches, and both are normalised by the summed gate (floored at 1) so overlapping beams **average instead of stacking** — a single light is bit-for-bit unchanged, two used to double the relief and blow out.
    - `reliefEmboss` (→ `_NormalEmboss`) — signed lambert of the authored normal against each light: facets facing a beam brighten *additively in albedo colour* (the one thing a multiply-only `Light2D` can never do), facets turned away shade multiplicatively, flat pixels are untouched. This is the light-following twin of `ambientFill` below — same math, real light direction instead of a fixed one.
    - `embossElevation` (→ `_EmbossElevation`) — how high each light sits above the surface plane for that lambert. **Low = grazing = the shading reads as shadow; high = overhead = it reads as tinting.** Was hardcoded at 0.5 before being exposed.
    - `directionalGrooves` / `directionalGrooveGain` (→ `_DirCavity`/`_DirCavityScale`) — the **light-following cavity**: the height field's second derivative projected onto the light direction (the Hessian's L-L component), so grooves running *across* a beam darken and grooves running *along* it don't, and the shading sweeps as the beam moves. This is not a duplicate of the emboss — the emboss is **antisymmetric**, so a fine groove's bright wall exactly cancels its dark wall and nets *zero* darkening; this term is the net energy loss raking light actually suffers crossing a groove. Shares `HeightCurvature` with the isotropic cavity below — see the **curvature basis** note after this list. Remaining approximation, shared with the emboss: a world-space light direction is dotted against a *tangent-space* normal, so heavily rotated sprites are slightly off.
  - **Ambient relief ("Ambient Relief" foldout)** — relief that survives with **no light on the surface**. Necessary because URP 2D shades normal maps *only* from **positional** lights (`LightingUtility.hlsl` does `lightColor *= saturate(dot(dirToLight, normal))`, which needs a light position — so a **Global `Light2D` cannot light a normal map at all**, it's a flat multiply), and `reliefEmboss` above is gated by each `SpecularLight2D`'s falloff/cone. Without these terms an unlit surface reads as completely flat. Three independent, light-INDEPENDENT terms behind one `ambientRelief` A/B toggle:
    - `ambientFill` + `ambientFillDir`/`ambientFillElevation` (→ `_AmbientFill`/`_AmbientDir`) — a **virtual directional "sun"**, the 2D stand-in for Unity's 3D directional light: signed lambert against a fixed direction, so slopes facing it brighten (additively, in albedo colour) and slopes turned away shade. Anchored so a flat normal contributes *exactly* zero — untextured sprites never wash out. **Keep the direction identical across materials** or each object reads as having its own sun. Lower elevation = more grazing = more contrast.
    - `slopeShading` (→ `_SlopeAO`) — steeper pixels darker (`pow(n.z, k)`). One instruction, direction-independent depth floor; not true occlusion (it also dims the rim of a big smooth bulge).
    - `cavityOcclusion` / `cavityRidge` / `cavityGain` (→ `_CavityAmount`/`_CavityRidge`/`_CavityScale`) — a **live cavity map with no baked texture**: the height field's Laplacian (`HeightCurvature` summed over two orthogonal world axes) measures local curvature, positive in pits and negative on ridge crests, split so the dark and bright halves weight independently. This is the same quantity a baker would write to an AO/cavity map, computed per-frame instead. Negative gain flips pit/ridge for normal maps with an inverted X channel. Caveat: still per-2×2-pixel-quad, so slightly blocky under heavy magnification.
    - `cavityOccludesSpecular` (→ `_CavitySpec`) — gates the **glint** by the same occlusion, so grit packed into a crevice doesn't sparkle. Sells depth about as hard as darkening the albedo does.
    - `cavityFadeUnderLight` (→ `_CavityLitFade`) — the bridge between the two halves of the model. Physically AO belongs to **ambient** light while direct light should cast real shadows instead (that's the light-following pair above), so at 0 a spotlit crevice is darkened *twice* by two different models. Raise toward 1 to hand crevices over to the directional terms wherever a beam actually lands. 0 = the terms simply stack.
  - **Curvature basis (shared by the cavity and groove terms)** — both read `HeightCurvature(basis, worldDir)`, the height field's second derivative along a world direction, built once per fragment by `BuildCurvatureBasis`. Three things it gets right that a naive `ddx(n.x) + ddy(n.y)` does not:
    - **Tiling.** The normal's xy is a height gradient in **UV** space (`dh/du = -n.x`) while lights are in world space, so the two must be reconciled through the UV↔world Jacobian. Moving one world unit along `L` is a screen step `Ls = J_world⁻¹ L` and a UV step `q = J_uv Ls`; writing the bilinear form in screen gradients cancels one `J_uv` and collapses to `-(q · (A_screen Ls))`. Because `q` carries the UV-per-world ratio, the result scales as tiling² exactly as real curvature does — so the gains survive a material or per-object (Mesh Textures) **tiling** override untouched. (**Offset**, being a translation, cancels in every derivative and never mattered.) `ComputeSurfaceNormal` reports the coordinate the normal varies over via an `out float2 uvEff` so this works for every normal mode, including the `_NormalTex` override's own tiling and the world modes (where `uvEff` *is* world position).
    - **Zoom / scale / platform.** Deriving `J_world` from `ddx/ddy(wpos)` rather than assuming screen axes align with world axes makes the gains invariant to camera zoom and non-uniform sprite scale, and immune to the D3D-vs-GL disagreement on `ddy`'s sign.
    - **What must NOT be differentiated.** The basis takes the normal *before* any caller-side bend — specifically the spline fill's **edge bevel**, a wide smooth ramp across the whole band that would otherwise register as genuine curvature and paint a false cavity ring around every rim, on top of the `_EdgeDarken` already treating that area. Also auto-disabled for the `Facets`/`WorldFacets` modes, whose piecewise-constant normals would yield cell outlines instead of relief. Known minor artifact: the `_NormalMapOnce` stamp window's hard cut to flat is a discontinuity, so a thin curvature line can appear exactly at its border.
    - All derivatives are taken **outside** the light loop — gradient instructions inside its divergent control flow (the `continue`s) are undefined behaviour — and the loop then does arithmetic only.
