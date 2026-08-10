using System;
using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;
using Submachina.Core;

namespace Submachina.Meta
{
    /** One line of a purchase price: N units of a banked resource type. */
    [Serializable]
    public struct ResourceCost
    {
        public ResourceType type;
        [Min(1)] public int amount;
    }

    /** A purchasable upgrade: the UpgradeDef plus its per-level price. */
    [Serializable]
    public class ShopEntry
    {
        [Tooltip("The upgrade granted when purchased.")]
        public UpgradeDef def;

        [Tooltip("Cost of ONE level. Later levels multiply by costGrowth^level.")]
        public ResourceCost[] costs;

        [Tooltip("Price multiplier per already-owned level. Example: 1.5 → level 2 costs 150% of level 1.")]
        [Min(1f)] public float costGrowth = 1.5f;
    }

    /**
     * The hub shop's stock list and the persistence layer's name→asset resolver.
     *
     * One catalog asset lists every permanently purchasable upgrade with its
     * typed-resource price, plus the loadout slot definitions. ProfileService
     * stores upgrade names; LoadoutApplier and the hub UI resolve them back
     * through FindDef here.
     */
    [CreateAssetMenu(menuName = "Submachina/Upgrade Catalog", fileName = "UpgradeCatalog")]
    public class UpgradeCatalog : ScriptableObject
    {
        [Title("Shop Stock")]
        [Tooltip("Every upgrade purchasable at the hub, with prices.")]
        public List<ShopEntry> entries = new();

        [Title("Loadout")]
        [Tooltip("Exclusive slot groups the player picks from before a mission.")]
        public List<LoadoutSlotDef> loadoutSlots = new();

        /** Resolves an UpgradeDef by asset name across shop entries and slot choices. */
        public UpgradeDef FindDef(string defName)
        {
            if (string.IsNullOrEmpty(defName)) return null;

            foreach (var entry in entries)
                if (entry.def != null && entry.def.name == defName) return entry.def;

            foreach (var slot in loadoutSlots)
            {
                if (slot == null) continue;
                foreach (var choice in slot.choices)
                    if (choice != null && choice.name == defName) return choice;
            }
            return null;
        }

        /** The shop entry for a def, or null when it isn't sold here. */
        public ShopEntry FindEntry(UpgradeDef def)
        {
            foreach (var entry in entries)
                if (entry.def == def) return entry;
            return null;
        }

        /** Cost of the NEXT level given how many are already owned (growth applied, rounded up). */
        public static ResourceCost[] CostForLevel(ShopEntry entry, int ownedLevels)
        {
            if (entry?.costs == null) return null;

            float multiplier = Mathf.Pow(entry.costGrowth, ownedLevels);
            var scaled = new ResourceCost[entry.costs.Length];
            for (int i = 0; i < entry.costs.Length; i++)
            {
                scaled[i] = entry.costs[i];
                scaled[i].amount = Mathf.CeilToInt(entry.costs[i].amount * multiplier);
            }
            return scaled;
        }
    }
}
