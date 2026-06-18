using System;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Data-driven definition of a single submarine upgrade.
     *
     * Each upgrade is a ScriptableObject asset that designers can create
     * via Create > Submachina > Upgrade Def. Supports three upgrade types
     * that can be combined in a single definition:
     *
     *   1. Stat modifiers — additive/multiplicative tweaks to numerical stats
     *   2. Behavioral add-ons — prefab instantiated alongside existing components
     *   3. Component swaps — prefab that replaces an existing SubmarineComponent
     *
     * Multi-level upgrades stack stat modifiers additively per level.
     * Prerequisites enable succession chains (e.g. DashV1 → DashV2).
     */
    [CreateAssetMenu(menuName = "Submachina/Upgrade Def")]
    public class UpgradeDef : ScriptableObject
    {
        // =====================
        // Identity
        // =====================

        [FoldoutGroup("Identity")]
        [Tooltip("Display name shown in the upgrade selection UI.")]
        public string upgradeName;

        [FoldoutGroup("Identity"), TextArea(2, 4)]
        [Tooltip("Short description of what this upgrade does.")]
        public string description;

        [FoldoutGroup("Identity")]
        [Tooltip("Icon shown in the upgrade selection UI.")]
        [PreviewField(50)]
        public Sprite icon;

        [FoldoutGroup("Identity")]
        [Tooltip("Tags for filtering draft pools (e.g. 'o2', 'combat', 'movement'). " +
                 "Used by UpgradeDraftPool to build themed or weighted selection sets.")]
        public string[] tags;

        // =====================
        // Levels
        // =====================

        [FoldoutGroup("Levels")]
        [Tooltip("Maximum times this upgrade can be acquired/stacked. " +
                 "Each Grant() call adds one level up to this cap.")]
        [Min(1)] public int maxLevel = 1;

        // =====================
        // Prerequisites
        // =====================

        [FoldoutGroup("Prerequisites")]
        [Tooltip("Upgrades that must be active before this one can be granted. " +
                 "Used for succession chains (e.g. DashV2 requires DashV1).")]
        public UpgradeDef[] prerequisites;

        // =====================
        // Stat Modifiers
        // =====================

        [FoldoutGroup("Stat Modifiers")]
        [Tooltip("Stat modifications applied per level. Each entry targets a StatId " +
                 "with an additive and/or multiplicative delta per level.")]
        [ListDrawerSettings(ShowPaging = false)]
        public StatModifierEntry[] statModifiers;

        // =====================
        // Behavioral Add-On
        // =====================

        [FoldoutGroup("Behavior")]
        [Tooltip("Optional prefab instantiated as a child of the submarine when this " +
                 "upgrade is first granted. Must contain a component implementing IUpgradeBehavior. " +
                 "Does NOT replace any existing component — adds new logic alongside them.")]
        public GameObject behaviorPrefab;

        // =====================
        // Component Swap
        // =====================

        [FoldoutGroup("Component Swap")]
        [Tooltip("Optional prefab containing a SubmarineComponent variant that replaces " +
                 "the existing component occupying the same facade slot. The original is " +
                 "deactivated (not destroyed) so it can be restored on removal or toggle. " +
                 "Stat modifiers carry over to the new component via shared StatId keys.")]
        public GameObject swapPrefab;
    }

    /**
     * A single stat modifier entry within an UpgradeDef.
     * Targets one StatId with per-level additive and multiplicative deltas.
     */
    [Serializable]
    public struct StatModifierEntry
    {
        [HorizontalGroup("Row"), LabelWidth(60)]
        [Tooltip("The stat to modify.")]
        public StatId stat;

        [HorizontalGroup("Row"), LabelWidth(100)]
        [Tooltip("Flat amount added to the stat's base value per level. " +
                 "Example: +5 means each level adds 5 to the base value.")]
        public float additivePerLevel;

        [HorizontalGroup("Row"), LabelWidth(110)]
        [Tooltip("Multiplier delta per level, as an offset from 1.0. " +
                 "Example: 0.2 = +20% per level, -0.15 = -15% per level.")]
        public float multiplierPerLevel;
    }
}
