#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Submachina.Core.EditorTools
{
    /**
     * Auto-configures the import settings of the terrain art (AI-generated or hand-authored)
     * so it drops straight into the Terrain Object Generator's layers and the SplineFill
     * material slots without hand-tweaking each texture.
     *
     * Convention by folder:
     *   …/Terrain/Materials/  — tileable PaintLayer / SplineFill maps → Repeat wrap, mipmaps on.
     *   …/Terrain/Decals/     — stamped DecalLayer features          → Clamp wrap, alpha-is-transparency.
     *
     * Convention by file-name suffix (within those folders):
     *   *_n, *_nrm, *_normal  — tangent-space normal, straight RGB   → linear, uncompressed, no alpha.
     *   *_s, *_spec, *_mask   — specular mask (RGB tint × strength)  → linear, uncompressed, no alpha.
     *   everything else       — colour albedo                        → sRGB.
     *
     * Data maps must match what TerrainObjectGenerator.ConfigureLinearImporter produces, because
     * both feed the same shaders (SpriteLitSpecular / SplineFillLitSpecular). Those read the maps
     * as straight RGB via UnpackNormal, so sRGB MUST be off and the alpha MUST be opaque — see
     * the per-setting notes below. Runs on every (re)import into those folders.
     */
    public class TerrainArtImportProcessor : AssetPostprocessor
    {
        // Suffix tables for the linear-data classification (checked case-insensitively)
        private static readonly string[] NormalSuffixes = { "_n", "_nrm", "_normal" };
        private static readonly string[] MaskSuffixes = { "_s", "_spec", "_mask" };

        private void OnPreprocessTexture()
        {
            string p = assetPath.Replace('\\', '/');
            bool isMaterial = p.Contains("/Terrain/Materials/");
            bool isDecal = p.Contains("/Terrain/Decals/");
            if (!isMaterial && !isDecal) return;

            var ti = (TextureImporter)assetImporter;

            // Don't clobber sprite sheets the Terrain Object Generator produced. It sets
            // textureType = Sprite + secondary textures; forcing Default here would silently
            // undo that if someone points its Output folder at one of these directories.
            if (ti.textureType == TextureImporterType.Sprite && !ti.importSettingsMissing) return;

            // Normal maps and spec masks are vector/scalar DATA, not colour. They must stay
            // linear (sRGB off) or the shader's decoded normals and glint strengths are
            // gamma-warped — e.g. a 0.5 grey mask would read as 0.21 instead of 0.5.
            string stem = Path.GetFileNameWithoutExtension(p);
            bool isDataMap = HasSuffix(stem, NormalSuffixes) || HasSuffix(stem, MaskSuffixes);

            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = !isDataMap;
            ti.mipmapEnabled = true;
            ti.filterMode = FilterMode.Bilinear;

            // Block compression quantises the very channels the decoder reads (BC1 gives green
            // only 6 bits — that's the normal's Y), producing blocky facets under a moving
            // torch light. Colour art tolerates it; data maps don't.
            ti.textureCompression = isDataMap
                ? TextureImporterCompression.Uncompressed
                : TextureImporterCompression.Compressed;

            if (isMaterial)
            {
                // Tiled across the object / spline fill → must repeat
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.alphaIsTransparency = false;

                // Strip alpha on data maps. UnpackNormal() resolves to UnpackNormalmapRGorAG,
                // which does `a *= r` then reads xy from (a, g) — that only yields the correct
                // X when alpha is exactly 1. Hand-painted / AI-exported PNGs are often saved
                // RGBA with a stray non-opaque channel, which silently skews the lighting.
                ti.alphaSource = isDataMap
                    ? TextureImporterAlphaSource.None
                    : TextureImporterAlphaSource.FromInput;
            }
            else
            {
                // Stamped once → clamp, and keep the chroma-keyed alpha meaningful
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = true;
            }
        }

        /** Case-insensitive suffix test against a table of naming conventions. */
        private static bool HasSuffix(string stem, string[] suffixes)
        {
            foreach (string s in suffixes)
                if (stem.EndsWith(s, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // -------------------------------------------------------
        // Manual reimport
        // -------------------------------------------------------

        /**
         * OnPreprocessTexture only fires when an asset is actually (re)imported, so textures
         * that landed in these folders BEFORE a rule above existed keep their stale .meta.
         * Run this after changing the conventions to bring every existing asset in line.
         */
        [MenuItem("Tools/Submachina/Reimport Terrain Art")]
        private static void ReimportTerrainArt()
        {
            string[] folders =
            {
                "Assets/Submachina/Art/Terrain/Materials",
                "Assets/Submachina/Art/Terrain/Decals"
            };

            // Only walk folders that actually exist — AssetDatabase throws on missing search roots
            var existing = new System.Collections.Generic.List<string>();
            foreach (string f in folders)
                if (AssetDatabase.IsValidFolder(f)) existing.Add(f);
            if (existing.Count == 0) return;

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", existing.ToArray());
            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < guids.Length; i++)
                    AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guids[i]), ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            Debug.Log($"[TerrainArtImportProcessor] Reimported {guids.Length} terrain art texture(s).");
        }
    }
}
#endif
