using System.Collections;
using Core.Modulation;
using UnityEngine;
using UnityEngine.Events;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace Submachina.Core
{
    /**
     * Flickers a light (or any ModulatedFloatTarget) by stepping its Multiplier channel
     * through random values, like a dying bulb or a signal cutting in and out. Always
     * restores Multiplier to neutral (1) when the flicker ends or the component is disabled.
     */
    public class LightFlicker : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("Light (or other float target) whose Multiplier channel is flickered.")]
        [SerializeField] private ModulatedFloatTarget target;

        [Header("Timing")]
        [Tooltip("Random flicker duration range (seconds) used by the parameterless PlayFlicker().")]
        [SerializeField] private Vector2 durationRange = new Vector2(0.4f, 1.2f);

        [Tooltip("Time between random multiplier steps.")]
        [SerializeField] private float stepIntervalSeconds = 1f / 24f;

        [Header("Intensity")]
        [Tooltip("Random multiplier value range for each step. 1 = full brightness, near 0 = near dark.")]
        [SerializeField] private Vector2 multiplierRange = new Vector2(0.05f, 1f);

        [Header("Events")]
        [Tooltip("Raised when a flicker begins playing.")]
        public UnityEvent onFlickerStarted;

        [Tooltip("Raised when a flicker finishes and Multiplier has been restored to 1.")]
        public UnityEvent onFlickerEnded;

        private Coroutine _flickerRoutine;

        // ------------------------------------------------------------------ public API

        /// <summary>Plays a flicker with a random duration drawn from durationRange.</summary>
#if ODIN_INSPECTOR
        [Button("Play Flicker (test)")]
#endif
        public void PlayFlicker()
        {
            PlayFlicker(Random.Range(durationRange.x, durationRange.y));
        }

        /** Plays a flicker for an explicit duration. Restarting cancels any flicker already in progress. */
        public void PlayFlicker(float duration)
        {
            if (_flickerRoutine != null) StopCoroutine(_flickerRoutine);
            _flickerRoutine = StartCoroutine(FlickerRoutine(duration));
        }

        // ------------------------------------------------------------------ coroutine

        /**
         * Steps the Multiplier channel through random values every stepIntervalSeconds until
         * duration elapses, then restores neutral. Example: multiplierRange (0.05, 1), step
         * 1/24s → a jittery bulb flicker at roughly film-frame-rate cadence.
         */
        private IEnumerator FlickerRoutine(float duration)
        {
            onFlickerStarted?.Invoke();

            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (target != null) target.Multiplier = Random.Range(multiplierRange.x, multiplierRange.y);
                yield return new WaitForSeconds(stepIntervalSeconds);
                elapsed += stepIntervalSeconds;
            }

            RestoreNeutral();
            _flickerRoutine = null;
            onFlickerEnded?.Invoke();
        }

        /// <summary>Snaps the Multiplier channel back to its neutral value of 1.</summary>
        private void RestoreNeutral()
        {
            if (target != null) target.Multiplier = 1f;
        }

        private void OnDisable()
        {
            if (_flickerRoutine != null) StopCoroutine(_flickerRoutine);
            _flickerRoutine = null;
            RestoreNeutral();
        }
    }
}
