using UnityEngine;
using UnityEngine.Events;

namespace Core.Modulation
{
    /**
     * Composited destination for a float property so multiple systems never fight over it.
     * Channels: Baseline (environmental value), Additive + Multiplier (Feel/feedback effects
     * like flickers and pulses), Override (scripted sequences).
     *
     *   final = overrideActive ? overrideValue : (baseline + additive) * multiplier
     *
     * The Baseline is usually driven by the built-in parameter binding: assign a parameter and
     * the target maps it through inputRange → responseCurve → outputRange every frame. Exactly
     * one binding exists per target, so a destination can never be double-driven. Leave the
     * parameter empty to drive Baseline externally (SetBaseline / UnityEvents) instead.
     *
     * Subclasses apply the final value to a concrete Unity property in ApplyValue().
     */
    public abstract class ModulatedFloatTarget : MonoBehaviour
    {
        [Header("Parameter Binding (drives Baseline)")]
        [Tooltip("Director to read from. Leave empty to auto-find (parents first, then scene).")]
        [SerializeField] private EnvironmentDirector director;

        [Tooltip("Semantic parameter that drives this target's Baseline. Empty = Baseline is set externally.")]
        [SerializeField] private DirectorParameterDef parameter;

        [Tooltip("Parameter values mapped to curve time 0..1.")]
        [SerializeField] private Vector2 inputRange = new Vector2(0f, 1f);

        [Tooltip("Response shape between parameter and output — expose creative behavior here, not in code.")]
        [SerializeField] private AnimationCurve responseCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("Curve result 0..1 remapped into this destination range, e.g. light intensity 1.2..0.05.")]
        [SerializeField] private Vector2 outputRange = new Vector2(0f, 1f);

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

        /// <summary>Short human label of what this target drives — shown by editor tooling.</summary>
        public virtual string TargetDescription => name;

        // Read-only binding accessors for editor tooling (Director Graph window).
        public DirectorParameterDef BoundParameter => parameter;
        public EnvironmentDirector Director => director;

        /// <summary>Mapped output for an arbitrary parameter value — exposed for testing and previews.</summary>
        public float MapParameter(float parameterValue)
        {
            float t = Mathf.InverseLerp(inputRange.x, inputRange.y, parameterValue);
            return Mathf.Lerp(outputRange.x, outputRange.y, responseCurve.Evaluate(t));
        }

        // Resolve the director once the binding becomes active so GetValue reads are cheap.
        protected virtual void OnEnable()
        {
            if (parameter == null) return;
            if (director == null) director = EnvironmentDirector.FindFor(this);
            if (director == null) { Debug.LogWarning($"[ModulatedFloatTarget] No EnvironmentDirector found for '{name}'.", this); return; }
            director.Track(parameter);
        }

        // The binding owns Baseline while a parameter is assigned (external SetBaseline would be overwritten).
        protected virtual void Update()
        {
            if (parameter == null || director == null) return;
            baseline = MapParameter(director.GetValue(parameter));
        }

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
