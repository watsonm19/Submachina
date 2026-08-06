using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Core.Rendering
{
    /**
     * Registry of scene lights the underwater effect treats as real illumination.
     *
     * DistortionLightSource components self-register here (OnEnable/OnDisable), and the
     * UnderwaterDistortionController reads the list each frame to upload the analytic
     * light pool — the same decoupled pattern as DistortionRippleBus: no scene refs,
     * so prefab-spawned submarines' spotlights just work.
     */
    public static class DistortionLightRegistry
    {
        /** Live sources, in registration order. The controller uploads up to its pool size. */
        public static readonly List<DistortionLightSource> Sources = new List<DistortionLightSource>();

        internal static void Register(DistortionLightSource source)
        {
            if (!Sources.Contains(source)) Sources.Add(source);
        }

        internal static void Unregister(DistortionLightSource source)
        {
            Sources.Remove(source);
        }
    }

    /**
     * Marks a Light2D (spot or point) as a light the underwater effect's darkness gating
     * should respect: features with a low Self Light setting become visible inside this
     * light's cone even when the global light is pitch black.
     *
     * Drop it next to any Light2D — position, radius, cone angles, intensity and color are
     * read from the light every frame, so animating the light animates the effect too.
     */
    [ExecuteAlways]
    [RequireComponent(typeof(Light2D))]
    public class DistortionLightSource : MonoBehaviour
    {
        [Tooltip("Extra multiplier on this light's contribution to the underwater scene-light estimate.")]
        [Range(0f, 4f)]
        public float intensityScale = 1f;

        [Tooltip("Scales the light's outer radius as seen by the underwater effect (widen/tighten the revealed area without touching the actual light).")]
        [Range(0.1f, 4f)]
        public float radiusScale = 1f;

        private Light2D _light;

        /** The Light2D this source mirrors (cached; RequireComponent guarantees it exists). */
        public Light2D Light
        {
            get
            {
                if (_light == null) _light = GetComponent<Light2D>();
                return _light;
            }
        }

        private void OnEnable() => DistortionLightRegistry.Register(this);
        private void OnDisable() => DistortionLightRegistry.Unregister(this);
    }
}
