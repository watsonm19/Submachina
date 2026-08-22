# 2D Specular Shader Family

One fragment brain, two thin shaders, one component driving them all.

## Files

- **`SpecularLitCore.hlsl`** — the shared fragment pipeline: global specular lights
  (SpecularLight2DManager), surface-normal modes (sprite map / procedural / albedo-height),
  Form Shape (RNM-composited dome/bevel/pillow/cylinder/slope/silhouette), ambient relief
  (fill sun / slope / cavity), the full Blinn-Phong glint compose, and the merged
  ProcCreature block (outline / emission+rim / flash).
- **`SpecularLitProperties.hlsl`** — the shared `UnityPerMaterial` declarations,
  `#include`d INSIDE every pass's `CBUFFER_START/END` (after shader-specific entries).
  This is what keeps all six passes across both shaders layout-identical for the SRP
  Batcher; add a property here once and both shaders get it. No include guard on purpose.
- **`SpriteLitSpecular.shader`** — SpriteRenderers / SpriteShapeRenderers. Sprite-specific
  vertex path (instancing, flip, skinning, atlas UV rect). No edge data, so outline/rim
  are inert here (properties hidden); flat emission + flash work.
- **`Mesh2DLitSpecular.shader`** (formerly SplineFillLitSpecular) — every GENERATED mesh:
  spline fills (SplineFillMeshBuilder), creature bodies (RadialMeshRenderer /
  ChainStripRenderer). Fill textures are first-class material slots with per-map STs and
  stamp-once windows, plus the edge band, outline/emission/flash, and everything shared.
- **`ProcCreature2D.shader`** — DEPRECATED. Its features live in the core now and all
  creature materials were flipped to Mesh2DLitSpecular; kept only as a lightweight
  fallback (note: it predates the TEXCOORD1 w channel — see its header).

## The TEXCOORD1 edge contract (all generated-mesh builders bake this)

- `xy` = outward direction at the nearest silhouette point (local space, unit)
- `z`  = NORMALIZED edge distance: 0 at the silhouette → 1 at the band inner edge / core.
         Drives the edge band (darken/fade/bevel) and the edge-band Form Shape.
- `w`  = WORLD-UNIT edge distance: 0 at the silhouette. Drives the constant-width
         outline and rim emission, taper-independent.

Spline meshes BAKED to assets before `w` existed read `w = 0`; the outline defaults off
so they render unchanged — re-bake before enabling outline on them. ChainStripRenderer
refreshes `xy` per frame (the body bends); the other builders bake it statically.

## Component layer

- **`SpecularController`** (Submachina.Core) — THE per-instance surface driver for every
  renderer kind (auto-detects Sprite/SpriteShape/Mesh children). Owns all shared shader
  params + the mesh-only fill texture set ("Mesh Textures") + "Outline & Emission".
  Subclasses: `SpriteShapeSpecularController` (fill/edge submesh routing),
  `OreSpecularController` (mining glow).
- **`EdgeBandOverride`** (Core.Rendering) — small dedicated component for the edge-band
  look on generated meshes. Component presence = opt-in.
- **`SplineFillOverride`** (Core.Rendering) — LEGACY, still functional; superseded by
  SpecularController's Mesh Textures + EdgeBandOverride for new objects.
- **`ProcCreatureColorOverride`** (Core.ProceduralAnimation) — creature recolor helper;
  still works (property names unchanged on the unified shader).

## Rules that bite

- All passes of a shader must share an identical `UnityPerMaterial` layout (SRP Batcher).
  That's what `SpecularLitProperties.hlsl` enforces — never add a spec property directly
  to one pass's CBUFFER.
- Never name a property `*_TexelSize` (and avoid new `*_ST` on the SPRITE shader): the 2D
  SRP Batcher rejects such materials. `_HeightTexel` / `_NormalTexST` are named around it.
- Everything per-instance goes through MaterialPropertyBlocks, read-modify-write, so
  SpecularController, EdgeBandOverride, creature brains (_FlashAmount/_EmissionColor),
  and the legacy overrides all compose on one renderer without clobbering each other.
