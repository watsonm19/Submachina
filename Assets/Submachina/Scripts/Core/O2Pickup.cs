using UnityEngine;
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
        [Tooltip("How much O2 this bubble restores when collected.")]
        [SerializeField, Min(0f)] private float replenishAmount = 25f;

        // =====================
        // References
        // =====================

        [FoldoutGroup("References")]
        [Tooltip("The scene's O2System. Assign on the prefab or at spawn time.")]
        [SerializeField] private O2System o2System;

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
            if (!other.CompareTag("Player")) return;

            Collect();
        }

        /**
         * Restores O2 and destroys this pickup.
         * Separated from OnTriggerEnter2D so it can be called from
         * other systems (e.g., a magnet upgrade that auto-collects pickups).
         */
        public void Collect()
        {
            if (o2System != null)
                o2System.AddO2(replenishAmount);
            else
                Debug.LogWarning("[O2Pickup] No O2System assigned — pickup consumed but O2 not restored.");

            Destroy(gameObject);
        }

        /** Assigns the O2System at spawn time. Called by enemies when dropping this pickup. */
        public void SetO2System(O2System system)
        {
            o2System = system;
        }
    }
}
