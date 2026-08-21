using Sirenix.OdinInspector;
using UnityEngine;

namespace Core.ProceduralAnimation
{
    /**
     * Per-instance color overrides for ProcCreature materials — the creature
     * equivalent of SplineFillOverride. One shared material serves every
     * creature variant; each instance dials its own fill/outline/emission/flash
     * through a MaterialPropertyBlock, so nothing is instanced or leaked.
     *
     * Works in edit mode (ExecuteAlways) for instant palette iteration on
     * prefab instances. Only the toggled channels are written, and writes are
     * read-modify-write, so this composes with creature brains that animate
     * _FlashAmount/_EmissionColor at runtime (their per-frame writes preserve
     * these values, and an emission-animating brain like the jellyfish will
     * simply win on that one channel).
     *
     * Sits next to (or on a parent of) the MeshRenderers it drives — by default
     * it collects every MeshRenderer under itself, so one component on a creature
     * root can recolor body + tentacles at once.
     */
    [ExecuteAlways]
    public class ProcCreatureColorOverride : MonoBehaviour
    {
        // =====================
        // Targets
        // =====================

        [FoldoutGroup("Targets")]
        [Tooltip("Renderers to drive. Leave empty to auto-collect every MeshRenderer under this object (body strips, tentacles, blobs alike).")]
        [SerializeField] private Renderer[] targets;

        // =====================
        // Fill
        // =====================

        [FoldoutGroup("Fill")]
        [Tooltip("Override the material's fill color (_Color) on these instances.")]
        [SerializeField] private bool overrideFill;

        [FoldoutGroup("Fill")]
        [Tooltip("Per-instance fill color. Multiplies vertex colors like the material color does.")]
        [SerializeField, EnableIf(nameof(overrideFill))] private Color fillColor = Color.white;

        // =====================
        // Outline
        // =====================

        [FoldoutGroup("Outline")]
        [Tooltip("Override the outline color (_OutlineColor). Alpha is outline strength.")]
        [SerializeField] private bool overrideOutline;

        [FoldoutGroup("Outline")]
        [SerializeField, EnableIf(nameof(overrideOutline))] private Color outlineColor = Color.black;

        [FoldoutGroup("Outline")]
        [Tooltip("Also override the outline width in world units (_OutlineWidth). Negative = leave the material's width.")]
        [SerializeField, EnableIf(nameof(overrideOutline))] private float outlineWidth = -1f;

        // =====================
        // Emission
        // =====================

        [FoldoutGroup("Emission")]
        [Tooltip("Override the HDR emission (_EmissionColor). NOTE: brains that animate their glow (jellyfish bell, anglerfish lure) rewrite this every frame and will win.")]
        [SerializeField] private bool overrideEmission;

        [FoldoutGroup("Emission")]
        [SerializeField, EnableIf(nameof(overrideEmission)), ColorUsage(true, true)] private Color emissionColor = Color.black;

        // =====================
        // Flash
        // =====================

        [FoldoutGroup("Flash")]
        [Tooltip("Override the flash color (_FlashColor) — what hit flashes / chromatophore flickers tint toward on this instance.")]
        [SerializeField] private bool overrideFlashColor;

        [FoldoutGroup("Flash")]
        [SerializeField, EnableIf(nameof(overrideFlashColor))] private Color flashColor = Color.white;

        private MaterialPropertyBlock _mpb;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void OnEnable() => Apply();

        private void OnValidate() => Apply();

        private void OnDisable() => RestoreMaterialDefaults();

        // -------------------------------------------------------
        // Public API
        // -------------------------------------------------------

        /** Re-pushes the enabled overrides — call after changing values from code. */
        [FoldoutGroup("Targets")]
        [Button("Apply Now")]
        public void Apply()
        {
            _mpb ??= new MaterialPropertyBlock();

            foreach (Renderer r in ResolveTargets())
            {
                if (r == null) continue;

                // Read-modify-write so animated channels (_FlashAmount etc.) survive.
                r.GetPropertyBlock(_mpb);
                if (overrideFill) _mpb.SetColor(ColorId, fillColor);
                if (overrideOutline)
                {
                    _mpb.SetColor(OutlineColorId, outlineColor);
                    if (outlineWidth >= 0f) _mpb.SetFloat(OutlineWidthId, outlineWidth);
                }
                if (overrideEmission) _mpb.SetColor(EmissionColorId, emissionColor);
                if (overrideFlashColor) _mpb.SetColor(FlashColorId, flashColor);
                r.SetPropertyBlock(_mpb);
            }
        }

        /** Sets and applies the fill color from code (e.g. spawn-time variety). */
        public void SetFill(Color color)
        {
            overrideFill = true;
            fillColor = color;
            Apply();
        }

        // -------------------------------------------------------
        // Internals
        // -------------------------------------------------------

        private Renderer[] ResolveTargets()
        {
            if (targets != null && targets.Length > 0) return targets;
            return GetComponentsInChildren<MeshRenderer>(true);
        }

        /**
         * On disable, push the material's own values back into the block for the
         * channels we were overriding (a property block can't un-set a single
         * property, so restoring the shared material's value is the clean exit).
         */
        private void RestoreMaterialDefaults()
        {
            if (_mpb == null) return;

            foreach (Renderer r in ResolveTargets())
            {
                if (r == null || r.sharedMaterial == null) continue;
                Material m = r.sharedMaterial;

                r.GetPropertyBlock(_mpb);
                if (overrideFill && m.HasProperty(ColorId)) _mpb.SetColor(ColorId, m.GetColor(ColorId));
                if (overrideOutline && m.HasProperty(OutlineColorId))
                {
                    _mpb.SetColor(OutlineColorId, m.GetColor(OutlineColorId));
                    if (m.HasProperty(OutlineWidthId)) _mpb.SetFloat(OutlineWidthId, m.GetFloat(OutlineWidthId));
                }
                if (overrideEmission && m.HasProperty(EmissionColorId)) _mpb.SetColor(EmissionColorId, m.GetColor(EmissionColorId));
                if (overrideFlashColor && m.HasProperty(FlashColorId)) _mpb.SetColor(FlashColorId, m.GetColor(FlashColorId));
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
