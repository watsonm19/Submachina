using UnityEngine;
using Sirenix.OdinInspector;
using Submachina.Core;

namespace Submachina.Meta
{
    /**
     * Applies the persistent player profile to a submarine at mission start.
     *
     * Place on the submarine root (next to Submarine) in mission scenes. On
     * Start — after every SubmarineComponent has registered in Awake — it grants:
     *   - every OWNED upgrade that is NOT part of any loadout slot (pure
     *     purchases like Reinforced Hull always apply), and
     *   - only the PICKED choices of each loadout slot (owned-but-unpicked
     *     slot upgrades stay inert — the "can't have it all" rule).
     *
     * Levels: UpgradeManager.Grant adds one level per call, so the applier
     * calls it once per owned level (capped by the def's own maxLevel).
     *
     * Scenes without a profile (fresh save) or without an UpgradeManager are
     * unaffected — the applier just no-ops, so test scenes keep working.
     */
    public class LoadoutApplier : MonoBehaviour
    {
        [Tooltip("The catalog used to resolve saved upgrade names back to UpgradeDef assets.")]
        [SerializeField] private UpgradeCatalog catalog;

        [Tooltip("Log each grant for debugging mission-start loadouts.")]
        [SerializeField] private bool verbose;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private int _grantedCount;

        /**
         * Runs in Start so all SubmarineComponents (including UpgradeManager)
         * have registered with the facade during Awake.
         */
        private void Start()
        {
            var sub = GetComponentInParent<Submarine>();
            if (sub?.Upgrades == null || catalog == null) return;

            // Grant every owned upgrade that the loadout rules allow this mission
            foreach (var owned in ProfileService.Current.upgrades)
            {
                var def = catalog.FindDef(owned.defName);
                if (def == null) { Debug.LogWarning($"[LoadoutApplier] Unknown upgrade '{owned.defName}' in profile."); continue; }
                if (!IsAllowedByLoadout(def, owned.defName)) continue;

                GrantLevels(sub, def, owned.level);
            }

            if (verbose) Debug.Log($"[LoadoutApplier] Applied {_grantedCount} upgrade level(s) to {sub.name}.");
        }

        /**
         * Slot membership check: a def inside any loadout slot applies only when
         * picked in that slot; defs outside every slot always apply.
         */
        private bool IsAllowedByLoadout(UpgradeDef def, string defName)
        {
            foreach (var slot in catalog.loadoutSlots)
            {
                if (slot == null || !slot.Contains(def)) continue;
                return ProfileService.IsLoadoutChoice(slot.slotName, defName);
            }
            return true;
        }

        /** Grants level-many stacks, respecting the def's own max and prerequisites. */
        private void GrantLevels(Submarine sub, UpgradeDef def, int levels)
        {
            for (int i = 0; i < levels; i++)
            {
                if (!sub.Upgrades.Grant(def)) break;   // maxed or prereq unmet — stop quietly
                _grantedCount++;
                if (verbose) Debug.Log($"[LoadoutApplier] Granted {def.name} (level {i + 1}).");
            }
        }
    }
}
