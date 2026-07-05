---
name: generate-terrain-art
description: Generate tileable materials and/or stamped decals for the Terrain Object Generator using Google Nano Banana (Gemini image API). Use when the user asks to generate, add, or regenerate terrain textures, materials, decals, rock surfaces, coral/fossil/feature stamps, etc. for the terrain/rock generator — in any art style. Handles per-machine API key prompting, prompt authoring, generation, chroma-keying decals to alpha, Unity import, and review.
---

# Generate terrain art (materials + decals) via Nano Banana

Produces art for the Terrain Object Generator's two texture-consuming layer types:
- **Materials** → tiled `PaintLayer.texture` (full-frame, tileable, saved as-is).
- **Decals** → stamped `DecalLayer.texture` (single isolated subject, chroma-keyed to alpha).

The engine (framing, flat-lighting/stylization prompts, recitation retries, magenta chroma-key +
despill, output paths) is fixed in `Tools/TextureGen/generate_terrain_art.ps1`. Your job per request
is: resolve the key, author a manifest for what/style they asked, run it, import, review. See
`Tools/TextureGen/README.md` and the [[nano-banana-texture-workflow]] memory for the underlying facts.

## Procedure

**1. Resolve the API key (per machine).**
- Check `$env:GEMINI_KEY`, then `Tools/TextureGen/gemini_key.local.txt`. If either exists, use it.
- If neither exists, ask the user for a **billing-enabled** Google AI (Gemini) API key — image gen is
  not free-tier (a free key returns `429 limit: 0`). Offer to save it to `Tools/TextureGen/gemini_key.local.txt`
  for future runs on this machine (it is gitignored). **Never** commit the key or write it into any
  tracked file; pass it via `$env:GEMINI_KEY` for the run.
- Quick validity check (optional): a text call to `gemini-2.5-flash` returns 200; the image model
  returning `429 limit:0` means billing isn't enabled on that key's project.

**2. Author a manifest** (write to the scratchpad, not the repo) capturing the request:
```json
{ "style": "<flavour from the user's ask, or empty>", "resolution": 1024,
  "materials": { "PascalName": "short full-frame subject" },
  "decals":    { "PascalName": "short single-isolated-subject" } }
```
- Only fill the section(s) the user wants (materials, decals, or both).
- Names: PascalCase, become `Mat_<Name>.png` / `Dec_<Name>.png`. Don't collide with existing files
  unless regenerating (the script skips existing unless `-Force`).
- Subjects: one clause. Materials = a tileable surface; decals = one isolated feature. Do **not**
  add lighting/framing/"stylized"/"magenta" wording — the script appends all of that. Put any art
  direction ("dark volcanic", "bright tropical reef") in `style`.
- If the user just says "more", extend the set in `manifest.example.json` with new, non-duplicate ideas.

**3. Generate** (long-running — run in background, it logs to `Tools/TextureGen/gen_progress.log`):
```
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/TextureGen/generate_terrain_art.ps1 -Manifest <scratch>/manifest.json
```
Pass `-Key` or set `$env:GEMINI_KEY` for the shell. Add `-Force` to overwrite. Respect any spend
limit the user gives (~$0.039/image; count = materials + decals). Use `-Tag '_Suffix'` to write a
**variant set that coexists** with existing art (e.g. a new style pass) instead of overwriting —
files become `Mat_<Name><Tag>.png` / `Dec_<Name><Tag>.png`.

**4. Import into Unity** (if the editor is available via the `unity-synaptic` MCP): `run_csharp` to
`AssetDatabase.ImportAsset(path, ForceSynchronousImport)` each new PNG. `TerrainArtImportProcessor`
sets wrap/alpha by folder. Note: run_csharp does not reload the domain, so brand-new C# types won't
appear until the user focuses Unity — texture import itself works fine.

**5. Review.** Build a contact-sheet montage (System.Drawing, decals composited over mid-grey to show
the cutout) and show the user. Point out any off-concept results and offer targeted rerolls (delete
that PNG + rerun, or `-Force`).

## Guardrails
- Sending prompts to Google incurs cost — this is durably authorized when the user asks to generate,
  but honor explicit budgets and confirm before large batches.
- Materials should read evenly-lit and top-down; decals single + isolated. If output looks lit with
  hard shadows or off-concept, reroll with a tighter subject or a `style` tweak rather than editing
  the fixed script.
