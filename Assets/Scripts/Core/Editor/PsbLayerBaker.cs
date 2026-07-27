using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.PSD;
using UnityEngine;

namespace Core.Editor
{
    /// <summary>
    /// Bakes specially-named PSB layers into standalone, canvas-aligned PNG textures so a single
    /// Photoshop file can author a sprite's albedo + normal + specular mask together.
    ///
    /// Unity's Secondary Textures must reference SEPARATE texture assets, so a PSB layer can't be
    /// used as one directly. This baker bridges the gap: after the PSD Importer imports the PSB
    /// (mosaic mode, one sprite per layer), it extracts the pixels of layers named "albedo",
    /// "normal" and "mask" (any subset), writes them as "&lt;Psb&gt;_albedo.png" etc. beside the
    /// source file, and wires "_NormalMap" / "_SpecMask" Secondary Textures onto the baked albedo
    /// sprite (the names SpriteLitSpecular.shader and URP 2D lighting expect).
    ///
    /// The PSB itself ends up referenced by nothing, so it never ships in a build — it acts as a
    /// pure authoring source. Baking is automatic on every PSB (re)import, plus available manually
    /// via Tools/Custom/Bake PSB Layers.
    ///
    /// Alignment: Photoshop trims each layer to its painted bounds, so the imported layer-sprites
    /// can differ in size/position. We read each layer's true document rectangle straight from the
    /// PSB header (names + rects only — Unity still does all pixel decoding) and place every layer
    /// at its document position in a canvas-sized image, guaranteeing the three PNGs line up.
    /// </summary>
    public static class PsbLayerBaker
    {
        // Layer-name -> role convention. Missing layers are simply skipped.
        const string AlbedoLayer = "albedo";
        const string NormalLayer = "normal";
        const string MaskLayer = "mask";

        // Secondary Texture names consumed by URP 2D lighting / SpriteLitSpecular.shader.
        const string NormalSecondaryName = "_NormalMap";
        const string MaskSecondaryName = "_SpecMask";

        // Baked-PNG paths pending their first import; OnPreprocessTexture applies the right
        // import settings (sprite vs linear data) BEFORE Unity's first import of the file.
        static readonly Dictionary<string, string> s_PendingConfig = new(); // pngPath -> layer role

        // PSBs queued for baking once the current import batch finishes.
        static readonly HashSet<string> s_PendingBakes = new();

        /// <summary>A layer's name and its rectangle in PSD document space (top-left origin).</summary>
        class PsbLayer
        {
            public string name;
            public int left, top, right, bottom;
            public int Width => right - left;
            public int Height => bottom - top;
        }

        // ------------------------------------------------------------------ import hooks

        /// <summary>
        /// Asset pipeline hooks: queue a bake whenever a .psb (re)imports, and give freshly baked
        /// PNGs their correct import settings on their very first import.
        /// </summary>
        class Hooks : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                // Queue each imported PSB; bake after the import batch so its sprites are loadable.
                foreach (string path in imported)
                {
                    if (!path.EndsWith(".psb", StringComparison.OrdinalIgnoreCase)) continue;
                    if (s_PendingBakes.Add(path) && s_PendingBakes.Count == 1)
                        EditorApplication.delayCall += FlushPendingBakes;
                }
            }

            void OnPreprocessTexture()
            {
                // First-import configuration for PNGs this baker just created (never re-applied
                // afterwards, so manual tweaks to the baked assets are respected).
                if (!s_PendingConfig.TryGetValue(assetPath, out string role)) return;
                s_PendingConfig.Remove(assetPath);

                var importer = (TextureImporter)assetImporter;
                if (role == AlbedoLayer)
                {
                    // Albedo: a regular single sprite; PPU is copied from the source PSB so the
                    // baked sprite drops in at the same world size.
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    if (s_PendingPpu > 0f) importer.spritePixelsPerUnit = s_PendingPpu;
                }
                else
                {
                    // Normal/mask: raw linear data (the shader samples straight RGB), uncompressed
                    // so the relief and mask gradients survive intact.
                    importer.textureType = TextureImporterType.Default;
                    importer.sRGBTexture = false;
                    importer.mipmapEnabled = false;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                }
            }
        }

        // PPU forwarded from the source PSB to a pending albedo import (set just before ImportAsset).
        static float s_PendingPpu;

        /// <summary>Runs all queued bakes once the editor is idle after an import batch.</summary>
        static void FlushPendingBakes()
        {
            string[] batch = s_PendingBakes.ToArray();
            s_PendingBakes.Clear();
            foreach (string path in batch) Bake(path);
        }

        // ------------------------------------------------------------------ manual entry point

        /// <summary>Menu command: force-bake the selected PSB assets.</summary>
        [MenuItem("Tools/Custom/Bake PSB Layers")]
        static void BakeSelected()
        {
            var psbs = Selection.objects
                .Select(AssetDatabase.GetAssetPath)
                .Where(p => p.EndsWith(".psb", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (psbs.Length == 0) { Debug.LogWarning("[PsbLayerBaker] Select one or more .psb assets first."); return; }
            foreach (string path in psbs) Bake(path);
        }

        [MenuItem("Tools/Custom/Bake PSB Layers", true)]
        static bool BakeSelectedValidate() =>
            Selection.objects.Any(o => AssetDatabase.GetAssetPath(o).EndsWith(".psb", StringComparison.OrdinalIgnoreCase));

        // ------------------------------------------------------------------ bake

        /// <summary>
        /// Bake one PSB: verify importer settings allow lossless pixel extraction, compose a
        /// canvas-aligned PNG per convention layer, and wire Secondary Textures onto the albedo.
        /// Safe to call repeatedly — unchanged PNGs are not rewritten, so nothing churns.
        /// </summary>
        public static void Bake(string psbPath)
        {
            // Read layer names + document rects straight from the file; a PSB without any of the
            // convention layers is simply not ours to touch.
            List<PsbLayer> layers;
            int canvasW, canvasH;
            try { layers = ParsePsbLayers(psbPath, out canvasW, out canvasH); }
            catch (Exception e) { Debug.LogError($"[PsbLayerBaker] Failed to parse '{psbPath}': {e.Message}"); return; }

            var wanted = new[] { AlbedoLayer, NormalLayer, MaskLayer };
            var targets = layers.Where(l => wanted.Contains(l.name.ToLowerInvariant()) && l.Width > 0 && l.Height > 0).ToList();
            if (targets.Count == 0) return;

            // The extraction needs readable, uncompressed atlas pixels; fix the importer once if
            // needed (the PSB never ships, so this costs nothing at runtime). The reimport this
            // triggers re-queues the bake via OnPostprocessAllAssets, so we just stop here.
            if (AssetImporter.GetAtPath(psbPath) is not PSDImporter psdImporter)
            { Debug.LogError($"[PsbLayerBaker] '{psbPath}' is not using the PSD Importer."); return; }
            if (EnsureExtractionSettings(psdImporter)) return;

            // Collect the imported layer-sprites by name. A layer present in the file but not
            // imported (e.g. hidden with 'Include Hidden Layers' off) is reported and skipped.
            var sprites = AssetDatabase.LoadAllAssetsAtPath(psbPath).OfType<Sprite>().ToList();
            Texture2D atlas = null;
            Color32[] atlasPixels = null;

            string dir = Path.GetDirectoryName(psbPath)?.Replace('\\', '/');
            string baseName = Path.GetFileNameWithoutExtension(psbPath);
            var bakedPaths = new Dictionary<string, string>(); // role -> baked png path
            var changedPaths = new List<string>();

            foreach (var layer in targets)
            {
                string role = layer.name.ToLowerInvariant();
                var sprite = sprites.FirstOrDefault(s => string.Equals(s.name, layer.name, StringComparison.OrdinalIgnoreCase));
                if (sprite == null)
                { Debug.LogWarning($"[PsbLayerBaker] Layer '{layer.name}' exists in '{psbPath}' but no sprite was imported for it (hidden layer?). Skipped."); continue; }

                // Grab the packed atlas pixels once (all layer-sprites share the same texture).
                if (atlas == null)
                {
                    atlas = sprite.texture;
                    atlasPixels = atlas.GetPixels32();
                }

                // Compose the layer at its true document position in a canvas-sized image, then
                // only touch the file on disk if the result actually changed.
                Texture2D canvas = ComposeCanvasImage(atlasPixels, atlas.width, sprite, layer, canvasW, canvasH, NeutralFill(role));
                byte[] png = canvas.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(canvas);

                string pngPath = $"{dir}/{baseName}_{role}.png";
                bakedPaths[role] = pngPath;
                if (File.Exists(pngPath) && File.ReadAllBytes(pngPath).SequenceEqual(png)) continue;

                // New files get first-import settings via OnPreprocessTexture (see Hooks).
                if (!File.Exists(pngPath)) s_PendingConfig[pngPath] = role;
                File.WriteAllBytes(pngPath, png);
                changedPaths.Add(pngPath);
            }

            // Import the new/changed PNGs (albedo first-import needs the source PPU forwarded).
            s_PendingPpu = psdImporter.spritePixelsPerUnit;
            foreach (string p in changedPaths) AssetDatabase.ImportAsset(p);
            s_PendingPpu = 0f;

            // Wire the normal/mask PNGs as Secondary Textures on the baked albedo sprite.
            if (bakedPaths.ContainsKey(AlbedoLayer)) WireSecondaryTextures(bakedPaths);
            else if (bakedPaths.Count > 0)
                Debug.LogWarning($"[PsbLayerBaker] '{psbPath}' has no '{AlbedoLayer}' layer — baked textures were not wired to any sprite.");

            if (changedPaths.Count > 0)
                Debug.Log($"[PsbLayerBaker] Baked {changedPaths.Count} texture(s) from '{psbPath}': {string.Join(", ", changedPaths.Select(Path.GetFileName))}");
        }

        /// <summary>
        /// Make sure the PSB imports with readable, uncompressed pixels (lossless extraction) and
        /// in per-layer mosaic mode. Returns true if a setting had to change (a reimport was
        /// triggered and the bake will re-run afterwards).
        /// </summary>
        static bool EnsureExtractionSettings(PSDImporter importer)
        {
            bool changed = false;

            // Readable so GetPixels32 works; uncompressed so baked PNGs aren't DXT-degraded.
            if (!importer.isReadable) { importer.isReadable = true; changed = true; }
            var platform = importer.GetImporterPlatformSettings(EditorUserBuildSettings.activeBuildTarget);
            if (platform.textureCompression != TextureImporterCompression.Uncompressed ||
                platform.format != TextureImporterFormat.Automatic)
            {
                platform.textureCompression = TextureImporterCompression.Uncompressed;
                platform.format = TextureImporterFormat.Automatic;
                importer.SetImporterPlatformSettings(platform);
                changed = true;
            }

            // Per-layer sprites require Sprite mode Multiple + mosaic (the .psb default).
            if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Multiple || !importer.useMosaicMode)
            {
                Debug.LogWarning($"[PsbLayerBaker] '{importer.assetPath}' must use Texture Type 'Sprite', Sprite Mode 'Multiple' and Mosaic layer import — fixing.");
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Multiple;
                importer.useMosaicMode = true;
                changed = true;
            }

            if (changed) importer.SaveAndReimport();
            return changed;
        }

        /// <summary>Neutral background for canvas areas the layer doesn't cover.</summary>
        static Color32 NeutralFill(string role) => role switch
        {
            NormalLayer => new Color32(128, 128, 255, 255), // flat "up" normal
            MaskLayer => new Color32(255, 255, 255, 255),   // white = specular unchanged
            _ => new Color32(0, 0, 0, 0),                   // albedo: transparent
        };

        /// <summary>
        /// Copy one layer-sprite's atlas pixels into a canvas-sized image at the layer's document
        /// position. Handles the Y flip between PSD rows (top-down) and Unity rows (bottom-up),
        /// and layers whose painted bounds extend past the canvas (Photoshop keeps the overflow;
        /// the importer may keep it or crop it — both sizes are recognized).
        /// </summary>
        static Texture2D ComposeCanvasImage(Color32[] atlasPixels, int atlasW, Sprite sprite, PsbLayer layer, int canvasW, int canvasH, Color32 fill)
        {
            // Start from a fully neutral canvas.
            var pixels = new Color32[canvasW * canvasH];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = fill;

            // The visible region is the layer rect clipped to the canvas (document coords).
            int visL = Mathf.Max(layer.left, 0), visT = Mathf.Max(layer.top, 0);
            int visR = Mathf.Min(layer.right, canvasW), visB = Mathf.Min(layer.bottom, canvasH);

            // Decide what the sprite's pixels actually cover: the full layer rect (overflow kept)
            // or just the visible clip. Mismatches fall back to the clip, anchored top-left.
            var spriteRect = sprite.rect;
            int srcX = Mathf.RoundToInt(spriteRect.x), srcY = Mathf.RoundToInt(spriteRect.y);
            int srcW = Mathf.RoundToInt(spriteRect.width), srcH = Mathf.RoundToInt(spriteRect.height);
            int baseL, baseT, baseH;
            if (srcW == layer.Width && srcH == layer.Height) { baseL = layer.left; baseT = layer.top; baseH = layer.Height; }
            else
            {
                baseL = visL; baseT = visT; baseH = visB - visT;
                if (srcW != visR - visL || srcH != visB - visT)
                    Debug.LogWarning($"[PsbLayerBaker] Sprite '{sprite.name}' is {srcW}x{srcH} but its PSB layer rect is {layer.Width}x{layer.Height} — baked alignment may be off.");
            }

            // Row-by-row copy. Example: canvas 479x436, layer row y=0 (document top) lands at
            // Unity row 435 (bottom-up), sourced from the sprite's own top row.
            for (int y = visT; y < visB; y++)
            {
                int srcRow = baseH - 1 - (y - baseT);           // sprite-local row, bottom-up
                if (srcRow < 0 || srcRow >= srcH) continue;
                int dstRow = canvasH - 1 - y;                   // canvas row, bottom-up
                for (int x = visL; x < visR; x++)
                {
                    int srcCol = x - baseL;
                    if (srcCol < 0 || srcCol >= srcW) continue;
                    pixels[dstRow * canvasW + x] = atlasPixels[(srcY + srcRow) * atlasW + srcX + srcCol];
                }
            }

            var tex = new Texture2D(canvasW, canvasH, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// Point the baked albedo's Secondary Textures at the baked normal/mask PNGs (only the
        /// ones that exist), reimporting only when the wiring actually changed.
        /// </summary>
        static void WireSecondaryTextures(Dictionary<string, string> bakedPaths)
        {
            string albedoPath = bakedPaths[AlbedoLayer];
            if (AssetImporter.GetAtPath(albedoPath) is not TextureImporter importer)
            { Debug.LogError($"[PsbLayerBaker] Baked albedo importer not found at '{albedoPath}'."); return; }

            // Build the desired secondary-texture set from whichever layers were baked.
            var desired = new List<SecondarySpriteTexture>();
            if (bakedPaths.TryGetValue(NormalLayer, out string normalPath))
                desired.Add(new SecondarySpriteTexture { name = NormalSecondaryName, texture = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath) });
            if (bakedPaths.TryGetValue(MaskLayer, out string maskPath))
                desired.Add(new SecondarySpriteTexture { name = MaskSecondaryName, texture = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath) });

            // Reimport only on a real change to avoid endless churn on every bake.
            var current = importer.secondarySpriteTextures;
            bool same = current.Length == desired.Count &&
                        desired.All(d => current.Any(c => c.name == d.name && c.texture == d.texture));
            if (same) return;

            importer.secondarySpriteTextures = desired.ToArray();
            importer.SaveAndReimport();
        }

        // ------------------------------------------------------------------ psb parsing

        /// <summary>
        /// Minimal PSD/PSB reader: extracts only the canvas size and each layer's name + document
        /// rectangle (no pixel decoding — Unity's importer owns that). Handles both classic PSD
        /// (version 1, 4-byte section lengths) and large-document PSB (version 2, 8-byte lengths).
        /// </summary>
        static List<PsbLayer> ParsePsbLayers(string path, out int canvasW, out int canvasH)
        {
            byte[] data = File.ReadAllBytes(path);
            int o = 0;

            // Big-endian primitive readers over the byte buffer.
            ushort U16() { ushort v = (ushort)((data[o] << 8) | data[o + 1]); o += 2; return v; }
            int I32() { int v = (data[o] << 24) | (data[o + 1] << 16) | (data[o + 2] << 8) | data[o + 3]; o += 4; return v; }
            long Len(bool big) { if (!big) return (uint)I32(); long v = 0; for (int i = 0; i < 8; i++) v = (v << 8) | data[o + i]; o += 8; return v; }

            // --- Header: "8BPS", version (1 = PSD, 2 = PSB), canvas dimensions.
            if (data.Length < 26 || data[0] != '8' || data[1] != 'B' || data[2] != 'P' || data[3] != 'S')
                throw new InvalidDataException("Not a PSD/PSB file (missing 8BPS signature).");
            o = 4;
            ushort version = U16();
            bool psb = version == 2;
            if (version != 1 && !psb) throw new InvalidDataException($"Unsupported PSD version {version}.");
            o += 8; // reserved(6) + channel count(2)
            canvasH = I32();
            canvasW = I32();
            o += 4; // depth(2) + color mode(2)

            // --- Skip the color mode data and image resources sections.
            // NOTE: `o += I32()` would read the OLD o before I32() advances it — keep two steps.
            int skip = I32(); o += skip; // color mode data
            skip = I32(); o += skip;     // image resources

            // --- Layer & mask info -> layer info -> layer records.
            Len(psb);                       // total layer & mask info length (unused)
            Len(psb);                       // layer info length (unused)
            short layerCount = (short)U16();
            int count = Math.Abs((int)layerCount); // negative count = first alpha is transparency

            var layers = new List<PsbLayer>();
            for (int i = 0; i < count; i++)
            {
                // Rect (top/left/bottom/right), then per-channel id + data length.
                var layer = new PsbLayer { top = I32(), left = I32(), bottom = I32(), right = I32() };
                int channels = U16();
                for (int c = 0; c < channels; c++) { o += 2; Len(psb); }

                // Blend signature/key, opacity, clipping, flags, filler — skipped.
                o += 12;

                // Extra data block: mask data + blending ranges + Pascal name (+ additional info).
                int extraLen = I32();
                int extraEnd = o + extraLen;
                int maskLen = I32(); o += maskLen;   // layer mask data
                int rangeLen = I32(); o += rangeLen; // blending ranges
                int nameLen = data[o];
                layer.name = System.Text.Encoding.UTF8.GetString(data, o + 1, nameLen);
                o = extraEnd;               // skip padding + additional info blocks

                layers.Add(layer);
            }
            return layers;
        }
    }
}
