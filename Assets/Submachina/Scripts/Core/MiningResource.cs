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

        // =====================
        // References
        // =====================

        [FoldoutGroup("References")]
        [Tooltip("Injected by WorldChunk at spawn time.")]
        [SerializeField] private ResourceManager resourceManager;

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
         * Awards resources and destroys this node.
         * Called by MiningLaser when the beam has been held on target
         * for the full mining duration.
         */
        public void Collect()
        {
            if (resourceManager != null)
                resourceManager.AddResources(resourceValue);
            else
                Debug.LogWarning("[MiningResource] No ResourceManager assigned.");

            Destroy(gameObject);
        }

        /** Injected by WorldChunk immediately after instantiation. */
        public void SetResourceManager(ResourceManager manager)
        {
            resourceManager = manager;
        }
    }
}
