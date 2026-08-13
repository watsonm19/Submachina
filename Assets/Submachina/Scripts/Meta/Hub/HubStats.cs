using System.Collections.Generic;
using UnityEngine;
using Submachina.Core;

namespace Submachina.Meta
{
    /**
     * Static helper that recomputes the hub's headline stats (rated depth,
     * cargo capacity, max O2) purely from the player's persisted upgrade
     * levels — no live submarine instance exists at the hub to ask.
     *
     * MIRROR ASSUMPTION: these formulas are hand-copies of the resolution
     * math baked into HullSystem, CargoHold and O2System:
     *   - StatModifierTable.Resolve: final = (base + Σ additivePerLevel×level)
     *     × (1 + Σ multiplierPerLevel×level)
     *   - HullSystem.RatedDepth: StrengthMod × safety / (pressurePerMeter × PressureMultMod)
     * If those components' serialized defaults or formulas change, this file
     * must be updated to match or the hub's numbers will drift from what the
     * player actually gets once a submarine is spawned.
     */
    public static class HubStats
    {
        // Mirrors HullSystem's serialized defaults (hullStrength, pressurePerMeter, ratedDepthSafetyFactor).
        private const float BaseHullStrength = 120f;
        private const float PressurePerMeter = 1f;
        private const float RatedDepthSafety = 0.8f;

        // Mirrors CargoHold's serialized baseCapacity default.
        private const float BaseCargoCapacity = 20f;

        // Mirrors O2System's serialized maxAirPressure default.
        private const float BaseMaxAirPressure = 100f;

        // -------------------------------------------------------
        // Public stat queries
        // -------------------------------------------------------

        /**
         * Rated depth as HullSystem.RatedDepth would report it for a submarine
         * built from every upgrade the player currently owns.
         * Example: +40 flat strength and a -20% pressure-load upgrade both owned
         * → strength (base+add) = 160, pressureMult = 1 - 0.2 = 0.8 →
         *   rated depth = 160 × 0.8 / (1 × 0.8) = 160 m.
         */
        public static float ComputeRatedDepth(UpgradeCatalog catalog)
        {
            AccumulateModifiers(catalog, SubStats.HullStrength, out float strengthAdd, out float strengthMult);
            float strengthMod = (BaseHullStrength + strengthAdd) * (1f + strengthMult);

            AccumulateModifiers(catalog, SubStats.PressureLoadMult, out _, out float pressureMultDelta);
            float pressureMult = Mathf.Max(0.1f, 1f + pressureMultDelta);

            // Shared formula lives on HullSystem so hub and gameplay can't drift
            return HullSystem.ComputeRatedDepth(strengthMod, PressurePerMeter, RatedDepthSafety, pressureMult);
        }

        /** Hull strength (impact absorption) as HullSystem.StrengthMod would resolve it. */
        public static float ComputeHullStrength(UpgradeCatalog catalog)
        {
            AccumulateModifiers(catalog, SubStats.HullStrength, out float add, out float mult);
            return (BaseHullStrength + add) * (1f + mult);
        }

        /**
         * Impact load reduction as a display percentage, from ImpactLoadMult
         * upgrades. Example: one Impact Skirt level (×0.85 load) → 15.
         */
        public static float ComputeImpactResistPercent(UpgradeCatalog catalog)
        {
            AccumulateModifiers(catalog, SubStats.ImpactLoadMult, out _, out float multDelta);
            return (1f - Mathf.Max(0.1f, 1f + multDelta)) * 100f;
        }

        /** Cargo hold capacity as CargoHold.Capacity would resolve it for the owned upgrades. */
        public static int ComputeCargoCapacity(UpgradeCatalog catalog)
        {
            AccumulateModifiers(catalog, SubStats.CargoCapacity, out float add, out float mult);
            return Mathf.RoundToInt((BaseCargoCapacity + add) * (1f + mult));
        }

        /** Max O2 pressure as O2System would resolve it for the owned upgrades. */
        public static float ComputeMaxO2(UpgradeCatalog catalog)
        {
            AccumulateModifiers(catalog, SubStats.MaxAirPressure, out float add, out float mult);
            return (BaseMaxAirPressure + add) * (1f + mult);
        }

        // -------------------------------------------------------
        // Modifier accumulation
        // -------------------------------------------------------

        /**
         * Sums additive/multiplier deltas for one stat across every owned level
         * of every UpgradeDef reachable from the catalog. A def is only counted
         * once even if it appears both as a shop entry and a loadout choice.
         */
        private static void AccumulateModifiers(UpgradeCatalog catalog, StatId stat, out float additiveSum, out float multiplierSum)
        {
            additiveSum = 0f;
            multiplierSum = 0f;
            if (catalog == null) return;

            foreach (var def in AllDefs(catalog))
            {
                int level = ProfileService.GetUpgradeLevel(def.name);
                if (level <= 0 || def.statModifiers == null) continue;

                foreach (var mod in def.statModifiers)
                {
                    if (mod.stat != stat) continue;
                    additiveSum += mod.additivePerLevel * level;
                    multiplierSum += mod.multiplierPerLevel * level;
                }
            }
        }

        /** Every UpgradeDef the catalog can grant — shop entries and loadout choices alike, de-duplicated. */
        private static IEnumerable<UpgradeDef> AllDefs(UpgradeCatalog catalog)
        {
            var seen = new HashSet<UpgradeDef>();

            foreach (var entry in catalog.entries)
                if (entry?.def != null && seen.Add(entry.def)) yield return entry.def;

            foreach (var slot in catalog.loadoutSlots)
            {
                if (slot == null) continue;
                foreach (var choice in slot.choices)
                    if (choice != null && seen.Add(choice)) yield return choice;
            }
        }
    }
}
