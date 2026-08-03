using UnityEngine;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace Core.Modulation
{
    /**
     * Generic pulse generator that writes a periodic wave into one channel of a
     * ModulatedFloatTarget — heartbeats, ambient pressure throbs, sonar dials, anything
     * that needs a steady rhythmic push without a bespoke component.
     *
     * Sine oscillates smoothly; Noise scrolls Perlin noise for an uneven, organic pulse.
     * The Additive channel pulses around 0 (neutral rest); the Multiplier channel pulses
     * around 1 (neutral scale) so either channel layers cleanly under other writers.
     */
    public class FloatTargetPulser : MonoBehaviour
    {
        public enum Waveform { Sine, Noise }
        public enum Channel { Additive, Multiplier }

        [Header("Target")]
        [Tooltip("Channel to write into. Defaults to a ModulatedFloatTarget on this GameObject.")]
        [SerializeField] private ModulatedFloatTarget target;

        [Header("Wave")]
        [Tooltip("Sine: smooth oscillation. Noise: scrolled Perlin noise for an uneven pulse.")]
        [SerializeField] private Waveform waveform = Waveform.Sine;

        [Tooltip("Peak deviation from neutral (0 for Additive, 1 for Multiplier).")]
        [SerializeField] private float amplitude = 0.5f;

        [Tooltip("Cycles per second.")]
        [SerializeField] private float frequency = 1f;

        [Tooltip("Which ModulatedFloatTarget channel the wave writes into.")]
        [SerializeField] private Channel channel = Channel.Additive;

        [Header("Lifecycle")]
        [Tooltip("Start pulsing automatically when this component becomes enabled.")]
        [SerializeField] private bool startActive = false;

        // Phase accumulator — resets each time the pulser (re)activates so the wave always starts at phase zero.
        private bool _active;
        private float _elapsed;

        private void Awake()
        {
            if (target == null) target = GetComponent<ModulatedFloatTarget>();
        }

        private void OnEnable()
        {
            if (startActive) Activate();
        }

        private void Update()
        {
            if (!_active || target == null) return;

            _elapsed += Time.deltaTime;
            float value = Evaluate();

            if (channel == Channel.Additive) target.Additive = value;
            else target.Multiplier = value;
        }

        // ------------------------------------------------------------------ public API

        /// <summary>UnityEvent-wireable toggle — true starts the pulse, false restores neutral and stops it.</summary>
        public void SetActive(bool active)
        {
            if (active) Activate();
            else Deactivate();
        }

        /** Starts (or restarts) the pulse from phase zero. Safe to call while already active. */
#if ODIN_INSPECTOR
        [Button("Activate (test)")]
#endif
        public void Activate()
        {
            _active = true;
            _elapsed = 0f;
        }

        /** Stops the pulse and restores the written channel to its neutral value (0 additive, 1 multiplier). */
#if ODIN_INSPECTOR
        [Button("Deactivate (test)")]
#endif
        public void Deactivate()
        {
            _active = false;
            if (target == null) return;

            if (channel == Channel.Additive) target.Additive = 0f;
            else target.Multiplier = 1f;
        }

        // ------------------------------------------------------------------ wave math

        /**
         * Computes this frame's channel value from the elapsed time.
         * Example: Sine, amplitude 0.3, frequency 1, Multiplier channel → oscillates
         * smoothly between 0.7 and 1.3, one full cycle per second.
         */
        private float Evaluate()
        {
            if (waveform == Waveform.Sine)
            {
                float phase = 2f * Mathf.PI * frequency * _elapsed;
                return channel == Channel.Additive
                    ? amplitude * (0.5f + 0.5f * Mathf.Sin(phase))
                    : 1f + amplitude * Mathf.Sin(phase);
            }

            // Perlin noise scrolled along one axis by frequency * elapsed — raw sample is 0..1.
            float raw = Mathf.PerlinNoise(_elapsed * frequency, 0.5f);
            return channel == Channel.Additive
                ? amplitude * raw
                : 1f + amplitude * (raw * 2f - 1f);
        }
    }
}
