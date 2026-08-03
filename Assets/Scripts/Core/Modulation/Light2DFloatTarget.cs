using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Core.Modulation
{
    /**
     * Composited float target driving a Light2D's intensity. The EnvironmentDirector
     * writes the environmental baseline while flickers/feedbacks use the multiplier
     * or additive channels — nobody writes Light2D.intensity directly.
     */
    public class Light2DFloatTarget : ModulatedFloatTarget
    {
        [Header("Light")]
        [Tooltip("Light to drive. Defaults to a Light2D on this GameObject.")]
        [SerializeField] private Light2D light2D;

        private void Awake()
        {
            if (light2D == null) light2D = GetComponent<Light2D>();
        }

        protected override void ApplyValue(float value)
        {
            if (light2D != null) light2D.intensity = value;
        }
    }
}
