using System.Collections.Generic;
using UnityEngine;
using UnityAtoms.BaseAtoms;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Manages the infinite procedural world by spawning and despawning
     * WorldChunks as the submarine descends.
     *
     * Tracks the camera's vertical position each frame:
     *   - If there aren't enough chunks spawned below the camera, spawn more.
     *   - If a chunk has scrolled far enough above the camera, destroy it.
     *
     * Each chunk is initialized with the player's current depth so obstacle
     * density and difficulty scale naturally the deeper the player goes.
     *
     * Place this on the GameManager object. Assign the Main Camera.
     * The camera must be Orthographic (standard for 2D).
     */
    public class ChunkSpawner : MonoBehaviour
    {
        // =====================
        // References
        // =====================

        [FoldoutGroup("References")]
        [Tooltip("The scene's main camera. Must be orthographic. Used to determine visible area.")]
        [SerializeField] private Camera gameCamera;

        [FoldoutGroup("References")]
        [Tooltip("Prefab instantiated for each rock obstacle. Must have SpriteRenderer + BoxCollider2D.")]
        [SerializeField] private GameObject rockPrefab;

        [FoldoutGroup("References")]
        [Tooltip("Prefab instantiated for each mining resource node. Must have SpriteRenderer + CircleCollider2D (trigger).")]
        [SerializeField] private GameObject resourcePrefab;

        [FoldoutGroup("References")]
        [Tooltip("The scene's ResourceManager. Injected into each resource pickup at spawn time.")]
        [SerializeField] private ResourceManager resourceManager;

        [FoldoutGroup("References")]
        [Tooltip("Enemy prefab spawned procedurally in chunks below the grace zone. " +
                 "Leave empty to disable chunk enemy spawning.")]
        [SerializeField] private GameObject enemyPrefab;

        [FoldoutGroup("References")]
        [Tooltip("The scene's O2System. Injected into chunk-spawned enemies so they drop O2 on death.")]
        [SerializeField] private O2System o2System;

        [FoldoutGroup("References")]
        [Tooltip("Depth atom from DepthTracker. Passed to each chunk so obstacle density scales with depth.")]
        [SerializeField] private FloatVariable currentDepth;

        // =====================
        // World Settings
        // =====================

        [FoldoutGroup("World")]
        [Tooltip("Vertical height of each chunk in world units. " +
                 "Should be roughly 2× the camera's visible height so the player always sees partial chunks above and below.")]
        [SerializeField, Min(5f)] private float chunkHeight = 20f;

        [FoldoutGroup("World")]
        [Tooltip("Half the navigable play area width. Rocks are generated within ±this value. " +
                 "Example: 9 = 18 units total width. Match to your camera's visible width.")]
        [SerializeField, Min(1f)] private float playAreaHalfWidth = 9f;

        // =====================
        // Spawn Settings
        // =====================

        [FoldoutGroup("Spawn")]
        [Tooltip("How many chunks to keep spawned below the camera's bottom edge. " +
                 "Higher = more lookahead. 3 is safe — the player never sees the world generate.")]
        [SerializeField, Min(1)] private int chunksAhead = 3;

        [FoldoutGroup("Spawn")]
        [Tooltip("A chunk is destroyed when its top edge is this many units above the camera. " +
                 "Should be comfortably larger than chunksAhead × chunkHeight to avoid pop-in on reversal.")]
        [SerializeField, Min(10f)] private float despawnDistance = 80f;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private int ActiveChunkCount => _activeChunks.Count;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float NextChunkTopY => _nextChunkTopY;

        // =====================
        // State
        // =====================

        // Y position of the TOP edge of the next chunk to be spawned
        // Decreases each time a chunk is spawned (world grows downward)
        private float _nextChunkTopY;
        private readonly List<WorldChunk> _activeChunks = new List<WorldChunk>();

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Start()
        {
            // Start spawning from the surface so rocks are present from the beginning
            _nextChunkTopY = 0f;
            SpawnChunksAhead();
        }

        private void Update()
        {
            SpawnChunksAhead();
            DespawnChunksBehind();
        }

        // -------------------------------------------------------
        // Spawn / Despawn
        // -------------------------------------------------------

        /**
         * Spawns new chunks until the lowest spawned chunk extends at least
         * (chunksAhead × chunkHeight) below the camera's bottom edge.
         *
         * Called every frame — most frames do nothing because the condition
         * is already satisfied. A new chunk only spawns when the submarine
         * has descended far enough to need one.
         */
        private void SpawnChunksAhead()
        {
            float targetBottomY = GetCameraBottom() - (chunksAhead * chunkHeight);

            while (_nextChunkTopY > targetBottomY)
                SpawnChunk();
        }

        /**
         * Destroys any chunk whose top edge has scrolled more than despawnDistance
         * above the camera. Iterates in reverse to allow safe mid-loop removal.
         */
        private void DespawnChunksBehind()
        {
            float cameraY = gameCamera.transform.position.y;

            for (int i = _activeChunks.Count - 1; i >= 0; i--)
            {
                WorldChunk chunk = _activeChunks[i];

                // Guard against externally destroyed chunks
                if (chunk == null) { _activeChunks.RemoveAt(i); continue; }

                // chunk.transform.position.y is the chunk's TOP edge
                // Positive difference means the chunk top is above the camera
                if (chunk.transform.position.y - cameraY > despawnDistance)
                {
                    _activeChunks.RemoveAt(i);
                    Destroy(chunk.gameObject);
                }
            }
        }

        /**
         * Instantiates a single WorldChunk at the current _nextChunkTopY position
         * and advances _nextChunkTopY downward for the next spawn.
         *
         * Passes current depth so each new chunk is appropriately difficult
         * for where the player is in the run.
         */
        private void SpawnChunk()
        {
            GameObject chunkGO = new GameObject($"Chunk_{_activeChunks.Count:000}");
            chunkGO.transform.SetParent(transform);
            chunkGO.transform.position = new Vector3(0f, _nextChunkTopY, 0f);

            WorldChunk chunk = chunkGO.AddComponent<WorldChunk>();
            // Depth is derived from the chunk's world position, not the player's current depth.
            // This ensures world generation is consistent regardless of when a chunk is spawned.
            float chunkDepth = Mathf.Max(0f, -_nextChunkTopY);
            chunk.Initialize(_nextChunkTopY, chunkHeight, playAreaHalfWidth, chunkDepth,
                rockPrefab, resourcePrefab, resourceManager, enemyPrefab, o2System);

            _activeChunks.Add(chunk);
            _nextChunkTopY -= chunkHeight;
        }

        // -------------------------------------------------------
        // Helpers
        // -------------------------------------------------------

        private float GetCameraBottom() => gameCamera.transform.position.y - gameCamera.orthographicSize;

        // -------------------------------------------------------
        // Editor Utilities
        // -------------------------------------------------------

#if UNITY_EDITOR
        [FoldoutGroup("Debug")]
        [Button("Log Active Chunks"), GUIColor(0.6f, 0.8f, 1f)]
        private void DebugLogChunks()
        {
            if (!Application.isPlaying) { Debug.Log("[ChunkSpawner] Play mode only."); return; }
            Debug.Log($"[ChunkSpawner] {_activeChunks.Count} active chunks. Next spawn at Y={_nextChunkTopY:F1}");
        }
#endif
    }
}
