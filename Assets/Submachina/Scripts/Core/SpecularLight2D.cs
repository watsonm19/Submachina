using UnityEngine;
using UnityEngine.Rendering.Universal;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Marks a Light2D as a driver of the SpriteLitSpecular glint. Lives on the light's
     * GameObject (e.g. the sub's turret spotlight).
     *
     * Instead of the CPU scanning for nearby sprites each tick and pushing per-instance
     * illumination, this component just registers itself with SpecularLight2DManager,
     * which packs a handful of lights into GLOBAL shader uniforms once per frame. The
     * SpriteLitSpecular shader then computes the glint per-pixel against the REAL light —
     * so the whole effect is GPU-side, per-frame exact (no lag), costs nothing per sprite,
     * and supports several lights at once (local multiplayer) up to the manager's cap.
     *
     * The cone/beam gate is taken straight from the Light2D's own inner/outer angles, so
     * the glint is confined to the visible beam by construction (no separate angle to
     * hand-match). Use coneScale only if the numeric URP angle differs from the beam you
     * see and you want to nudge it.
     */
    [RequireComponent(typeof(Light2D))]
    public class SpecularLight2D : MonoBehaviour
    {
        [Tooltip("Overall strength multiplier for this light's glint contribution.")]
        [SerializeField, Min(0f)] private float strength = 1f;

        [Tooltip("Use the Light2D's outer radius as the glint reach. Off = use the explicit range below.")]
        [SerializeField] private bool useLightRadius = true;

        [HideIf(nameof(useLightRadius))]
        [Tooltip("Glint reach in world units when not using the light radius.")]
        [SerializeField, Min(0f)] private float range = 6f;

        [Tooltip("Also scale the glint by the light's current intensity (dimming the light dims the glints).")]
        [SerializeField] private bool scaleByLightIntensity = false;

        // =====================
        // Cone (the beam)
        // =====================

        [BoxGroup("Cone"), Tooltip("Confine the glint to the beam arc. Off = omni-directional glint.")]
        [SerializeField] private bool useCone = true;

        [BoxGroup("Cone"), ShowIf(nameof(useCone))]
        [Tooltip("Beam axis in the light's local space. +Y matches URP's 2D spotlight direction; leave it unless your light is built differently.")]
        [SerializeField] private Vector2 aimAxisLocal = Vector2.up;

        [BoxGroup("Cone"), ShowIf(nameof(useCone))]
        [Tooltip("Fudge factor on the Light2D's inner/outer angles if the numeric cone doesn't match the visible beam. 1 = use the light's angles as-is.")]
        [SerializeField, Range(0.1f, 2f)] private float coneScale = 1f;

        private Light2D _light;

        /** Cache the light and hook this driver into the global manager. */
        private void Awake() => _light = GetComponent<Light2D>();

        private void OnEnable() => SpecularLight2DManager.Register(this);
        private void OnDisable() => SpecularLight2DManager.Unregister(this);

        /**
         * Packs this light's current state into the two float4s the shader loop expects:
         *   a = (posX, posY, reach, strength)
         *   b = (aimX, aimY, cos(outerHalf), cos(innerHalf))
         * Returns false (skip this slot) if the light has no usable reach.
         *
         * The cone is packed as cosines so the shader gate is a cheap dot() + smoothstep:
         * inside the inner cone cos is largest, outside the outer cone it's smaller, so
         * smoothstep(cosOuter, cosInner, dot(aim,dir)) fades 0..1 across the beam edge.
         */
        public bool TryPack(out Vector4 a, out Vector4 b)
        {
            a = default;
            b = default;
            if (_light == null) return false;

            // Reach: light outer radius or the explicit override.
            float reach = useLightRadius ? _light.pointLightOuterRadius : range;
            if (reach <= 0f) return false;

            // Strength, optionally scaled by the live light intensity.
            float str = strength * (scaleByLightIntensity ? Mathf.Max(0f, _light.intensity) : 1f);

            Vector2 pos = transform.position;
            a = new Vector4(pos.x, pos.y, reach, str);

            // Cone packed from the light's own angles (or fully open when the cone is off).
            Vector2 aim = ((Vector2)transform.TransformDirection(aimAxisLocal)).normalized;
            if (useCone)
            {
                float outerHalf = Mathf.Clamp(_light.pointLightOuterAngle * 0.5f * coneScale, 0f, 180f);
                float innerHalf = Mathf.Clamp(_light.pointLightInnerAngle * 0.5f * coneScale, 0f, outerHalf);
                float cosOuter = Mathf.Cos(outerHalf * Mathf.Deg2Rad);
                float cosInner = Mathf.Cos(innerHalf * Mathf.Deg2Rad);
                b = new Vector4(aim.x, aim.y, cosOuter, cosInner);
            }
            else
            {
                // edge0 = -2, edge1 = -1 → smoothstep returns 1 for every valid cos in [-1,1].
                b = new Vector4(aim.x, aim.y, -2f, -1f);
            }

            return true;
        }

        /** Draw the reach circle so the glint range is visible in the Scene view. */
        private void OnDrawGizmosSelected()
        {
            var lt = _light != null ? _light : GetComponent<Light2D>();
            float reach = useLightRadius && lt != null ? lt.pointLightOuterRadius : range;
            Gizmos.color = new Color(1f, 0.9f, 0.4f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, reach);
        }
    }
}
