using UnityEngine;

namespace Submachina.Core
{
    /**
     * Configuration for a single enemy type within a chunk.
     * Passed from ChunkSpawner so spawn chances and depth gates are
     * tunable in the inspector without touching WorldChunk logic.
     */
    [System.Serializable]
    public struct EnemySpawnConfig
    {
        /** Prefab to instantiate. Leave null to disable this enemy type. */
        public GameObject prefab;

        /** Probability (0–1) that one of this enemy spawns per chunk.
         *  Example: 0.4 → roughly 2 in 5 chunks contain one. */
        [Range(0f, 1f)] public float spawnChance;

        /** Minimum depth (metres) before this enemy can appear.
         *  Example: 200 → never spawns shallower than 200m. */
        [Min(0f)] public float minDepth;
    }

    /**
     * A single procedurally-generated section of the underwater world.
     *
     * Spawned and initialized by ChunkSpawner. Each chunk occupies a fixed
     * vertical slice of the world and generates its own rock obstacles as
     * child GameObjects when initialized.
     *
     * Rocks are split into two types:
     *   - Wall protrusions: rectangles jutting in from the left or right edge,
     *     creating natural bottlenecks and trench walls.
     *   - Center obstacles: smaller rocks scattered in the open space,
     *     forcing the player to weave rather than fly straight down.
     *
     * Both rock counts and sizes scale with depth so the world gets
     * progressively denser and tighter as the player descends.
     *
     * Rocks are instantiated from a prefab assigned in ChunkSpawner so the
     * SpriteRenderer material is correctly configured in the editor.
     */
    public class WorldChunk : MonoBehaviour
    {
        // Chunk bounds — stored after Initialize for gizmo drawing
        private float _topY;
        private float _height;
        private float _halfWidth;
        private GameObject _rockPrefab;
        private GameObject _resourcePrefab;
        private GameObject _enemyPrefab;
        private GameObject _o2BubblePrefab;
        private EnemySpawnConfig _passiveCreature;
        private EnemySpawnConfig _rammingEnemy;
        private float _o2SizeMin;
        private float _o2SizeMax;
        private AnimationCurve _o2SizeDistribution;

        // -------------------------------------------------------
        // Initialization
        // -------------------------------------------------------

        /**
         * Called by ChunkSpawner immediately after instantiation.
         * Spawned prefabs (resources, enemies, pickups) resolve their own
         * submarine dependencies via Submarine.All or collision context.
         *
         * o2SizeMin/o2SizeMax control the random size range for passive O2
         * bubbles in this chunk — evaluated from ChunkSpawner's depth curves.
         * o2SizeDistribution remaps the uniform roll to bias toward certain
         * sizes (null = uniform distribution).
         */
        public void Initialize(float topY, float height, float halfWidth, float depth,
            GameObject rockPrefab, GameObject resourcePrefab,
            GameObject enemyPrefab, GameObject o2BubblePrefab,
            EnemySpawnConfig passiveCreature, EnemySpawnConfig rammingEnemy,
            float o2SizeMin = 1f, float o2SizeMax = 1f,
            AnimationCurve o2SizeDistribution = null)
        {
            _topY = topY;
            _height = height;
            _halfWidth = halfWidth;
            _rockPrefab = rockPrefab;
            _resourcePrefab = resourcePrefab;
            _enemyPrefab = enemyPrefab;
            _o2BubblePrefab = o2BubblePrefab;
            _passiveCreature = passiveCreature;
            _rammingEnemy = rammingEnemy;
            _o2SizeMin = o2SizeMin;
            _o2SizeMax = o2SizeMax;
            _o2SizeDistribution = o2SizeDistribution;

            GenerateObstacles(depth);
            GenerateResources(depth);
            GeneratePassiveO2(depth);
            GenerateEnemies(depth);
        }

        // -------------------------------------------------------
        // Generation
        // -------------------------------------------------------

        /**
         * Determines total rock count based on depth and distributes them
         * between wall-hugging and center obstacles.
         *
         * Count formula: lerp from 2 → 9 over the first 300 metres.
         * Example: depth=150 → ~5 rocks, depth=300+ → 9 rocks.
         */
        private void GenerateObstacles(float depth)
        {
            // Near-surface grace zone: first 10m has no obstacles so player can orient
            if (depth < 10f) return;
            if (_rockPrefab == null) { Debug.LogWarning("[WorldChunk] Rock prefab not assigned."); return; }

            int totalCount = Mathf.RoundToInt(Mathf.Lerp(2f, 9f, Mathf.Clamp01(depth / 300f)));
            int wallCount = totalCount / 2;
            int centerCount = totalCount - wallCount;

            for (int i = 0; i < wallCount; i++) SpawnWallObstacle(depth);
            for (int i = 0; i < centerCount; i++) SpawnCenterObstacle(depth);
        }

        /**
         * Spawns a rectangle jutting inward from the left or right edge.
         * Protrusion amount scales with depth so walls close in over time.
         *
         * Example: at depth=200, protrusion can reach up to 55% of halfWidth.
         */
        private void SpawnWallObstacle(float depth)
        {
            bool leftSide = Random.value > 0.5f;

            float maxProtrusion = Mathf.Lerp(_halfWidth * 0.25f, _halfWidth * 0.6f, Mathf.Clamp01(depth / 300f));
            float protrusion = Random.Range(_halfWidth * 0.15f, maxProtrusion);
            float rockHeight = Random.Range(1.5f, 4.5f);

            // Center the rock on the wall edge so it appears flush
            float x = leftSide
                ? -_halfWidth + protrusion * 0.5f
                : _halfWidth - protrusion * 0.5f;
            float y = Random.Range(_topY - _height + 1f, _topY - 1f);

            SpawnRockAt(x, y, protrusion, rockHeight);
        }

        /**
         * Spawns a smaller rock within the central 60% of the chunk width.
         * Constrained to the center band so wall protrusions and center rocks
         * don't stack and block the entire passage.
         */
        private void SpawnCenterObstacle(float depth)
        {
            float maxSize = Mathf.Lerp(1.2f, 3.5f, Mathf.Clamp01(depth / 300f));
            float w = Random.Range(0.8f, maxSize);
            float h = Random.Range(0.5f, maxSize * 0.75f);
            float x = Random.Range(-_halfWidth * 0.55f, _halfWidth * 0.55f);
            float y = Random.Range(_topY - _height + 1f, _topY - 1f);

            SpawnRockAt(x, y, w, h);
        }

        /**
         * Scatters mining resource pickups throughout the chunk.
         * Resources are rarer than rocks — 1 to 3 per chunk — and intentionally
         * placed near the edges and center to reward exploration over just flying straight down.
         *
         * Count scales slightly with depth so deeper zones feel more rewarding.
         * Example: depth=0 → 1 resource, depth=300+ → 3 resources.
         */
        private void GenerateResources(float depth)
        {
            if (_resourcePrefab == null) return;

            int count = Mathf.RoundToInt(Mathf.Lerp(3f, 6f, Mathf.Clamp01(depth / 300f)));

            for (int i = 0; i < count; i++)
            {
                float x = Random.Range(-_halfWidth * 0.8f, _halfWidth * 0.8f);
                float y = Random.Range(_topY - _height + 1f, _topY - 1f);

                Instantiate(_resourcePrefab, new Vector3(transform.position.x + x, y, 0f), Quaternion.identity, transform);
            }
        }

        /**
         * Scatters a small number of O2 bubbles passively throughout the chunk.
         * These exist independently of enemy drops and give the player a baseline
         * source of capacity restoration while exploring.
         *
         * Count is intentionally low (1–2) so combat still drives the main loop.
         */
        private void GeneratePassiveO2(float depth)
        {
            if (_o2BubblePrefab == null) return;

            int count = depth < 3f ? 0 : Random.Range(1, 3);

            for (int i = 0; i < count; i++)
            {
                float x = Random.Range(-_halfWidth * 0.7f, _halfWidth * 0.7f);
                float y = Random.Range(_topY - _height + 1f, _topY - 1f);

                GameObject bubble = Instantiate(_o2BubblePrefab,
                    new Vector3(transform.position.x + x, y, 0f),
                    Quaternion.identity, transform);

                // Apply depth-driven size, remapped through the distribution curve
                O2Pickup pickup = bubble.GetComponent<O2Pickup>();
                if (pickup != null)
                {
                    float t = _o2SizeDistribution != null
                        ? _o2SizeDistribution.Evaluate(Random.value)
                        : Random.value;
                    pickup.SetSize(Mathf.Lerp(_o2SizeMin, _o2SizeMax, t));
                }
            }
        }

        /**
         * Scatters enemies throughout the chunk, scaled by depth.
         * Grace zone is deeper than resources (20m) since enemies at the surface
         * would kill new players before they understand the combat system.
         *
         * Count formula: lerp 0 → 3 over the first 400 metres.
         * Example: depth=200 → ~1-2 enemies, depth=400+ → up to 3 enemies.
         */
        /**
         * Scatters enemies throughout the chunk, scaled by depth.
         *
         * Three enemy types, each with independent spawn rules:
         *   Regular enemy  — grace zone 20m, scales 1→4 over first 400m.
         *   Passive creature — one per chunk at spawnChance probability, no depth gate.
         *   Ramming enemy  — one per chunk at spawnChance probability, minDepth gate.
         */
        private void GenerateEnemies(float depth)
        {
            // Regular enemy — scales with depth, grace zone below 20m
            if (_enemyPrefab != null && depth >= 20f)
            {
                int count = Mathf.RoundToInt(Mathf.Lerp(1f, 4f, Mathf.Clamp01((depth - 20f) / 380f)));
                for (int i = 0; i < count; i++)
                {
                    float x = Random.Range(-_halfWidth * 0.65f, _halfWidth * 0.65f);
                    float y = Random.Range(_topY - _height + 2f, _topY - 2f);
                    Instantiate(_enemyPrefab, new Vector3(transform.position.x + x, y, 0f), Quaternion.identity, transform);
                }
            }

            // Passive creature — rare, spawns anywhere (no depth gate beyond minDepth)
            if (_passiveCreature.prefab != null && depth >= _passiveCreature.minDepth
                && Random.value < _passiveCreature.spawnChance)
            {
                float x = Random.Range(-_halfWidth * 0.8f, _halfWidth * 0.8f);
                float y = Random.Range(_topY - _height + 1f, _topY - 1f);
                Instantiate(_passiveCreature.prefab, new Vector3(transform.position.x + x, y, 0f), Quaternion.identity, transform);
            }

            // Ramming enemy — rare, deep water only
            if (_rammingEnemy.prefab != null && depth >= _rammingEnemy.minDepth
                && Random.value < _rammingEnemy.spawnChance)
            {
                float x = Random.Range(-_halfWidth * 0.65f, _halfWidth * 0.65f);
                float y = Random.Range(_topY - _height + 2f, _topY - 2f);
                Instantiate(_rammingEnemy.prefab, new Vector3(transform.position.x + x, y, 0f), Quaternion.identity, transform);
            }
        }

        /**
         * Instantiates the rock prefab at the given world position and scale.
         * Scale is applied to the prefab instance so the BoxCollider2D scales with it.
         */
        private void SpawnRockAt(float x, float y, float w, float h)
        {
            // x is chunk-local — offset by chunk world X so rocks follow the chunk position
            GameObject rock = Instantiate(_rockPrefab, new Vector3(transform.position.x + x, y, 0f), Quaternion.identity, transform);
            rock.transform.localScale = new Vector3(w, h, 1f);
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
