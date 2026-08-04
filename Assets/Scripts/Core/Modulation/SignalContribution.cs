using UnityEngine;

namespace Core.Modulation
{
    /**
     * Scene component mapping one raw FloatSignal into one semantic parameter:
     *   raw value → normalize by input range → response curve → remap to output range → blend.
     * Example: DepthMeters 0..800 → curve → Darkness contribution 0..0.8, blend Add.
     * Registers with its EnvironmentDirector on enable and cleans up on disable,
     * so disabling/destroying the object removes its influence automatically.
     */
    public class SignalContribution : MonoBehaviour, IParameterContribution
    {
        [Header("Wiring")]
        [Tooltip("Director to contribute to. Leave empty to auto-find (parents first, then scene).")]
        [SerializeField] private EnvironmentDirector director;

        [Tooltip("Raw signal being interpreted. Defaults to a FloatSignal on this GameObject.")]
        [SerializeField] private FloatSignal signal;

        [Tooltip("Semantic parameter this contribution feeds.")]
        [SerializeField] private DirectorParameterDef parameter;

        [Header("Mapping")]
        [Tooltip("Raw signal values mapped to curve time 0..1. Example: 0..800 meters of depth.")]
        [SerializeField] private Vector2 inputRange = new Vector2(0f, 1f);

        [Tooltip("Response shape between input and output — expose creative behavior here, not in code.")]
        [SerializeField] private AnimationCurve responseCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("Curve result 0..1 remapped into this contribution range. Example: 0..0.8 Darkness.")]
        [SerializeField] private Vector2 outputRange = new Vector2(0f, 1f);

        [Header("Blending")]
        [SerializeField] private ParameterBlendMode blendMode = ParameterBlendMode.Add;

        [Tooltip("Blend strength 0..1 (1 = full effect). Lets a contribution be globally scaled back.")]
        [Range(0f, 1f)]
        [SerializeField] private float weight = 1f;

        [Tooltip("Used only for Override blending — highest priority override wins.")]
        [SerializeField] private int priority;

        public DirectorParameterDef Parameter => parameter;
        public ParameterBlendMode Blend => blendMode;
        public int Priority => priority;
        public float Weight => weight;
        public bool IsActive => isActiveAndEnabled && signal != null && signal.IsValid;

        // Read-only wiring accessors for editor tooling (Director Graph window).
        public FloatSignal Signal => signal;
        public EnvironmentDirector Director => director;
        public Vector2 InputRange => inputRange;
        public Vector2 OutputRange => outputRange;

        /// <summary>Raw signal pushed through normalize → curve → remap. Weight is applied by the director.</summary>
        public float Evaluate(float deltaTime)
        {
            float t = Mathf.InverseLerp(inputRange.x, inputRange.y, signal.Value);
            return Mathf.Lerp(outputRange.x, outputRange.y, responseCurve.Evaluate(t));
        }

        private void Awake()
        {
            if (signal == null) signal = GetComponent<FloatSignal>();
        }

        // Register/unregister with the director so influence lifetime tracks component lifetime.
        private void OnEnable()
        {
            if (director == null) director = EnvironmentDirector.FindFor(this);
            if (director == null) { Debug.LogWarning($"[SignalContribution] No EnvironmentDirector found for '{name}'.", this); return; }
            director.RegisterContribution(this);
        }

        private void OnDisable()
        {
            if (director != null) director.UnregisterContribution(this);
        }
    }
}
