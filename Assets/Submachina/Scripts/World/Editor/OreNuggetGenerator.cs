#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;

namespace Submachina.Core.EditorTools
{
    /**
     * Editor tool that procedurally bakes a set of grayscale ore-nugget sprites
     * (each with a matching tangent-space normal map) into PNGs and wires them up
     * for URP 2D lighting.
     *
     * Why grayscale: RockObstacle tints its SpriteRenderer.color (dark blue normally,
     * warm orange while the mining laser heats it). Keeping the texture neutral grey
     * lets that tint read cleanly and lets one texture set serve copper/scrap too.
     *
     * Why a normal map: the submarine carries several Light2D torches. The Sprite-Lit
     * normals pass samples a per-sprite "_NormalMap" Secondary Texture, so each nugget
     * lights up with moving highlights and rounded, domed shading — the "shiny rocky"
     * look — while every nugget still shares the single default lit material.
     *
     * IMPORTANT (verified against the URP 2D shader): the sprite normals pass uses
     * UnpackNormalRGBNoScale (straight RGB, x=R y=G z=B, *2-1). So the normal PNG must
     * be imported as a Default texture with sRGB OFF and no compression — NOT as Unity's
     * NormalMap type (that would store DXT5nm and read back wrong here).
     *
     * Usage: Tools/Custom/Generate Ore Nuggets, tune params, press Generate. Output
     * lands in the configured folder as Nugget_{i}_albedo.png + Nugget_{i}_normal.png.
     */
    public class OreNuggetGenerator : OdinEditorWindow
    {
        [MenuItem("Tools/Custom/Generate Ore Nuggets")]
        private static void Open() => GetWindow<OreNuggetGenerator>("Ore Nugget Generator");

        // =====================
        // Output
        // =====================

        [BoxGroup("Output"), FolderPath, Tooltip("Folder the PNG pairs are written to (created if missing).")]
        public string outputFolder = "Assets/Submachina/Art/Ore";

        [BoxGroup("Output"), Tooltip("Base seed. Each variant offsets from this so results are reproducible.")]
        public int seed = 12345;

        [BoxGroup("Output"), PropertyRange(1, 12), Tooltip("How many distinct nugget shapes to bake.")]
        public int variantCount = 5;

        [BoxGroup("Output"), PropertyRange(64, 512), Tooltip("Texture size in pixels (square).")]
        public int resolution = 256;

        [BoxGroup("Output"), Min(1f), Tooltip("Sprite pixels-per-unit. 256 matches a ~1 world-unit nugget at default size.")]
        public float pixelsPerUnit = 256f;

        // =====================
        // Silhouette (alpha shape)
        // =====================

        [BoxGroup("Shape"), PropertyRange(0.25f, 0.49f), Tooltip("Nugget radius in normalized space (0.5 = touches texture edge). Leave margin for the feathered rim.")]
        public float baseRadius = 0.42f;

        [BoxGroup("Shape"), PropertyRange(0f, 0.2f), Tooltip("How much the silhouette wobbles in/out from a circle — higher = chunkier, more irregular.")]
        public float edgeNoiseAmount = 0.1f;

        [BoxGroup("Shape"), PropertyRange(1f, 8f), Tooltip("Frequency of the silhouette wobble — higher = more, smaller lumps around the rim.")]
        public float edgeNoiseFrequency = 3f;

        // =====================
        // Surface (grayscale albedo + height detail)
        // =====================

        [BoxGroup("Surface"), PropertyRange(0f, 1f), Tooltip("Base grey level before tinting. ~0.8 keeps the dark-blue tint readable while leaving room for highlights.")]
        public float baseGray = 0.8f;

        [BoxGroup("Surface"), PropertyRange(0f, 1f), Tooltip("Contrast of the rocky surface noise in the albedo. Higher reads more polished/metallic.")]
        public float surfaceContrast = 0.5f;

        [BoxGroup("Surface"), PropertyRange(1f, 16f), Tooltip("Frequency of the rocky surface noise.")]
        public float surfaceFrequency = 6f;

        [BoxGroup("Surface"), PropertyRange(0f, 1f), Tooltip("Strength of dark cracks/facets carved by cellular noise.")]
        public float crackStrength = 0.45f;

        [BoxGroup("Surface"), PropertyRange(1f, 16f), Tooltip("Frequency of the crack/facet pattern.")]
        public float crackFrequency = 5f;

        [BoxGroup("Surface"), PropertyRange(0f, 1f), Tooltip("Rim ambient-occlusion: darkens edges so the nugget reads as a rounded mass even unlit.")]
        public float aoStrength = 0.45f;

        [BoxGroup("Surface"), PropertyRange(0f, 0.08f), Tooltip("Chance per pixel of a bright mineral speck (metallic sparkle).")]
        public float speckChance = 0.018f;

        [BoxGroup("Surface"), PropertyRange(0f, 1f), Tooltip("How bright each mineral speck is. Higher = sharper metallic glints.")]
        public float speckBrightness = 0.85f;

        // =====================
        // Normal map (lighting depth)
        // =====================

        [BoxGroup("Normal"), PropertyRange(0f, 2f), Tooltip("Height of the overall hemispherical dome — makes each nugget look physically rounded under light.")]
        public float domeStrength = 1.5f;

        [BoxGroup("Normal"), PropertyRange(0f, 1f), Tooltip("Smooth medium-frequency facets in the normal — a few clean lobes give a coherent, metallic sheen (vs noisy rocky bumps).")]
        public float facetStrength = 0.35f;

        [BoxGroup("Normal"), PropertyRange(1f, 8f), Tooltip("Frequency of the smooth facets. Low = a few big lobes (more metallic); high = busier.")]
        public float facetFrequency = 3f;

        [BoxGroup("Normal"), PropertyRange(0f, 1f), Tooltip("Faint high-frequency crag detail in the normal. Keep low for a polished metallic look.")]
        public float detailStrength = 0.15f;

        [BoxGroup("Normal"), PropertyRange(0.5f, 10f), Tooltip("Overall normal steepness. Higher = tighter, shinier highlight.")]
        public float normalStrength = 5f;

        // -------------------------------------------------------
        // Generation entry point
        // -------------------------------------------------------

        /**
         * Bakes every variant: builds a shared heightfield + alpha mask, derives a
         * grayscale albedo and an RGB normal map from it, writes both PNGs, then
         * configures the importers (and attaches the normal as the albedo sprite's
         * "_NormalMap" Secondary Texture).
         */
        [Button(ButtonSizes.Large), GUIColor(0.5f, 0.9f, 0.6f)]
        private void Generate()
        {
            // Make sure the destination exists before we start writing files
            Directory.CreateDirectory(outputFolder);

            for (int v = 0; v < variantCount; v++)
            {
                // Per-variant deterministic RNG drives noise offsets and speck placement
                var rng = new System.Random(seed + v * 7919);
                float ox = (float)rng.NextDouble() * 1000f;
                float oy = (float)rng.NextDouble() * 1000f;
                float edgeOx = (float)rng.NextDouble() * 1000f;
                float edgeOy = (float)rng.NextDouble() * 1000f;
                float crackOx = (float)rng.NextDouble() * 1000f;
                float crackOy = (float)rng.NextDouble() * 1000f;

                int res = resolution;
                var alpha = new float[res * res];
                var height = new float[res * res];
                var gray = new float[res * res];

                // --- Pass 1: build alpha mask, height field, and grayscale albedo ---
                for (int y = 0; y < res; y++)
                {
                    for (int x = 0; x < res; x++)
                    {
                        int i = y * res + x;

                        // Centered coords in -1..1, plus radial distance from center
                        float cx = ((x + 0.5f) / res - 0.5f) * 2f;
                        float cy = ((y + 0.5f) / res - 0.5f) * 2f;
                        float dist = Mathf.Sqrt(cx * cx + cy * cy);

                        // Wobble the silhouette radius by direction so the rim is irregular
                        float dirLen = Mathf.Max(dist, 1e-4f);
                        float nx = cx / dirLen, ny = cy / dirLen;
                        float rWobble = Mathf.PerlinNoise(nx * edgeNoiseFrequency + edgeOx,
                                                          ny * edgeNoiseFrequency + edgeOy) - 0.5f;
                        float radius = baseRadius + edgeNoiseAmount * rWobble * 2f;

                        // Feathered edge → smooth alpha (1 inside, 0 outside)
                        const float feather = 0.015f;
                        float a = 1f - Smooth01(radius - feather, radius + feather, dist);
                        alpha[i] = a;

                        // Normalized "how far inside" for AO and dome (0 at rim, 1 at center)
                        float insideT = Mathf.Clamp01((radius - dist) / Mathf.Max(radius, 1e-4f));

                        // Rocky surface noise (0..1) drives both albedo detail and bump height
                        float u = (x + 0.5f) / res, w = (y + 0.5f) / res;
                        float surf = Fbm(u * surfaceFrequency + ox, w * surfaceFrequency + oy, 4);

                        // Cellular cracks: small worley distance → dark crevice lines
                        float cell = Worley(u * crackFrequency + crackOx, w * crackFrequency + crackOy);
                        float crack = Smooth01(0f, 0.18f, cell); // 0 in crevice, 1 on flats

                        // Height for the normal = smooth dome + a few smooth facets + faint crags.
                        // Keeping it mostly smooth concentrates the spotlight into a tight
                        // sliding sheen (metallic) instead of scattering it (rocky/matte).
                        float domeT = Mathf.Clamp01(dist / Mathf.Max(radius, 1e-4f));
                        float dome = domeStrength * Mathf.Sqrt(Mathf.Max(0f, 1f - domeT * domeT));
                        float facet = facetStrength * (Fbm(u * facetFrequency + ox, w * facetFrequency + oy, 2) - 0.5f);
                        float detail = detailStrength * (surf - 0.5f) - (1f - crack) * detailStrength * 0.5f;
                        height[i] = dome + facet + detail;

                        // Albedo grey: base + surface contrast, darkened by cracks and rim AO
                        float g = baseGray + surfaceContrast * (surf - 0.5f);
                        g *= Mathf.Lerp(1f - crackStrength, 1f, crack);
                        g *= Mathf.Lerp(1f - aoStrength, 1f, Mathf.Pow(insideT, 0.6f));

                        // Sparse bright mineral specks (metallic glints)
                        if (rng.NextDouble() < speckChance) g = Mathf.Min(1f, g + speckBrightness);

                        gray[i] = Mathf.Clamp01(g);
                    }
                }

                // --- Pass 2: derive normals from the height field (Sobel-style) ---
                var albedoPixels = new Color[res * res];
                var normalPixels = new Color[res * res];
                for (int y = 0; y < res; y++)
                {
                    for (int x = 0; x < res; x++)
                    {
                        int i = y * res + x;

                        // Albedo: greyscale RGB, silhouette in alpha
                        float gg = gray[i];
                        albedoPixels[i] = new Color(gg, gg, gg, alpha[i]);

                        // Flat normal outside the silhouette so the rim doesn't catch stray light
                        if (alpha[i] <= 0.001f)
                        {
                            normalPixels[i] = new Color(0.5f, 0.5f, 1f, 1f);
                            continue;
                        }

                        // Central differences on height → tangent-space normal
                        float hL = height[i - (x > 0 ? 1 : 0)];
                        float hR = height[i + (x < res - 1 ? 1 : 0)];
                        float hD = height[i - (y > 0 ? res : 0)];
                        float hU = height[i + (y < res - 1 ? res : 0)];
                        Vector3 n = new Vector3((hL - hR) * normalStrength,
                                                (hD - hU) * normalStrength,
                                                1f).normalized;

                        // Encode -1..1 → 0..1 (straight RGB, matches UnpackNormalRGBNoScale)
                        normalPixels[i] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                    }
                }

                // --- Write both PNGs to disk ---
                string albedoPath = $"{outputFolder}/Nugget_{v}_albedo.png";
                string normalPath = $"{outputFolder}/Nugget_{v}_normal.png";
                WritePng(albedoPixels, res, albedoPath);
                WritePng(normalPixels, res, normalPath);

                AssetDatabase.ImportAsset(normalPath);
                AssetDatabase.ImportAsset(albedoPath);

                // --- Configure importers (normal first so the sprite can reference it) ---
                ConfigureNormalImporter(normalPath);
                ConfigureAlbedoImporter(albedoPath, normalPath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[OreNuggetGenerator] Generated {variantCount} nugget variant(s) in {outputFolder}.");
        }

        // -------------------------------------------------------
        // Importer configuration
        // -------------------------------------------------------

        /**
         * Normal map import settings. Must stay a Default, linear, uncompressed texture
         * because the URP 2D sprite normals pass reads it as straight RGB.
         */
        private void ConfigureNormalImporter(string path)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = false;                 // linear — these are vectors, not colors
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.SaveAndReimport();
        }

        /**
         * Albedo sprite import settings: a tight single sprite with a fallback physics
         * shape, plus the matching normal attached as the "_NormalMap" Secondary Texture
         * so the default lit material picks it up automatically.
         */
        private void ConfigureAlbedoImporter(string albedoPath, string normalPath)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(albedoPath);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = pixelsPerUnit;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.Uncompressed;

            // Tight mesh + generated physics shape from the alpha silhouette
            var settings = new TextureImporterSettings();
            ti.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.Tight;
            settings.spriteGenerateFallbackPhysicsShape = true;
            ti.SetTextureSettings(settings);

            // Attach the normal as a per-sprite secondary texture named "_NormalMap"
            var normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            ti.secondarySpriteTextures = new[]
            {
                new SecondarySpriteTexture { name = "_NormalMap", texture = normalTex }
            };
            ti.SaveAndReimport();
        }

        // -------------------------------------------------------
        // Helpers
        // -------------------------------------------------------

        /** Encodes a pixel buffer to PNG bytes and writes them to disk. */
        private static void WritePng(Color[] pixels, int res, string path)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        /** Hermite smoothstep returning 0 below edge0, 1 above edge1. */
        private static float Smooth01(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / Mathf.Max(edge1 - edge0, 1e-5f));
            return t * t * (3f - 2f * t);
        }

        /** Fractional Brownian motion built from layered Perlin noise, normalized to 0..1. */
        private static float Fbm(float x, float y, int octaves)
        {
            float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * Mathf.PerlinNoise(x * freq, y * freq);
                norm += amp;
                amp *= 0.5f;
                freq *= 2f;
            }
            return sum / norm;
        }

        /** Cellular (Worley) F1 distance — small near feature points, used to carve cracks. */
        private static float Worley(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            float minD = 10f;

            // Search the 3x3 neighborhood for the nearest hashed feature point
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    Vector2 fp = Hash2(xi + dx, yi + dy);
                    float px = dx + fp.x - fx;
                    float py = dy + fp.y - fy;
                    minD = Mathf.Min(minD, px * px + py * py);
                }
            }
            return Mathf.Sqrt(minD);
        }

        /** Deterministic 2D hash → a point in [0,1)x[0,1) for a given integer cell. */
        private static Vector2 Hash2(int x, int y)
        {
            float a = Mathf.Sin(x * 127.1f + y * 311.7f) * 43758.5453f;
            float b = Mathf.Sin(x * 269.5f + y * 183.3f) * 43758.5453f;
            return new Vector2(a - Mathf.Floor(a), b - Mathf.Floor(b));
        }
    }
}
#endif
