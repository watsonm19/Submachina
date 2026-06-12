using System.Collections.Generic;
using UnityEngine;
using UnityAtoms.BaseAtoms;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Generates the world as a persistent 2D grid of cells around the camera.
     *
     * Each frame, all cells within spawnRadius of the camera's current cell are
     * checked. Any cell that hasn't been generated yet is spawned immediately.
     * Cells are NEVER despawned — the world persists as the player explores,
     * so returning to a previously visited area looks exactly as they left it.
     *
     * Cells only spawn below the water surface (cellY < 0). The surface boundary
     * collider at Y=0 handles blocking the player from going above water.
     *
     * Place this on the GameManager object. Assign the Main Camera.
     */
    public class ChunkSpawner : MonoBehaviour
    {
        // =====================
        // References
        // =====================

        [FoldoutGroup("References")]
        [Tooltip("The scene's main camera. Used to determine which cells to generate.")]
        [SerializeField] private Camera gameCamera;

        [FoldoutGroup("References")]
        [Tooltip("Prefab for rock obstacles.")]
        [SerializeField] private GameObject rockPrefab;

        [FoldoutGroup("References")]
        [Tooltip("Prefab for mining resource nodes.")]
        [SerializeField] private GameObject resourcePrefab;

        [FoldoutGroup("References")]
        [Tooltip("The scene's ResourceManager — injected into each spawned resource.")]
        [SerializeField] private ResourceManager resourceManager;

        [FoldoutGroup("References")]
        [Tooltip("Enemy prefab — injected into chunks below the grace zone.")]
        [SerializeField] private GameObject enemyPrefab;

        [FoldoutGroup("References")]
        [Tooltip("The submarine's O2System — injected into enemies and O2 pickups at spawn.")]
        [SerializeField] private O2System o2System;

        [FoldoutGroup("References")]
        [Tooltip("O2 bubble prefab scattered passively throughout each chunk.")]
        [SerializeField] private GameObject o2BubblePrefab;

        // =====================
        // World Settings
        // =====================

        [FoldoutGroup("World")]
        [Tooltip("Width of each grid cell in world units.")]
        [SerializeField, Min(5f)] private float cellWidth = 20f;

        [FoldoutGroup("World")]
        [Tooltip("Height of each grid cell in world units.")]
        [SerializeField, Min(5f)] private float cellHeight = 20f;

        [FoldoutGroup("World")]
        [Tooltip("How many cells out from the camera to pre-generate in every direction. " +
                 "Example: 3 = a 7×7 ring of cells (circular check), covering 140 units each way.")]
        [SerializeField, Min(1)] private int spawnRadius = 3;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private int GeneratedCellCount => _chunks.Count;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private string CameraCell => _chunks.Count > 0
            ? WorldToCell(gameCamera.transform.position).ToString()
            : "-";

        // =====================
        // State
        // =====================

        // Keyed by cell coordinate — persisted forever once generated
        private readonly Dictionary<Vector2Int, WorldChunk> _chunks
            = new Dictionary<Vector2Int, WorldChunk>();

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Update()
        {
            SpawnCellsAroundCamera();
        }

        // -------------------------------------------------------
        // Generation
        // -------------------------------------------------------

        /**
         * Checks every cell within spawnRadius of the camera's current cell.
         * Uses a circular distance check so cells generate in a round area
         * rather than a square, matching the "360 around the player" feel.
         *
         * Most frames this does nothing — the vast majority of nearby cells
         * are already generated. A new cell only spawns when the camera moves
         * into an area that hasn't been visited yet.
         */
        private void SpawnCellsAroundCamera()
        {
            Vector2Int center = WorldToCell(gameCamera.transform.position);

            for (int dy = -spawnRadius; dy <= spawnRadius; dy++)
            {
                for (int dx = -spawnRadius; dx <= spawnRadius; dx++)
                {
                    // Circular check: skip corners outside the radius
                    if (dx * dx + dy * dy > spawnRadius * spawnRadius) continue;

                    Vector2Int cell = new Vector2Int(center.x + dx, center.y + dy);

                    // Only generate below the water surface (cellY < 0)
                    if (cell.y >= 0) continue;

                    if (!_chunks.ContainsKey(cell))
                        SpawnCell(cell);
                }
            }
        }

        /**
         * Generates a single world cell at the given grid coordinate.
         *
         * Cell coordinate → world space mapping:
         *   topY    = (cellY + 1) * cellHeight  (e.g. cell(-1) → topY=0, the surface)
         *   centerX = cellX * cellWidth + cellWidth * 0.5
         *   depth   = max(0, -topY)             (positive metres below surface)
         *
         * Example: cell (2, -3) → topY=-40, depth=40, centerX=50
         */
        private void SpawnCell(Vector2Int cell)
        {
            float topY    = (cell.y + 1) * cellHeight;
            float centerX = cell.x * cellWidth + cellWidth * 0.5f;
            float depth   = Mathf.Max(0f, -topY);

            GameObject cellGO = new GameObject($"Cell_{cell.x}_{cell.y}");
            cellGO.transform.SetParent(transform);
            cellGO.transform.position = new Vector3(centerX, topY, 0f);

            WorldChunk chunk = cellGO.AddComponent<WorldChunk>();
            chunk.Initialize(topY, cellHeight, cellWidth * 0.5f, depth,
                rockPrefab, resourcePrefab, resourceManager,
                enemyPrefab, o2BubblePrefab, o2System);

            _chunks[cell] = chunk;
        }

        // -------------------------------------------------------
        // Helpers
        // -------------------------------------------------------

        /**
         * Converts a world position to its grid cell coordinate.
         * FloorToInt handles negative coordinates correctly —
         * e.g. worldX=-1, cellWidth=20 → cellX=-1 (not 0).
         */
        private Vector2Int WorldToCell(Vector3 worldPos)
        {
            return new Vector2Int(
                Mathf.FloorToInt(worldPos.x / cellWidth),
                Mathf.FloorToInt(worldPos.y / cellHeight));
        }

        // -------------------------------------------------------
        // Editor Utilities
        // -------------------------------------------------------

#if UNITY_EDITOR
        [FoldoutGroup("Debug")]
        [Button("Log Generated Cells"), GUIColor(0.6f, 0.8f, 1f)]
        private void DebugLogChunks()
        {
            if (!Application.isPlaying) { Debug.Log("[ChunkSpawner] Play mode only."); return; }
            Debug.Log($"[ChunkSpawner] {_chunks.Count} cells generated. Camera at cell {WorldToCell(gameCamera.transform.position)}.");
        }
#endif
    }
}
