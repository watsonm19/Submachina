using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;
using Submachina.Core;

namespace Submachina.Meta
{
    /**
     * An exclusive loadout slot — the "you can't have it all" restriction.
     *
     * Each slot names a group of upgrades of which only maxPicks may be active
     * on a mission (e.g. Hull Feature: Ballast Tank OR Double O2 OR Impact
     * Reinforcement). The player must OWN a choice (hub purchase) before it can
     * be picked. Owned-but-unpicked choices are inert for that mission —
     * LoadoutApplier simply doesn't grant them.
     */
    [CreateAssetMenu(menuName = "Submachina/Loadout Slot", fileName = "LoadoutSlot")]
    public class LoadoutSlotDef : ScriptableObject
    {
        [Tooltip("Player-facing slot name, e.g. 'Hull Feature'.")]
        public string slotName;

        [TextArea]
        [Tooltip("Short description of what this slot represents.")]
        public string description;

        [Tooltip("How many choices may be selected simultaneously (1 for most slots, 2 for Tools).")]
        [Min(1)] public int maxPicks = 1;

        [Tooltip("The upgrades competing for this slot.")]
        public List<UpgradeDef> choices = new();

        /** True when the def is one of this slot's competing choices. */
        public bool Contains(UpgradeDef def) => def != null && choices.Contains(def);
    }
}
