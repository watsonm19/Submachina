using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Spawns decorative content across a parallax layer as the camera moves —
     * the layer-space sibling of ChunkSpawner.
     *
     * Why not WorldChunk? Chunks are world-anchored and persist forever, but a
     * layer with movement factor w ≠ 1 slides relative to the world, so its
     * decor must be tracked in LAYER-LOCAL space. The visible layer-local
     * window maps linearly (and invertibly) to camera position:
     *
     *   localCentre = camPos - layerPos = camPos·w + anchor·(1-w) - rest
     *
     * so every layer-local grid cell corresponds to a unique camera position,
     * giving each cell a stable deterministic seed AND an exact depth — decor
     * regenerates identically when the player returns after a despawn.
     *
     * Cells despawn + recycle through a pool by default (decor is stateless,
     * keeps object count bounded); disable despawnOutsideRadius to persist
     * forever like world chunks instead.
     *
     * Requires w > 0 on at least one axis: a camera-locked layer (w = 0,0) has
     * a static window and nothing to spawn — use ParallaxTiledLayer there.
     */
    [RequireComponent(typeof(ParallaxLayer))]
    public class ParallaxLayerSpawner : MonoBehaviour, IParallaxLayerExtension
    {
        // =====================
        // Spawn Data
        // =====================

        [FoldoutGroup("Spawn Data")]
        [Tooltip("The decor rules driving this layer. Create via Assets → Create → Submachina → Parallax → Decor Profile.")]
        [Required, InlineEditor(objectFieldMode: InlineEditorObjectFieldModes.Boxed)]
        [SerializeField] private ParallaxDecorProfile profile;

        // =====================
        // Grid
        // =====================

        [FoldoutGroup("Grid")]
        [Tooltip("Size of each layer-local grid cell in world units.")]
        [SerializeField, Min(5f)] private float cellSize = 20f;

        [FoldoutGroup("Grid")]
        [Tooltip("How many cells out from the visible centre to keep populated. " +
                 "Must cover half the widest possible view plus a margin (see the validation warning).")]
        [SerializeField, Min(1)] private int radiusCells = 2;

        [FoldoutGroup("Grid")]
        [Tooltip("Largest camera orthographic size assumed when validating coverage (match the camera's max zoom).")]
        [SerializeField, Min(1f)] private float assumedMaxOrthoSize = 16f;

        [FoldoutGroup("Grid")]
        [Tooltip("Aspect ratio assumed when validating coverage (2.4 ≈ 21:9 ultrawide safety).")]
        [SerializeField, Min(0.5f)] private float assumedAspect = 2.4f;

        // =====================
        // Determinism
        // =====================

        [FoldoutGroup("Determinism")]
        [Tooltip("Mixed into every cell seed so layers sharing a profile still generate differently.")]
        [SerializeField] private int layerSalt = 0;

        [FoldoutGroup("Determinism")]
        [Tooltip("Source of the world seed (ChunkSpawner). Auto-resolved from the scene when empty — " +
                 "decor then rerolls with the world, matching the main spawn system.")]
        [SerializeField] private ChunkSpawner worldSeedSource;

        // =====================
        // Lifecycle
        // =====================

        [FoldoutGroup("Lifecycle")]
        [Tooltip("Release cells (and pool their instances) once they fall outside the radius. " +
                 "Disable to persist decor forever like world chunks — object count then grows unbounded.")]
        [SerializeField] private bool despawnOutsideRadius = true;

        [FoldoutGroup("Lifecycle"), ShowIf(nameof(despawnOutsideRadius))]
        [Tooltip("Recycle despawned instances through a per-prefab pool instead of destroying them.")]
        [SerializeField] private bool pool = true;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private int LiveCellCount => _cells.Count;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private int PooledInstanceCount
        {
            get
            {
                int total = 0;
                foreach (Stack<GameObject> stack in _pool.Values) total += stack.Count;
                return total;
            }
        }

        // =====================
        // State
        // =====================

        /** Everything spawned for one grid cell, remembered for pooled release. */
        private class Cell
        {
            public readonly List<(GameObject prefab, GameObject instance)> spawned = new();
        }

        // Live cells keyed by layer-local grid coordinate
        private readonly Dictionary<Vector2Int, Cell> _cells = new();

        // Recycled instances per source prefab
        private readonly Dictionary<GameObject, Stack<GameObject>> _pool = new();

        // Scratch list for the per-cell spacing checks (avoids per-cell allocation)
        private readonly List<Vector2> _placedScratch = new();

        private ParallaxLayer _layer;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            _layer = GetComponent<ParallaxLayer>();
            if (worldSeedSource == null) worldSeedSource = FindFirstObjectByType<ChunkSpawner>();
        }

        private void OnValidate()
        {
            // The populated square must cover the widest possible view or decor pops in on-screen
            Vector2 viewHalf = CameraViewUtil.ViewHalfExtents(assumedMaxOrthoSize, assumedAspect);
            float needed = Mathf.Max(viewHalf.x, viewHalf.y) + cellSize;
            if (radiusCells * cellSize < needed)
                Debug.LogWarning($"[ParallaxLayerSpawner] {name}: radiusCells×cellSize " +
                                 $"({radiusCells * cellSize}) < view half-extent + margin ({needed:F0}) — " +
                                 "decor will spawn visibly on-screen. Increase radiusCells.", this);
        }

        // -------------------------------------------------------
        // IParallaxLayerExtension
        // -------------------------------------------------------

        /**
         * Called by ParallaxLayer after it repositions each frame. Keeps the
         * grid around the visible layer-local centre populated, and releases
         * far-away cells. Play mode only — edit preview must not spawn.
         */
        public void OnLayerUpdated(Vector3 camPos)
        {
            if (!Application.isPlaying || profile == null) return;
            if (_layer == null) _layer = GetComponent<ParallaxLayer>();

            // Camera-locked layers have a static window — nothing traverses, nothing to spawn
            Vector2 w = _layer.MovementFactor;
            if (Mathf.Abs(w.x) < 0.001f && Mathf.Abs(w.y) < 0.001f)
            {
                Debug.LogWarning($"[ParallaxLayerSpawner] {name}: layer is camera-locked (w=0) — " +
                                 "spawner disabled. Use ParallaxTiledLayer or hand-placed art instead.", this);
                enabled = false;
                return;
            }

            Vector2Int centre = LocalToCell(WorldToLocal(camPos));

            SpawnAround(centre);
            if (despawnOutsideRadius) ReleaseFarCells(centre);
        }

        // -------------------------------------------------------
        // Generation
        // -------------------------------------------------------

        /** Spawns any missing cell in the square around the visible centre. */
        private void SpawnAround(Vector2Int centre)
        {
            for (int dy = -radiusCells; dy <= radiusCells; dy++)
            {
                for (int dx = -radiusCells; dx <= radiusCells; dx++)
                {
                    Vector2Int cell = new Vector2Int(centre.x + dx, centre.y + dy);
                    if (!_cells.ContainsKey(cell))
                        SpawnCell(cell);
                }
            }
        }

        /**
         * Generates one layer-local cell: derives the deterministic seed and
         * exact depth, then runs every profile rule with a fresh RNG (matching
         * WorldChunk's per-cell isolation — rules never perturb other cells).
         */
        private void SpawnCell(Vector2Int cell)
        {
            Cell record = new Cell();
            _cells[cell] = record;

            System.Random rng = new System.Random(SeedFor(cell));
            float cellDepth = DepthForCell(cell);

            foreach (ParallaxDecorRule rule in profile.Rules)
            {
                if (rule.TotalWeight <= 0f) continue;
                if (!rule.depth.Contains(cellDepth)) continue;

                // Prevalence folds into density, then the count model resolves the instance count
                float prevalence = rule.prevalenceByDepth != null ? rule.prevalenceByDepth.Evaluate(cellDepth) : 1f;
                int n = rule.count.Evaluate(cellDepth, rng, profile.GlobalDensity * prevalence);

                // This rule's own placements for the spacing check (per-rule, like SpawnRuleData)
                _placedScratch.Clear();

                for (int i = 0; i < n; i++)
                    SpawnInstance(rule, cell, rng, record);
            }
        }

        /** Places and configures a single decor instance inside its cell. */
        private void SpawnInstance(ParallaxDecorRule rule, Vector2Int cell, System.Random rng, Cell record)
        {
            GameObject prefab = rule.PickPrefab(rng);
            if (prefab == null) return;

            // Uniform scatter inside the cell rect, retrying a few times for spacing (8, matching SpawnRuleData)
            Vector2 local = RandomPointInCell(cell, rng);
            if (rule.minSpacing > 0f && TooClose(local, rule.minSpacing))
            {
                bool placed = false;
                for (int r = 0; r < 8; r++)
                {
                    local = RandomPointInCell(cell, rng);
                    if (!TooClose(local, rule.minSpacing)) { placed = true; break; }
                }
                if (!placed) return;
            }
            _placedScratch.Add(local);

            // Reuse a pooled instance when available, otherwise instantiate fresh
            GameObject go = GetFromPool(prefab);
            if (go == null) go = Instantiate(prefab, transform);
            go.transform.localPosition = new Vector3(local.x, local.y, 0f);

            ApplyJitter(rule, prefab, go, rng);
            record.spawned.Add((prefab, go));
        }

        /**
         * Rolls the rule's visual variety onto an instance. All rolls happen
         * unconditionally so the RNG draw count is identical for fresh and
         * pooled instances — determinism is preserved either way.
         */
        private void ApplyJitter(ParallaxDecorRule rule, GameObject prefab, GameObject go, System.Random rng)
        {
            float scale = rng.NextFloat(rule.scaleRange.x, rule.scaleRange.y);
            float rot = rng.NextFloat(-rule.rotationJitter, rule.rotationJitter);
            bool flip = rule.randomFlipX && rng.NextBool();
            float alpha = rng.NextFloat(rule.alphaRange.x, rule.alphaRange.y);
            int orderOffset = rng.Next(rule.sortingOrderRange.x, rule.sortingOrderRange.y + 1);

            // Scale (with X mirror), rotation
            go.transform.localScale = new Vector3(flip ? -scale : scale, scale, 1f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, rot);

            // Alpha multiplies the PREFAB's authored alpha (never the instance's, so pooled
            // reuse can't compound), and sorting offsets the prefab's authored order.
            SpriteRenderer[] prefabRenderers = prefab.GetComponentsInChildren<SpriteRenderer>(true);
            SpriteRenderer[] instanceRenderers = go.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < instanceRenderers.Length && i < prefabRenderers.Length; i++)
            {
                Color c = prefabRenderers[i].color;
                c.a *= alpha;
                instanceRenderers[i].color = c;
                instanceRenderers[i].sortingOrder = prefabRenderers[i].sortingOrder + orderOffset;
            }
        }

        // -------------------------------------------------------
        // Despawn & pooling
        // -------------------------------------------------------

        /** Releases every cell more than one ring outside the populated radius. */
        private void ReleaseFarCells(Vector2Int centre)
        {
            List<Vector2Int> toRelease = null;
            foreach (KeyValuePair<Vector2Int, Cell> kv in _cells)
            {
                // Chebyshev distance: cells stay for one extra ring of hysteresis so
                // hovering on a cell boundary doesn't churn spawn/despawn every frame
                int dist = Mathf.Max(Mathf.Abs(kv.Key.x - centre.x), Mathf.Abs(kv.Key.y - centre.y));
                if (dist <= radiusCells + 1) continue;

                (toRelease ??= new List<Vector2Int>()).Add(kv.Key);
            }
            if (toRelease == null) return;

            foreach (Vector2Int cell in toRelease)
            {
                foreach ((GameObject prefab, GameObject instance) in _cells[cell].spawned)
                    Release(prefab, instance);
                _cells.Remove(cell);
            }
        }

        /** Pops a recycled instance for this prefab, or null when none is available. */
        private GameObject GetFromPool(GameObject prefab)
        {
            if (!pool || !_pool.TryGetValue(prefab, out Stack<GameObject> stack) || stack.Count == 0)
                return null;

            GameObject go = stack.Pop();
            if (go == null) return null; // destroyed externally (e.g. scene teardown)
            go.SetActive(true);
            return go;
        }

        /** Returns an instance to its prefab's pool (or destroys it when pooling is off). */
        private void Release(GameObject prefab, GameObject instance)
        {
            if (instance == null) return;

            if (!pool) { Destroy(instance); return; }

            instance.SetActive(false);
            if (!_pool.TryGetValue(prefab, out Stack<GameObject> stack))
                _pool[prefab] = stack = new Stack<GameObject>();
            stack.Push(instance);
        }

        // -------------------------------------------------------
        // Layer-space mapping
        // -------------------------------------------------------

        /**
         * The layer-local point currently at the centre of the view. Because
         * layerPos = rest + (camPos - anchor)(1 - w), this equals
         * camPos·w + anchor·(1-w) - rest — linear in camPos, so cells map to
         * unique camera positions. Assumes the layer root is unscaled.
         */
        private Vector2 WorldToLocal(Vector3 camPos)
        {
            return camPos - transform.position;
        }

        /** Layer-local position → grid cell (FloorToInt handles negatives correctly). */
        private Vector2Int LocalToCell(Vector2 local)
        {
            return new Vector2Int(
                Mathf.FloorToInt(local.x / cellSize),
                Mathf.FloorToInt(local.y / cellSize));
        }

        /** True if a candidate is within minSpacing of a point this rule already placed in the cell. */
        private bool TooClose(Vector2 candidate, float minSpacing)
        {
            float sqr = minSpacing * minSpacing;
            for (int i = 0; i < _placedScratch.Count; i++)
                if ((_placedScratch[i] - candidate).sqrMagnitude < sqr) return true;
            return false;
        }

        /** Uniform random layer-local point inside a cell's rect. */
        private Vector2 RandomPointInCell(Vector2Int cell, System.Random rng)
        {
            return new Vector2(
                rng.NextFloat(cell.x * cellSize, (cell.x + 1) * cellSize),
                rng.NextFloat(cell.y * cellSize, (cell.y + 1) * cellSize));
        }

        /**
         * The depth (positive metres below surface) the camera sits at when
         * this cell is centred in view — the inverse of the layer mapping:
         *
         *   cellCamY = (localY + rest.y - anchor.y·(1-w_y)) / w_y
         *
         * Falls back to the live camera depth when the layer doesn't scroll
         * vertically (w_y ≈ 0), where no unique inverse exists.
         */
        private float DepthForCell(Vector2Int cell)
        {
            float wy = _layer.MovementFactor.y;
            if (Mathf.Abs(wy) < 0.001f)
            {
                Camera cam = Camera.main;
                return cam != null ? Mathf.Max(0f, -cam.transform.position.y) : 0f;
            }

            float localY = (cell.y + 0.5f) * cellSize;
            float camY = (localY + _layer.RestPosition.y - _layer.Anchor.y * (1f - wy)) / wy;
            return Mathf.Max(0f, -camY);
        }

        /**
         * Deterministic per-cell seed: the project's spawn hash pattern plus a
         * per-layer salt so layers sharing one profile still differ.
         */
        private int SeedFor(Vector2Int cell)
        {
            unchecked
            {
                int hash = cell.x * 73856093 ^ cell.y * 19349663;
                if (worldSeedSource != null) hash ^= worldSeedSource.WorldSeed * 83492791;
                hash ^= layerSalt * 0x2545F491;
                return hash;
            }
        }

        // -------------------------------------------------------
        // Editor Utilities
        // -------------------------------------------------------

#if UNITY_EDITOR
        [FoldoutGroup("Debug")]
        [Button("Test Spawn Around Camera"), GUIColor(0.6f, 0.8f, 1f)]
        [Tooltip("Runs one spawn pass around the current camera position (edit or play mode) to preview density.")]
        private void TestSpawnAroundCamera()
        {
            Camera cam = Camera.main;
            if (cam == null || profile == null) { Debug.Log("[ParallaxLayerSpawner] Needs a main camera and a profile."); return; }

            if (_layer == null) _layer = GetComponent<ParallaxLayer>();
            if (worldSeedSource == null) worldSeedSource = FindFirstObjectByType<ChunkSpawner>();

            SpawnAround(LocalToCell(WorldToLocal(cam.transform.position)));
            Debug.Log($"[ParallaxLayerSpawner] {name}: {_cells.Count} cells live.");
        }

        [FoldoutGroup("Debug")]
        [Button("Clear All Spawned"), GUIColor(1f, 0.7f, 0.6f)]
        [Tooltip("Destroys every spawned instance and empties the pool.")]
        private void ClearAllSpawned()
        {
            foreach (Cell cell in _cells.Values)
                foreach ((GameObject _, GameObject instance) in cell.spawned)
                    if (instance != null) DestroyImmediateSafe(instance);
            _cells.Clear();

            foreach (Stack<GameObject> stack in _pool.Values)
                while (stack.Count > 0)
                {
                    GameObject go = stack.Pop();
                    if (go != null) DestroyImmediateSafe(go);
                }
            _pool.Clear();
        }

        /** Destroy that works in both edit mode (immediate) and play mode. */
        private static void DestroyImmediateSafe(GameObject go)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
#endif
    }
}
