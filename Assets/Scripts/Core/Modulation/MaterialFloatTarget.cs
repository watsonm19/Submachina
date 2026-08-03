using UnityEngine;

namespace Core.Modulation
{
    /**
     * Composited float target writing a shader property through a MaterialPropertyBlock,
     * so materials are never instantiated per-object (matches SpecularController's approach).
     */
    public class MaterialFloatTarget : ModulatedFloatTarget
    {
        [Header("Material")]
        [Tooltip("Renderer whose material property is driven. Defaults to a Renderer on this GameObject.")]
        [SerializeField] private Renderer targetRenderer;

        [Tooltip("Shader property name, e.g. _DarknessAmount.")]
        [SerializeField] private string propertyName = "_Intensity";

        private MaterialPropertyBlock _block;
        private int _propertyId;

        private void Awake()
        {
            if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
            _block = new MaterialPropertyBlock();
            _propertyId = Shader.PropertyToID(propertyName);
        }

        protected override void ApplyValue(float value)
        {
            if (targetRenderer == null) return;

            // Preserve any existing block contents, update just our float, and push back.
            targetRenderer.GetPropertyBlock(_block);
            _block.SetFloat(_propertyId, value);
            targetRenderer.SetPropertyBlock(_block);
        }
    }
}
