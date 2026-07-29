using System;
using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /** One entry in a decor rule's prefab pool, picked by relative weight. */
    [Serializable]
    public struct WeightedPrefab
    {
        [HorizontalGroup("Row"), HideLabel]
        [Tooltip("Decor prefab (usually a single sprite: kelp, silhouette rock, haze blob).")]
        [AssetsOnly]
        public GameObject prefab;

        [HorizontalGroup("Row", width: 80), LabelWidth(45)]
        [Tooltip("Relative pick weight against the other prefabs in this rule.")]
        [Min(0f)]
        public float weight;
    }

    /**
     * The data for one decorative spawnable on a parallax layer.
     *
     * A deliberately slimmer sibling of SpawnRuleData: decor lives in layer
     * space (no trench-wall geometry), so there is no PlacementStrategy or
     * InstanceConfigurator — placement is uniform scatter plus the visual
     * jitter fields below. If a future rule genuinely needs strategies, add a
     * [SerializeReference] ParallaxPlacement seam then, not before.
     */
    [Serializable]
    public class ParallaxDecorRule
    {
        // =====================
        // Identity & Docs
        // =====================

        [BoxGroup("$RuleTitle", centerLabel: true)]
        [Tooltip("Designer-facing name for this rule. Shown as the rule's header.")]
        public string ruleName = "New Decor Rule";

        [BoxGroup("$RuleTitle")]
        [Tooltip("Notes for other developers: what this decor is for, why these numbers.")]
        [MultiLineProperty(3)]
        public string developerNotes = "";

        // =====================
        // What to Spawn
        // =====================

        [BoxGroup("$RuleTitle")]
        [Title("What to Spawn")]
        [Tooltip("Weighted prefab pool — one is picked per instance.")]
        [ListDrawerSettings(ShowFoldout = false)]
        public List<WeightedPrefab> prefabs = new List<WeightedPrefab>();

        // =====================
        // Depth & Count
        // =====================

        [BoxGroup("$RuleTitle")]
        [Title("Depth & Frequency")]
        [Tooltip("Depth window (m) over which this rule is active. Depth is derived from the " +
                 "camera position each layer cell corresponds to.")]
        [InlineProperty, HideLabel]
        public DepthRange depth = new DepthRange { minDepth = 0f, hasMax = false, maxDepth = 400f };

        [BoxGroup("$RuleTitle")]
        [Tooltip("Extra depth-based multiplier on the count. Leave flat at 1 for no change.")]
        public AnimationCurve prevalenceByDepth = AnimationCurve.Constant(0f, 1000f, 1f);

        [BoxGroup("$RuleTitle")]
        [Tooltip("How many instances spawn per layer cell.")]
        [InlineProperty, HideLabel]
        public CountModel count = new CountModel();

        [BoxGroup("$RuleTitle")]
        [Tooltip("Minimum distance between instances of this rule within a cell (0 = none).")]
        [Min(0f)] public float minSpacing = 0f;

        // =====================
        // Visual Jitter
        // =====================

        [BoxGroup("$RuleTitle")]
        [Title("Visual Jitter")]
        [Tooltip("Uniform scale range rolled per instance.")]
        [MinMaxSlider(0.1f, 4f, true)]
        public Vector2 scaleRange = new Vector2(0.8f, 1.2f);

        [BoxGroup("$RuleTitle")]
        [Tooltip("Rotation jitter in ± degrees rolled per instance (0 = always upright).")]
        [Range(0f, 180f)] public float rotationJitter = 0f;

        [BoxGroup("$RuleTitle")]
        [Tooltip("Randomly mirror instances horizontally for variety.")]
        public bool randomFlipX = true;

        [BoxGroup("$RuleTitle")]
        [Tooltip("Alpha multiplier range rolled per instance — multiplies the prefab's authored alpha. " +
                 "Use below 1 for hazy/blurry silhouettes (e.g. 0.35–0.7 for foreground kelp).")]
        [MinMaxSlider(0f, 1f, true)]
        public Vector2 alphaRange = new Vector2(1f, 1f);

        [BoxGroup("$RuleTitle")]
        [Tooltip("Sorting-order offset range added to the prefab's authored order (within the layer's " +
                 "sorting layer) so overlapping decor stacks with variety.")]
        public Vector2Int sortingOrderRange = Vector2Int.zero;

        // Header label for the rule's box group
        private string RuleTitle => string.IsNullOrEmpty(ruleName) ? "Decor Rule" : ruleName;

        /** Total pick weight across the prefab pool (0 = rule effectively disabled). */
        public float TotalWeight
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < prefabs.Count; i++)
                    if (prefabs[i].prefab != null) total += prefabs[i].weight;
                return total;
            }
        }

        /** Picks a prefab from the weighted pool with exactly one RNG draw. */
        public GameObject PickPrefab(System.Random rng)
        {
            float total = TotalWeight;
            if (total <= 0f) return null;

            float roll = rng.NextFloat(0f, total);
            for (int i = 0; i < prefabs.Count; i++)
            {
                if (prefabs[i].prefab == null) continue;
                roll -= prefabs[i].weight;
                if (roll <= 0f) return prefabs[i].prefab;
            }
            return prefabs[prefabs.Count - 1].prefab;
        }
    }

    /**
     * The set of decor rules driving one (or more) ParallaxLayerSpawners.
     * Sibling of SpawnProfile, scoped to decorative parallax content.
     *
     * Create via: Assets → Create → Submachina → Parallax → Decor Profile
     */
    [CreateAssetMenu(fileName = "ParallaxDecorProfile", menuName = "Submachina/Parallax/Decor Profile")]
    public class ParallaxDecorProfile : ScriptableObject
    {
        [FoldoutGroup("Profile")]
        [Tooltip("Master density multiplier over every rule in this profile.")]
        [Range(0f, 5f)]
        [SerializeField] private float globalDensityMultiplier = 1f;

        [FoldoutGroup("Profile")]
        [Tooltip("Notes about this profile's intent (e.g. 'sparse foreground kelp for Act 1').")]
        [MultiLineProperty(3)]
        [SerializeField] private string profileNotes = "";

        [Tooltip("The decor rules run for every layer cell.")]
        [ListDrawerSettings(ShowFoldout = false)]
        [SerializeField] private List<ParallaxDecorRule> rules = new List<ParallaxDecorRule>();

        /** Master density multiplier applied to every rule. */
        public float GlobalDensity => globalDensityMultiplier;

        /** All authored rules (may contain effectively-disabled entries with no prefabs). */
        public IReadOnlyList<ParallaxDecorRule> Rules => rules;
    }
}
