using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;
using System.Collections;

namespace Utility
{
    /// <summary>
    /// Executes a Unity event after a configurable millisecond delay.
    /// Can be triggered on Start or manually via code/button.
    /// </summary>
    public class DelayedEvent : MonoBehaviour
    {
        [TitleGroup("Delay Configuration")]
        [Tooltip("Delay in milliseconds before invoking the event")]
        [MinValue(0)]
        public int delayMilliseconds = 1000;

        [TitleGroup("Delay Configuration")]
        [Tooltip("If true, the delayed event will trigger automatically on Start")]
        public bool triggerOnStart = true;

        [TitleGroup("Event")]
        [Tooltip("Event invoked after the specified delay")]
        public UnityEvent onDelayComplete;

        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly]
        [Tooltip("Whether a delayed event is currently waiting to execute")]
        #pragma warning disable CS0414 // Odin Inspector reads this via reflection
        private bool isWaiting = false;
        #pragma warning restore CS0414

        private Coroutine _activeCoroutine;

        /**
         * Unity lifecycle - triggers delayed event if configured to run on start.
         */
        private void Start()
        {
            if (triggerOnStart)
            {
                TriggerDelayedEvent();
            }
        }

        /**
         * Starts the delayed event execution. If another delayed event is already
         * in progress, it will be cancelled and replaced with a new one.
         */
        [Button("Trigger Delayed Event")]
        [ButtonGroup("Manual Controls")]
        public void TriggerDelayedEvent()
        {
            // Cancel any existing delayed event
            if (_activeCoroutine != null)
            {
                StopCoroutine(_activeCoroutine);
                _activeCoroutine = null;
            }

            // Start the new delayed event
            _activeCoroutine = StartCoroutine(DelayedEventCoroutine());
        }

        /**
         * Cancels the currently waiting delayed event if one exists.
         */
        [Button("Cancel Delayed Event")]
        [ButtonGroup("Manual Controls")]
        [EnableIf(nameof(isWaiting))]
        public void CancelDelayedEvent()
        {
            if (_activeCoroutine != null)
            {
                StopCoroutine(_activeCoroutine);
                _activeCoroutine = null;
                isWaiting = false;
            }
        }

        /**
         * Coroutine that waits for the specified delay and then invokes the event.
         * Example: 1000ms delay = 1 second wait before event fires
         */
        private IEnumerator DelayedEventCoroutine()
        {
            isWaiting = true;

            // Convert milliseconds to seconds for Unity's time system
            float delaySeconds = delayMilliseconds / 1000f;

            // Wait for the specified duration
            yield return new WaitForSeconds(delaySeconds);

            // Invoke the event after delay completes
            onDelayComplete?.Invoke();

            // Cleanup state
            isWaiting = false;
            _activeCoroutine = null;
        }

        /**
         * Unity lifecycle - cleanup any active coroutines when disabled.
         */
        private void OnDisable()
        {
            // Cancel delayed event if component is disabled
            if (_activeCoroutine != null)
            {
                StopCoroutine(_activeCoroutine);
                _activeCoroutine = null;
                isWaiting = false;
            }
        }
    }
}

