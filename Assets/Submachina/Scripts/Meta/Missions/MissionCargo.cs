using UnityEngine;
using UnityEngine.Events;
using Submachina.Core;

namespace Submachina.Meta
{
    /**
     * The retrieval objective — a cargo pod spawned at the mission's target
     * depth. Touching it with the sub latches it aboard (it parents to the sub
     * and adds mass), and MissionController listens for onRetrieved to mark the
     * objective complete. Dying with the pod aboard fails the mission as usual —
     * nothing special to do, the scene just ends.
     *
     * Prefab wants: a visible sprite, a trigger Collider2D, and a SonarTarget
     * so the sonar suite can ping it.
     */
    [RequireComponent(typeof(Collider2D))]
    public class MissionCargo : MonoBehaviour
    {
        [Tooltip("Mass added to the sub while hauling the pod — retrieval should feel like a haul.")]
        [SerializeField, Min(0f)] private float haulMass = 0.8f;

        [Tooltip("Local offset from the sub root while latched (visually slung below the hull).")]
        [SerializeField] private Vector2 latchOffset = new(0f, -0.9f);

        public UnityEvent onRetrieved;

        public bool IsRetrieved { get; private set; }

        private void Awake() => GetComponent<Collider2D>().isTrigger = true;

        /** Latch onto the first submarine that touches the pod. */
        private void OnTriggerEnter2D(Collider2D other)
        {
            if (IsRetrieved) return;
            var sub = other.GetComponentInParent<Submarine>();
            if (sub == null) return;

            IsRetrieved = true;

            // Sling under the hull and weigh the sub down for the trip home
            transform.SetParent(sub.transform, worldPositionStays: false);
            transform.localPosition = latchOffset;
            sub.Physics?.RegisterMass(this, haulMass);

            onRetrieved?.Invoke();
        }

        private void OnDestroy()
        {
            // Release the haul mass if the pod is destroyed while latched
            var sub = GetComponentInParent<Submarine>();
            sub?.Physics?.UnregisterMass(this);
        }
    }
}
