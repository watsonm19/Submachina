using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Curated pool of upgrade definitions available for drafting.
     *
     * On each level-up the draft UI draws N random upgrades from this pool,
     * filtering out any that are already at max level on the submarine.
     * Designers add or remove upgrades from the pool asset without touching code.
     */
    [CreateAssetMenu(menuName = "Submachina/Upgrade Draft Pool")]
    public class UpgradeDraftPool : ScriptableObject
    {
        [Tooltip("How many upgrade choices to present per level-up.")]
        [Min(1)] public int draftsPerLevelUp = 3;

        [Tooltip("All upgrades eligible for drafting. Duplicates are ignored.")]
        [ListDrawerSettings(ShowPaging = false)]
        public List<UpgradeDef> upgrades = new();

        /**
         * Returns up to draftsPerLevelUp random upgrades from the pool,
         * excluding any that are at max level or whose prerequisites aren't met
         * on the given submarine.
         */
        public List<UpgradeDef> DrawChoices(UpgradeManager upgradeManager)
        {
            // Build the eligible list
            var eligible = new List<UpgradeDef>();
            for (int i = 0; i < upgrades.Count; i++)
            {
                var def = upgrades[i];
                if (def == null) continue;
                if (upgradeManager.GetLevel(def) >= def.maxLevel) continue;
                if (!upgradeManager.MeetsPrerequisites(def)) continue;
                eligible.Add(def);
            }

            // Fisher-Yates shuffle then take the first N
            for (int i = eligible.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (eligible[i], eligible[j]) = (eligible[j], eligible[i]);
            }

            int count = Mathf.Min(draftsPerLevelUp, eligible.Count);
            return eligible.GetRange(0, count);
        }
    }
}
