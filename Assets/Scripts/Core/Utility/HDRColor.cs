using UnityEngine;
using UnityEngine.UI;
using Sirenix.OdinInspector;

namespace Utility
{
    /// <summary>
    /// Applies an HDR color to a SpriteRenderer or UI Image, enabling glow effects
    /// when paired with bloom post-processing. Unity's default inspectors don't expose
    /// HDR color picking for these components, so this bridges that gap.
    /// Auto-detects which renderer type is present on the GameObject.
    ///
    /// For SpriteRenderers, sets color directly (HDR passes through).
    /// For Images, creates a material instance and sets _Color on the material,
    /// because Image.color is passed as vertex color which gets clamped by most shaders.
    /// </summary>
    public class HDRColor : MonoBehaviour
    {
        [TitleGroup("HDR Color")]
        [Tooltip("HDR color applied to the renderer. Values above 1.0 will bloom/glow when bloom post-processing is active.")]
        [ColorUsage(true, true)]
        [SerializeField]
        Color glowColor = Color.white;

        [TitleGroup("HDR Color")]
        [Tooltip("Additional intensity multiplier applied on top of the HDR color")]
        [Range(0f, 10f)]
        [SerializeField]
        float intensityMultiplier = 1f;

        // Cached renderer references — only one will be non-null
        SpriteRenderer spriteRenderer;
        Image image;

        // Material instance for Image (needed to set HDR color via material property)
        Material imageMaterialInstance;

        static readonly int ColorID = Shader.PropertyToID("_Color");

        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly]
        string TargetType => spriteRenderer != null ? "SpriteRenderer" : image != null ? "Image" : "None";

        void Awake()
        {
            CacheRenderer();
            ApplyColor();
        }

        void OnDestroy()
        {
            // Clean up the material instance to avoid leaks
            if (imageMaterialInstance != null)
            {
                if (Application.isPlaying)
                    Destroy(imageMaterialInstance);
                else
                    DestroyImmediate(imageMaterialInstance);
            }
        }

        void OnValidate()
        {
            CacheRenderer();
            ApplyColor();
        }

        /** Finds the SpriteRenderer or Image on this GameObject and caches it. */
        void CacheRenderer()
        {
            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();

            if (image == null)
                image = GetComponent<Image>();
        }

        /**
         * Applies the HDR color scaled by intensity to whichever renderer is present.
         * SpriteRenderer: sets color directly (supports HDR natively).
         * Image: creates a material instance and sets _Color property,
         * bypassing the vertex color path that clamps HDR values.
         */
        [TitleGroup("HDR Color")]
        [Button("Apply Now")]
        public void ApplyColor()
        {
            Color finalColor = glowColor * intensityMultiplier;

            // SpriteRenderer handles HDR color directly
            if (spriteRenderer != null)
                spriteRenderer.color = finalColor;

            // Image needs a material instance with _Color set on the material
            if (image != null)
                ApplyColorToImage(finalColor);
        }

        /** Creates a material instance from the Image's current material and sets _Color. */
        void ApplyColorToImage(Color finalColor)
        {
            if (image.material == null) return;

            // Create a material instance if we haven't yet, or if the source material changed
            if (imageMaterialInstance == null)
            {
                imageMaterialInstance = new Material(image.material);
                image.material = imageMaterialInstance;
            }

            // Set HDR color on the material itself
            if (imageMaterialInstance.HasProperty(ColorID))
                imageMaterialInstance.SetColor(ColorID, finalColor);

            // Keep Image.color white so it doesn't double-tint via vertex color
            image.color = Color.white;
        }

        /** Sets the glow color at runtime and applies it immediately. */
        public void SetGlowColor(Color color)
        {
            glowColor = color;
            ApplyColor();
        }

        /** Sets the intensity multiplier at runtime and applies it immediately. */
        public void SetIntensity(float intensity)
        {
            intensityMultiplier = intensity;
            ApplyColor();
        }
    }
}
