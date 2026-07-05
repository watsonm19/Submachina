# Terrain art generator (Nano Banana)

Generates **tileable materials** and **stamped decals** for the Terrain Object Generator's
`PaintLayer.texture` / `DecalLayer.texture` slots, using Google's **Nano Banana**
(`gemini-2.5-flash-image`). Output lands in `Assets/Submachina/Art/Terrain/{Materials,Decals}/`,
where `TerrainArtImportProcessor` auto-configures the imports (materials tile, decals keep alpha).

**The easy path:** just ask Claude — *"generate more materials and decals for the terrain object
generator in \<style>"*. The `generate-terrain-art` skill drives everything below (it will prompt you
for an API key the first time on a new machine). This README is the manual fallback / reference.

## Art direction (baked into the engine)

The style tails target a **gritty, grounded hard-sci-fi undersea-mining** look in the spirit of
**Factorio** and **Delta V: Rings of Saturn** — *realistic painterly* (believable natural materials
with confident concept-art brushwork, not cartoonish, not photographic, not mechanical), muted and
desaturated, weathered and moody. Rocks/minerals lean grimy and industrial; organic features
(coral, anemones…) stay natural but grimy. To shift a batch (e.g. "bright tropical", "volcanic
ash"), set the manifest's `style` field — it's appended on top of this base direction.

## Prerequisites

- **Windows PowerShell** (the script uses `Invoke-RestMethod` + `System.Drawing`).
- A **billing-enabled Google AI API key.** Image generation is *not* free-tier — a free key returns
  `429 ... limit: 0`. Enable billing on the key's project in Google AI Studio / Cloud. Cost ≈ **$0.039/image**.

## Providing the key (per machine)

Resolution order (first hit wins):
1. `-Key '...'` argument
2. `$env:GEMINI_KEY`
3. `Tools/TextureGen/gemini_key.local.txt` (gitignored — safe place to persist it on this machine)

Never commit the key. To persist it locally: create `Tools/TextureGen/gemini_key.local.txt`
containing just the key.

## Running

```powershell
# regenerate / extend the standard pack
$env:GEMINI_KEY = '...'
./Tools/TextureGen/generate_terrain_art.ps1 -Manifest ./Tools/TextureGen/manifest.example.json

# a custom set / style — author your own manifest first
./Tools/TextureGen/generate_terrain_art.ps1 -Manifest my_set.json -Force
```

- **Resume-safe:** existing files are skipped unless you pass `-Force`.
- Re-run a single texture by deleting its PNG (or use `-Force`) — the manifest is the source of truth.
- **`-Tag '_Suffix'`** writes a variant set that coexists with the current art (`Mat_<Name>_Suffix.png`),
  handy for trying a new style pass without overwriting what you have.

### Current style sets in the project
- **Base names** (`Mat_<Name>.png` / `Dec_<Name>.png`) = the **gritty** set (this engine's default) — the primary art.
- **`_Painterly`** = an earlier softer generic-painterly set, kept as extra options.
An untagged run overwrites the gritty defaults; use `-Tag` to add a new coexisting style pack.

## Manifest schema

```json
{
  "style": "optional global flavour woven into every prompt (e.g. 'dark volcanic basalt, ashy tones')",
  "resolution": 1024,
  "materials": { "RockBare": "bare seabed rock, tight grain" },
  "decals":    { "CoralBranch": "branching staghorn coral clump" }
}
```

- **materials** → `Mat_<Name>.png`, tileable, saved as-is. Give full-frame material subjects.
- **decals** → `Dec_<Name>.png`, single isolated subjects; generated on magenta then chroma-keyed to alpha.
- You only write the **subject**; framing, flat top-down lighting, stylization and the magenta
  background are appended automatically by the script's style tails.

## Why these choices (gotchas baked in)

- **Opaque output only.** Nano Banana never returns alpha; asking for transparency makes it paint a
  checkerboard. Hence decals are keyed from a solid **magenta** background (+ mild despill).
- **`IMAGE_RECITATION`.** Photographic phrasing ("photo", "material scan") makes the model *block*
  output (0 image parts). The style tails force **"stylized hand-painted game art, not a photograph"**,
  and each texture retries up to 4× with a variation nudge to clear the occasional trip.
- **Flat lighting / top-down.** The rock bake applies its own URP 2D lighting + normal relief, so the
  source art must be evenly lit and orthographic or the lighting double-ups.

## After generating

The skill handles Unity import + a review montage. Manually: focus Unity so it imports the PNGs
(`TerrainArtImportProcessor` sets wrap/alpha), then assign textures to a `PaintLayer` or `DecalLayer`
in the Terrain Object Generator.
