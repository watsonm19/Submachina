using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Events;

namespace Core.Rendering
{
    /**
     * Per-instance driver for the "Submachina/2D/SpriteBlur" material.
     *
     * Writes the blur properties through a MaterialPropertyBlock, so any number of sprites
     * can SHARE one blur material while each carries its own blur amount — no material
     * duplication, no shared-asset mutation. ExecuteAlways makes the sliders preview live
     * in the editor.
     *
     * Overall see-through-ness is NOT handled here — that's just the SpriteRenderer's
     * colour alpha, exactly like any other sprite (blurry glass = blur + tint alpha < 1).
     *
     * For animation (focus pulls, "surfacing from the deep" reveals, motion streaks that
     * track velocity), tween `Blur01` / `MotionAngle` from code/DOTween, or wire
     * OnBlurChanged to feedbacks.
     */
    [ExecuteAlways]
    [RequireComponent(typeof(SpriteRenderer))]
    public class SpriteBlur : MonoBehaviour
    {
        /** Mirror of the shader's _BlurMode enum. */
        public enum Mode
        {
            Gaussian = 0,   // out-of-focus disc blur
            Motion = 1,     // directional streak (Photoshop-style motion blur)
        }

        [FoldoutGroup("Blur")]
        [Tooltip("Gaussian = out-of-focus disc blur. Motion = directional streak along Motion Angle.")]
        [SerializeField, OnValueChanged(nameof(Apply))]
        private Mode mode = Mode.Gaussian;

        [FoldoutGroup("Blur")]
        [Tooltip("Blur radius in texels of the sprite texture (Motion: half-length of the streak). 0 = fully sharp.")]
        [SerializeField, Range(0f, 64f), OnValueChanged(nameof(Apply))]
        private float blurRadius = 8f;

        [FoldoutGroup("Blur")]
        [Tooltip("Texture taps per pixel. 16 is smooth up to ~12 texels of radius; for bigger blurs prefer Auto Mip / Mip Bias over adding taps.")]
        [SerializeField, Range(4, 48), OnValueChanged(nameof(Apply))]
        private int samples = 16;

        [FoldoutGroup("Blur")]
        [Tooltip("Motion mode only: streak direction in degrees (0 = horizontal, 90 = vertical).")]
        [SerializeField, Range(0f, 180f), OnValueChanged(nameof(Apply)), ShowIf(nameof(IsMotion))]
        private float motionAngle;

        [FoldoutGroup("Blur/Quality")]
        [Tooltip("Per-pixel noise jitter of the tap pattern — dissolves the 'cellular' under-sampling artifact into fine grain. 1 = full de-banding (recommended).")]
        [SerializeField, Range(0f, 1f), OnValueChanged(nameof(Apply))]
        private float patternNoise = 1f;

        [FoldoutGroup("Blur/Quality")]
        [Tooltip("Reads the texture from a higher mip — the cheap way to get huge, creamy blurs. Needs mipmaps enabled on the texture or it does nothing.")]
        [SerializeField, Range(0f, 6f), OnValueChanged(nameof(Apply))]
        private float mipBias;

        [FoldoutGroup("Blur/Quality")]
        [Tooltip("Auto-raise the mip so tap footprints cover the gaps between taps — kills the cellular look at any radius, at some extra softness. Needs mipmaps on the texture.")]
        [SerializeField, OnValueChanged(nameof(Apply))]
        private bool autoMip;

        [FoldoutGroup("Blur")]
        [Tooltip("Radius that Blur01 = 1 maps to, for normalized tweening (e.g. a focus pull driving Blur01 0..1).")]
        [SerializeField, Min(0f)]
        private float maxRadius = 24f;

        [FoldoutGroup("Events")]
        [Tooltip("Fired whenever the blur radius changes, with the new normalized 0..1 amount. Handy for tying particle/feedback intensity to how blurred the sprite is.")]
        public UnityEvent<float> OnBlurChanged;

        // Shader property ids, resolved once.
        private static readonly int BlurModeId = Shader.PropertyToID("_BlurMode");
        private static readonly int BlurRadiusId = Shader.PropertyToID("_BlurRadius");
        private static readonly int BlurSamplesId = Shader.PropertyToID("_BlurSamples");
        private static readonly int BlurAngleId = Shader.PropertyToID("_BlurAngle");
        private static readonly int BlurNoiseId = Shader.PropertyToID("_BlurNoise");
        private static readonly int BlurMipId = Shader.PropertyToID("_BlurMip");
        private static readonly int BlurAutoMipId = Shader.PropertyToID("_BlurAutoMip");

        private SpriteRenderer _renderer;
        private MaterialPropertyBlock _mpb;

        private bool IsMotion => mode == Mode.Motion;

        /** Which blur algorithm this sprite uses. Setting it re-applies immediately. */
        public Mode BlurMode
        {
            get => mode;
            set
            {
                mode = value;
                Apply();
            }
        }

        /** Blur radius in texels. Setting it re-applies immediately. */
        public float BlurRadius
        {
            get => blurRadius;
            set
            {
                blurRadius = Mathf.Clamp(value, 0f, 64f);
                Apply();
            }
        }

        /** Normalized blur (0 = sharp, 1 = maxRadius) — the convenient handle for tweens. */
        public float Blur01
        {
            get => maxRadius > 0f ? blurRadius / maxRadius : 0f;
            set => BlurRadius = Mathf.Lerp(0f, maxRadius, Mathf.Clamp01(value));
        }

        /** Motion streak direction in degrees — e.g. aim it along a rigidbody's velocity. */
        public float MotionAngle
        {
            get => motionAngle;
            set
            {
                motionAngle = Mathf.Repeat(value, 180f);
                Apply();
            }
        }

        private void OnEnable() => Apply();

        private void OnValidate() => Apply();

        /**
         * Pushes the current blur settings into this renderer's MaterialPropertyBlock.
         * Cheap enough to call every time a value changes; does nothing per-frame.
         */
        [FoldoutGroup("Blur"), Button("Re-Apply")]
        public void Apply()
        {
            // Lazy-init so OnValidate can run before OnEnable in the editor.
            if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
            if (_renderer == null) return;
            _mpb ??= new MaterialPropertyBlock();

            // Merge our properties into whatever else already drives this renderer's block.
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(BlurModeId, (float)mode);
            _mpb.SetFloat(BlurRadiusId, blurRadius);
            _mpb.SetFloat(BlurSamplesId, samples);
            _mpb.SetFloat(BlurAngleId, motionAngle);
            _mpb.SetFloat(BlurNoiseId, patternNoise);
            _mpb.SetFloat(BlurMipId, mipBias);
            _mpb.SetFloat(BlurAutoMipId, autoMip ? 1f : 0f);
            _renderer.SetPropertyBlock(_mpb);

            OnBlurChanged?.Invoke(Blur01);
        }
    }
}
