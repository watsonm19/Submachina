using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Per-submarine upgrade state manager.
     *
     * Owns the StatModifierTable that all subsystem components query for
     * their modified stat values. Provides the API for granting, removing,
     * and toggling upgrades at runtime.
     *
     * Four upgrade types are supported:
     *   1. Stat modifiers — pushed to the StatModifierTable
     *   2. Behavioral add-ons — prefab instantiated as a child of the sub
     *   3. Component swaps — deactivate the original, instantiate the variant
     *   4. Hierarchy toggles — switch existing tagged objects on/off (reference-counted)
     *
     * Component swaps use deactivation rather than destruction so the
     * original can be restored on removal or toggle. The UpgradeManager
     * explicitly manages facade registration since SubmarineComponent
     * only registers in Awake (which doesn't re-fire on reactivation).
     */
    public class UpgradeManager : SubmarineComponent
    {
        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired when an upgrade is granted. Passes (UpgradeDef, newLevel).")]
        public UnityEvent<UpgradeDef, int> onUpgradeGranted;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when an upgrade is fully removed.")]
        public UnityEvent<UpgradeDef> onUpgradeRemoved;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when an upgrade is toggled on or off. Passes (UpgradeDef, enabled).")]
        public UnityEvent<UpgradeDef, bool> onUpgradeToggled;

        // =====================
        // Runtime State
        // =====================

        private readonly Dictionary<UpgradeDef, UpgradeInstance> _upgrades = new();

        /** Per-feature reference counts of active upgrades wanting it on vs. off. */
        private readonly Dictionary<UpgradeFeature, FeatureToggleState> _featureCounts = new();

        /** Reusable buffer for hierarchy target lookups (avoids per-call allocation). */
        private readonly List<UpgradeToggleTarget> _targetBuffer = new();

        /** Tracks how many active upgrades request a feature on vs. off. */
        private class FeatureToggleState
        {
            public int onCount;
            public int offCount;
        }

        /** The modifier table queried by all subsystem components. */
        public StatModifierTable Stats { get; } = new();

        /** All granted upgrades, for UI display. */
        public IReadOnlyDictionary<UpgradeDef, UpgradeInstance> GrantedUpgrades => _upgrades;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private int ActiveUpgradeCount => _upgrades.Count;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private int TotalModifiers => Stats.TotalModifierCount;

        // -------------------------------------------------------
        // Public API
        // -------------------------------------------------------

        /**
         * Grants one level of an upgrade.
         *
         * First level: applies stat modifiers, instantiates behavior prefab
         * and/or performs component swap. Subsequent levels: stacks additional
         * stat modifiers and notifies behavioral upgrades of the new level.
         *
         * Returns false if already at max level or prerequisites are unmet.
         */
        public bool Grant(UpgradeDef def)
        {
            if (def == null) return false;
            if (!MeetsPrerequisites(def)) return false;

            // Existing upgrade — try to add another level
            if (_upgrades.TryGetValue(def, out var instance))
            {
                if (instance.level >= def.maxLevel) return false;

                instance.level++;
                ApplyStatModifiers(def, 1);

                // Notify behavioral upgrades of the new level
                if (instance.behaviorInstance != null)
                {
                    foreach (var behavior in instance.behaviorInstance.GetComponents<IUpgradeBehavior>())
                        behavior.OnUpgradeEnabled(Sub, instance.level);
                }

                Sub?.Feedbacks?.Play(SubFeedbacks.UpgradeGranted, transform.position);
                if (instance.level >= def.maxLevel)
                    Sub?.Feedbacks?.Play(SubFeedbacks.UpgradeMaxed, transform.position);

                onUpgradeGranted?.Invoke(def, instance.level);
                return true;
            }

            // New upgrade — create instance at level 1
            instance = new UpgradeInstance { level = 1, enabled = true };
            _upgrades[def] = instance;

            // Apply stat modifiers
            ApplyStatModifiers(def, 1);

            // Component swap — deactivate original, instantiate variant
            if (def.swapPrefab != null)
                PerformComponentSwap(def, instance);

            // Behavioral add-on — instantiate and enable
            if (def.behaviorPrefab != null)
            {
                instance.behaviorInstance = Instantiate(def.behaviorPrefab, Sub.transform);
                foreach (var behavior in instance.behaviorInstance.GetComponents<IUpgradeBehavior>())
                    behavior.OnUpgradeEnabled(Sub, instance.level);
            }

            // Hierarchy toggles — switch existing tagged objects on/off
            ApplyToggles(def);

            Sub?.Feedbacks?.Play(SubFeedbacks.UpgradeGranted, transform.position);
            if (instance.level >= def.maxLevel)
                Sub?.Feedbacks?.Play(SubFeedbacks.UpgradeMaxed, transform.position);

            onUpgradeGranted?.Invoke(def, instance.level);
            return true;
        }

        /**
         * Removes all levels of an upgrade.
         * Reverses stat modifiers, destroys behavior, reverts component swap.
         */
        public void Remove(UpgradeDef def)
        {
            if (def == null || !_upgrades.TryGetValue(def, out var instance)) return;

            // Disable behavioral upgrade
            if (instance.behaviorInstance != null)
            {
                foreach (var behavior in instance.behaviorInstance.GetComponents<IUpgradeBehavior>())
                    behavior.OnUpgradeDisabled(Sub);
                Destroy(instance.behaviorInstance);
            }

            // Revert component swap
            if (instance.swapInstance != null)
                RevertComponentSwap(instance);

            // Release hierarchy toggles (only if still applied — a disabled upgrade already released)
            if (instance.enabled)
                ReleaseToggles(def);

            // Remove all stacked stat modifiers
            RemoveStatModifiers(def, instance.level);

            _upgrades.Remove(def);
            onUpgradeRemoved?.Invoke(def);
        }

        /**
         * Toggles an upgrade on or off without removing it.
         * Disabled upgrades have their stat modifiers removed and their
         * behaviors/swaps deactivated. Re-enabling restores everything.
         */
        public void SetEnabled(UpgradeDef def, bool enabled)
        {
            if (def == null || !_upgrades.TryGetValue(def, out var instance)) return;
            if (instance.enabled == enabled) return;

            instance.enabled = enabled;

            if (enabled)
            {
                // Re-apply stat modifiers
                ApplyStatModifiers(def, instance.level);

                // Re-enable behavior
                if (instance.behaviorInstance != null)
                {
                    instance.behaviorInstance.SetActive(true);
                    foreach (var behavior in instance.behaviorInstance.GetComponents<IUpgradeBehavior>())
                        behavior.OnUpgradeEnabled(Sub, instance.level);
                }

                // Re-enable component swap
                if (instance.swapInstance != null)
                {
                    // Deactivate the restored original
                    if (instance.deactivatedOriginal != null)
                    {
                        var origComp = instance.deactivatedOriginal.GetComponentInChildren<SubmarineComponent>();
                        if (origComp != null) Sub?.Unregister(origComp);
                        instance.deactivatedOriginal.SetActive(false);
                    }

                    // Re-activate the swap variant
                    instance.swapInstance.SetActive(true);
                    var swapComp = instance.swapInstance.GetComponentInChildren<SubmarineComponent>();
                    if (swapComp != null) Sub?.Register(swapComp);
                }

                // Re-apply hierarchy toggles
                ApplyToggles(def);
            }
            else
            {
                // Remove stat modifiers
                RemoveStatModifiers(def, instance.level);

                // Disable behavior
                if (instance.behaviorInstance != null)
                {
                    foreach (var behavior in instance.behaviorInstance.GetComponents<IUpgradeBehavior>())
                        behavior.OnUpgradeDisabled(Sub);
                    instance.behaviorInstance.SetActive(false);
                }

                // Revert component swap (deactivate variant, re-activate original)
                if (instance.swapInstance != null)
                {
                    var swapComp = instance.swapInstance.GetComponentInChildren<SubmarineComponent>();
                    if (swapComp != null) Sub?.Unregister(swapComp);
                    instance.swapInstance.SetActive(false);

                    if (instance.deactivatedOriginal != null)
                    {
                        instance.deactivatedOriginal.SetActive(true);
                        var origComp = instance.deactivatedOriginal.GetComponentInChildren<SubmarineComponent>();
                        if (origComp != null) Sub?.Register(origComp);
                    }
                }

                // Release hierarchy toggles
                ReleaseToggles(def);
            }

            onUpgradeToggled?.Invoke(def, enabled);
        }

        /** Returns the current level of an upgrade (0 if not granted). */
        public int GetLevel(UpgradeDef def)
        {
            return def != null && _upgrades.TryGetValue(def, out var instance) ? instance.level : 0;
        }

        /** True if the upgrade is granted AND enabled. */
        public bool IsActive(UpgradeDef def)
        {
            return def != null && _upgrades.TryGetValue(def, out var instance) && instance.enabled;
        }

        /** Checks whether all prerequisites for an upgrade are met. */
        public bool MeetsPrerequisites(UpgradeDef def)
        {
            if (def.prerequisites == null || def.prerequisites.Length == 0) return true;

            for (int i = 0; i < def.prerequisites.Length; i++)
            {
                if (!IsActive(def.prerequisites[i])) return false;
            }
            return true;
        }

        // -------------------------------------------------------
        // Stat Modifier Helpers
        // -------------------------------------------------------

        /** Pushes stat modifiers for N levels of an upgrade into the table. */
        private void ApplyStatModifiers(UpgradeDef def, int levels)
        {
            if (def.statModifiers == null) return;

            for (int i = 0; i < def.statModifiers.Length; i++)
            {
                var mod = def.statModifiers[i];
                for (int lvl = 0; lvl < levels; lvl++)
                    Stats.Add(mod.stat, mod.additivePerLevel, mod.multiplierPerLevel);
            }
        }

        /** Removes stat modifiers for N levels of an upgrade from the table. */
        private void RemoveStatModifiers(UpgradeDef def, int levels)
        {
            if (def.statModifiers == null) return;

            for (int i = 0; i < def.statModifiers.Length; i++)
            {
                var mod = def.statModifiers[i];
                for (int lvl = 0; lvl < levels; lvl++)
                    Stats.Remove(mod.stat, mod.additivePerLevel, mod.multiplierPerLevel);
            }
        }

        // -------------------------------------------------------
        // Component Swap Helpers
        // -------------------------------------------------------

        /**
         * Deactivates the current occupant of the facade slot targeted by
         * the swap prefab, then instantiates the variant in its place.
         */
        private void PerformComponentSwap(UpgradeDef def, UpgradeInstance instance)
        {
            // Identify what slot the swap targets by inspecting the prefab
            var swapComp = def.swapPrefab.GetComponentInChildren<SubmarineComponent>(true);
            if (swapComp == null)
            {
                Debug.LogWarning($"[UpgradeManager] Swap prefab '{def.swapPrefab.name}' has no SubmarineComponent.");
                return;
            }

            // Find the current occupant of that slot on the submarine
            var currentOccupant = FindCurrentOccupant(swapComp);
            if (currentOccupant != null)
            {
                // Unregister from facade and deactivate
                Sub?.Unregister(currentOccupant);
                instance.deactivatedOriginal = currentOccupant.gameObject;
                currentOccupant.gameObject.SetActive(false);
            }

            // Instantiate the swap variant
            instance.swapInstance = Instantiate(def.swapPrefab, Sub.transform);
        }

        /**
         * Destroys the swap variant and re-activates the original component.
         */
        private void RevertComponentSwap(UpgradeInstance instance)
        {
            // Destroy the swap variant
            if (instance.swapInstance != null)
                Destroy(instance.swapInstance);

            // Re-activate the original
            if (instance.deactivatedOriginal != null)
            {
                instance.deactivatedOriginal.SetActive(true);
                var origComp = instance.deactivatedOriginal.GetComponentInChildren<SubmarineComponent>();
                if (origComp != null) Sub?.Register(origComp);
            }

            instance.swapInstance = null;
            instance.deactivatedOriginal = null;
        }

        /**
         * Finds the current SubmarineComponent on this sub that occupies the
         * same facade slot as the given component type from the swap prefab.
         *
         * Uses the same type-matching logic as Submarine.Register — checks the
         * facade properties to find which slot the swap prefab's component type
         * would register into.
         */
        private SubmarineComponent FindCurrentOccupant(SubmarineComponent swapComponent)
        {
            if (Sub == null) return null;

            // Match by the same types that Submarine.Register uses
            return swapComponent switch
            {
                O2System _             => Sub.O2,
                SubmarinePhysicsController _ => Sub.Physics,
                TurretAim _            => Sub.Turret,
                ResourceManager _      => Sub.Resources,
                ScrapManager _         => Sub.Scrap,
                SubmarineFeedbackRouter _ => Sub.Feedbacks,
                SubmarinePumpRouter _  => Sub.Pumps,
                SubmarineAnchorRouter _ => Sub.Anchors,
                PickupRangeDetector _  => Sub.PickupRange,
                UpgradeManager _       => Sub.Upgrades,
                _ => null
            };
        }

        // -------------------------------------------------------
        // Hierarchy Toggle Helpers
        // -------------------------------------------------------

        /**
         * Adds this upgrade's toggle requests to the per-feature reference counts
         * and re-resolves each affected feature's live state.
         *
         * Example: an upgrade with {AdvancedThruster: on, BasicThruster: off}
         * bumps AdvancedThruster.onCount and BasicThruster.offCount, then drives
         * every matching object to the resolved state.
         */
        private void ApplyToggles(UpgradeDef def)
        {
            if (def.toggles == null) return;

            for (int i = 0; i < def.toggles.Length; i++)
            {
                var entry = def.toggles[i];
                if (entry.feature == null) continue;

                // Bump the on/off reference count for this feature
                if (!_featureCounts.TryGetValue(entry.feature, out var counts))
                {
                    counts = new FeatureToggleState();
                    _featureCounts[entry.feature] = counts;
                }
                if (entry.setActive) counts.onCount++;
                else counts.offCount++;

                ResolveFeature(entry.feature);
            }
        }

        /**
         * Removes this upgrade's toggle requests from the per-feature reference
         * counts and re-resolves each affected feature. When a feature drops to
         * zero requests its tagged objects are restored to their authored state.
         */
        private void ReleaseToggles(UpgradeDef def)
        {
            if (def.toggles == null) return;

            for (int i = 0; i < def.toggles.Length; i++)
            {
                var entry = def.toggles[i];
                if (entry.feature == null) continue;
                if (!_featureCounts.TryGetValue(entry.feature, out var counts)) continue;

                // Decrement the matching count, clamped at zero
                if (entry.setActive) counts.onCount = Mathf.Max(0, counts.onCount - 1);
                else counts.offCount = Mathf.Max(0, counts.offCount - 1);

                // Drop the entry entirely once nothing references the feature
                if (counts.onCount == 0 && counts.offCount == 0)
                    _featureCounts.Remove(entry.feature);

                ResolveFeature(entry.feature);
            }
        }

        /**
         * Drives every object tagged with the given feature to its resolved state:
         *   - any active upgrade wants it on  → on   (ON wins ties over OFF)
         *   - only off requests remain        → off
         *   - no requests remain              → restore authored state
         *
         * Targets are re-discovered each call so objects added by other upgrades
         * (e.g. behavior/swap prefabs) are picked up correctly.
         */
        private void ResolveFeature(UpgradeFeature feature)
        {
            if (Sub == null || feature == null) return;

            _featureCounts.TryGetValue(feature, out var counts);
            bool restore = counts == null || (counts.onCount == 0 && counts.offCount == 0);
            bool desired = counts != null && counts.onCount > 0;

            // Collect every matching marker in the hierarchy (including inactive)
            Sub.GetComponentsInChildren(true, _targetBuffer);
            for (int i = 0; i < _targetBuffer.Count; i++)
            {
                var target = _targetBuffer[i];
                if (target.Feature != feature) continue;

                // Capture authored state before the first override, then apply
                target.EnsureOriginalCaptured();
                if (restore) target.RestoreOriginal();
                else target.SetActiveState(desired);
            }
        }

        // -------------------------------------------------------
        // Cleanup
        // -------------------------------------------------------

        protected override void OnDestroy()
        {
            // Revert all active swaps and destroy behaviors before unregistering
            foreach (var kvp in _upgrades)
            {
                var instance = kvp.Value;
                if (instance.behaviorInstance != null) Destroy(instance.behaviorInstance);
                if (instance.swapInstance != null) RevertComponentSwap(instance);
            }
            _upgrades.Clear();
            Stats.Clear();

            base.OnDestroy();
        }

        // -------------------------------------------------------
        // Editor — Overview
        // -------------------------------------------------------

#if UNITY_EDITOR
        /** True while in Play mode — gates the runtime-only overview tables. */
        private bool IsPlaying => Application.isPlaying;

        /**
         * The hierarchy root used for edit-time discovery. At runtime the cached
         * Sub reference is authoritative; in edit mode (Sub not yet populated) we
         * walk up to the owning Submarine, falling back to the transform root.
         */
        private Transform DiscoveryRoot
        {
            get
            {
                if (Sub != null) return Sub.transform;
                var sub = GetComponentInParent<Submarine>();
                return sub != null ? sub.transform : transform.root;
            }
        }

        /** Friendly label for a feature: its display name, else the asset name. */
        private static string FeatureLabel(UpgradeFeature f)
            => f == null ? "<none>" : (string.IsNullOrEmpty(f.displayName) ? f.name : f.displayName);

        // ── Summary banner ──

        /**
         * One-line summary shown at the top of the Overview foldout. Reports live
         * counts in Play mode and the static hierarchy "purview" in edit mode.
         */
        [FoldoutGroup("Overview", Order = -1), PropertyOrder(0)]
        [ShowInInspector, HideLabel, DisplayAsString(false)]
        [GUIColor(0.6f, 0.85f, 1f)]
        private string Summary
        {
            get
            {
                var purview = FeaturePurview;
                int targets = 0;
                for (int i = 0; i < purview.Count; i++) targets += purview[i].Targets;

                if (IsPlaying)
                    return $"{_upgrades.Count} upgrade(s) granted   •   {Stats.TotalModifierCount} stat modifier(s) active   •   " +
                           $"{_featureCounts.Count} feature(s) toggled   •   purview: {purview.Count} feature(s) / {targets} tagged object(s)";

                return $"Edit mode  —  purview: {purview.Count} feature(s) / {targets} tagged object(s) in this submarine. " +
                       "Granted upgrades and stat modifiers populate here during Play.";
            }
        }

        // ── Granted upgrades ──

        /** One row per granted upgrade in the runtime overview table. */
        private struct GrantedUpgradeRow
        {
            [TableColumnWidth(170, false), DisplayAsString]
            public string Upgrade;

            [TableColumnWidth(60, false), DisplayAsString]
            public string Level;

            [TableColumnWidth(70, false)]
            public bool Enabled;

            [DisplayAsString(false)]
            public string Provides;
        }

        [FoldoutGroup("Overview"), PropertyOrder(1)]
        [ShowInInspector, ShowIf(nameof(IsPlaying))]
        [LabelText("Granted Upgrades")]
        [TableList(IsReadOnly = true, AlwaysExpanded = true)]
        private List<GrantedUpgradeRow> GrantedUpgradeRows
        {
            get
            {
                var rows = new List<GrantedUpgradeRow>(_upgrades.Count);

                foreach (var kvp in _upgrades)
                {
                    var def = kvp.Key;
                    var inst = kvp.Value;

                    // Describe which upgrade mechanisms this entry actually drives
                    var parts = new List<string>(4);
                    if (def.statModifiers != null && def.statModifiers.Length > 0)
                        parts.Add($"{def.statModifiers.Length} stat");
                    if (inst.behaviorInstance != null) parts.Add("behavior");
                    if (inst.swapInstance != null) parts.Add("swap");
                    if (def.toggles != null && def.toggles.Length > 0)
                        parts.Add($"{def.toggles.Length} toggle");

                    rows.Add(new GrantedUpgradeRow
                    {
                        Upgrade = string.IsNullOrEmpty(def.upgradeName) ? def.name : def.upgradeName,
                        Level = $"{inst.level}/{def.maxLevel}",
                        Enabled = inst.enabled,
                        Provides = parts.Count > 0 ? string.Join(", ", parts) : "—"
                    });
                }

                return rows;
            }
        }

        // ── Active stat modifiers ──

        /** One row per modified stat in the runtime overview table. */
        private struct StatModifierRow
        {
            [TableColumnWidth(190, false), DisplayAsString]
            public string Stat;

            [TableColumnWidth(90, false), DisplayAsString]
            public string Additive;

            [TableColumnWidth(110, false), DisplayAsString]
            public string Multiplier;

            [TableColumnWidth(60, false), DisplayAsString]
            public int Stacks;
        }

        [FoldoutGroup("Overview"), PropertyOrder(2)]
        [ShowInInspector, ShowIf(nameof(IsPlaying))]
        [LabelText("Active Stat Modifiers")]
        [TableList(IsReadOnly = true, AlwaysExpanded = true)]
        private List<StatModifierRow> StatModifierRows
        {
            get
            {
                var rows = new List<StatModifierRow>();

                // Aggregate the live table into one row per affected stat
                foreach (var s in Stats.EditorSnapshot())
                {
                    string mult = $"{(s.multiplier >= 0f ? "+" : "")}{s.multiplier * 100f:0.#}%";
                    rows.Add(new StatModifierRow
                    {
                        Stat = s.stat.ToString(),
                        Additive = s.additive.ToString("+0.##;-0.##;0"),
                        Multiplier = mult,
                        Stacks = s.count
                    });
                }

                return rows;
            }
        }

        // ── Feature toggle reference counts (runtime) ──

        /** One row per actively-toggled feature, showing on/off request counts. */
        private struct FeatureToggleRow
        {
            [TableColumnWidth(190, false), DisplayAsString]
            public string Feature;

            [TableColumnWidth(60, false), DisplayAsString]
            public int On;

            [TableColumnWidth(60, false), DisplayAsString]
            public int Off;

            [TableColumnWidth(90, false), DisplayAsString]
            public string Resolved;
        }

        [FoldoutGroup("Overview"), PropertyOrder(3)]
        [ShowInInspector, ShowIf(nameof(IsPlaying))]
        [LabelText("Toggled Features (ref-counted)")]
        [TableList(IsReadOnly = true, AlwaysExpanded = true)]
        private List<FeatureToggleRow> FeatureToggleRows
        {
            get
            {
                var rows = new List<FeatureToggleRow>(_featureCounts.Count);

                foreach (var kvp in _featureCounts)
                {
                    var counts = kvp.Value;
                    // ON wins ties; only-off → OFF; nothing → restored
                    string resolved = counts.onCount > 0 ? "ON"
                                    : counts.offCount > 0 ? "OFF" : "restored";

                    rows.Add(new FeatureToggleRow
                    {
                        Feature = FeatureLabel(kvp.Key),
                        On = counts.onCount,
                        Off = counts.offCount,
                        Resolved = resolved
                    });
                }

                return rows;
            }
        }

        // ── Hierarchy purview (edit + runtime) ──

        /** One row per feature discovered in the hierarchy, with its tagged objects. */
        private struct FeaturePurviewRow
        {
            [TableColumnWidth(170, false), DisplayAsString]
            public string Feature;

            [TableColumnWidth(60, false), DisplayAsString]
            public int Targets;

            [DisplayAsString(false)]
            public string Objects;
        }

        [FoldoutGroup("Overview"), PropertyOrder(4)]
        [ShowInInspector]
        [LabelText("Feature Purview (hierarchy)")]
        [InfoBox("No UpgradeToggleTarget components found under this submarine. " +
                 "Add UpgradeToggleTarget markers to objects you want upgrades to switch on/off.",
                 InfoMessageType.Info, VisibleIf = "@this.FeaturePurview.Count == 0")]
        [TableList(IsReadOnly = true, AlwaysExpanded = true)]
        private List<FeaturePurviewRow> FeaturePurview
        {
            get
            {
                var rows = new List<FeaturePurviewRow>();
                var root = DiscoveryRoot;
                if (root == null) return rows;

                // Discover every toggle target in the sub hierarchy (including inactive)
                var targets = root.GetComponentsInChildren<UpgradeToggleTarget>(true);

                // Group the targets by the feature they are tagged with
                var byFeature = new Dictionary<UpgradeFeature, List<UpgradeToggleTarget>>();
                for (int i = 0; i < targets.Length; i++)
                {
                    var f = targets[i].Feature;
                    if (f == null) continue;
                    if (!byFeature.TryGetValue(f, out var list))
                    {
                        list = new List<UpgradeToggleTarget>();
                        byFeature[f] = list;
                    }
                    list.Add(targets[i]);
                }

                // One row per feature, with a capped preview of the object names
                foreach (var kvp in byFeature)
                {
                    var objs = kvp.Value;
                    string names = string.Join(", ", objs.ConvertAll(o => o.name));
                    if (names.Length > 80) names = names.Substring(0, 77) + "…";

                    rows.Add(new FeaturePurviewRow
                    {
                        Feature = FeatureLabel(kvp.Key),
                        Targets = objs.Count,
                        Objects = names
                    });
                }

                return rows;
            }
        }
#endif

        // -------------------------------------------------------
        // Editor Utilities
        // -------------------------------------------------------

#if UNITY_EDITOR
        [FoldoutGroup("Debug")]
        [Tooltip("Drag an UpgradeDef here and use the buttons below to test upgrades at runtime.")]
        [SerializeField] private UpgradeDef debugUpgrade;

        [FoldoutGroup("Debug")]
        [Button("Grant Upgrade"), GUIColor(0.4f, 1f, 0.4f)]
        private void DebugGrant()
        {
            if (!Application.isPlaying) { Debug.Log("[UpgradeManager] Play mode only."); return; }
            if (debugUpgrade == null) { Debug.Log("[UpgradeManager] Assign a debug upgrade first."); return; }

            bool result = Grant(debugUpgrade);
            Debug.Log(result
                ? $"[UpgradeManager] Granted '{debugUpgrade.upgradeName}' → level {GetLevel(debugUpgrade)}"
                : $"[UpgradeManager] Could not grant '{debugUpgrade.upgradeName}' (max level or prerequisites unmet)");
        }

        [FoldoutGroup("Debug")]
        [Button("Remove Upgrade"), GUIColor(1f, 0.4f, 0.4f)]
        private void DebugRemove()
        {
            if (!Application.isPlaying) { Debug.Log("[UpgradeManager] Play mode only."); return; }
            if (debugUpgrade == null) { Debug.Log("[UpgradeManager] Assign a debug upgrade first."); return; }

            Remove(debugUpgrade);
            Debug.Log($"[UpgradeManager] Removed '{debugUpgrade.upgradeName}'");
        }

        [FoldoutGroup("Debug")]
        [Button("Toggle Upgrade"), GUIColor(0.8f, 0.8f, 0.4f)]
        private void DebugToggle()
        {
            if (!Application.isPlaying) { Debug.Log("[UpgradeManager] Play mode only."); return; }
            if (debugUpgrade == null) { Debug.Log("[UpgradeManager] Assign a debug upgrade first."); return; }

            bool currentlyActive = IsActive(debugUpgrade);
            SetEnabled(debugUpgrade, !currentlyActive);
            Debug.Log($"[UpgradeManager] '{debugUpgrade.upgradeName}' → {(currentlyActive ? "disabled" : "enabled")}");
        }

        [FoldoutGroup("Debug")]
        [Button("List Active Upgrades"), GUIColor(0.6f, 0.8f, 1f)]
        private void DebugListActive()
        {
            if (!Application.isPlaying) { Debug.Log("[UpgradeManager] Play mode only."); return; }

            if (_upgrades.Count == 0)
            {
                Debug.Log("[UpgradeManager] No active upgrades.");
                return;
            }

            foreach (var kvp in _upgrades)
            {
                var def = kvp.Key;
                var inst = kvp.Value;
                string status = inst.enabled ? "enabled" : "DISABLED";
                Debug.Log($"  [{status}] {def.upgradeName} — level {inst.level}/{def.maxLevel}");
            }
        }
#endif
    }
}
