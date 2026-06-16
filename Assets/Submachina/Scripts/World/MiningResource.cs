using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * A mining resource node that requires a sustained laser beam to collect.
     *
     * The node does NOT collect on player touch — the MiningLaser script on the
     * submarine drives mining progress by calling SetMiningProgress each frame
     * while the beam is on target. When progress reaches 1.0, MiningLaser calls
     * Collect() directly.
     *
     * Visual feedback: the sprite transitions toward white as mining progresses,
     * giving the player a clear signal that the laser is working.
     *
     * Setup:
     *   - Attach to the CopperResource prefab alongside a CircleCollider2D.
     *   - Set the prefab's layer to "Resource" so MiningLaser's raycast can hit it.
     *   - ResourceManager is injected at spawn time by WorldChunk.
     */
    [RequireComponent(typeof(Collider2D))]
    public class MiningResource : MonoBehaviour
    {
        // =====================
        // Settings
        // =====================

        [FoldoutGroup("Settings")]
        [Tooltip("Resource units awarded on successful collection.")]
        [SerializeField, Min(0f)] private float resourceValue = 10f;

        [FoldoutGroup("Settings")]
        [Tooltip("Probability of dropping one scrap on collection. " +
                 "Example: 0.20 = 20% chance, roughly 1 scrap per 5 nodes mined.")]
        [SerializeField, Range(0f, 1f)] private float scrapDropChance = 0.20f;

        [FoldoutGroup("Settings")]
        [Tooltip("Prefab to spawn in the world on a successful scrap drop roll. " +
                 "Assign the ScrapPickup prefab here — it will be collected by the " +
                 "submarine's PickupRangeDetector when the player comes within range.")]
        [SerializeField] private GameObject scrapPickupPrefab;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float MiningProgress => _currentProgress;

        // =====================
        // State
        // =====================

        private SpriteRenderer _spriteRenderer;
        private Color _baseColor;
        private float _currentProgress;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            // Trigger collider allows MiningLaser raycasts to detect this node
            GetComponent<Collider2D>().isTrigger = true;

            _spriteRenderer = GetComponent<SpriteRenderer>();
            if (_spriteRenderer != null) _baseColor = _spriteRenderer.color;
        }

        // -------------------------------------------------------
        // Mining API
        // -------------------------------------------------------

        /**
         * Called by MiningLaser each frame the beam is on this node.
         * Progress is 0..1; at 1.0 the node is ready to be collected.
         * Sprite bleaches toward white to signal mining activity.
         */
        public void SetMiningProgress(float progress)
        {
            _currentProgress = Mathf.Clamp01(progress);

            if (_spriteRenderer != null)
                _spriteRenderer.color = Color.Lerp(_baseColor, Color.white, _currentProgress);
        }

        /**
         * Awards resources to the collecting submarine, rolls for a scrap drop, then destroys this node.
         * Called by MiningLaser when the beam has been held on target
         * for the full mining duration.
         *
         * Scrap roll: Random.value produces a uniform 0-1 value each call.
         * Example: scrapDropChance=0.20 → ~1 scrap dropped per 5 nodes mined on average.
         */
        public void Collect(Submarine sub)
        {
            if (sub?.Resources != null)
                sub.Resources.AddResources(resourceValue);
            else
                Debug.LogWarning("[MiningResource] No Submarine ResourceManager available.");

            // Roll for a scrap pickup drop — spawns a physical world object that the
            // player must come within pickup range to collect (bank-full check happens there)
            if (scrapPickupPrefab != null && Random.value < scrapDropChance)
                Instantiate(scrapPickupPrefab, transform.position, Quaternion.identity);

            Destroy(gameObject);
        }
    }
}
