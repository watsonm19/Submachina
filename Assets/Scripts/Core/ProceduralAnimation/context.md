# Core / ProceduralAnimation

Reusable procedural-animation toolkit for organic creatures (fish, eels, tentacles,
jellyfish, squid). Game-agnostic — creature brains live in
`Assets/Submachina/Scripts/World/Creatures/`.

## Design

The core technique is a **follow-the-leader constraint chain**: a head point is
pinned to a moving anchor and every subsequent point trails at a fixed segment
length, with per-joint bend limits (stiffness) and a straightening bias (whip
relaxation). Drivers nudge points sideways (traveling sine wave, perlin sway,
constant droop force) *before* the constraint pass, which converts raw offsets
into smooth organic S-curves. Motion of the anchor therefore *is* the animation:
a fast-swimming creature undulates harder automatically.

This deliberately does NOT reuse `SplineFillMeshBuilder` (Core/Rendering): that
component is edit-time oriented — closed silhouettes only, O(n²) ear-clip
retriangulation, per-rebuild allocations. Creature meshes here have fixed
topology and persistent buffers; only vertex positions are written per frame.

## Classes

- **ProcChain** — pure C# solver (no Unity lifetime). `Points[]`, `Solve(head,
  facing, maxBend, straighten)`, `Teleport()`, `TangentAt/NormalAt`. Zero
  allocation after construction.
- **ChainSimulator** (`[DefaultExecutionOrder(50)]`, LateUpdate) — owns one
  ProcChain anchored to a Transform; estimates anchor velocity, applies wave /
  sway / constant-force drivers, then solves. Public runtime channels for
  creature brains: `WaveFrequencyMultiplier`, `WaveAmplitudeMultiplier`,
  `SnapToAnchor()` (call after teleports / cull-restore; also runs OnEnable),
  plus the two hit reactions below.

## Hit reactions

`FacingMode.Velocity` means the head aims wherever the anchor is travelling — so a
knockback that reverses velocity flips the facing 180° while the swim wave keeps
running, and the creature reads as having *chosen* to turn around and swim off.
Two duration-based reactions fix that; both take a plain float, so they wire
straight to any UnityEvent (`HitReceiver.onHit`, `Health.onDamaged`) with the
duration typed in the Inspector. Repeat calls extend the window.

- **`Limp(duration)`** — ragdoll. Passes `Vector2.zero` as `headFacing`, so `Solve`
  falls back to the chain's own current direction and no facing is imposed at all.
  Joints loosen to `limpBendDegrees`, straightening drops to `limpStraightenSpeed`,
  the swim wave scales by `limpWaveMultiplier` (0 = stop swimming) and the perlin
  sway scales up by `limpSwayMultiplier` (the wobble). `Facing` is also held frozen
  for the window so the reversed heading is never recorded.
- **`FreezePose(duration)`** — the stiff alternative. Holds the solved shape rigid
  and translates it with the anchor each frame (it must still track the anchor, or
  the body detaches while the creature flies off). No drivers run.

**Recovery** (`limpRecoverDuration`, shared by both). Each reaction hands off to an
eased weight that decays 1 → 0: the bend limit and straightening blend back from
their limp values, so the body gathers itself instead of snapping rigid in one
frame. Only the limp's weight also scales the drivers, so a frozen pose keeps its
stiff character on the way out. Freeze recovery additionally eases `Facing` back to
the travel direction — a held pose can end up aimed far from where the creature
actually went, and adopting that in one frame is what reads as popping into a new
pose. The limp path doesn't need that, since it keeps solving throughout and never
diverges as far.

`EndLimp()` / `EndFreeze()` close a window early; `SnapToAnchor()` clears both.
Odin "Test Limp" / "Test Freeze Pose" buttons preview each in Play mode.

Which creatures care: only chains in **Velocity** facing get the turn-around
artifact — currently just the eel body. Jellyfish and squid tentacles are
`FacingMode.None`, so limp only loosens them as a flinch.
- **ChainStripRenderer** (`[DefaultExecutionOrder(60)]`) — tapered ribbon mesh
  over a ChainSimulator: 3 verts per point (edge/spine/edge) + rounded end caps.
  UV0 maps head→tail × across; UV1.z carries world-space distance to the
  silhouette for the ProcCreature shader's constant-width outline. Vertex color
  gradient along length (tentacle tip fades). Off-screen it throttles to an
  interval. Edit-mode preview shows the rest pose.
- **ChainSpriteRenderer** — alternative renderer: one SpriteRenderer per chain
  segment (art-directed shells/plates/links), auto-managed children, batches
  through the normal sprite pipeline.
- **RadialMeshRenderer** — deformable closed blob (jelly bells, squid mantles):
  rim ring + center fan, silhouette authored as a radius-by-angle curve.
  Runtime deform channels: `Squash` (Vector2), `UniformScale`, `RimOffsets[]`
  (per-vertex radial push). Same UV1 edge-distance convention.

## Rendering

Pair with `Submachina/2D/ProcCreature` (Assets/Submachina/Shaders/
ProcCreature2D.shader): fill color/texture × vertex color, world-unit outline
from UV1, HDR `_EmissionColor` with `_RimEmission` rim boost, `_FlashAmount`
flash channel for hit/chromatophore effects (drive via MaterialPropertyBlock).
Universal2D lit pass + flat NormalsRendering + UniversalForward fallback. Any
other material works too — the outline just needs UV1 support.

## Interactions

- Creature brains (Submachina) read `Speed`/`Facing` and write the multiplier
  channels so state machines speak through body language.
- `Submachina.Core.DistanceCullable` disables simulators/renderers far from all
  submarines; OnEnable re-snap prevents whip artifacts on restore.
