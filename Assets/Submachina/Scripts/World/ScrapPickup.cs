using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * A physical scrap metal drop spawned when mining a resource node.
     *
     * Sits in the world until the submarine's PickupRangeDetector sweeps it
     * into range. If the scrap bank is full, the pickup stays put until the
     * player spends their stock and returns within pickup range.
     *
     * This object has no collection logic of its own — it just needs a
     * Collider2D so the physics overlap scan can find it. PickupRangeDetector
     * calls Collect() when conditions are met.
     *
     * Setup:
     *   - Attach to a prefab with a CircleCollider2D set as Is Trigger.
     *   - Wire onCollected to a VFX/SFX event for juice.
     */
    [RequireComponent(typeof(Collider2D))]
    public class ScrapPickup : MonoBehaviour
    {
        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired the moment this scrap is banked. Wire to a collect VFX or SFX.")]
        public UnityEvent onCollected;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            // Ensure the collider is a trigger — the pickup should never block movement
            GetComponent<Collider2D>().isTrigger = true;
        }

        // -------------------------------------------------------
        // Collection
        // -------------------------------------------------------

        /**
         * Banks this scrap into the submarine's ScrapManager, then destroys itself.
         * Called by PickupRangeDetector when this pickup is within range and
         * the bank has capacity. Do not call from other sites.
         */
        public void Collect(Submarine sub)
        {
            sub?.Scrap?.AddScrap();
            onCollected?.Invoke();
            Destroy(gameObject);
        }
    }
}
