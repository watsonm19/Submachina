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
     * Three upgrade types are supported:
     *   1. Stat modifiers — pushed to the StatModifierTable
     *   2. Behavioral add-ons — prefab instantiated as a child of the sub
     *   3. Component swaps — deactivate the original, instantiate the variant
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
