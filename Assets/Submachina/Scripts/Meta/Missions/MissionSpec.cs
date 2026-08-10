using System;
using System.Collections.Generic;

namespace Submachina.Meta
{
    public enum MissionType { Retrieval, Neutralize, Research }

    /**
     * A generated mission offer — everything the hub needs to show a scanner
     * report and everything the mission scene needs to configure itself.
     *
     * Plain serializable data (no Unity object references) so it can ride the
     * static MissionContext across a scene load, and later be saved if offers
     * should persist between sessions. Resource abundance references types by
     * key; UIs resolve icons/tints through their serialized ResourceType lists.
     */
    [Serializable]
    public class MissionSpec
    {
        // =====================
        // Identity
        // =====================

        public MissionType type;
        public int seed;
        public string title;
        public string flavor;

        // =====================
        // Objective
        // =====================

        /** Depth (m) where the objective spawns. The de facto difficulty dial. */
        public float targetDepth;

        /** Research only: number of scan targets. */
        public int researchTargetCount = 3;

        // =====================
        // Environment
        // =====================

        /** Additional descent-current speed (0 = calm, ~2 = strong pull). */
        public float currentStrength;

        /** O2 bubble richness multiplier (0.5 = thin water, 1.5 = rich). */
        public float o2Richness = 1f;

        /** 0 = none, 1 = normal, 2 = aggressive spawns (reserved for hazard tuning). */
        public float hazardLevel = 1f;

        // =====================
        // Scanner forecast
        // =====================

        /** Long-range scanner estimate per resource key ("VentBrass" → 1.4 = rich). */
        public List<ResourceForecast> forecast = new();

        /** Reward for completing the objective, banked on extraction. */
        public string rewardResourceKey;
        public int rewardAmount;

        [Serializable]
        public struct ResourceForecast
        {
            public string resourceKey;
            public float abundance;   // relative abundance, 1 = typical

            /** Scanner wording for the report card. */
            public string Grade => abundance >= 1.3f ? "RICH" : abundance >= 0.8f ? "DETECTED" : "TRACE";
        }
    }
}
