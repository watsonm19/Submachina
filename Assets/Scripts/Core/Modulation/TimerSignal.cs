using UnityEngine;

namespace Core.Modulation
{
    /**
     * Reports seconds elapsed since the signal was enabled or last reset.
     * Useful for pacing rules such as "seconds since the last scare".
     */
    public class TimerSignal : FloatSignal
    {
        [Header("Timing")]
        [Tooltip("Use unscaled time so the timer keeps running while the game is paused or slowed.")]
        [SerializeField] private bool useUnscaledTime;

        private float _startTime;

        public override float Value => Now - _startTime;

        private float Now => useUnscaledTime ? Time.unscaledTime : Time.time;

        private void OnEnable() => ResetTimer();

        /// <summary>Restarts the timer at zero — wire this to events that should reset pacing.</summary>
        public void ResetTimer() => _startTime = Now;
    }
}
