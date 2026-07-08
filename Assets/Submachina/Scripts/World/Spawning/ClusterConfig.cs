using System;
using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Recipe for a procedurally built scatter cluster: a handful of inert
     * "filler" prefabs arranged naturally around a center, sometimes joined by
     * one value prefab (e.g. a minable ore nugget) whose presence is protected
     * against bad-luck droughts by SpawnLuck.
     *
     * Purely data — ClusterBuilder reads this at build time. One config asset
     * can describe each cluster flavor (ore, coral, wreckage, ...) and be shared
     * by any number of cluster prefabs.
     *
     * Create via Assets → Create → Submachina → Spawning → Cluster Config.
     */
    [CreateAssetMenu(fileName = "ClusterConfig", menuName = "Submachina/Spawning/Cluster Config")]
    public class ClusterConfig : ScriptableObject
    {
        /** One weighted entry in the filler-rock pool. Weight 2 = twice as likely as weight 1. */
        [Serializable]
        public struct WeightedPrefab
        {
            [HorizontalGroup, HideLabel, Required, AssetsOnly]
            public GameObject prefab;

            [HorizontalGroup(width: 90), LabelWidth(45), Min(0f)]
            public float weight;
        }

        // =====================
        // Rocks
        // =====================

        [Title("Rocks")]
        [InfoBox("The inert filler prefabs scattered to form the cluster body. Weights are relative: " +
                 "an entry with weight 2 appears twice as often as one with weight 1.",
            InfoMessageType.None, "@Submachina.Core.SpawnDocs.ShowHelp")]
        [Tooltip("Weighted pool of inert prefabs; one is picked per rock.")]
        [ListDrawerSettings(DefaultExpandedState = true)]
        public List<WeightedPrefab> rockPrefabs = new List<WeightedPrefab>();

        [HorizontalGroup("Count"), LabelWidth(90)]
        [Tooltip("Fewest rocks a cluster can roll.")]
        [Min(0)] public int countMin = 4;

        [HorizontalGroup("Count"), LabelWidth(90)]
        [Tooltip("Most rocks a cluster can roll.")]
        [Min(0)] public int countMax = 8;

        // =====================
        // Layout
        // =====================

        [Title("Layout")]
        [Tooltip("Maximum scatter distance from the cluster center, in local units.")]
        [Min(0f)] public float radius = 2.5f;

        [Tooltip("Where rocks land within the radius. X = a uniform 0-1 roll per rock, " +
                 "Y = normalized distance from center (0 = center, 1 = rim). " +
                 "Straight diagonal = uniform spread; flat-then-steep = dense core with a sparse rim.")]
        public AnimationCurve radialFalloff = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("Minimum distance between rock centers. 0 = rocks may overlap freely. " +
                 "Placements that can't satisfy this after a few retries are skipped.")]
        [Min(0f)] public float minSpacing = 0.5f;

        // =====================
        // Size / rotation / flip
        // =====================

        [Title("Size / Rotation / Flip")]
        [HorizontalGroup("Scale"), LabelWidth(90)]
        [Tooltip("Smallest scale a rock can roll.")]
        [Min(0.01f)] public float scaleMin = 0.6f;

        [HorizontalGroup("Scale"), LabelWidth(90)]
        [Tooltip("Largest scale a rock can roll.")]
        [Min(0.01f)] public float scaleMax = 1.4f;

        [Tooltip("Biases which scales are common vs rare. X = uniform 0-1 roll, Y = position in the " +
                 "scale range (0 = min, 1 = max). Flat-then-steep = mostly small rocks, rare big ones.")]
        public AnimationCurve scaleDistribution = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("Each rock rolls a Z rotation within ± this many degrees.")]
        [Range(0f, 180f)] public float maxRotation = 180f;

        [HorizontalGroup("Flip"), LabelWidth(90)]
        [Tooltip("Chance to mirror a rock horizontally (negative X scale, so colliders flip too).")]
        [Range(0f, 1f)] public float flipXChance = 0.5f;

        [HorizontalGroup("Flip"), LabelWidth(90)]
        [Tooltip("Chance to mirror a rock vertically.")]
        [Range(0f, 1f)] public float flipYChance = 0f;

        [Tooltip("Give each rock's SpriteRenderers a sorting order equal to its spawn index so " +
                 "overlaps stack stably under the cluster root's SortingGroup.")]
        public bool orderChildrenBySpawnIndex = true;

        // =====================
        // Value
        // =====================

        [Title("Value")]
        [InfoBox("The valuable prefab (its own colliders / MiningResource) that sometimes joins the " +
                 "cluster. The chance is protected by the pity settings below — long droughts ramp the " +
                 "chance up until a value spawn is guaranteed.",
            InfoMessageType.None, "@Submachina.Core.SpawnDocs.ShowHelp")]
        [Tooltip("Prefab spawned on a successful value roll. Null = clusters are always inert.")]
        [AssetsOnly] public GameObject valuePrefab;

        [Tooltip("Base chance that a cluster contains the value prefab.")]
        [Range(0f, 1f)] public float valueChance = 0.1f;

        [Tooltip("Pulls the value prefab toward the cluster center: 1 = dead center, " +
                 "0 = same spread as the rocks.")]
        [Range(0f, 1f)] public float valueCenterBias = 0.7f;

        [Tooltip("On: the value prefab rolls the same scale/rotation/flip as the rocks. " +
                 "Off: it keeps its authored transform.")]
        public bool randomizeValueTransform = false;

        // =====================
        // Luck
        // =====================

        [Title("Luck")]
        [Tooltip("SpawnLuck streak key. Configs sharing a key share one pity pool. " +
                 "Defaults to this asset's name.")]
        public string luckKey;

        [InlineProperty, HideLabel]
        [InfoBox("$PityPreview", InfoMessageType.None)]
        public PitySettings pity = PitySettings.Default;

        // -------------------------------------------------------
        // Runtime helpers (used by ClusterBuilder)
        // -------------------------------------------------------

        /**
         * Picks one rock prefab from the weighted pool, consuming exactly ONE
         * rng sample so callers can rely on a fixed draw count per rock.
         * Null/non-positive-weight entries are skipped. Returns null if the
         * pool is empty or has no usable weight.
         *
         * Example: weights A=1, B=3 → B is picked ~75% of the time.
         */
        public GameObject PickRock(System.Random rng)
        {
            // Total usable weight — skip broken entries rather than corrupting odds
            float total = 0f;
            foreach (WeightedPrefab entry in rockPrefabs)
                if (entry.prefab != null && entry.weight > 0f) total += entry.weight;
            if (total <= 0f) return null;

            // Walk the pool until the roll falls inside an entry's slice
            float roll = rng.NextFloat(0f, total);
            foreach (WeightedPrefab entry in rockPrefabs)
            {
                if (entry.prefab == null || entry.weight <= 0f) continue;
                roll -= entry.weight;
                if (roll < 0f) return entry.prefab;
            }

            // Float edge case (roll == total) — fall back to the last usable entry
            for (int i = rockPrefabs.Count - 1; i >= 0; i--)
                if (rockPrefabs[i].prefab != null && rockPrefabs[i].weight > 0f) return rockPrefabs[i].prefab;
            return null;
        }

        /** Default the luck key to the asset name so designers never hand-type one. */
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(luckKey)) luckKey = name;
        }

        // -------------------------------------------------------
        // Editor preview / tools
        // -------------------------------------------------------

        /**
         * Live plain-language summary of the pity ramp: guarantee point and the
         * long-run effective rate. Rate math: the expected rolls per hit is
         * E = Σ q_m, where q_m = Π_{j<m}(1 - effective(j)) is the probability a
         * streak survives to m misses; the effective rate is then 1/E.
         */
        private string PityPreview
        {
            get
            {
                if (valueChance <= 0f) return "Value chance is 0 — clusters never roll the value prefab.";
                if (!pity.enabled) return $"Pity off — flat {valueChance:P0} chance, droughts are unbounded.";
                if (valueChance >= 1f) return "Value chance is 100% — every cluster has the value prefab.";

                // Walk streaks accumulating survival probability until a guaranteed hit
                float survive = 1f;      // q_m: chance the streak reaches m misses
                float expectedRolls = 0f; // Σ q_m
                int guaranteedAt = -1;
                for (int m = 0; m < 10000; m++)
                {
                    expectedRolls += survive;
                    float chance = SpawnLuck.EffectiveChanceAtStreak(m, valueChance, in pity);
                    if (chance >= 1f) { guaranteedAt = m; break; }
                    survive *= 1f - chance;
                    if (survive < 1e-7f) break; // ramp never reaches exactly 1 — sum has converged
                }

                float rate = expectedRolls > 0f ? 1f / expectedRolls : 1f;
                string guarantee = guaranteedAt >= 0
                    ? $"guaranteed at miss {guaranteedAt} (cluster #{guaranteedAt + 1} at latest)"
                    : "never fully guaranteed (ramp shape stays below 1)";
                return $"{valueChance:P0} base → ramp starts after miss {pity.graceMisses} → {guarantee}.\n" +
                       $"Long-run: ~{rate:P1} (≈1 in {expectedRolls:F1} clusters).";
            }
        }

#if UNITY_EDITOR
        [Title("Editor Tools")]
        [Button("Spawn Test Cluster", ButtonSizes.Medium), GUIColor(0.6f, 0.9f, 0.6f)]
        private void SpawnTestCluster()
        {
            // Build a throwaway cluster in the open scene with a random seed
            GameObject go = new GameObject($"TEST_Cluster_{name}");
            ClusterBuilder builder = go.AddComponent<ClusterBuilder>();
            builder.Config = this;
            int seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            builder.BuildFromSeed(seed, 0f);
            UnityEditor.Selection.activeGameObject = go;
            Debug.Log($"[ClusterConfig] Test cluster built with seed {seed} — {go.transform.childCount} children.", go);
        }

        [Button("Clear Test Clusters"), GUIColor(0.95f, 0.7f, 0.6f)]
        private void ClearTestClusters()
        {
            // Sweep all roots whose name marks them as test clusters
            int removed = 0;
            foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name.StartsWith("TEST_Cluster")) { DestroyImmediate(root); removed++; }
            Debug.Log($"[ClusterConfig] Removed {removed} test cluster(s).");
        }
#endif
    }
}
