using UnityEngine;
using UnityEngine.U2D;
using Sirenix.OdinInspector;

namespace Core.Rendering
{
    /**
     * `SpecularController` for SpriteShape terrain, with EXPLICIT per-submesh routing.
     *
     * Why this exists as its own component: a SpriteShapeRenderer draws two submeshes from
     * two different materials, and the shader's three textures reach them by completely
     * different routes —
     *
     *   EDGE submesh — stitched sprites along the spline. Unity binds each sprite's
     *     Secondary Textures, so `_NormalMap` and `_SpecMask` are whatever you wired up in
     *     the sprite's import settings. Works exactly like a plain SpriteRenderer.
     *
     *   FILL submesh — a raw Texture2D tiled from the SpriteShape Profile. A Texture2D has
     *     NO secondary-texture channel, so nothing ever binds `_NormalMap` or `_SpecMask`
     *     for the fill and it silently falls back to the shader's property defaults:
     *       _NormalMap = "bump"  → a FLAT normal (no relief, so the glint is a dull even lobe)
     *       _SpecMask  = "white" → FULLY shiny everywhere, untinted, at full strength
     *
     * That white mask default is the usual source of "the fill looks blown out / very
     * additive": it is unmasked while the edges are attenuated by their authored masks, and
     * — if the Glow Zone is on with a threshold <= 1 — its brightness of 1.0 puts the ENTIRE
     * fill inside the glow zone, which forces omnidirectional bias, the HDR glow gain, and a
     * pure-additive compose (Spec Screen and Spec Replace are deliberately faded out in the
     * zone). Use "Disable Glow Zone" on the fill below to opt it out.
     *
     * The base class writes ONE renderer-level property block that both submeshes read, so
     * out of the box the fill inherits every setting tuned against the edge sprites' textures
     * while having none of those textures. This component layers a per-material-index block
     * on top of whichever slots you enable, letting you give each submesh its own normal map,
     * spec mask and specular strength. Anything left un-overridden simply falls through to the
     * shared baseline on the base component, so you only fill in what actually differs.
     *
     * Nothing here needs shader support — it is all property-block binding.
     */
    public class SpriteShapeSpecularController : SpecularController
    {
        // Only needed locally (the base keeps this one private).
        private static readonly int GlowThresholdID = Shader.PropertyToID("_GlowThreshold");

        /**
         * Per-submesh override set. Every field is opt-in: leave a texture null or a scalar at
         * its neutral value and that aspect keeps whatever the shared baseline / the slot's own
         * material or sprite already provides.
         */
        [System.Serializable]
        public class SubmeshOverride
        {
            [Tooltip("Write a dedicated property block for this material slot. Off = this submesh " +
                     "just uses the shared baseline from the Specular Controller sections above.")]
            public bool enabled = false;

            [ShowIf(nameof(enabled)), Min(0)]
            [Tooltip("Which material slot on the SpriteShapeRenderer this is. On a default SpriteShape " +
                     "the fill is slot 0 and the edges are slot 1 — the Diagnostics readout confirms it.")]
            public int materialIndex = 0;

            // ---- Normal (what shapes the glint AND the 2D lighting relief) ----

            [ShowIf(nameof(enabled)), BoxGroup("Normal")]
            [Tooltip("Take this submesh's surface normal from this component instead of the shared " +
                     "Normal Source. Off = it follows the baseline Normal Source setting.")]
            public bool overrideNormal = false;

            [ShowIf("@enabled && overrideNormal"), BoxGroup("Normal")]
            [Tooltip("Normal mode for this submesh only. SpriteNormalMap reads the _NormalMap slot — " +
                     "which for EDGES is the sprite's secondary texture, and for the FILL is nothing " +
                     "at all unless you supply the map below.")]
            public NormalSource normalSource = NormalSource.SpriteNormalMap;

            [ShowIf("@enabled && overrideNormal && normalSource == Core.Rendering.SpecularController.NormalSource.SpriteNormalMap")]
            [BoxGroup("Normal")]
            [Tooltip("Normal map bound into the _NormalMap slot for this submesh — the same slot the " +
                     "edge sprites' secondary textures land in, so the submesh gets specular relief AND " +
                     "normal-mapped 2D diffuse lighting. Sampled with the submesh's own UVs, so a map " +
                     "authored as the pair of the fill texture (same tiling) lines up 1:1. Import it " +
                     "linear — sRGB OFF — or the decoded normals come out gamma-warped. " +
                     "Leave null to keep whatever is already bound (edge sprites keep their own).")]
            public Texture2D normalMap;

            [ShowIf("@enabled && overrideNormal"), BoxGroup("Normal"), Min(0f)]
            [Tooltip("Relief depth for this submesh's specular normal. 1 = as authored, >1 = deeper.")]
            public float normalStrength = 1f;

            // ---- Specular mask / strength (how shiny, and where) ----

            [ShowIf(nameof(enabled)), BoxGroup("Specular")]
            [Tooltip("Per-pixel specular mask (RGB tints AND scales all specular) for this submesh. " +
                     "Leave null to keep what's already bound: edge sprites use their _SpecMask " +
                     "secondary texture, the fill has none and defaults to solid WHITE = fully shiny " +
                     "everywhere. Use Spec Strength below for a flat 'how shiny' value instead of a map.")]
            public Texture2D specMask;

            [ShowIf(nameof(enabled)), BoxGroup("Specular"), Range(0f, 4f)]
            [Tooltip("Flat shininess for this submesh — multiplies its specular colour. This is the " +
                     "'plain spec mask value' knob: with no mask texture the shader's white default " +
                     "means full strength, and 0.3 here is mathematically identical to painting a " +
                     "uniform 30% grey mask. 1 = unchanged, 0 = this submesh never glints.")]
            public float specStrength = 1f;

            [ShowIf(nameof(enabled)), BoxGroup("Specular")]
            [Tooltip("Tints this submesh's specular colour (multiplied into the baseline Spec Color). " +
                     "White = unchanged. Lets the fill glint a different hue than the edge trim.")]
            public Color specTint = Color.white;

            [ShowIf(nameof(enabled)), BoxGroup("Specular")]
            [Tooltip("Force the Glow Zone off for this submesh. Strongly recommended on the FILL when " +
                     "the Glow Zone is enabled: with no spec mask the fill's mask brightness is a solid " +
                     "1.0, so the ENTIRE fill lands in the glow zone and renders omnidirectional, " +
                     "gain-boosted and forced-additive — the classic 'why is my fill blown out'.")]
            public bool disableGlowZone = false;
        }

        [FoldoutGroup("Fill submesh (tiled texture from the profile)", expanded: true)]
        [HideLabel, SerializeField]
        private SubmeshOverride fill = new SubmeshOverride { materialIndex = 0 };

        [FoldoutGroup("Edge submesh (stitched sprites along the spline)")]
        [HideLabel, SerializeField]
        private SubmeshOverride edge = new SubmeshOverride { materialIndex = 1 };

        [FoldoutGroup("SpriteShape Diagnostics", expanded: true)]
        [ShowInInspector, ReadOnly, MultiLineProperty(16), HideLabel]
        [Tooltip("What each material slot actually resolves to right now.")]
        private string SlotReport => BuildSlotReport();

#if UNITY_EDITOR
        /**
         * Re-imports every map assigned above as a tiling data texture: Wrap = Repeat (so it
         * tiles across the fill instead of smearing), mipmaps on (a 2k normal map viewed small
         * aliases badly into the specular otherwise), and — for normal maps — sRGB OFF, since
         * normals are vector data and gamma-decoding them warps the relief.
         */
        [FoldoutGroup("SpriteShape Diagnostics")]
        [Button("Fix Tiling Import Settings", ButtonSizes.Medium)]
        private void FixTilingImportSettings()
        {
            FixTexture(fill.normalMap, true);
            FixTexture(fill.specMask, false);
            FixTexture(edge.normalMap, true);
            FixTexture(edge.specMask, false);
            UnityEditor.AssetDatabase.Refresh();
        }

        /** Applies tiling-friendly import settings to one texture, if it needs them. */
        private static void FixTexture(Texture2D t, bool isNormalData)
        {
            if (t == null) return;

            string path = UnityEditor.AssetDatabase.GetAssetPath(t);
            var ti = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
            if (ti == null) return;

            // Skip the reimport entirely when nothing would change (it's not cheap on a 2k map).
            bool needsSrgb = isNormalData && ti.sRGBTexture;
            if (ti.wrapMode == TextureWrapMode.Repeat && ti.mipmapEnabled && !needsSrgb) return;

            ti.wrapMode = TextureWrapMode.Repeat;
            ti.mipmapEnabled = true;
            if (isNormalData) ti.sRGBTexture = false;
            ti.SaveAndReimport();
            Debug.Log($"[SpriteShapeSpecularController] Fixed tiling import settings on '{t.name}'.", t);
        }
#endif

        // -------------------------------------------------------
        // Per-submesh property blocks
        // -------------------------------------------------------

        /**
         * Applies the enabled submesh overrides. A per-material-index block REPLACES the
         * renderer-level block for that slot, so each one repeats the full shared baseline
         * first and then re-points only the properties this component owns. Disabled slots
         * get their block cleared so they fall back to the shared baseline cleanly.
         */
        protected override void ApplyRendererOverrides(Renderer r)
        {
            if (!(r is SpriteShapeRenderer)) return;

            ApplySubmesh(r, fill);
            ApplySubmesh(r, edge);
        }

        /** Writes (or clears) one submesh's per-material property block. */
        private void ApplySubmesh(Renderer r, SubmeshOverride o)
        {
            // Out-of-range slot: nothing to write, and nothing to clear either.
            var mats = r.sharedMaterials;
            if (o.materialIndex < 0 || o.materialIndex >= mats.Length) return;

            // Not overriding: drop any stale block so the renderer-level baseline rules again.
            if (!o.enabled) { r.SetPropertyBlock(null, o.materialIndex); return; }

            // Start from the complete shared baseline, then re-point what this submesh owns.
            r.GetPropertyBlock(Mpb, o.materialIndex);
            WriteBaselineProperties(Mpb, r);

            // Normal routing: mode first, then the map (only meaningful in SpriteNormalMap mode).
            if (o.overrideNormal)
            {
                Mpb.SetFloat(NormalModeID, (float)(int)o.normalSource);
                Mpb.SetFloat(NormalStrengthID, o.normalStrength);
                if (o.normalSource == NormalSource.SpriteNormalMap && o.normalMap != null)
                    Mpb.SetTexture(NormalMapID, o.normalMap);
            }

            // Specular mask texture — otherwise the slot keeps its sprite secondary texture
            // (edges) or the shader's white default (fill).
            if (o.specMask != null) Mpb.SetTexture(SpecMaskID, o.specMask);

            // Flat shininess + hue: fold into the spec colour, preserving alpha and HDR range.
            // e.g. specStrength 0.3 on the fill == a uniform 30% grey mask.
            Color baseCol = GetEffectiveSpecColor(r, o.materialIndex);
            Mpb.SetColor(SpecColorID, new Color(
                baseCol.r * o.specTint.r * o.specStrength,
                baseCol.g * o.specTint.g * o.specStrength,
                baseCol.b * o.specTint.b * o.specStrength,
                baseCol.a));

            // Threshold 2 is unreachable by a 0..1 mask, so the glow zone goes inert here.
            if (o.disableGlowZone) Mpb.SetFloat(GlowThresholdID, 2f);

            r.SetPropertyBlock(Mpb, o.materialIndex);
        }

        /**
         * Per-material blocks don't see the renderer-level `_SpecBoost` write, so re-stamp the
         * transient flare into each enabled slot or the fill/edge would sit out every Pulse().
         */
        protected override void WriteBoostOverrides(Renderer r, float boost)
        {
            if (!(r is SpriteShapeRenderer)) return;

            WriteSubmeshBoost(r, fill, boost);
            WriteSubmeshBoost(r, edge, boost);
        }

        /** Stamps just `_SpecBoost` into one submesh's block, leaving the rest of it intact. */
        private void WriteSubmeshBoost(Renderer r, SubmeshOverride o, float boost)
        {
            if (!o.enabled) return;
            if (o.materialIndex < 0 || o.materialIndex >= r.sharedMaterials.Length) return;

            r.GetPropertyBlock(Mpb, o.materialIndex);
            Mpb.SetFloat(SpecBoostID, boost);
            r.SetPropertyBlock(Mpb, o.materialIndex);
        }

        // -------------------------------------------------------
        // Diagnostics
        // -------------------------------------------------------

        /**
         * Human-readable dump of what each material slot resolves to: its material and shader,
         * where its three textures come from, and which of them this component is overriding.
         * Exists because the fill/edge split is the single most confusing thing about putting
         * a specular material on a SpriteShape — this makes it inspectable instead of guessed.
         */
        private string BuildSlotReport()
        {
            var ssr = GetComponentInChildren<SpriteShapeRenderer>(true);
            if (ssr == null) return "No SpriteShapeRenderer found — this component only adds value on SpriteShape terrain.\nUse the plain SpecularController for a SpriteRenderer.";

            var mats = ssr.sharedMaterials;
            string s = BuildWarnings(ssr) + $"SpriteShapeRenderer '{ssr.name}' — {mats.Length} material slot(s)\n";

            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                // Which override (if any) claims this slot.
                string owner = fill.enabled && fill.materialIndex == i ? "FILL override"
                             : edge.enabled && edge.materialIndex == i ? "EDGE override"
                             : "shared baseline";

                s += $"\n[{i}] {(m == null ? "<no material>" : m.name)}  ({owner})";
                if (m == null) continue;

                s += $"\n     shader : {m.shader.name}";
                // NOTE: this reads the SHARED material. The SpriteShapeRenderer binds the fill
                // texture (and the edge atlas) at draw time, so _MainTex reads as the shader
                // default here even though it is populated on screen — say so rather than mislead.
                s += $"\n     _MainTex   : {TexName(m, "_MainTex")}  (bound at draw time from the SpriteShape Profile)";
                s += $"\n     _NormalMap : {TexName(m, "_NormalMap")}{NormalNote(m, i)}";
                s += $"\n     _SpecMask  : {TexName(m, "_SpecMask")}{MaskNote(m, i)}";
            }

            return s;
        }

        /**
         * Up-front warnings for the mistakes that silently ruin a SpriteShape fill.
         *
         * The big one is WRAP MODE. SpriteShape ALWAYS generates fill UVs that run outside
         * 0..1 — world-space UVs are worldPos/(texSize/fillPPU), and local UVs are the shape
         * bounds scaled by the fill scale — so a map imported as Clamp shows its real content
         * only in the single patch where uv lands in 0..1 and smears edge texels across all
         * the rest. That reads as "one part looks right, everything else is stretched", or, if
         * the shape is large enough that no part lands in range, as no visible relief at all.
         * Repeat is the only correct setting for a tiling fill map.
         */
        private string BuildWarnings(SpriteShapeRenderer ssr)
        {
            string w = "";

            w += WrapWarning(fill.overrideNormal ? fill.normalMap : null, "Fill normal map");
            w += WrapWarning(fill.specMask, "Fill spec mask");
            w += WrapWarning(edge.overrideNormal ? edge.normalMap : null, "Edge normal map");
            w += WrapWarning(edge.specMask, "Edge spec mask");

            // Local (non-world) fill UVs normalize X and Y by the bounding box SEPARATELY, so a
            // square texture is squashed to the shape's aspect ratio and shifts whenever the
            // spline is edited. Fine for a bespoke stretched backdrop, wrong for a seamless tile.
            var ctrl = ssr.GetComponent<SpriteShapeController>();
            if (ctrl != null && !ctrl.worldSpaceUVs && fill.enabled && fill.overrideNormal && fill.normalMap != null)
            {
                Vector3 b = ssr.bounds.size;
                float aspect = b.y > 1e-4f ? b.x / b.y : 1f;
                if (aspect < 0.6f || aspect > 1.7f)
                    w += $"! World Space UVs are OFF, so fill UVs are normalized to the bounding box per-axis — " +
                         $"this shape is {aspect:0.0}:1, so the normal map is stretched by that much and will shift " +
                         $"as you edit the spline. Turn World Space UVs ON for a seamless tiling map.\n";
            }

            return w.Length == 0 ? "" : w + "\n";
        }

        /** Flags a tiling map imported with a non-Repeat wrap mode (see BuildWarnings). */
        private static string WrapWarning(Texture2D t, string label)
        {
            if (t == null || t.wrapMode == TextureWrapMode.Repeat) return "";
            return $"! {label} '{t.name}' has Wrap Mode = {t.wrapMode}. SpriteShape fill UVs go outside 0..1, " +
                   $"so it will smear instead of tiling. Set it to Repeat.\n";
        }

        /** Name of a texture bound on the shared material, or a marker for the shader default. */
        private static string TexName(Material m, string prop)
        {
            if (!m.HasProperty(prop)) return "<not on this shader>";
            var t = m.GetTexture(prop);
            return t == null ? "<shader default>" : t.name;
        }

        /** Flags a slot whose normal is flat with no override supplying one. */
        private string NormalNote(Material m, int slot)
        {
            bool overridden = (fill.enabled && fill.materialIndex == slot && fill.overrideNormal && fill.normalMap != null)
                           || (edge.enabled && edge.materialIndex == slot && edge.overrideNormal && edge.normalMap != null);
            if (overridden) return "  → replaced by this component";
            if (m.HasProperty("_NormalMap") && m.GetTexture("_NormalMap") == null)
                return "  (flat 'bump' unless a sprite secondary texture binds one — the FILL never does)";
            return "";
        }

        /** Flags the white-mask trap that makes an unmasked fill read as blown out. */
        private string MaskNote(Material m, int slot)
        {
            bool overridden = (fill.enabled && fill.materialIndex == slot && fill.specMask != null)
                           || (edge.enabled && edge.materialIndex == slot && edge.specMask != null);
            if (overridden) return "  → replaced by this component";
            if (m.HasProperty("_SpecMask") && m.GetTexture("_SpecMask") == null)
                return "  (solid WHITE = fully shiny; also puts the whole slot in the Glow Zone if enabled)";
            return "";
        }
    }
}
