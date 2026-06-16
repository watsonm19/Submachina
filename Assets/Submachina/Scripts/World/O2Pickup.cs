using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * An O2 bubble collectible dropped by enemies when killed.
     *
     * When the player's collider overlaps this trigger, it calls AddO2 on
     * the O2System and destroys itself. Enemies will instantiate this prefab
     * on death — the pickup itself has no knowledge of how it was spawned.
     *
     * Setup:
     *   - Attach to a prefab with a CircleCollider2D set as Is Trigger.
     *   - Assign the scene's O2System (injected at runtime by WorldChunk).
     *   - Tag the player GameObject as "Player".
     */
    [RequireComponent(typeof(Collider2D))]
    public class O2Pickup : MonoBehaviour
    {
        // =====================
        // Settings
        // =====================

        [FoldoutGroup("Settings")]
        [Tooltip("How much current air pressure this bubble restores when collected.")]
        [SerializeField, Min(0f)] private float replenishAmount = 10f;

        [FoldoutGroup("Settings")]
        [Tooltip("How much max air capacity this bubble restores when collected.")]
        [SerializeField, Min(0f)] private float capacityRestoreAmount = 10f;

        [FoldoutGroup("Settings")]
        [Tooltip("If true, the player collects this pickup just by touching it. " +
                 "Disabled by default — collection now goes through O2PickupPump's " +
                 "sweet spot mechanic, which calls Collect() directly.")]
        [SerializeField] private bool collectOnContact;
        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired the moment this bubble is collected, before it is destroyed. " +
                 "Wire VFX/SFX (e.g. a ripple emit) here.")]
        public UnityEvent onCollected;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            // Ensure the collider is a trigger — pickups should never block movement
            Collider2D col = GetComponent<Collider2D>();
            col.isTrigger = true;
        }

        // -------------------------------------------------------
        // Collection
        // -------------------------------------------------------

        /**
         * Fires when any collider enters this trigger.
         * Resolves which submarine collected the pickup from the collision context.
         */
        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!collectOnContact) return;

            Submarine sub = other.GetComponentInParent<Submarine>();
            if (sub == null) return;

            Collect(sub);
        }

        /**
         * Restores O2 and destroys this pickup.
         * Separated from OnTriggerEnter2D so it can be called from
         * other systems (e.g., O2PickupPump, or a magnet upgrade).
         *
         * airMultiplier scales the air granted — lets the collector grade the
         * reward by timing quality. Example: replenishAmount=10, multiplier=0.35
         * → a weak pump stop restores 3.5 air instead of 10.
         */
        public void Collect(Submarine sub, float airMultiplier = 1f)
        {
            if (sub?.O2 != null)
            {
                sub.O2.RestoreCapacity(capacityRestoreAmount);
                sub.O2.AddAir(replenishAmount * airMultiplier);
            }
            else
                Debug.LogWarning("[O2Pickup] No Submarine O2System available — pickup consumed but air not restored.");

            onCollected?.Invoke();
            Destroy(gameObject);
        }
    }
}
