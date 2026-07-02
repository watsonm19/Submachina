#if UNITY_EDITOR
using System.IO;
using UnityEngine;

namespace Submachina.Core.EditorTools
{
    /**
     * Pure generation core for the Terrain Object Generator: turns a TerrainObjectSettings
     * + variant index into pixel buffers (albedo, normal, optional specular mask, optional
     * separate crystal layer). No AssetDatabase work happens here — the window owns file
     * writing and importer configuration — so the same code drives both the in-window
     * preview and the final bake.
     *
     * Pipeline per variant:
     *   1. Silhouette — a signed field built from the chosen shape family (blob/boulder/
     *      slab/shard/cluster), warped and wobbled. Gives alpha + "how far inside" depth.
     *   2. Rock surface — FBM surface noise, Worley cracks, optional strata bands, rim AO
     *      and mineral specks → shaded albedo + a height field for the normal map.
     *   3. Paint layers — noise-masked overlays (external PNGs or flat tints), optionally
     *      biased into crevices (sediment) or onto ridges (dust).
     *   4. Crystals — prismatic spikes and/or druse patches rasterized as analytic height
     *      fields (a cheap "3D baked to 2D" look), composited over (or beside) the rock.
     *   5. Normals — Sobel-style central differences on the final height field.
     */
    public static class TerrainObjectBaker
    {
        /** Everything one variant bake produces. Crystal buffers are null unless 'separateCrystalLayer' is on. */
        public class BakeResult
        {
            public int resolution;
            public Color[] albedo;         // RGB shaded colour, A = silhouette (incl. protruding crystals)
            public Color[] normal;         // straight-RGB tangent normal (matches UnpackNormalRGBNoScale)
            public Color[] specMask;       // R = per-pixel specular multiplier; null when not baked
            public Color[] crystalAlbedo;  // separate crystal overlay sprite (own alpha); null when combined
            public Color[] crystalNormal;
        }

        // -------------------------------------------------------
        // Entry point
        // -------------------------------------------------------

        /** Bakes one variant into pixel buffers. Deterministic for a given (settings.seed, variantIndex). */
        public static BakeResult Bake(TerrainObjectSettings s, int variantIndex)
        {
            // Per-variant deterministic RNG drives noise offsets, shape tables and crystal placement
            var rng = new System.Random(s.seed + variantIndex * 7919);
            float ox = Rand1k(rng), oy = Rand1k(rng);
            float crackOx = Rand1k(rng), crackOy = Rand1k(rng);
            float strataOx = Rand1k(rng), strataOy = Rand1k(rng);
            var sil = new Silhouette(s, rng);

            // Specks use their own stream so toggling other features never reshuffles them
            var speckRng = new System.Random(s.seed * 31 + variantIndex * 101 + 7);

            int res = s.resolution;
            int n = res * res;
            var alpha = new float[n];
            var height = new float[n];
            var relief = new float[n];   // 0..1 local relief (cracks low, bumps high) for layer height-bias
            var shade = new float[n];    // rock shading factor, reused so paint layers inherit AO/cracks
            var albedo = new Color[n];
            var spec = new float[n];

            // Strata band axis (bands run along the angle; the banding coordinate is the perpendicular)
            float strataRad = s.strataAngle * Mathf.Deg2Rad;
            float strataSin = Mathf.Sin(strataRad), strataCos = Mathf.Cos(strataRad);

            // --- Pass 1: silhouette, height field, shaded rock albedo, spec level ---
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    int i = y * res + x;

                    // Centered coords in -1..1 and plain 0..1 UV
                    float cx = ((x + 0.5f) / res - 0.5f) * 2f;
                    float cy = ((y + 0.5f) / res - 0.5f) * 2f;
                    float u = (x + 0.5f) / res, w = (y + 0.5f) / res;

                    // Signed silhouette field (negative inside) → feathered alpha + inside depth
                    float sd = sil.Signed(cx, cy);
                    float feather = 3f / res;
                    float a = 1f - Smooth01(-feather, feather, sd);
                    alpha[i] = a;
                    float insideT = Mathf.Clamp01(-sd / (s.baseRadius * 0.85f));

                    // Rocky surface noise + cellular cracks (0 in crevice, 1 on flats)
                    float surf = Fbm(u * s.surfaceFrequency + ox, w * s.surfaceFrequency + oy, 4);
                    float cell = Worley(u * s.crackFrequency + crackOx, w * s.crackFrequency + crackOy);
                    float crack = Smooth01(0f, 0.18f, cell);

                    // Optional sedimentary strata: sine bands across a rotated axis, bent by noise
                    float strata01 = 0.5f;
                    if (s.strataStrength > 0f)
                    {
                        float band = -cx * strataSin + cy * strataCos;
                        float wob = (Fbm(u * 2.5f + strataOx, w * 2.5f + strataOy, 2) - 0.5f) * s.strataWarp;
                        strata01 = Mathf.Sin((band + wob) * s.strataFrequency * Mathf.PI) * 0.5f + 0.5f;
                    }

                    // Height = shaped mass + smooth facets + faint crag detail + strata steps
                    float dome = s.domeStrength * Mathf.Pow(insideT, Mathf.Max(s.domeProfile, 0.05f));
                    float facet = s.facetStrength * (Fbm(u * s.facetFrequency + ox, w * s.facetFrequency + oy, 2) - 0.5f);
                    float detail = s.detailStrength * (surf - 0.5f) - (1f - crack) * s.detailStrength * 0.5f;
                    float strataBump = s.strataStrength * 0.12f * (strata01 - 0.5f);
                    height[i] = dome + facet + detail + strataBump;
                    relief[i] = Mathf.Clamp01(surf * 0.65f + crack * 0.35f);

                    // Shading factor: surface contrast, darkened by cracks, rim AO and strata
                    float sh = 1f + s.surfaceContrast * (surf - 0.5f) * 1.2f;
                    sh *= Mathf.Lerp(1f - s.crackStrength, 1f, crack);
                    sh *= Mathf.Lerp(1f - s.aoStrength, 1f, Mathf.Pow(insideT, 0.6f));
                    if (s.strataStrength > 0f) sh *= Mathf.Lerp(1f - s.strataStrength * 0.35f, 1f, strata01);
                    shade[i] = sh;

                    // Albedo = base colour * shading, plus sparse bright mineral specks
                    float sp = s.rockSpecLevel;
                    Color col = new Color(s.baseColor.r * sh, s.baseColor.g * sh, s.baseColor.b * sh, a);
                    if (speckRng.NextDouble() < s.speckChance)
                    {
                        col.r += s.speckBrightness; col.g += s.speckBrightness; col.b += s.speckBrightness;
                        if (s.specksAreShiny) sp = Mathf.Max(sp, 0.9f);
                    }
                    albedo[i] = new Color(Mathf.Clamp01(col.r), Mathf.Clamp01(col.g), Mathf.Clamp01(col.b), a);
                    spec[i] = sp;
                }
            }

            // --- Pass 2: paint layers (sand/dirt/rust…) masked by noise ± relief bias ---
            if (s.paintLayers != null)
            {
                foreach (var layer in s.paintLayers)
                {
                    if (layer == null || !layer.enabled || layer.opacity <= 0f) continue;

                    // Per-layer offsets consumed in list order → deterministic
                    float lox = Rand1k(rng), loy = Rand1k(rng);

                    // Read the external texture once (works even when not import-readable)
                    Color[] tpx = null; int tw = 0, th = 0;
                    if (layer.texture != null) tpx = ReadTexturePixels(layer.texture, out tw, out th);

                    for (int i = 0; i < n; i++)
                    {
                        if (alpha[i] <= 0.002f) continue;
                        float u = ((i % res) + 0.5f) / res;
                        float w = ((i / res) + 0.5f) / res;

                        // Placement mask: FBM noise, biased into valleys (bias<0) or onto peaks (bias>0)
                        float m = Fbm(u * layer.maskFrequency + lox, w * layer.maskFrequency + loy, 3);
                        m += layer.heightBias * (relief[i] - 0.5f);
                        float threshold = 1f - layer.coverage;
                        float soft = Mathf.Max(layer.maskSoftness * 0.5f, 0.02f);
                        float mask = Smooth01(threshold - soft, threshold + soft, m) * layer.opacity;
                        if (mask <= 0.004f) continue;

                        // Layer colour = tint (× tiled texture), re-shaded by the rock's AO/cracks
                        Color lc = layer.tint;
                        if (tpx != null) lc *= SampleBilinearWrap(tpx, tw, th, u * layer.textureTiling, w * layer.textureTiling);
                        float sh = shade[i];
                        var shaded = new Color(Mathf.Clamp01(lc.r * sh), Mathf.Clamp01(lc.g * sh), Mathf.Clamp01(lc.b * sh), albedo[i].a);
                        albedo[i] = Color.Lerp(albedo[i], shaded, mask);

                        // Texture brightness can also add tactile grain to the normal
                        if (layer.bumpInfluence > 0f && tpx != null)
                        {
                            float lum = lc.r * 0.3f + lc.g * 0.59f + lc.b * 0.11f;
                            height[i] += (lum - 0.5f) * layer.bumpInfluence * 0.35f * mask;
                        }
                    }
                }
            }

            // --- Pass 3: crystals (prism spikes and/or druse patches) ---
            bool wantCrystals = s.crystals != null && s.crystals.style != CrystalStyle.None;
            float[] cAlpha = null, cHeight = null, cSpec = null;
            Color[] cColor = null;
            if (wantCrystals)
                RasterizeCrystals(s, rng, sil, res, height, alpha, out cAlpha, out cColor, out cHeight, out cSpec);

            Color[] crystalAlbedoOut = null, crystalNormalOut = null;
            if (wantCrystals && s.separateCrystalLayer)
            {
                // Crystals become their own overlay sprite: rock buffers stay untouched
                crystalAlbedoOut = new Color[n];
                var ch = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float ca = cAlpha[i];
                    crystalAlbedoOut[i] = new Color(cColor[i].r, cColor[i].g, cColor[i].b, ca);
                    ch[i] = ca > 0.003f ? cHeight[i] : 0f;
                }
                crystalNormalOut = DeriveNormals(ch, cAlpha, res, s.normalStrength * 1.2f);
            }
            else if (wantCrystals)
            {
                // Composite crystals into the rock buffers (they can protrude past the rim → max alpha)
                for (int i = 0; i < n; i++)
                {
                    float ca = cAlpha[i];
                    if (ca <= 0.003f) continue;
                    var rc = albedo[i];
                    albedo[i] = new Color(
                        Mathf.Lerp(rc.r, cColor[i].r, ca),
                        Mathf.Lerp(rc.g, cColor[i].g, ca),
                        Mathf.Lerp(rc.b, cColor[i].b, ca),
                        Mathf.Max(rc.a, ca));
                    height[i] = Mathf.Lerp(height[i], cHeight[i], ca);
                    alpha[i] = Mathf.Max(alpha[i], ca);
                    spec[i] = Mathf.Lerp(spec[i], cSpec[i], ca);
                }
            }

            // --- Pass 4: derive normals + pack the spec mask ---
            var normal = DeriveNormals(height, alpha, res, s.normalStrength);

            Color[] specMask = null;
            if (s.bakeSpecMask)
            {
                specMask = new Color[n];
                for (int i = 0; i < n; i++)
                {
                    float v = Mathf.Clamp01(spec[i]);
                    specMask[i] = new Color(v, v, v, 1f);
                }
            }

            return new BakeResult
            {
                resolution = res,
                albedo = albedo,
                normal = normal,
                specMask = specMask,
                crystalAlbedo = crystalAlbedoOut,
                crystalNormal = crystalNormalOut
            };
        }

        // -------------------------------------------------------
        // Silhouette field
        // -------------------------------------------------------

        /**
         * Signed silhouette field for one variant: negative inside, ~radial units outside.
         * Built once per bake from the settings + variant RNG (rotation, shard corner table,
         * cluster lobes), then evaluated per pixel with warp + edge wobble applied on top.
         */
        private class Silhouette
        {
            private readonly TerrainObjectSettings _s;
            private readonly float _rot;
            private readonly float _edgeOx, _edgeOy, _warpOx, _warpOy;
            private readonly float[] _shardAngles;   // increasing corner angles in 0..2π
            private readonly float[] _shardRadii;
            private readonly Vector2[] _lobePos;
            private readonly float[] _lobeRadius;

            public Silhouette(TerrainObjectSettings s, System.Random rng)
            {
                _s = s;
                _rot = s.randomizeRotation ? (float)rng.NextDouble() * Mathf.PI * 2f : 0f;
                _edgeOx = Rand1k(rng); _edgeOy = Rand1k(rng);
                _warpOx = Rand1k(rng); _warpOy = Rand1k(rng);

                // Shard: random-but-ordered corner angles with per-corner radius jitter
                if (s.shape == SilhouetteShape.Shard)
                {
                    int c = Mathf.Max(s.shardVertices, 3);
                    _shardAngles = new float[c];
                    _shardRadii = new float[c];
                    for (int i = 0; i < c; i++)
                    {
                        _shardAngles[i] = (i + (float)rng.NextDouble() * 0.8f) / c * Mathf.PI * 2f;
                        _shardRadii[i] = s.baseRadius * (1f - s.shardIrregularity * (float)rng.NextDouble() * 0.55f);
                    }
                }

                // Cluster: scatter lobes around the centre, sized so the mass stays in bounds
                if (s.shape == SilhouetteShape.Cluster)
                {
                    int c = Mathf.Max(s.lobeCount, 2);
                    _lobePos = new Vector2[c];
                    _lobeRadius = new float[c];
                    float lobeR0 = s.baseRadius * (1f - s.lobeSpread * 0.5f);
                    for (int i = 0; i < c; i++)
                    {
                        float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                        float dist = i == 0 ? 0f : s.baseRadius * s.lobeSpread * (0.35f + 0.65f * (float)rng.NextDouble());
                        _lobePos[i] = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist;
                        _lobeRadius[i] = lobeR0 * (1f - s.lobeSizeVariance * (float)rng.NextDouble() * 0.6f);
                    }
                }
            }

            /** Signed field: negative inside the silhouette. */
            public float Signed(float cx, float cy)
            {
                // Whole-shape rotation, then aspect stretch (divide X so the boundary widens)
                float ca = Mathf.Cos(-_rot), sa = Mathf.Sin(-_rot);
                float px = cx * ca - cy * sa;
                float py = cx * sa + cy * ca;
                px /= Mathf.Max(_s.aspect, 0.05f);

                // Domain warp for gnarled outlines
                if (_s.warpAmount > 0f)
                {
                    float wx = Fbm(px * _s.warpFrequency + _warpOx, py * _s.warpFrequency + _warpOy, 3) - 0.5f;
                    float wy = Fbm(px * _s.warpFrequency + _warpOy + 31.7f, py * _s.warpFrequency + _warpOx + 17.3f, 3) - 0.5f;
                    px += wx * _s.warpAmount;
                    py += wy * _s.warpAmount;
                }

                float d = ShapeSigned(px, py);

                // Rim wobble: positional noise pushed through the field
                if (_s.edgeNoiseAmount > 0f)
                    d += (Fbm(px * _s.edgeNoiseFrequency + _edgeOx, py * _s.edgeNoiseFrequency + _edgeOy, 3) - 0.5f) * 2f * _s.edgeNoiseAmount;

                return d;
            }

            /** Per-shape signed value before warp/wobble. */
            private float ShapeSigned(float px, float py)
            {
                float dist = Mathf.Sqrt(px * px + py * py);
                switch (_s.shape)
                {
                    default:
                    case SilhouetteShape.Blob:
                        return dist - _s.baseRadius;

                    case SilhouetteShape.Boulder:
                    case SilhouetteShape.Slab:
                    {
                        // Superellipse |x/a|^n + |y/b|^n = 1; Slab additionally squashes vertically
                        float a = _s.baseRadius;
                        float b = _s.shape == SilhouetteShape.Slab ? _s.baseRadius * 0.55f : _s.baseRadius;
                        float e = Mathf.Max(_s.squareness, 1.01f);
                        float v = Mathf.Pow(Mathf.Abs(px) / a, e) + Mathf.Pow(Mathf.Abs(py) / b, e);
                        return (Mathf.Pow(v, 1f / e) - 1f) * Mathf.Min(a, b);
                    }

                    case SilhouetteShape.Shard:
                        return dist - ShardRadius(Mathf.Atan2(py, px));

                    case SilhouetteShape.Cluster:
                    {
                        float d = 1e5f;
                        for (int i = 0; i < _lobePos.Length; i++)
                        {
                            float dx = px - _lobePos[i].x, dy = py - _lobePos[i].y;
                            float di = Mathf.Sqrt(dx * dx + dy * dy) - _lobeRadius[i];
                            d = SmoothMin(d, di, _s.lobeSmoothness);
                        }
                        return d;
                    }
                }
            }

            /**
             * Polygon radius at angle θ: exact straight-chord radius between the two neighbouring
             * corners (flat facet edges), optionally blended toward a smooth interpolation by
             * shardRounding. Example: with corners at (A1,r1)/(A2,r2) the flat-edge radius is
             * r = r1·r2·sin(A2−A1) / (r2·sin(A2−θ) + r1·sin(θ−A1)).
             */
            private float ShardRadius(float theta)
            {
                int c = _shardAngles.Length;
                if (theta < 0f) theta += Mathf.PI * 2f;

                // Find the corner pair bracketing θ (wrapping the last→first edge)
                int i0 = c - 1;
                for (int i = 0; i < c; i++)
                {
                    if (theta < _shardAngles[i]) { i0 = i - 1; break; }
                    if (i == c - 1) i0 = c - 1;
                }
                int i1 = (i0 + 1) % c;
                float a1 = i0 >= 0 ? _shardAngles[i0] : _shardAngles[c - 1] - Mathf.PI * 2f;
                if (i0 < 0) i0 = c - 1;
                float a2 = _shardAngles[i1];
                if (a2 <= a1) a2 += Mathf.PI * 2f;
                if (theta < a1) theta += Mathf.PI * 2f;

                float r1 = _shardRadii[i0], r2 = _shardRadii[i1];

                // Flat polygon edge
                float denom = r2 * Mathf.Sin(a2 - theta) + r1 * Mathf.Sin(theta - a1);
                float flat = Mathf.Abs(denom) < 1e-5f ? r1 : (r1 * r2 * Mathf.Sin(a2 - a1)) / denom;

                // Rounded alternative: hermite blend between the corner radii
                float t = Mathf.Clamp01((theta - a1) / Mathf.Max(a2 - a1, 1e-5f));
                float round = Mathf.Lerp(r1, r2, t * t * (3f - 2f * t));

                return Mathf.Lerp(flat, round, _s.shardRounding);
            }

            /** Farthest inside point along a ray from centre (normalized distance) — used to root rim crystals. */
            public float FindRim(float angle)
            {
                float dx = Mathf.Cos(angle), dy = Mathf.Sin(angle);
                float lastInside = 0f;
                for (float t = 0f; t <= 1f; t += 0.01f)
                    if (Signed(dx * t, dy * t) < 0f) lastInside = t;
                return lastInside;
            }

            /** Random point at least minDepth inside the silhouette (falls back to centre). */
            public Vector2 RandomInside(System.Random rng, float minDepth)
            {
                for (int tries = 0; tries < 64; tries++)
                {
                    var p = new Vector2(((float)rng.NextDouble() - 0.5f) * 1.6f, ((float)rng.NextDouble() - 0.5f) * 1.6f);
                    if (Signed(p.x, p.y) < -minDepth) return p;
                }
                return Vector2.zero;
            }
        }

        // -------------------------------------------------------
        // Crystals
        // -------------------------------------------------------

        /**
         * Rasterizes all crystals into their own buffers (alpha/colour/height/spec). Uses a
         * height "z-buffer" so overlapping crystals occlude correctly. Druse first, prisms on
         * top. The caller decides whether to composite over the rock or keep them separate.
         */
        private static void RasterizeCrystals(TerrainObjectSettings s, System.Random rng, Silhouette sil, int res,
            float[] rockHeight, float[] rockAlpha,
            out float[] cAlpha, out Color[] cColor, out float[] cHeight, out float[] cSpec)
        {
            var cs = s.crystals;
            int n = res * res;
            cAlpha = new float[n];
            cColor = new Color[n];
            cHeight = new float[n];
            cSpec = new float[n];
            for (int i = 0; i < n; i++) cHeight[i] = float.NegativeInfinity;

            bool prisms = cs.style == CrystalStyle.Prisms || cs.style == CrystalStyle.PrismsAndDruse;
            bool druse = cs.style == CrystalStyle.Druse || cs.style == CrystalStyle.PrismsAndDruse;

            if (druse) RasterizeDruse(s, rng, sil, res, rockHeight, rockAlpha, cAlpha, cColor, cHeight, cSpec);
            if (prisms) RasterizePrisms(s, rng, sil, res, rockHeight, cAlpha, cColor, cHeight, cSpec);
        }

        /** Grows prism clusters (quartz-point bundles), each crystal an elongated tapered ridge. */
        private static void RasterizePrisms(TerrainObjectSettings s, System.Random rng, Silhouette sil, int res,
            float[] rockHeight, float[] cAlpha, Color[] cColor, float[] cHeight, float[] cSpec)
        {
            var cs = s.crystals;
            float feather = 4f / res;

            for (int c = 0; c < cs.clusterCount; c++)
            {
                // Cluster root: near the rim pointing outward (spikes sticking out), or anywhere
                Vector2 basePt, mainDir;
                if (cs.growFromRim)
                {
                    float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                    mainDir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    basePt = mainDir * sil.FindRim(ang) * 0.85f;
                }
                else
                {
                    basePt = sil.RandomInside(rng, s.baseRadius * 0.15f);
                    float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                    mainDir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                }

                for (int k = 0; k < cs.crystalsPerCluster; k++)
                {
                    // Fan each crystal off the cluster axis with jittered root, size and colour
                    float spreadAng = ((float)rng.NextDouble() - 0.5f) * cs.spread * 2.4f;
                    Vector2 dir = Rotate(mainDir, spreadAng);
                    Vector2 perp = new Vector2(-dir.y, dir.x);
                    float len = Mathf.Lerp(cs.lengthRange.x, cs.lengthRange.y, (float)rng.NextDouble());
                    float halfW = Mathf.Lerp(cs.widthRange.x, cs.widthRange.y, (float)rng.NextDouble()) * 0.5f;
                    Vector2 root = basePt
                                   + perp * (((float)rng.NextDouble() - 0.5f) * halfW * 4f)
                                   - dir * ((float)rng.NextDouble() * len * 0.25f);
                    Color tint = JitterColor(cs.color, cs.colorVariation, rng);
                    float lift = 0.06f + (float)rng.NextDouble() * 0.08f;
                    float streakSeed = (float)rng.NextDouble() * 10f;

                    // Root the crystal on the local rock height so it stands proud of the surface
                    float hBase = SampleField(rockHeight, res, root) + lift;

                    RasterizePrism(res, feather, root, dir, perp, len, halfW, cs, tint, hBase, streakSeed,
                                   rockHeight, cAlpha, cColor, cHeight, cSpec);
                }
            }
        }

        /** Rasterizes one prism: tapered quad in (t = along, s = across) space with a ridged height profile. */
        private static void RasterizePrism(int res, float feather, Vector2 root, Vector2 dir, Vector2 perp,
            float len, float halfW, CrystalSettings cs, Color tint, float hBase, float streakSeed,
            float[] rockHeight, float[] cAlpha, Color[] cColor, float[] cHeight, float[] cSpec)
        {
            // Bounding box in pixel space (root..tip expanded by width + feather)
            Vector2 tip = root + dir * len;
            float pad = halfW + feather * 2f;
            float minX = Mathf.Min(root.x, tip.x) - pad, maxX = Mathf.Max(root.x, tip.x) + pad;
            float minY = Mathf.Min(root.y, tip.y) - pad, maxY = Mathf.Max(root.y, tip.y) + pad;
            int x0 = Mathf.Clamp(Mathf.FloorToInt((minX * 0.5f + 0.5f) * res), 0, res - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((maxX * 0.5f + 0.5f) * res), 0, res - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt((minY * 0.5f + 0.5f) * res), 0, res - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt((maxY * 0.5f + 0.5f) * res), 0, res - 1);

            float tipStart = 1f - cs.tipFraction;

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int i = y * res + x;
                    float cx = ((x + 0.5f) / res - 0.5f) * 2f;
                    float cy = ((y + 0.5f) / res - 0.5f) * 2f;

                    // Local prism coords: t along the axis (0..len), sDist across (±halfW)
                    float rx = cx - root.x, ry = cy - root.y;
                    float t = rx * dir.x + ry * dir.y;
                    if (t < 0f || t > len) continue;
                    float tt = t / len;
                    float sDist = rx * perp.x + ry * perp.y;

                    // Width tapers to a point across the tip fraction
                    float wLocal = halfW * (tt <= tipStart ? 1f : Mathf.Max(1e-4f, (1f - tt) / Mathf.Max(cs.tipFraction, 1e-4f)));
                    float edge = wLocal - Mathf.Abs(sDist);
                    float cov = Smooth01(0f, feather, edge);
                    if (cov <= 0.003f) continue;

                    // Height: rooted on the rock, rising toward the tip, ridged across the width
                    float ridge = 1f - Mathf.Clamp01(Mathf.Abs(sDist) / Mathf.Max(wLocal, 1e-4f));
                    float h = hBase + tt * halfW * 2f + ridge * halfW * cs.heightScale * 5f;
                    h = Mathf.Max(h, rockHeight[i] + 0.04f);
                    if (h <= cHeight[i]) continue; // painter's algorithm by height

                    // Facets: two side planes split at the ridge line, brighter toward the tip,
                    // a hot ridge highlight, and faint lengthwise streaks
                    float sideT = Mathf.Clamp(sDist / Mathf.Max(wLocal, 1e-4f), -1f, 1f);
                    float sh = Mathf.Lerp(1f - cs.facetContrast * 0.45f, 1f + cs.facetContrast * 0.45f,
                                          Smooth01(-0.2f, 0.2f, sideT));
                    sh *= Mathf.Lerp(0.7f, 1.1f, tt);
                    if (tt > tipStart) sh *= 1f + cs.tipBrightness * 0.6f * (tt - tipStart) / Mathf.Max(cs.tipFraction, 1e-4f);
                    sh += Mathf.Pow(ridge, 4f) * (0.25f + cs.tipBrightness * 0.3f);
                    if (cs.streakStrength > 0f)
                        sh *= 1f + (Mathf.PerlinNoise(tt * 14f, streakSeed) - 0.5f) * 2f * cs.streakStrength;

                    cColor[i] = new Color(Mathf.Clamp01(tint.r * sh), Mathf.Clamp01(tint.g * sh), Mathf.Clamp01(tint.b * sh), 1f);
                    cHeight[i] = h;
                    cAlpha[i] = Mathf.Max(cAlpha[i], cov);
                    cSpec[i] = cs.specMaskValue;
                }
            }
        }

        /** Grows druse patches: noisy-edged discs filled with Voronoi cells shaded as faceted terminations. */
        private static void RasterizeDruse(TerrainObjectSettings s, System.Random rng, Silhouette sil, int res,
            float[] rockHeight, float[] rockAlpha, float[] cAlpha, Color[] cColor, float[] cHeight, float[] cSpec)
        {
            var cs = s.crystals;

            for (int c = 0; c < cs.drusePatchCount; c++)
            {
                Vector2 center = sil.RandomInside(rng, s.baseRadius * 0.2f);
                float radius = Mathf.Lerp(cs.drusePatchRadius.x, cs.drusePatchRadius.y, (float)rng.NextDouble());
                float nOx = Rand1k(rng), nOy = Rand1k(rng);
                float cellOx = Rand1k(rng), cellOy = Rand1k(rng);

                // Patch bounding box
                float pad = radius * 1.6f;
                int x0 = Mathf.Clamp(Mathf.FloorToInt(((center.x - pad) * 0.5f + 0.5f) * res), 0, res - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt(((center.x + pad) * 0.5f + 0.5f) * res), 0, res - 1);
                int y0 = Mathf.Clamp(Mathf.FloorToInt(((center.y - pad) * 0.5f + 0.5f) * res), 0, res - 1);
                int y1 = Mathf.Clamp(Mathf.CeilToInt(((center.y + pad) * 0.5f + 0.5f) * res), 0, res - 1);

                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        int i = y * res + x;
                        if (rockAlpha[i] < 0.4f) continue; // druse grows on the rock face only

                        float cx = ((x + 0.5f) / res - 0.5f) * 2f;
                        float cy = ((y + 0.5f) / res - 0.5f) * 2f;
                        float u = (x + 0.5f) / res, w = (y + 0.5f) / res;

                        // Noisy-edged patch mask
                        float dx = cx - center.x, dy = cy - center.y;
                        float pd = Mathf.Sqrt(dx * dx + dy * dy);
                        float noisyEdge = (Fbm(u * 7f + nOx, w * 7f + nOy, 2) - 0.5f) * radius * 1.2f;
                        float mask = Smooth01(0f, radius * 0.35f, radius - pd + noisyEdge);
                        if (mask <= 0.01f) continue;

                        // Voronoi bed: each cell is one crystal termination — a tiny pyramid with
                        // hashed facet brightness per direction sector and dark crevice borders
                        var ws = WorleySample(cx * cs.druseDensity + cellOx, cy * cs.druseDensity + cellOy);
                        float pyramid = Mathf.Clamp01(1f - ws.f1 * 1.5f);
                        float border = Smooth01(0.03f, 0.16f, ws.f2 - ws.f1);

                        float sectorAng = Mathf.Atan2(ws.toFeature.y, ws.toFeature.x);
                        int sector = (int)Mathf.Floor((sectorAng / (Mathf.PI * 2f) + 0.5f) * 4f) & 3;
                        float fh = HashF(ws.cell.x * 7 + sector * 131, ws.cell.y * 13 + sector);
                        float sh = Mathf.Lerp(1f, 0.55f + 0.5f * fh, cs.facetContrast);
                        sh *= Mathf.Lerp(0.3f, 1f, border);
                        sh += cs.tipBrightness * 0.7f * Mathf.Pow(Mathf.Clamp01(1f - ws.f1 * 2.2f), 2f);

                        // Cell-stable colour jitter so each termination reads as its own crystal
                        Color tint = JitterColorHash(cs.color, cs.colorVariation, ws.cell);

                        float h = rockHeight[i] + mask * (0.05f + pyramid * 0.28f * cs.heightScale);
                        if (h <= cHeight[i]) continue;

                        cColor[i] = new Color(Mathf.Clamp01(tint.r * sh), Mathf.Clamp01(tint.g * sh), Mathf.Clamp01(tint.b * sh), 1f);
                        cHeight[i] = h;
                        cAlpha[i] = Mathf.Max(cAlpha[i], mask);
                        cSpec[i] = cs.specMaskValue;
                    }
                }
            }
        }

        // -------------------------------------------------------
        // Normals
        // -------------------------------------------------------

        /** Central-difference normals from a height field; flat outside the alpha so the rim doesn't catch stray light. */
        private static Color[] DeriveNormals(float[] height, float[] alpha, int res, float strength)
        {
            var normals = new Color[res * res];
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    int i = y * res + x;
                    if (alpha[i] <= 0.001f)
                    {
                        normals[i] = new Color(0.5f, 0.5f, 1f, 1f);
                        continue;
                    }

                    float hL = height[i - (x > 0 ? 1 : 0)];
                    float hR = height[i + (x < res - 1 ? 1 : 0)];
                    float hD = height[i - (y > 0 ? res : 0)];
                    float hU = height[i + (y < res - 1 ? res : 0)];
                    Vector3 nv = new Vector3((hL - hR) * strength, (hD - hU) * strength, 1f).normalized;

                    // Encode -1..1 → 0..1 (straight RGB, matches UnpackNormalRGBNoScale)
                    normals[i] = new Color(nv.x * 0.5f + 0.5f, nv.y * 0.5f + 0.5f, nv.z * 0.5f + 0.5f, 1f);
                }
            }
            return normals;
        }

        // -------------------------------------------------------
        // Texture IO helpers
        // -------------------------------------------------------

        /** Encodes a pixel buffer to PNG bytes and writes them to disk. */
        public static void WritePng(Color[] pixels, int res, string path)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        /** Reads a texture's pixels even when it isn't import-readable (GPU blit round-trip). */
        private static Color[] ReadTexturePixels(Texture2D tex, out int w, out int h)
        {
            w = tex.width; h = tex.height;
            if (tex.isReadable) return tex.GetPixels();

            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var tmp = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tmp.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tmp.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = tmp.GetPixels();
            Object.DestroyImmediate(tmp);
            return px;
        }

        /** Bilinear sample with wrapped UVs (for tiled paint-layer textures). */
        private static Color SampleBilinearWrap(Color[] px, int w, int h, float u, float v)
        {
            u -= Mathf.Floor(u); v -= Mathf.Floor(v);
            float fx = u * w - 0.5f, fy = v * h - 0.5f;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0, ty = fy - y0;
            int x1 = (x0 + 1) % w, y1 = (y0 + 1) % h;
            x0 = (x0 % w + w) % w; y0 = (y0 % h + h) % h;
            if (x1 < 0) x1 += w;
            if (y1 < 0) y1 += h;

            Color a = Color.Lerp(px[y0 * w + x0], px[y0 * w + x1], tx);
            Color b = Color.Lerp(px[y1 * w + x0], px[y1 * w + x1], tx);
            return Color.Lerp(a, b, ty);
        }

        // -------------------------------------------------------
        // Math / noise helpers
        // -------------------------------------------------------

        private static float Rand1k(System.Random rng) => (float)rng.NextDouble() * 1000f;

        private static Vector2 Rotate(Vector2 v, float radians)
        {
            float c = Mathf.Cos(radians), sn = Mathf.Sin(radians);
            return new Vector2(v.x * c - v.y * sn, v.x * sn + v.y * c);
        }

        /** Nearest sample of a field buffer at a centered (-1..1) coordinate. */
        private static float SampleField(float[] field, int res, Vector2 c)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt((c.x * 0.5f + 0.5f) * res - 0.5f), 0, res - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt((c.y * 0.5f + 0.5f) * res - 0.5f), 0, res - 1);
            return field[y * res + x];
        }

        /** Random per-crystal colour jitter (channel-wise value wobble). */
        private static Color JitterColor(Color c, float variation, System.Random rng)
        {
            float j() => 1f + ((float)rng.NextDouble() - 0.5f) * 2f * variation;
            return new Color(Mathf.Clamp01(c.r * j()), Mathf.Clamp01(c.g * j()), Mathf.Clamp01(c.b * j()), 1f);
        }

        /** Cell-stable colour jitter (same cell → same colour) for druse terminations. */
        private static Color JitterColorHash(Color c, float variation, Vector2Int cell)
        {
            Vector2 hv = Hash2(cell.x, cell.y);
            float a = 1f + (hv.x - 0.5f) * 2f * variation;
            float b = 1f + (hv.y - 0.5f) * 2f * variation;
            return new Color(Mathf.Clamp01(c.r * a), Mathf.Clamp01(c.g * b), Mathf.Clamp01(c.b * (a + b) * 0.5f), 1f);
        }

        /** Hermite smoothstep returning 0 below edge0, 1 above edge1. */
        private static float Smooth01(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / Mathf.Max(edge1 - edge0, 1e-5f));
            return t * t * (3f - 2f * t);
        }

        /** Polynomial smooth-min (for merging cluster lobes without creases). */
        private static float SmoothMin(float a, float b, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / Mathf.Max(k, 1e-4f));
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
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

        /** Extended Worley sample: nearest (F1) + second (F2) distances, cell id and vector to the feature. */
        private struct WorleyData
        {
            public float f1, f2;
            public Vector2 toFeature;
            public Vector2Int cell;
        }

        private static WorleyData WorleySample(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            var d = new WorleyData { f1 = 1e5f, f2 = 1e5f };
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    Vector2 fp = Hash2(xi + dx, yi + dy);
                    float px = dx + fp.x - fx;
                    float py = dy + fp.y - fy;
                    float dist = Mathf.Sqrt(px * px + py * py);
                    if (dist < d.f1)
                    {
                        d.f2 = d.f1;
                        d.f1 = dist;
                        d.toFeature = new Vector2(px, py);
                        d.cell = new Vector2Int(xi + dx, yi + dy);
                    }
                    else if (dist < d.f2) d.f2 = dist;
                }
            }
            return d;
        }

        /** Deterministic 2D hash → a point in [0,1)x[0,1) for a given integer cell. */
        private static Vector2 Hash2(int x, int y)
        {
            float a = Mathf.Sin(x * 127.1f + y * 311.7f) * 43758.5453f;
            float b = Mathf.Sin(x * 269.5f + y * 183.3f) * 43758.5453f;
            return new Vector2(a - Mathf.Floor(a), b - Mathf.Floor(b));
        }

        /** Deterministic scalar hash in [0,1). */
        private static float HashF(int x, int y)
        {
            float a = Mathf.Sin(x * 157.31f + y * 113.97f) * 43758.5453f;
            return a - Mathf.Floor(a);
        }
    }
}
#endif
