using UnityEngine;
using Sirenix.OdinInspector;

namespace Core.Rendering
{
    /**
     * Per-object EDGE BAND look for a generated mesh sharing one Mesh2DLitSpecular
     * material — the small, dedicated sibling of SpecularController (which owns the
     * surface/specular look and the mesh texture set). Adding this component IS the
     * opt-in: while enabled it writes the edge set into the renderer's property block;
     * remove it (and clear the block) to fall back to the material.
     *
     * Only meaningful on meshes that bake the TEXCOORD1 edge contract (spline fills,
     * creature bodies) — the effects read the baked edge distance/direction, which
     * sprites don't have.
     *
     * Compose-safe with SpecularController and the creature MPB drivers: everyone
     * Gets the renderer's current block, adds their own properties, and Sets it back.
     */
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public class EdgeBandOverride : MonoBehaviour
    {
        // Cached shader property ids (the Mesh2DLitSpecular edge-band slots).
        private static readonly int EdgeColorID = Shader.PropertyToID("_EdgeColor");
        private static readonly int EdgeDarkenID = Shader.PropertyToID("_EdgeDarken");
        private static readonly int EdgeWidthID = Shader.PropertyToID("_EdgeWidth");
        private static readonly int EdgeFalloffID = Shader.PropertyToID("_EdgeFalloff");
        private static readonly int EdgeAlphaFadeID = Shader.PropertyToID("_EdgeAlphaFade");
        private static readonly int EdgeBevelID = Shader.PropertyToID("_EdgeBevel");

        [Tooltip("Colour the rim multiplies toward (black = the Photoshop inner-glow-multiply look).")]
        [SerializeField] private Color edgeColor = Color.black;

        [Range(0f, 1f)]
        [Tooltip("Strength of the multiply darkening at the rim.")]
        [SerializeField] private float edgeDarken = 0.8f;

        [Range(0.01f, 8f)]
        [Tooltip("How much of the mesh's baked band the effects span (1 = the full band width). " +
                 "Above 1 the effect spills past the band as a flat plateau over the interior — " +
                 "for a genuinely deeper gradient raise the builder's Edge Band Width instead.")]
        [SerializeField] private float edgeWidth = 1f;

        [Range(0.25f, 8f)]
        [Tooltip("Falloff exponent across the band — higher hugs the rim tighter.")]
        [SerializeField] private float edgeFalloff = 2f;

        [Range(0f, 1f)]
        [Tooltip("Fade the rim toward transparent so the shape melts into the background.")]
        [SerializeField] private float edgeAlphaFade = 0f;

        [Range(0f, 4f)]
        [Tooltip("Bevel-normal strength — rounds the edge under Light2Ds and specular lights. " +
                 "For a full 3D form with detail riding it, prefer SpecularController's Form Shape.")]
        [SerializeField] private float edgeBevel = 1.5f;

        private MaterialPropertyBlock _mpb;

        /** Re-apply whenever the object wakes or values change in the inspector. */
        private void OnEnable() => Apply();
        private void OnValidate() => Apply();

        /** Writes the edge set into the renderer's property block (read-modify-write). */
        [Button]
        public void Apply()
        {
            var r = GetComponent<Renderer>();
            if (r == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();

            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(EdgeColorID, edgeColor);
            _mpb.SetFloat(EdgeDarkenID, edgeDarken);
            _mpb.SetFloat(EdgeWidthID, edgeWidth);
            _mpb.SetFloat(EdgeFalloffID, edgeFalloff);
            _mpb.SetFloat(EdgeAlphaFadeID, edgeAlphaFade);
            _mpb.SetFloat(EdgeBevelID, edgeBevel);
            r.SetPropertyBlock(_mpb);
        }

        /**
         * Rescue hatch: nukes the renderer's whole property block (they live on the
         * native renderer and can outlive components), then rewrites this component's
         * set fresh. Nudge any SpecularController on the same renderer afterwards so
         * it re-applies its baseline.
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
