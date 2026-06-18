using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Submachina.Core
{
    /**
     * A single procedurally-generated section of the underwater world.
     *
     * Spawned and initialized by ChunkSpawner. Each chunk occupies a fixed
     * vertical slice of the world. WorldChunk is now a generic executor: it
     * holds no knowledge of rocks/enemies/resources — it simply iterates the
     * rules in a SpawnProfile and lets each rule place its own prefab.
     *
     * All WHAT-to-spawn lives in data (SpawnProfile → SpawnRule/SpawnRuleData);
     * this component only provides the chunk geometry and a deterministic RNG.
     */
    public class WorldChunk : MonoBehaviour
    {
        // Chunk bounds — stored after Initialize for gizmo drawing
        private float _topY;
        private float _height;
        private float _halfWidth;

        [Tooltip("Fired after the chunk finishes generating all its rules. " +
                 "Hook particle effects, audio, or analytics here.")]
        public UnityEvent onChunkGenerated;

        // -------------------------------------------------------
        // Initialization
        // -------------------------------------------------------

        /**
         * Called by ChunkSpawner immediately after instantiation.
         *
         * Builds a deterministic System.Random from the supplied seed (so the
         * same world seed + cell always produces identical contents) and a
         * SpawnContext describing the chunk's geometry, then runs every rule in
         * the profile against this chunk's depth. Spawned prefabs resolve their
         * own submarine dependencies via Submarine.FindNearest or collision.
         *
         * @param topY      world Y of the chunk's top edge
         * @param height    chunk height in world units
         * @param halfWidth half-extent from center to either trench wall
         * @param worldX    chunk center X in world space
         * @param depth     positive metres below the surface at the chunk top
         * @param profile   the spawn rules to execute (null → empty chunk)
         * @param seed      deterministic seed for this chunk's RNG
         */
        public void Initialize(float topY, float height, float halfWidth, float worldX,
            float depth, SpawnProfile profile, int seed)
        {
            // Cache bounds for the gizmo
            _topY = topY;
            _height = height;
            _halfWidth = halfWidth;

            if (profile == null)
            {
                Debug.LogWarning("[WorldChunk] No SpawnProfile assigned — chunk left empty.", this);
                return;
            }

            // Deterministic RNG + shared placement record (used for per-rule spacing)
            System.Random rng = new System.Random(seed);
            List<Vector2> placed = new List<Vector2>();
            SpawnContext ctx = new SpawnContext(topY, height, halfWidth, worldX, depth, placed);

            // Run every rule; each self-gates on depth and places its own prefabs
            foreach (SpawnRuleData rule in profile.AllRules)
                rule.Execute(in ctx, rng, profile.GlobalDensity, transform);

            onChunkGenerated?.Invoke();
        }

        // -------------------------------------------------------
        // Editor Gizmos
        // -------------------------------------------------------

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Draw chunk bounds as a cyan wire rectangle
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.3f);
            Vector3 center = new Vector3(transform.position.x, _topY - _height * 0.5f, 0f);
            Vector3 size = new Vector3(_halfWidth * 2f, _height, 0f);
            Gizmos.DrawWireCube(center, size);
        }
#endif
    }
}
