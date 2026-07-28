using Core.Rendering;
using UnityEngine;

namespace Submachina.Core
{
    /**
     * Base configuration for spawning pickup drops (O2 bubbles, scrap, etc.).
     *
     * Handles the shared pattern: hold a prefab + count, scatter N instances
     * in a radius around a position, then let each subclass configure the
     * spawned instance via the virtual Configure() method.
     *
     * Subclass checklist:
     *   1. Extend DropConfig.
     *   2. Add type-specific serialized fields (e.g. sizeMin/sizeMax for O2).
     *   3. Override Configure() to set up the spawned instance.
     *
     * Usage: serialize via [SerializeReference] on any MonoBehaviour that
     * spawns drops. Odin Inspector renders a type-picker for the array.
     */
    [System.Serializable]
    public class DropConfig
    {
        [Tooltip("Prefab to instantiate. Leave null to disable this drop.")]
        public GameObject prefab;

        [Tooltip("How many instances to scatter per spawn call.")]
        [Min(0)] public int count;

        [Tooltip("Radius around the spawn point within which drops are scattered.")]
        [Min(0f)] public float scatterRadius = 0.8f;

        // -------------------------------------------------------
        // Spawning
        // -------------------------------------------------------

        /**
         * Instantiates count copies of the prefab scattered randomly within
         * scatterRadius of the given position. Each instance is passed to
         * Configure() for type-specific setup.
         */
        public void Spawn(Vector3 position)
        {
            if (prefab == null || count <= 0) return;

            for (int i = 0; i < count; i++)
            {
                Vector2 offset = Random.insideUnitCircle * scatterRadius;
                GameObject instance = Object.Instantiate(
                    prefab,
                    position + (Vector3)offset,
                    Quaternion.identity);
                ShadowCaster2DRefresher.RefreshHierarchy(instance); // URP 2D casters don't rebuild their mesh on clone — force it

                Configure(instance);
            }
        }

        /**
         * Called on each spawned instance immediately after Instantiate.
         * Override in subclasses to apply type-specific setup (e.g. SetSize
         * for O2 bubbles, SetAmount for scrap). Base implementation is a
         * no-op so DropConfig itself works for simple drops that just need
         * instantiation with no special configuration.
         */
        protected virtual void Configure(GameObject instance) { }
    }
}
