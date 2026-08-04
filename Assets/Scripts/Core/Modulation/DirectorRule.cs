using MoreMountains.Feedbacks;
using UnityEngine;
using UnityEngine.Events;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace Core.Modulation
{
    /**
     * Discrete event rule watching one semantic parameter: fires actions when the value
     * crosses a threshold, with hysteresis (separate re-arm threshold), sustain duration,
     * cooldown, probability gating, and an optional one-shot mode.
     *
     * Example: Dread rises above 0.75, held 1s, 25s cooldown, 60% chance
     *          → play stinger feedback + UnityEvent, re-arm only after Dread falls below 0.5.
     */
    public class DirectorRule : MonoBehaviour
    {
        public enum TriggerDirection { RisesAbove, FallsBelow }

        [Header("Source")]
        [Tooltip("Director to read from. Leave empty to auto-find (parents first, then scene).")]
        [SerializeField] private EnvironmentDirector director;

        [Tooltip("Semantic parameter this rule watches.")]
        [SerializeField] private DirectorParameterDef parameter;

        [Header("Trigger")]
        [SerializeField] private TriggerDirection direction = TriggerDirection.RisesAbove;

        [Tooltip("Firing threshold — value must cross this in the trigger direction.")]
        [SerializeField] private float triggerThreshold = 0.75f;

        [Tooltip("Hysteresis: value must return past this before the rule can fire again. " +
                 "Keep a gap from the trigger threshold to prevent oscillation spam.")]
        [SerializeField] private float resetThreshold = 0.5f;

        [Tooltip("Value must stay past the trigger threshold this long before firing. 0 = instant.")]
        [SerializeField] private float sustainSeconds = 0f;

        [Header("Pacing")]
        [Tooltip("Minimum seconds between firings of this rule.")]
        [SerializeField] private float cooldownSeconds = 10f;

        [Tooltip("Chance 0..1 that an eligible crossing actually fires. Failed rolls wait for the next crossing.")]
        [Range(0f, 1f)]
        [SerializeField] private float probability = 1f;

        [Tooltip("When true the rule fires once per play session and then disarms permanently.")]
        [SerializeField] private bool oneShot;

        [Tooltip("Start armed. Disable to require an explicit Arm() call (e.g. from a scripted phase).")]
        [SerializeField] private bool armedOnStart = true;

        [Header("Actions")]
        [Tooltip("Feel feedbacks played when the rule fires.")]
        [SerializeField] private MMF_Player[] feedbacks;

        [Tooltip("Invoked when the rule fires — wire stingers, spawns, modifiers, anything.")]
        public UnityEvent onTriggered;

        [Tooltip("Invoked when the rule re-arms after the reset threshold is crossed back.")]
        public UnityEvent onRearmed;

        // --- Runtime state ---
        private bool _armed;
        private bool _spent;             // one-shot has fired
        private float _lastFireTime = float.NegativeInfinity;
        private float _sustainStart = -1f;

        public bool IsArmed => _armed;
        public bool CooldownReady => Time.time - _lastFireTime >= cooldownSeconds;

        // Read-only wiring accessors for editor tooling (Director Graph window).
        public DirectorParameterDef Parameter => parameter;
        public EnvironmentDirector Director => director;
        public TriggerDirection Direction => direction;
        public float TriggerThreshold => triggerThreshold;
        public float ResetThreshold => resetThreshold;
        public float CooldownSeconds => cooldownSeconds;
        public float Probability => probability;
        public bool OneShot => oneShot;
        public bool IsSpent => _spent;
        public float CooldownRemaining => Mathf.Max(0f, cooldownSeconds - (Time.time - _lastFireTime));

        private void OnEnable()
        {
            if (director == null) director = EnvironmentDirector.FindFor(this);
            if (director == null) { Debug.LogWarning($"[DirectorRule] No EnvironmentDirector found for '{name}'.", this); enabled = false; return; }
            director.Track(parameter);
            _armed = armedOnStart;
        }

        private void Update()
        {
            if (parameter == null || _spent) return;
            float value = director.GetValue(parameter);
            bool pastTrigger = direction == TriggerDirection.RisesAbove ? value >= triggerThreshold : value <= triggerThreshold;
            bool pastReset = direction == TriggerDirection.RisesAbove ? value <= resetThreshold : value >= resetThreshold;

            // Re-arm once the value has retreated past the reset threshold.
            if (!_armed)
            {
                if (!pastReset) return;
                _armed = true;
                _sustainStart = -1f;
                onRearmed?.Invoke();
                return;
            }

            // Track how long the value has been held past the trigger threshold.
            if (!pastTrigger) { _sustainStart = -1f; return; }
            if (_sustainStart < 0f) _sustainStart = Time.time;
            if (Time.time - _sustainStart < sustainSeconds) return;

            // Eligibility gates: cooldown first, then the probability roll consumes the crossing.
            if (!CooldownReady) return;
            _armed = false;
            if (Random.value > probability) return;

            Fire();
        }

        /**
         * Executes the rule's actions: Feel feedbacks then the UnityEvent. Public so
         * debug panels and scripted sequences can force-fire for testing.
         */
#if ODIN_INSPECTOR
        [Button("Fire Now (test)"), PropertyOrder(100)]
#endif
        public void Fire()
        {
            _lastFireTime = Time.time;
            if (oneShot) _spent = true;

            // Feel feedbacks at this rule's position (rules usually sit on rig/manager objects).
            if (feedbacks != null)
                foreach (var player in feedbacks)
                    if (player != null) player.PlayFeedbacks(transform.position);

            onTriggered?.Invoke();
        }

        /// <summary>Manually arm the rule (used by scripted phases with armedOnStart off).</summary>
        public void Arm() => _armed = true;

        /// <summary>Disarm without firing — the rule stays quiet until Arm() or the reset threshold logic re-arms it.</summary>
        public void Disarm() => _armed = false;
    }
}
