using UnityEngine;
using UnityEngine.Events;

namespace Core.Modulation
{
    /**
     * Composited destination for a float property so multiple systems never fight over it.
     * Channels: Baseline (EnvironmentDirector routes), Additive + Multiplier (Feel/feedback
     * effects like flickers and pulses), Override (scripted sequences).
     *
     *   final = overrideActive ? overrideValue : (baseline + additive) * multiplier
     *
     * Subclasses apply the final value to a concrete Unity property in ApplyValue().
     */
    public abstract class ModulatedFloatTarget : MonoBehaviour
    {
        [Header("Channels")]
        [Tooltip("Long-running environmental value, normally written by a FloatRoute.")]
        [SerializeField] private float baseline;

        [Tooltip("Temporary additive channel for feedback effects (flashes, boosts).")]
        [SerializeField] private float additive;

        [Tooltip("Temporary multiplicative channel for feedback effects (flickers, dips). 1 = neutral.")]
        [SerializeField] private float multiplier = 1f;

        [Header("Output")]
        [Tooltip("Final values are clamped into this range before being applied.")]
        [SerializeField] private Vector2 outputClamp = new Vector2(0f, float.MaxValue);

        [Tooltip("Skip applying when the final value moved less than this since the last apply.")]
        [SerializeField] private float epsilon = 0.0001f;

        [Tooltip("Raised whenever a new final value is applied — handy for chaining into other systems.")]
        public UnityEvent<float> onValueApplied;

        private bool _overrideActive;
        private float _overrideValue;
        private float _lastApplied = float.NaN;

        // Public channel accessors — Feel float controllers or UnityEvents can drive these directly.
        public float Baseline { get => baseline; set => baseline = value; }
        public float Additive { get => additive; set => additive = value; }
        public float Multiplier { get => multiplier; set => multiplier = value; }

        /// <summary>Current composited value (before clamping).</summary>
        public float FinalValue => _overrideActive ? _overrideValue : (baseline + additive) * multiplier;

        // UnityEvent-friendly setters.
        public void SetBaseline(float value) => baseline = value;
        public void SetAdditive(float value) => additive = value;
        public void SetMultiplier(float value) => multiplier = value;

        /// <summary>Forces the output to an exact value until ClearOverride() is called.</summary>
        public void SetOverride(float value)
        {
            _overrideActive = true;
            _overrideValue = value;
        }

        public void ClearOverride() => _overrideActive = false;

        // Apply after all writers (director routes in Update, Feel in Update) have had their say.
        protected virtual void LateUpdate()
        {
            float final = Mathf.Clamp(FinalValue, outputClamp.x, outputClamp.y);
            if (!float.IsNaN(_lastApplied) && Mathf.Abs(final - _lastApplied) < epsilon) return;
            _lastApplied = final;
            ApplyValue(final);
            onValueApplied?.Invoke(final);
        }

        /// <summary>Writes the composited value to the concrete destination property.</summary>
        protected abstract void ApplyValue(float value);
    }
}
