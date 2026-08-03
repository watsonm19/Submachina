using System.Collections;
using UnityEngine;
using UnityEngine.Events;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace Submachina.Core
{
    /**
     * Drives a target-depth wreck encounter: reveals a dormant wreck object off to one
     * side of the player, then polls until the nearest submarine reaches it.
     *
     * One-shot — BeginEncounter() does nothing once an encounter is already underway
     * (call EndEncounter() to reset and allow another BeginEncounter()).
     */
    public class WreckEncounter : MonoBehaviour
    {
        [Header("Wreck")]
        [Tooltip("Scene object for the wreck. Starts inactive — BeginEncounter activates and positions it.")]
        [SerializeField] private GameObject wreckObject;

        [Header("Placement")]
        [Tooltip("Reference point the wreck is offset from. Defaults to Camera.main's transform at BeginEncounter time.")]
        [SerializeField] private Transform placementReference;

        [Tooltip("Distance (world units) the wreck is placed to the left or right of the reference point (random 50/50).")]
        [SerializeField] private float horizontalOffset = 30f;

        [Tooltip("When true, the wreck's Y snaps to the reference point's Y (same depth). When false, the wreck keeps its own authored Y.")]
        [SerializeField] private bool placeAtReferenceDepth = true;

        [Header("Reach Detection")]
        [Tooltip("Distance from the wreck at which a submarine counts as having reached it.")]
        [SerializeField] private float reachRadius = 6f;

        [Tooltip("How often to poll for the nearest submarine while the encounter is active.")]
        [SerializeField] private float pollIntervalSeconds = 0.25f;

        [Header("Events")]
        [Tooltip("Raised once the wreck is activated and positioned.")]
        public UnityEvent onEncounterBegan;

        [Tooltip("Raised once, the first time a submarine comes within reachRadius of the wreck.")]
        public UnityEvent onWreckReached;

        private Coroutine _pollRoutine;
        private bool _begun;

        // ------------------------------------------------------------------ public API

        /** Activates and positions the wreck, then starts polling for a submarine reaching it. Ignored if already begun. */
#if ODIN_INSPECTOR
        [Button("Begin Encounter (test)")]
#endif
        public void BeginEncounter()
        {
            if (_begun) return;
            if (wreckObject == null) return;
            _begun = true;

            PositionWreck();
            wreckObject.SetActive(true);
            onEncounterBegan?.Invoke();

            _pollRoutine = StartCoroutine(PollForReach());
        }

        /// <summary>Stops polling and deactivates the wreck. Safe to call at any time, including before BeginEncounter().</summary>
        public void EndEncounter()
        {
            if (_pollRoutine != null) StopCoroutine(_pollRoutine);
            _pollRoutine = null;
            _begun = false;

            if (wreckObject != null) wreckObject.SetActive(false);
        }

        // ------------------------------------------------------------------ internals

        /**
         * Places the wreck to a random side (left or right) of the reference point.
         * Example: reference at x=0, horizontalOffset=30 → wreck lands at x=-30 or x=30.
         */
        private void PositionWreck()
        {
            Transform reference = placementReference != null ? placementReference : (Camera.main != null ? Camera.main.transform : null);
            if (reference == null) return;

            float side = Random.value < 0.5f ? -1f : 1f;
            Vector3 pos = wreckObject.transform.position;
            pos.x = reference.position.x + side * horizontalOffset;
            if (placeAtReferenceDepth) pos.y = reference.position.y;
            wreckObject.transform.position = pos;
        }

        /**
         * Polls for the nearest submarine to the wreck every pollIntervalSeconds. Fires
         * onWreckReached once and stops as soon as a submarine is within reachRadius.
         */
        private IEnumerator PollForReach()
        {
            var wait = new WaitForSeconds(pollIntervalSeconds);
            while (true)
            {
                yield return wait;

                Submarine nearest = Submarine.FindNearest(wreckObject.transform.position);
                if (nearest == null) continue;

                float dist = Vector3.Distance(nearest.transform.position, wreckObject.transform.position);
                if (dist > reachRadius) continue;

                onWreckReached?.Invoke();
                _pollRoutine = null;
                yield break;
            }
        }

        private void OnDisable()
        {
            // Unity already stops owned coroutines on disable; this just drops the stale handle.
            _pollRoutine = null;
        }
    }
}
