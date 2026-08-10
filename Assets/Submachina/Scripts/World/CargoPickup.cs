using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * A jettisoned (or dropped) parcel of typed cargo floating in the water.
     *
     * Spawned by CargoHold when the player dumps weight; touching it with the
     * sub re-stows as many units as the hold has room for (partial pickups
     * shrink the parcel instead of destroying it). Ore is heavy, so parcels
     * sink slowly — dump it and dawdle and it drifts toward the seabed.
     *
     * Prefab wants: a sprite (tinted by the resource's color at Init), a
     * trigger Collider2D, and optionally a SonarTarget so it pings.
     */
    [RequireComponent(typeof(Collider2D))]
    public class CargoPickup : MonoBehaviour
    {
        [Tooltip("Slow sink speed in units/sec — dumped ore drifts toward the seabed.")]
        [SerializeField, Min(0f)] private float sinkSpeed = 0.4f;

        [Tooltip("Seconds after spawning before the sub can re-collect — without this, a dumped " +
                 "parcel would re-stow itself the same frame it leaves the hold.")]
        [SerializeField, Min(0f)] private float collectGrace = 1.5f;

        [Tooltip("Sprites tinted with the resource color on Init.")]
        [SerializeField] private SpriteRenderer[] tintRenderers;

        public UnityEvent onCollected;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        public ResourceType Type { get; private set; }

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        public int Units { get; private set; } = 1;

        private float _collectableAt;

        private void Awake()
        {
            GetComponent<Collider2D>().isTrigger = true;
            _collectableAt = Time.time + collectGrace;
        }

        /** Stamps the parcel's contents and tints it to match the resource. */
        public void Init(ResourceType type, int units)
        {
            Type = type;
            Units = Mathf.Max(1, units);
            _collectableAt = Time.time + collectGrace;

            var renderers = tintRenderers is { Length: > 0 } ? tintRenderers : GetComponentsInChildren<SpriteRenderer>(true);
            if (type != null)
                foreach (var r in renderers) r.color = type.tint;
        }

        private void Update()
        {
            // Heavy parcels drift down; no physics body needed for a slow sink
            if (sinkSpeed > 0f) transform.Translate(Vector3.down * (sinkSpeed * Time.deltaTime), Space.World);
        }

        /**
         * Re-stow into a touching sub's hold — partial pickups shrink the parcel.
         * Stay (not Enter) so a parcel dumped while overlapping the sub is still
         * collectible once the grace period passes.
         */
        private void OnTriggerStay2D(Collider2D other)
        {
            if (Type == null || Time.time < _collectableAt) return;
            var sub = other.GetComponentInParent<Submarine>();
            if (sub?.Cargo == null) return;

            int stored = sub.Cargo.Add(Type, Units);
            if (stored <= 0) return;   // hold full — parcel stays in the water

            Units -= stored;
            if (Units <= 0)
            {
                onCollected?.Invoke();
                Destroy(gameObject);
            }
        }
    }
}
