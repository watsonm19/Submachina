using UnityEngine;
using Sirenix.OdinInspector;

namespace Core.Rendering
{
    /**
     * Per-object overrides for a spline-fill renderer sharing ONE SplineFillLitSpecular
     * material, via MaterialPropertyBlock: texture set, fill tiling/offset, the edge-band
     * look, and a whole-object tint. One material serves every level piece; each object
     * dials in its own appearance here.
     *
     * Performance: this costs nothing extra in this project. Property blocks are already
     * the per-instance pattern here (SpecularController writes one to every specular
     * renderer at spawn), and a renderer with ANY property block is off the SRP Batcher
     * fast path regardless — more properties in the same block change nothing.
     *
     * Compose-safe with SpecularController: both components Get the renderer's current
     * block, add their own properties, and Set it back, so neither clobbers the other.
     *
     * The full faked-3D recipe for a spline fill: assign the albedo + a tiled detail
     * normal map HERE, then add a SpecularController on the same object and pick a
     * Form Shape — on spline-fill meshes the form uses the baked edge band, so the
     * whole piece domes/bevels/pillows up from its own outline with the tiled detail
     * relief riding the curved form (Reoriented Normal Mapping in the shader). The
     * form dials live on SpecularController ON PURPOSE — one owner for the _Shape*
     * properties keeps the two components from fighting over the block.
     *
     * Note: property blocks can't remove single properties — to UNDO an override at edit
     * time, set the field back to the material's value (or clear the renderer's block).
     */
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public class SplineFillOverride : MonoBehaviour
    {
        // Cached shader property ids (the SplineFillLitSpecular slots).
        private static readonly int MainTexID = Shader.PropertyToID("_MainTex");
        private static readonly int NormalMapID = Shader.PropertyToID("_NormalMap");
        private static readonly int SpecMaskID = Shader.PropertyToID("_SpecMask");
        private static readonly int MainTexSTID = Shader.PropertyToID("_MainTex_ST");
        private static readonly int NormalMapSTID = Shader.PropertyToID("_NormalMap_ST");
        private static readonly int SpecMaskSTID = Shader.PropertyToID("_SpecMask_ST");
        private static readonly int NormalMapOnceID = Shader.PropertyToID("_NormalMapOnce");
        private static readonly int SpecMaskOnceID = Shader.PropertyToID("_SpecMaskOnce");
        private static readonly int SpecMaskOnceBgID = Shader.PropertyToID("_SpecMaskOnceBg");
        private static readonly int ColorID = Shader.PropertyToID("_Color");
        private static readonly int EdgeColorID = Shader.PropertyToID("_EdgeColor");
        private static readonly int EdgeDarkenID = Shader.PropertyToID("_EdgeDarken");
        private static readonly int EdgeWidthID = Shader.PropertyToID("_EdgeWidth");
        private static readonly int EdgeFalloffID = Shader.PropertyToID("_EdgeFalloff");
        private static readonly int EdgeAlphaFadeID = Shader.PropertyToID("_EdgeAlphaFade");
        private static readonly int EdgeBevelID = Shader.PropertyToID("_EdgeBevel");

        // =====================
        // Tint (the SpriteRenderer.color equivalent — always applied)
        // =====================

        [FoldoutGroup("Tint")]
        [Tooltip("Whole-object multiply tint, exactly like SpriteRenderer.color: white = unchanged, " +
                 "grey darkens everything evenly, colours colorize. Alpha fades the whole object. " +
                 "Note the specular glint colour is separate — dim it in step via SpecularController's " +
                 "tint (ApplyTint) if the piece should also glint darker.")]
        [SerializeField] private Color tint = Color.white;

        // =====================
        // Texture set
        // =====================

        [FoldoutGroup("Textures")]
        [Tooltip("Tiling albedo for this object. Empty = keep the material's texture.")]
        [SerializeField] private Texture2D albedo;

        [FoldoutGroup("Textures")]
        [Tooltip("Tiling normal map (straight RGB, sRGB off). Empty = keep the material's texture.")]
        [SerializeField] private Texture2D normalMap;

        [FoldoutGroup("Textures")]
        [Tooltip("Tiling specular mask (RGB tint × strength). Empty = keep the material's texture.")]
        [SerializeField] private Texture2D specMask;

        // =====================
        // Fill UV transform
        // =====================

        [FoldoutGroup("Tiling")]
        [Tooltip("Also override the fill's UV tiling/offset for this object (all three textures " +
                 "move together — they're a matched set). Off = keep the material's Tiling/Offset.")]
        [SerializeField] private bool overrideTiling = false;

        [FoldoutGroup("Tiling"), ShowIf(nameof(overrideTiling))]
        [Tooltip("UV scale on top of the mesh's baked tiles-per-unit (2 = texture repeats twice as often).")]
        [SerializeField] private Vector2 tiling = Vector2.one;

        [FoldoutGroup("Tiling"), ShowIf(nameof(overrideTiling))]
        [Tooltip("UV offset — slides the pattern across the shape (handy to de-sync two neighbours " +
                 "using the same texture set with local-space UVs).")]
        [SerializeField] private Vector2 offset = Vector2.zero;

        // =====================
        // Per-map UV transforms (RELATIVE to the fill UV — (1,1)/(0,0) follows the fill
        // exactly). Use these to fit a reused graphic that doesn't match the fill set,
        // e.g. a decal-sized spec mask positioned as one glowy spot.
        // =====================

        [FoldoutGroup("Normal Map UVs")]
        [Tooltip("Give the normal map its own tiling/offset, relative to the fill UV. " +
                 "Off = the normal map follows the fill (and any Tiling override above) exactly.")]
        [SerializeField] private bool overrideNormalMapUVs = false;

        [FoldoutGroup("Normal Map UVs"), ShowIf(nameof(overrideNormalMapUVs))]
        [Tooltip("Tiling relative to the fill UV (values < 1 enlarge the map's features).")]
        [SerializeField] private Vector2 normalMapTiling = Vector2.one;

        [FoldoutGroup("Normal Map UVs"), ShowIf(nameof(overrideNormalMapUVs))]
        [Tooltip("Offset relative to the fill UV — slides the map across the shape.")]
        [SerializeField] private Vector2 normalMapOffset = Vector2.zero;

        [FoldoutGroup("Normal Map UVs"), ShowIf(nameof(overrideNormalMapUVs))]
        [Tooltip("Place the map ONCE at its window instead of tiling — outside it the surface " +
                 "reads as flat. Works regardless of the texture's import wrap mode.")]
        [SerializeField] private bool normalMapStampOnce = false;

        [FoldoutGroup("Spec Mask UVs")]
        [Tooltip("Give the spec mask its own tiling/offset, relative to the fill UV. " +
                 "Off = the mask follows the fill (and any Tiling override above) exactly.")]
        [SerializeField] private bool overrideSpecMaskUVs = false;

        [FoldoutGroup("Spec Mask UVs"), ShowIf(nameof(overrideSpecMaskUVs))]
        [Tooltip("Tiling relative to the fill UV (values < 1 enlarge the mask — a small stamp " +
                 "graphic usually wants well below 1 so it covers a readable area).")]
        [SerializeField] private Vector2 specMaskTiling = Vector2.one;

        [FoldoutGroup("Spec Mask UVs"), ShowIf(nameof(overrideSpecMaskUVs))]
        [Tooltip("Offset relative to the fill UV — positions the mask (e.g. put the glowy blob " +
                 "exactly where you want it on this shape).")]
        [SerializeField] private Vector2 specMaskOffset = Vector2.zero;

        [FoldoutGroup("Spec Mask UVs"), ShowIf(nameof(overrideSpecMaskUVs))]
        [Tooltip("Place the mask ONCE at its window instead of tiling. The one-glowy-spot switch.")]
        [SerializeField] private bool specMaskStampOnce = false;

        [FoldoutGroup("Spec Mask UVs"), ShowIf(nameof(specMaskStampOnce))]
        [Tooltip("What the mask reads as OUTSIDE the stamp window — match the stamp graphic's " +
                 "border so the seam disappears. White = neutral (full spec) elsewhere; black = " +
                 "dull everywhere except the stamp (the classic glowy-spot setup); grey = dimmed.")]
        [SerializeField] private Color specMaskStampBackground = Color.white;

        // =====================
        // Edge band (only meaningful on SplineFillMeshBuilder meshes — the effects read
        // the edge distance/direction the builder bakes into TEXCOORD1, which sprites
        // don't have; sprite edge treatment stays an art/SpecularController concern)
        // =====================

        [FoldoutGroup("Edge Band")]
        [Tooltip("Override the material's edge-band look for this object. Off = keep the material's values.")]
        [SerializeField] private bool overrideEdge = false;

        [FoldoutGroup("Edge Band"), ShowIf(nameof(overrideEdge))]
        [Tooltip("Colour the rim multiplies toward (black = the Photoshop inner-glow-multiply look).")]
        [SerializeField] private Color edgeColor = Color.black;

        [FoldoutGroup("Edge Band"), ShowIf(nameof(overrideEdge)), Range(0f, 1f)]
        [Tooltip("Strength of the multiply darkening at the rim.")]
        [SerializeField] private float edgeDarken = 0.8f;

        [FoldoutGroup("Edge Band"), ShowIf(nameof(overrideEdge)), Range(0.01f, 8f)]
        [Tooltip("How much of the mesh's baked band the effects span (1 = the full band width). " +
                 "Above 1 the effect spills past the band as a flat plateau over the interior — " +
                 "the mesh has no edge-distance data deeper than its band, so for a genuinely " +
                 "deeper GRADIENT raise Edge Band Width on the SplineFillMeshBuilder instead " +
                 "(and re-bake if the mesh was baked).")]
        [SerializeField] private float edgeWidth = 1f;

        [FoldoutGroup("Edge Band"), ShowIf(nameof(overrideEdge)), Range(0.25f, 8f)]
        [Tooltip("Falloff exponent across the band — higher hugs the rim tighter.")]
        [SerializeField] private float edgeFalloff = 2f;

        [FoldoutGroup("Edge Band"), ShowIf(nameof(overrideEdge)), Range(0f, 1f)]
        [Tooltip("Fade the rim toward transparent so the shape melts into the background.")]
        [SerializeField] private float edgeAlphaFade = 0f;

        [FoldoutGroup("Edge Band"), ShowIf(nameof(overrideEdge)), Range(0f, 4f)]
        [Tooltip("Bevel-normal strength — rounds the edge under Light2Ds and specular lights.")]
        [SerializeField] private float edgeBevel = 1.5f;

        private MaterialPropertyBlock _mpb;

        /** Re-apply whenever the object wakes or values change in the inspector. */
        private void OnEnable() => Apply();
        private void OnValidate() => Apply();

        /**
         * Writes the overrides into the renderer's property block. Texture slots left empty
         * and toggled-off groups are untouched, so the shared material shows through.
         */
        [Button]
        public void Apply()
        {
            var r = GetComponent<Renderer>();
            if (r == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();

            r.GetPropertyBlock(_mpb);

            // Whole-object tint (multiplies albedo + alpha, exactly like SpriteRenderer.color).
            _mpb.SetColor(ColorID, tint);

            // Texture set — unassigned slots keep the material's textures.
            if (albedo != null) _mpb.SetTexture(MainTexID, albedo);
            if (normalMap != null) _mpb.SetTexture(NormalMapID, normalMap);
            if (specMask != null) _mpb.SetTexture(SpecMaskID, specMask);

            // Fill UV transform (_MainTex_ST is the BASE transform all maps ride on).
            if (overrideTiling) _mpb.SetVector(MainTexSTID, new Vector4(tiling.x, tiling.y, offset.x, offset.y));

            // Per-map UV transforms, relative to the fill UV, plus their stamp-once windows.
            if (overrideNormalMapUVs)
            {
                _mpb.SetVector(NormalMapSTID, new Vector4(normalMapTiling.x, normalMapTiling.y, normalMapOffset.x, normalMapOffset.y));
                _mpb.SetFloat(NormalMapOnceID, normalMapStampOnce ? 1f : 0f);
            }
            if (overrideSpecMaskUVs)
            {
                _mpb.SetVector(SpecMaskSTID, new Vector4(specMaskTiling.x, specMaskTiling.y, specMaskOffset.x, specMaskOffset.y));
                _mpb.SetFloat(SpecMaskOnceID, specMaskStampOnce ? 1f : 0f);
                _mpb.SetColor(SpecMaskOnceBgID, specMaskStampBackground);
            }

            // Edge band look.
            if (overrideEdge)
            {
                _mpb.SetColor(EdgeColorID, edgeColor);
                _mpb.SetFloat(EdgeDarkenID, edgeDarken);
                _mpb.SetFloat(EdgeWidthID, edgeWidth);
                _mpb.SetFloat(EdgeFalloffID, edgeFalloff);
                _mpb.SetFloat(EdgeAlphaFadeID, edgeAlphaFade);
                _mpb.SetFloat(EdgeBevelID, edgeBevel);
            }

            r.SetPropertyBlock(_mpb);
        }

        /**
         * Rescue hatch: nukes the renderer's property block entirely, then rewrites this
         * component's overrides fresh. Property blocks live on the NATIVE renderer — they
         * survive component removal, domain reloads, and scene tinkering — so a fill can
         * end up stuck with values no component claims (classic symptom: material edits
         * stop having any visible effect). If a SpecularController shares this renderer,
         * nudge it afterwards (toggle it or edit any field) so it re-applies its baseline.
         */
        [Button(ButtonSizes.Medium)]
        public void ClearStaleBlockAndReapply()
        {
            var r = GetComponent<Renderer>();
            if (r == null) return;
            r.SetPropertyBlock(null);
            Apply();
        }
    }
}
