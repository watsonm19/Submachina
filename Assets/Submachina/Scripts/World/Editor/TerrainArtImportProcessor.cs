#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Submachina.Core.EditorTools
{
    /**
     * Auto-configures the import settings of the AI-generated terrain art so it drops straight
     * into the Terrain Object Generator's layers without hand-tweaking each texture.
     *
     * Convention by folder:
     *   …/Terrain/Materials/  — tileable PaintLayer materials → Repeat wrap, sRGB, mipmaps on.
     *   …/Terrain/Decals/     — stamped DecalLayer features    → Clamp wrap, alpha-is-transparency.
     *
     * Both stay uncompressed-ish colour textures the baker reads via a GPU blit round-trip, so
     * the only things that actually matter here are wrap mode (tiling vs clamped) and that the
     * decal alpha survives. Runs on every (re)import into those folders.
     */
    public class TerrainArtImportProcessor : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            string p = assetPath.Replace('\\', '/');
            bool isMaterial = p.Contains("/Terrain/Materials/");
            bool isDecal = p.Contains("/Terrain/Decals/");
            if (!isMaterial && !isDecal) return;

            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;          // colour data (tinted at bake time)
            ti.mipmapEnabled = true;
            ti.filterMode = FilterMode.Bilinear;

            if (isMaterial)
            {
                // Tiled across the object → must repeat
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.alphaIsTransparency = false;
            }
            else
            {
                // Stamped once → clamp, and keep the chroma-keyed alpha meaningful
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = true;
            }
        }
    }
}
#endif
