using UnityEngine;
using UnityEngine.Events;

namespace Core.Modulation
{
    /**
     * Continuous mapping from a semantic parameter to a destination:
     *   parameter value → normalize by input range → response curve → remap → target/event.
     * Example: Darkness 0..1 → curve → GlobalLight baseline 1.2..0.05.
     * Primary smoothing lives on the parameter itself; routes stay dumb and fast.
     */
    public class FloatRoute : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("Director to read from. Leave empty to auto-find (parents first, then scene).")]
        [SerializeField] private EnvironmentDirector director;

        [Tooltip("Semantic parameter driving this route.")]
        [SerializeField] private DirectorParameterDef parameter;

        [Header("Mapping")]
        [Tooltip("Parameter values mapped to curve time 0..1.")]
        [SerializeField] private Vector2 inputRange = new Vector2(0f, 1f);

        [SerializeField] private AnimationCurve responseCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("Curve result 0..1 remapped into this destination range, e.g. light intensity 1.2..0.05.")]
        [SerializeField] private Vector2 outputRange = new Vector2(0f, 1f);

        [Header("Destination")]
        [Tooltip("Composited target whose Baseline channel this route writes. Optional if only using the event.")]
        [SerializeField] private ModulatedFloatTarget target;

        [Tooltip("Skip pushing values that moved less than this since the last push.")]
        [SerializeField] private float epsilon = 0.0005f;

        [Tooltip("Raised with the mapped value whenever it changes beyond epsilon.")]
        public UnityEvent<float> onValueChanged;

        private float _lastPushed = float.NaN;

        /// <summary>Mapped output for an arbitrary parameter value — exposed for testing and previews.</summary>
        public float Map(float parameterValue)
        {
            float t = Mathf.InverseLerp(inputRange.x, inputRange.y, parameterValue);
            return Mathf.Lerp(outputRange.x, outputRange.y, responseCurve.Evaluate(t));
        }

        private void OnEnable()
        {
            if (director == null) director = EnvironmentDirector.FindFor(this);
            if (director == null) { Debug.LogWarning($"[FloatRoute] No EnvironmentDirector found for '{name}'.", this); enabled = false; return; }
            director.Track(parameter);
            _lastPushed = float.NaN;
        }

        private void Update()
        {
            if (parameter == null) return;

            // Read the smoothed parameter and push only meaningful changes downstream.
            float mapped = Map(director.GetValue(parameter));
            if (!float.IsNaN(_lastPushed) && Mathf.Abs(mapped - _lastPushed) < epsilon) return;
            _lastPushed = mapped;

            if (target != null) target.Baseline = mapped;
            onValueChanged?.Invoke(mapped);
        }
    }
}
