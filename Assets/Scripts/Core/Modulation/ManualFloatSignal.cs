using UnityEngine;

namespace Core.Modulation
{
    /**
     * Inspector-slider signal for development and testing. Lets the rest of the
     * modulation chain be exercised without any live gameplay input.
     */
    public class ManualFloatSignal : FloatSignal
    {
        [Header("Manual Value")]
        [Tooltip("Value reported by this signal — drag during play mode to test downstream parameters.")]
        [SerializeField] private float value;

        [Tooltip("Slider range used purely for inspector convenience.")]
        [SerializeField] private Vector2 range = new Vector2(0f, 1f);

        public override float Value => value;

        /// <summary>Sets the manual value from code or UnityEvents (clamped to the configured range).</summary>
        public void SetValue(float newValue) => value = Mathf.Clamp(newValue, range.x, range.y);

        // Keep the serialized value inside its advertised range when edited.
        private void OnValidate() => value = Mathf.Clamp(value, range.x, range.y);
    }
}
