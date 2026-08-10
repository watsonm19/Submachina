using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;
using Submachina.Core;

namespace Submachina.Meta
{
    /**
     * A research-survey objective site — scan by holding the sub inside the
     * radius until scanTime accumulates. Leaving the radius pauses (not resets)
     * progress, so partial scans aren't punished; the tension is having to sit
     * still while the environment threatens.
     *
     * MissionController counts onScanned events. Prefab wants a sprite +
     * SonarTarget so it can be found by sonar.
     */
    public class ResearchTarget : MonoBehaviour
    {
        [Tooltip("How close the sub must hold to scan.")]
        [SerializeField, Min(0.5f)] private float scanRadius = 3f;

        [Tooltip("Seconds inside the radius to complete the scan.")]
        [SerializeField, Min(0.5f)] private float scanTime = 5f;

        public UnityEvent onScanStarted;
        public UnityEvent<float> onScanProgress;   // 0..1
        public UnityEvent onScanned;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        public float Progress { get; private set; }

        public bool IsScanned { get; private set; }

        private bool _scanning;

        /** Poll the nearest sub — cheap, and works for any number of subs. */
        private void Update()
        {
            if (IsScanned) return;

            var sub = Submarine.FindNearest(transform.position);
            bool inRange = sub != null &&
                           (sub.transform.position - transform.position).sqrMagnitude <= scanRadius * scanRadius;

            // Edge-trigger the started event for feedback wiring
            if (inRange && !_scanning) { _scanning = true; if (Progress <= 0f) onScanStarted?.Invoke(); }
            if (!inRange) { _scanning = false; return; }

            // Accumulate dwell time toward the completed scan
            Progress += Time.deltaTime / scanTime;
            onScanProgress?.Invoke(Mathf.Clamp01(Progress));

            if (Progress >= 1f)
            {
                IsScanned = true;
                onScanned?.Invoke();
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = IsScanned ? Color.green : Color.cyan;
            Gizmos.DrawWireSphere(transform.position, scanRadius);
        }
    }
}
