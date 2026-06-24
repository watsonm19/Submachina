using System.Collections.Generic;
using UnityEngine;

namespace Submachina.Core
{
    /**
     * Stores stacked stat modifiers keyed by StatId.
     *
     * Each StatId can accumulate multiple additive and multiplicative
     * contributions from different upgrades. Resolution formula:
     *
     *   final = (base + sumAdditives) * (1 + sumMultiplierDeltas)
     *
     * Multiplier deltas are stored as offsets from 1.0:
     *   +0.2 means "20% more", -0.15 means "15% less".
     *   Two +0.2 boosts give (1 + 0.2 + 0.2) = 1.4x, not 1.2 * 1.2.
     *   This prevents multiplicative explosion from stacking.
     *
     * Owned by UpgradeManager — one per submarine.
     * Components query it via Sub.Upgrades.Stats.Resolve(statId, baseValue).
     */
    public class StatModifierTable
    {
        private struct ModEntry
        {
            public float additive;
            public float multiplier;
        }

        private readonly Dictionary<StatId, List<ModEntry>> _mods = new();

        // -------------------------------------------------------
        // Mutation
        // -------------------------------------------------------

        /** Pushes an additive + multiplicative modifier for a stat. */
        public void Add(StatId stat, float additive, float multiplier)
        {
            if (!_mods.TryGetValue(stat, out var list))
            {
                list = new List<ModEntry>(4);
                _mods[stat] = list;
            }
            list.Add(new ModEntry { additive = additive, multiplier = multiplier });
        }

        /** Removes the first modifier matching the given values. */
        public void Remove(StatId stat, float additive, float multiplier)
        {
            if (!_mods.TryGetValue(stat, out var list)) return;

            for (int i = 0; i < list.Count; i++)
            {
                if (Mathf.Approximately(list[i].additive, additive) &&
                    Mathf.Approximately(list[i].multiplier, multiplier))
                {
                    list.RemoveAt(i);
                    if (list.Count == 0) _mods.Remove(stat);
                    return;
                }
            }
        }

        /** Removes all modifiers for all stats. */
        public void Clear() => _mods.Clear();

        // -------------------------------------------------------
        // Resolution
        // -------------------------------------------------------

        /**
         * Computes the final value for a stat:
         *   (baseValue + sumAdditives) * (1 + sumMultiplierDeltas)
         *
         * Returns baseValue unchanged if no modifiers are registered.
         */
        public float Resolve(StatId stat, float baseValue)
        {
            if (!_mods.TryGetValue(stat, out var list)) return baseValue;

            float add = 0f;
            float mult = 0f;
            for (int i = 0; i < list.Count; i++)
            {
                add += list[i].additive;
                mult += list[i].multiplier;
            }

            return (baseValue + add) * (1f + mult);
        }

        /** Integer variant — rounds the resolved float to the nearest int. */
        public int ResolveInt(StatId stat, int baseValue)
        {
            return Mathf.RoundToInt(Resolve(stat, baseValue));
        }

        // -------------------------------------------------------
        // Debug
        // -------------------------------------------------------

        /** Returns true if any modifiers are registered for the given stat. */
        public bool HasModifiers(StatId stat) => _mods.ContainsKey(stat) && _mods[stat].Count > 0;

        /** Returns the total number of active modifier entries across all stats. */
        public int TotalModifierCount
        {
            get
            {
                int count = 0;
                foreach (var list in _mods.Values)
                    count += list.Count;
                return count;
            }
        }

#if UNITY_EDITOR
        /** Read-only per-stat aggregate, used purely for inspector display. */
        public readonly struct StatModifierSummary
        {
            public readonly StatId stat;     // which stat is modified
            public readonly float additive;  // summed additive contributions
            public readonly float multiplier;// summed multiplier deltas (offset from 1.0)
            public readonly int count;       // how many individual modifiers stack here

            public StatModifierSummary(StatId stat, float additive, float multiplier, int count)
            {
                this.stat = stat;
                this.additive = additive;
                this.multiplier = multiplier;
                this.count = count;
            }
        }

        /**
         * Editor-only snapshot of the table aggregated per stat. Each entry collapses
         * all stacked modifiers for a single StatId into summed additive/multiplier
         * totals so the inspector can show one row per affected stat.
         */
        public IEnumerable<StatModifierSummary> EditorSnapshot()
        {
            foreach (var kvp in _mods)
            {
                // Sum every stacked contribution for this stat
                float add = 0f, mult = 0f;
                for (int i = 0; i < kvp.Value.Count; i++)
                {
                    add += kvp.Value[i].additive;
                    mult += kvp.Value[i].multiplier;
                }

                yield return new StatModifierSummary(kvp.Key, add, mult, kvp.Value.Count);
            }
        }
#endif
    }
}
