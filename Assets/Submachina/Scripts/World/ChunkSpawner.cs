using System;
using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;
using Submachina.Meta;

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
     * WHAT spawns in each cell is entirely data-driven via the SpawnProfile —
     * this component owns only the grid, camera tracking, and persistence.
     *
     * Place this on the GameManager object. The main camera is found automatically.
     */
    public class ChunkSpawner : MonoBehaviour
    {
        /** How each chunk's deterministic seed is derived. */
        public enum SeedMode
        {
            /** Seed from the cell coordinate only — identical worlds every run. */
            CoordHash,

            /** Mix in worldSeed — same layout for a given worldSeed, varies between runs/seeds. */
            WorldSeedPlusCoord
        }

        // =====================
        // Spawn Data
        // =====================

        [FoldoutGroup("Spawn Data")]
        [Tooltip("The set of spawn rules driving every chunk. Create via " +
                 "Assets → Create → Submachina → Spawning → Spawn Profile.")]
        [Required, InlineEditor(objectFieldMode: InlineEditorObjectFieldModes.Boxed)]
        [SerializeField] private SpawnProfile spawnProfile;

        [FoldoutGroup("Spawn Data")]
        [Tooltip("Optional per-mission profile swaps: the FIRST entry whose flags overlap the active " +
                 "mission's flags replaces Spawn Profile for the whole level. Sandbox / no-mission play " +
                 "always uses the default profile. Resolved once at Awake.")]
        [SerializeField] private List<MissionProfileOverride> missionProfileOverrides = new();

        /** One flag-conditional profile swap entry. */
        [Serializable]
        private class MissionProfileOverride
        {
            [Tooltip("Applies when the mission has ANY of these flags.")]
            public MissionFlags requireAny = MissionFlags.None;

            [Required] public SpawnProfile profile;
        }

        [FoldoutGroup("Spawn Data")]
        [ReadOnly, ShowInInspector, LabelText("Active Profile (runtime)")]
        private string ActiveProfileName => Application.isPlaying
            ? (_activeProfile != null ? _activeProfile.name : "<none>")
            : "resolved at Awake";

        // =====================
        // Determinism
        // =====================

        [FoldoutGroup("Determinism")]
        [Tooltip("How each chunk's RNG seed is derived. WorldSeedPlusCoord gives a " +
                 "reproducible-but-varied world per worldSeed; CoordHash is identical every run.")]
        [SerializeField] private SeedMode seedMode = SeedMode.WorldSeedPlusCoord;

        [FoldoutGroup("Determinism")]
        [Tooltip("World seed mixed into each chunk's RNG (used when seedMode is WorldSeedPlusCoord). " +
                 "Change it to reroll the whole world layout.")]
        [ShowIf(nameof(seedMode), SeedMode.WorldSeedPlusCoord)]
        [SerializeField] private int worldSeed = 12345;

        /** The world seed used to vary chunk layout (read by editor spawn-preview tools). */
        public int WorldSeed => worldSeed;

        // Surfaces the implicit camera dependency: not a serialized field, but
        // resolved automatically from Camera.main at Awake so it's clear a camera
        // is part of this component's references under the hood.
        [FoldoutGroup("Determinism")]
        [Tooltip("Resolved automatically from Camera.main at runtime — not assignable.")]
        [ReadOnly, ShowInInspector, LabelText("Camera (auto)")]
        private string CameraReference => Application.isPlaying
            ? (_camera != null ? _camera.name : "<none found>")
            : "Main Camera (resolved at runtime)";

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
            ? WorldToCell(_camera.transform.position).ToString()
            : "-";

        // =====================
        // State
        // =====================

        // Keyed by cell coordinate — persisted forever once generated
        private readonly Dictionary<Vector2Int, WorldChunk> _chunks
            = new Dictionary<Vector2Int, WorldChunk>();

        // Cached main camera — used every frame to decide which cells to generate
        private Camera _camera;

        // The profile actually driving chunks this level (default or mission override)
        private SpawnProfile _activeProfile;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        /**
         * Caches the scene's main camera once so Update doesn't pay the cost
         * of Camera.main's tagged lookup every frame, and resolves the spawn
         * profile once — the mission cannot change mid-level.
         */
        private void Awake()
        {
            _camera = Camera.main;
            _activeProfile = ResolveProfile();
        }

        /**
         * Picks the profile for this level: the first mission-flag override
         * matching the active mission wins, else the default spawnProfile.
         */
        private SpawnProfile ResolveProfile()
        {
            MissionSpec spec = MissionContext.Current;
            if (spec != null && missionProfileOverrides != null)
                foreach (MissionProfileOverride entry in missionProfileOverrides)
                    if (entry?.profile != null && (spec.flags & entry.requireAny) != 0)
                        return entry.profile;
            return spawnProfile;
        }

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
            Vector2Int center = WorldToCell(_camera.transform.position);

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

            // Hand the chunk its geometry, the resolved profile, and a deterministic seed
            WorldChunk chunk = cellGO.AddComponent<WorldChunk>();
            chunk.Initialize(topY, cellHeight, cellWidth * 0.5f, centerX,
                depth, _activeProfile, SeedFor(cell));

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

        /**
         * Derives a stable seed for a cell. The coordinate hash uses large
         * primes so neighbouring cells get well-separated seeds; worldSeed is
         * mixed in (when enabled) to vary whole-world layout between runs.
         */
        private int SeedFor(Vector2Int cell)
        {
            unchecked
            {
                int hash = cell.x * 73856093 ^ cell.y * 19349663;
                if (seedMode == SeedMode.WorldSeedPlusCoord)
                    hash ^= worldSeed * 83492791;
                return hash;
            }
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
            Debug.Log($"[ChunkSpawner] {_chunks.Count} cells generated. Camera at cell {WorldToCell(_camera.transform.position)}.");
        }

        /**
         * Draws each spawn rule's active depth band as a horizontal line in the
         * scene, colored per rule, so designers can see where things appear
         * relative to the surface (Y=0) without entering Play mode.
         */
        private void OnDrawGizmosSelected()
        {
            // At runtime show the resolved profile; in edit mode the default
            SpawnProfile profile = _activeProfile != null ? _activeProfile : spawnProfile;
            if (profile == null) return;

            float lineHalf = cellWidth * (spawnRadius + 1);
            int index = 0;

            foreach (SpawnRuleData rule in profile.AllRules)
            {
                // Distinct hue per rule for quick visual separation
                Gizmos.color = Color.HSVToRGB((index * 0.13f) % 1f, 0.7f, 1f);
                index++;

                // Top of the band (min depth → world Y = -minDepth)
                float minY = -rule.depth.minDepth;
                Gizmos.DrawLine(new Vector3(-lineHalf, minY, 0f), new Vector3(lineHalf, minY, 0f));

                // Bottom of the band when bounded
                if (rule.depth.hasMax)
                {
                    float maxY = -rule.depth.maxDepth;
                    Gizmos.DrawLine(new Vector3(-lineHalf, maxY, 0f), new Vector3(lineHalf, maxY, 0f));
                }

                UnityEditor.Handles.color = Gizmos.color;
                UnityEditor.Handles.Label(new Vector3(lineHalf, minY, 0f),
                    $"  {rule.ruleName} (from {rule.depth.minDepth:F0}m)");
            }
        }
#endif
    }
}
