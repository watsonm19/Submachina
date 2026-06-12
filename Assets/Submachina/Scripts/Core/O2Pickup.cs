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
     *   - Assign the scene's O2System in the slot below.
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
        // References
        // =====================

        [FoldoutGroup("References")]
        [Tooltip("The player's ManualBellowsPump. Injected at spawn time by WorldChunk.")]
        [SerializeField] private ManualBellowsPump pump;

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
         * Only responds to the Player tag to avoid being collected by enemies
         * or environment geometry.
         */
        private void OnTriggerEnter2D(Collider2D other)
        {
            // Contact collection is off in the pump-gated flow — O2PickupPump calls Collect()
            if (!collectOnContact) return;
            if (!other.CompareTag("Player")) return;

            Collect();
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
        public void Collect(float airMultiplier = 1f)
        {
            if (pump != null)
            {
                pump.RestoreCapacity(capacityRestoreAmount);
                pump.AddAir(replenishAmount * airMultiplier);
            }
            else
                Debug.LogWarning("[O2Pickup] No ManualBellowsPump assigned — pickup consumed but air not restored.");

            // Notify listeners (VFX/SFX such as a ripple) before the object goes away.
            onCollected?.Invoke();

            Destroy(gameObject);
        }

        /** Assigns the pump at spawn time. Called by WorldChunk when spawning pickups. */
        public void SetPump(ManualBellowsPump bellowsPump)
        {
            pump = bellowsPump;
        }
    }
}
