using System;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Depth zone classification for a level.
     * Passed from ChunkSpawner to WorldChunk so spawn logic
     * reads from zone configs rather than raw depth math.
     */
    public enum ZoneType { Shallow, Midnight, Abyss }

    /**
     * Per-level data asset defining the trench's shape, zone boundaries,
     * and spawn budgets for each depth tier.
     *
     * Zone thresholds are normalized (0–1 of totalDepth) so levels of
     * different lengths automatically scale without manual recalculation.
     *
     * Example (totalDepth = 400):
     *   midnightStart = 0.35 → Midnight begins at 140m
     *   abyssStart    = 0.70 → Abyss begins at 280m
     *   exitGateDepth = 0.88 → Exit gates at 352m (Abyss continues to 400m)
     *
     * Create via: Assets → Create → Submachina → Level Config
     */
    [CreateAssetMenu(fileName = "LevelConfig", menuName = "Submachina/Level Config")]
    public class LevelConfig : ScriptableObject
    {
        // =====================
        // Level Identity
        // =====================

        [FoldoutGroup("Level")]
        [Tooltip("Display name shown in UI.")]
        [SerializeField] private string levelName = "Level 1";

        [FoldoutGroup("Level")]
        [Tooltip("Zero-based level index used for difficulty scaling across runs.")]
        [SerializeField, Min(0)] private int levelIndex = 0;

        // =====================
        // World Shape
        // =====================

        [FoldoutGroup("World Shape")]
        [Tooltip("Total depth of the level in world units. " +
                 "The trench bottom (and elite zone) sits at -totalDepth.")]
        [SerializeField, Min(100f)] private float totalDepth = 400f;

        [FoldoutGroup("World Shape")]
        [Tooltip("Half-width of the playable trench from center (X=0). " +
                 "Example: 100 = 200 units total, roughly 5-6 screens wide.")]
        [SerializeField, Min(20f)] private float halfWidth = 100f;

        // =====================
        // Zone Thresholds
        // =====================

        [FoldoutGroup("Zone Thresholds")]
        [Tooltip("Normalized depth (0–1) where the Midnight Zone begins. " +
                 "Example: 0.35 at totalDepth=400 → Midnight starts at 140m.")]
        [SerializeField, Range(0.05f, 0.95f)] private float midnightStart = 0.35f;

        [FoldoutGroup("Zone Thresholds")]
        [Tooltip("Normalized depth (0–1) where the Abyss begins. Must be > midnightStart.")]
        [SerializeField, Range(0.1f, 0.99f)] private float abyssStart = 0.70f;

        [FoldoutGroup("Zone Thresholds")]
        [Tooltip("Normalized depth (0–1) where the Exit Gates are placed. " +
                 "The optional Elite Encounter lives between this depth and totalDepth.")]
        [SerializeField, Range(0.1f, 0.99f)] private float exitGateDepth = 0.88f;

        // =====================
        // Zone Spawn Configs
        // =====================

        [FoldoutGroup("Shallow Zone")]
        [Tooltip("Spawn budgets for the early level — high O2, basic enemies, low-tier resources.")]
        [SerializeField] private ZoneConfig shallow = new ZoneConfig
        {
            enemyMin = 0, enemyMax = 1, enemyGraceDepth = 20f,
            passiveO2Min = 1, passiveO2Max = 2,
            resourceMin = 1, resourceMax = 2
        };

        [FoldoutGroup("Midnight Zone")]
        [Tooltip("Spawn budgets for mid-level — reduced O2, more aggressive enemies, better resources.")]
        [SerializeField] private ZoneConfig midnight = new ZoneConfig
        {
            enemyMin = 1, enemyMax = 3, enemyGraceDepth = 0f,
            passiveO2Min = 0, passiveO2Max = 1,
            resourceMin = 1, resourceMax = 3
        };

        [FoldoutGroup("Abyss Zone")]
        [Tooltip("Spawn budgets for late level — no passive O2, hyper-aggressive enemies, rare scrap.")]
        [SerializeField] private ZoneConfig abyss = new ZoneConfig
        {
            enemyMin = 2, enemyMax = 4, enemyGraceDepth = 0f,
            passiveO2Min = 0, passiveO2Max = 0,
            resourceMin = 1, resourceMax = 2
        };

        // =====================
        // Public API
        // =====================

        public string LevelName  => levelName;
        public int    LevelIndex => levelIndex;
        public float  TotalDepth => totalDepth;
        public float  HalfWidth  => halfWidth;

        /** World-space Y of the exit gates (negative, below surface). */
        public float ExitGateWorldY => -(totalDepth * exitGateDepth);

        /**
         * Returns the ZoneType for a given absolute depth (positive metres below surface).
         * Example: depth=300, totalDepth=400, abyssStart=0.70 → 300/400=0.75 >= 0.70 → Abyss
         */
        public ZoneType GetZone(float depth)
        {
            float t = Mathf.Clamp01(depth / totalDepth);
            if (t >= abyssStart)    return ZoneType.Abyss;
            if (t >= midnightStart) return ZoneType.Midnight;
            return ZoneType.Shallow;
        }

        /** Returns the ZoneConfig for the given zone type. */
        public ZoneConfig GetZoneConfig(ZoneType zone)
        {
            return zone switch
            {
                ZoneType.Midnight => midnight,
                ZoneType.Abyss    => abyss,
                _                 => shallow
            };
        }

        /** Convenience overload — resolves zone from depth then returns its config. */
        public ZoneConfig GetZoneConfig(float depth) => GetZoneConfig(GetZone(depth));

        // =====================
        // Editor Utilities
        // =====================

#if UNITY_EDITOR
        [FoldoutGroup("Zone Thresholds")]
        [Button("Preview Zone Depths"), GUIColor(0.6f, 0.8f, 1f)]
        private void PreviewZones()
        {
            Debug.Log($"[LevelConfig] '{levelName}' zone depths:\n" +
                      $"  Shallow:  0m – {totalDepth * midnightStart:F0}m\n" +
                      $"  Midnight: {totalDepth * midnightStart:F0}m – {totalDepth * abyssStart:F0}m\n" +
                      $"  Abyss:    {totalDepth * abyssStart:F0}m – {totalDepth:F0}m\n" +
                      $"  Exit Gate at: {totalDepth * exitGateDepth:F0}m");
        }
#endif
    }

    /**
     * Spawn budget for a single depth zone.
     * Min/max ranges add natural variation between chunks in the same zone.
     * SampleX() methods return a random value in the range each time they're called.
     */
    [Serializable]
    public class ZoneConfig
    {
        // ---- Enemies ----

        [HorizontalGroup("Enemies"), LabelWidth(80)]
        [Tooltip("Minimum enemies spawned per chunk in this zone.")]
        public int enemyMin = 0;

        [HorizontalGroup("Enemies"), LabelWidth(80)]
        [Tooltip("Maximum enemies spawned per chunk in this zone.")]
        public int enemyMax = 2;

        [Tooltip("Enemies are suppressed above this absolute depth (metres). " +
                 "Set to 0 for zones where enemies always spawn.")]
        public float enemyGraceDepth = 0f;

        // ---- Passive O2 ----

        [HorizontalGroup("O2"), LabelWidth(80)]
        [Tooltip("Minimum passive O2 bubbles per chunk.")]
        public int passiveO2Min = 0;

        [HorizontalGroup("O2"), LabelWidth(80)]
        [Tooltip("Maximum passive O2 bubbles per chunk.")]
        public int passiveO2Max = 2;

        // ---- Resources ----

        [HorizontalGroup("Resources"), LabelWidth(80)]
        [Tooltip("Minimum resource nodes per chunk.")]
        public int resourceMin = 1;

        [HorizontalGroup("Resources"), LabelWidth(80)]
        [Tooltip("Maximum resource nodes per chunk.")]
        public int resourceMax = 2;

        // ---- Sampling ----

        public int SampleEnemyCount()   => UnityEngine.Random.Range(enemyMin, enemyMax + 1);
        public int SampleO2Count()      => UnityEngine.Random.Range(passiveO2Min, passiveO2Max + 1);
        public int SampleResourceCount()=> UnityEngine.Random.Range(resourceMin, resourceMax + 1);
    }
}
