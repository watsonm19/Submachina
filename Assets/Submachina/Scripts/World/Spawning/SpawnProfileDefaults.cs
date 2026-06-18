using System.Collections.Generic;
using UnityEngine;

namespace Submachina.Core
{
    /**
     * Builds the default rule set that reproduces the original hardcoded
     * WorldChunk behavior, so existing worlds keep generating identically after
     * the migration. Invoked by SpawnProfile's "Generate Default Rules" button.
     *
     * Prefabs are intentionally left null — the matching prefab is named in
     * each rule's developerNotes for the designer to assign once.
     */
    public static class SpawnProfileDefaults
    {
        /** Returns the seven legacy-parity rules (rocks, resource, O2, enemies). */
        public static List<SpawnRuleData> BuildLegacyRules()
        {
            return new List<SpawnRuleData>
            {
                // Wall rocks — half the rock budget, jutting from the trench edges
                new SpawnRuleData
                {
                    ruleName = "Rock_Wall",
                    zoneTag = ZoneType.Shallow,
                    developerNotes = "Assign RockObstacle.prefab.\n" +
                                     "Wall protrusions from the trench edges. Shares the rock budget with " +
                                     "Rock_Center via Floor/Ceil half over the same 2→9 curve.",
                    depth = new DepthRange { minDepth = 10f, hasMax = false, maxDepth = 400f },
                    count = new CountModel
                    {
                        kind = CountKind.CurveRange,
                        refMinDepth = 0f, refMaxDepth = 300f,
                        countAtMinDepth = 2f, countAtMaxDepth = 9f,
                        split = CountSplit.FloorHalf
                    },
                    placement = new WallProtrusionPlacement()
                },

                // Center rocks — the other half of the budget, scattered centrally and depth-scaled
                new SpawnRuleData
                {
                    ruleName = "Rock_Center",
                    zoneTag = ZoneType.Shallow,
                    developerNotes = "Assign RockObstacle.prefab.\n" +
                                     "Smaller rocks in the central band; size grows with depth. " +
                                     "Shares the rock budget with Rock_Wall (Ceil half).",
                    depth = new DepthRange { minDepth = 10f, hasMax = false, maxDepth = 400f },
                    count = new CountModel
                    {
                        kind = CountKind.CurveRange,
                        refMinDepth = 0f, refMaxDepth = 300f,
                        countAtMinDepth = 2f, countAtMaxDepth = 9f,
                        split = CountSplit.CeilHalf
                    },
                    placement = new CenterBandPlacement { bandFraction = 0.55f },
                    configurator = new DepthScaleConfigurator
                    {
                        refMinDepth = 0f, refMaxDepth = 300f,
                        maxSizeShallow = 1.2f, maxSizeDeep = 3.5f,
                        minWidth = 0.8f, minHeight = 0.5f, heightFactor = 0.75f
                    }
                },

                // Mining resources — outer band, count ramps 3→6 with depth
                new SpawnRuleData
                {
                    ruleName = "Resource",
                    zoneTag = ZoneType.Shallow,
                    developerNotes = "Assign CopperResource.prefab (or any MiningResource).\n" +
                                     "Scattered in the outer 80% band to reward exploration.",
                    depth = new DepthRange { minDepth = 0f, hasMax = false, maxDepth = 400f },
                    count = new CountModel
                    {
                        kind = CountKind.CurveRange,
                        refMinDepth = 0f, refMaxDepth = 300f,
                        countAtMinDepth = 3f, countAtMaxDepth = 6f
                    },
                    placement = new ScatterPlacement { widthFraction = 0.8f }
                },

                // Passive O2 bubbles — sparse, central, sized by depth
                new SpawnRuleData
                {
                    ruleName = "PassiveO2",
                    zoneTag = ZoneType.Shallow,
                    developerNotes = "Assign O2Bubble.prefab.\n" +
                                     "Baseline air source (1–2 per chunk past 3m). DepthSizeConfigurator " +
                                     "makes deeper bubbles larger/worth more.",
                    depth = new DepthRange { minDepth = 3f, hasMax = false, maxDepth = 400f },
                    count = new CountModel { kind = CountKind.Range, min = 1, max = 2 },
                    placement = new ScatterPlacement { widthFraction = 0.7f },
                    configurator = new DepthSizeConfigurator()
                },

                // Regular enemy — grace to 20m, ramps 1→4 over 20–400m, central band
                new SpawnRuleData
                {
                    ruleName = "Enemy",
                    zoneTag = ZoneType.Midnight,
                    developerNotes = "Assign SeaCreature.prefab (EnemyController).\n" +
                                     "Grace zone to 20m; count ramps 1→4 over 20–400m. Kept central for patrol room.",
                    depth = new DepthRange { minDepth = 20f, hasMax = false, maxDepth = 400f },
                    count = new CountModel
                    {
                        kind = CountKind.CurveRange,
                        refMinDepth = 20f, refMaxDepth = 400f,
                        countAtMinDepth = 1f, countAtMaxDepth = 4f
                    },
                    placement = new ScatterPlacement { widthFraction = 0.65f, topInset = 2f, bottomInset = 2f }
                },

                // Passive creature — rare single roll, flees the player
                new SpawnRuleData
                {
                    ruleName = "PassiveCreature",
                    zoneTag = ZoneType.Shallow,
                    developerNotes = "Assign PassiveCreature.prefab.\n" +
                                     "Rare (40% per chunk), no depth gate. Flees and drops extra O2.",
                    depth = new DepthRange { minDepth = 0f, hasMax = false, maxDepth = 400f },
                    count = new CountModel { kind = CountKind.SingleRoll, spawnChance = 0.4f },
                    placement = new ScatterPlacement { widthFraction = 0.8f }
                },

                // Ramming enemy — deep water only, rare single roll
                new SpawnRuleData
                {
                    ruleName = "RammingEnemy",
                    zoneTag = ZoneType.Abyss,
                    developerNotes = "Assign RammerEnemy.prefab.\n" +
                                     "Deep water only (200m+), 30% per chunk. Telegraphed charge.",
                    depth = new DepthRange { minDepth = 200f, hasMax = false, maxDepth = 400f },
                    count = new CountModel { kind = CountKind.SingleRoll, spawnChance = 0.3f },
                    placement = new ScatterPlacement { widthFraction = 0.65f, topInset = 2f, bottomInset = 2f }
                }
            };
        }
    }
}