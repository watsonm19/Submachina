# Parallax System

Multi-layer parallax backgrounds/foregrounds for the 2D camera, plus the level-bounds
integration that guarantees the player can never see past the furthest backdrop.

## Components

- **ParallaxController** (`[ExecuteAlways]`, `[DefaultExecutionOrder(100)]`) — lives on a root
  `Parallax` object; drives every `ParallaxLayer` child from the Main Camera's world position in
  LateUpdate, AFTER the camera scripts (order 0) settle. Reading the camera's world position means
  MMF shake propagates into layers, attenuated naturally by each layer's factor.
  `ForceUpdate()` is called by `MultiTargetCamera2D.SnapToTargets` so snaps show correct parallax
  the same frame. Edit-mode preview toggle moves layers while you drag the camera in the scene view;
  Capture/Restore rest-position buttons make that lossless.
- **ParallaxLayer** — one per layer. Holds the movement factor, anchor, rest position, and the fit
  tooling (Odin buttons). Runs `IParallaxLayerExtension` components (tiling, spawner) in component
  order after positioning — the composability seam.
- **ParallaxTiledLayer** — extension for infinitely repeating layers: Tiled-draw-mode sprite,
  wrapped by whole tile periods toward the camera (invisible jumps). Needs texture wrap = Repeat,
  sprite Mesh Type = Full Rect.
- **ParallaxLayerSpawner** + **ParallaxDecorProfile** (SO) — deterministic decor spawning in
  layer-local space (see below). Despawn+recycle by default; can persist like world chunks.
- **ParallaxMath** — the only home of the positioning/fit formulas.
- **LevelBounds** (`Scripts/Core/`) — authoritative level rect with per-side unbounded flags.
  `MultiTargetCamera2D`/`CameraFollow` clamp the full view rect (and zoom) inside it.
- **CameraViewUtil** (`Scripts/Core/`) — projection-agnostic "view half-extents at z=0" helper.
  All view-size math routes through it so a future ortho→perspective switch is localized.

## Factor convention

`layerPos = rest + (camPos - anchor) * (1 - w)` per axis. **w = 0** camera-locked (infinitely
far, never scrolls — required for unbounded levels), **w = 1** world-locked, **w > 1** foreground
whizzing past. Apparent scroll speed = w × camera speed. Position is a pure function of camera
position — no accumulation — so teleports/snaps need no special handling.

## Backdrop fit (bounded levels)

For level extent `L`, worst-case view extent `V` (max ortho size × reference aspect — worst case
is max zoom-out), art extent `S`:
`S = w·L + (1-w)·V` (required size) ⇔ `w = (S-V)/(L-V)` (fit factor; 0 when `L ≤ V`).
Buttons on ParallaxLayer compute either direction per axis. Unbounded levels: `FitMode.CameraLocked`
forces w = 0 and scales art to cover V + margin.

## Layer-space decor spawning

The visible layer-local window maps linearly to camera position:
`localCentre = camPos - layerPos = camPos·w + anchor·(1-w) - rest`.
Each layer-local grid cell therefore has a stable seed (project hash pattern ^ worldSeed ^
layerSalt) and an exact depth via the inverse mapping — revisited areas regenerate identically
even after despawn. Fresh `System.Random` per cell (WorldChunk-style isolation). Camera-locked
layers can't spawn (static window) — the spawner warns and disables itself.

## Gotchas

- Layer roots must stay unscaled (layer-space math assumes translation only).
- Sorting layers back→front: `BG-Far, BG-Mid, BG-Near, Back, Default, FG-Near, Front`. All parallax
  content sorts via sorting layers, not Z — everything stays at z = 0.
- Far layers should use Sprite-Unlit materials so 2D lights/headlamp don't illuminate "distant"
  content (instant depth cue); foreground silhouettes also read best unlit + semi-transparent.
- Backdrop-style textures need mipmaps (zoom animates 6–16); tiled textures need Repeat wrap.
- `MultiTargetCamera2D` applies a post-lerp hard clamp with the actual current ortho size — do not
  remove it; the lerped position and size are momentarily inconsistent and can otherwise reveal
  past a bounded edge for a frame.
