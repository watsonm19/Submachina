using System.Collections.Generic;
using UnityEngine;
using Submachina.Core;

namespace Submachina.Meta
{
    /**
     * Procedural mission-offer generator for the hub's mission board.
     *
     * Generates a batch of offers scaled around the sub's rated depth:
     *   - a COMFORTABLE dive (~70% of rated depth, calm, modest reward)
     *   - a RATED dive (at the limit, moderate hazards)
     *   - a STRETCH dive (~130% — beyond crush depth at full integrity, so the
     *     player needs upgrades or accepts cascade damage; premium reward)
     *
     * That spread is the depth-progression engine: early hulls only see shallow
     * offers as "safe", and the stretch card advertises what upgrading buys.
     *
     * Forecast abundance derives from each ResourceType's native depthBand vs the
     * mission's target depth — the scanner is honest about what naturally spawns
     * there (spawn rules already gate by depth), then rewards top it up.
     */
    public static class MissionGenerator
    {
        // Depth multipliers per offer slot: comfortable / rated / stretch
        private static readonly float[] DepthTiers = { 0.7f, 1.0f, 1.3f };

        // Maximum world depth used to normalize resource depth bands (m).
        // Matches the deep end of current level content; revisit when levels grow.
        public const float WorldDepthScale = 400f;

        /**
         * Produces one mission offer per depth tier. availableTypes drives the
         * scanner forecast and reward rolls (pass the types the game can spawn).
         */
        public static List<MissionSpec> GenerateOffers(float ratedDepth, IReadOnlyList<ResourceType> availableTypes, int batchSeed)
        {
            var offers = new List<MissionSpec>();
            var rng = new System.Random(batchSeed);

            for (int i = 0; i < DepthTiers.Length; i++)
            {
                float depth = Mathf.Max(40f, ratedDepth * DepthTiers[i]) * Lerp(rng, 0.9f, 1.1f);
                offers.Add(GenerateOffer(depth, i, availableTypes, rng));
            }
            return offers;
        }

        /** Rolls a single offer at a target depth. tier index scales hazards + rewards. */
        private static MissionSpec GenerateOffer(float targetDepth, int tier, IReadOnlyList<ResourceType> types, System.Random rng)
        {
            var spec = new MissionSpec
            {
                type = RollType(rng),
                seed = rng.Next(),
                targetDepth = Mathf.Round(targetDepth),
                currentStrength = Mathf.Round(Lerp(rng, 0f, 0.7f + 0.65f * tier) * 10f) / 10f,
                o2Richness = Mathf.Round(Lerp(rng, 1.2f - 0.25f * tier, 1.4f - 0.35f * tier) * 100f) / 100f,
                hazardLevel = 1f + 0.5f * tier,
                researchTargetCount = 2 + rng.Next(0, 3),
            };

            // Scanner forecast: abundance peaks where the mission depth sits inside
            // a type's native band, fading toward its edges
            float normalizedDepth = Mathf.Clamp01(spec.targetDepth / WorldDepthScale);
            foreach (var type in types)
            {
                if (type == null) continue;
                float abundance = BandAbundance(normalizedDepth, type.depthBand) * Lerp(rng, 0.8f, 1.25f);
                if (abundance < 0.15f) continue;   // scanner doesn't report what isn't there

                spec.forecast.Add(new MissionSpec.ResourceForecast
                {
                    resourceKey = type.Key,
                    abundance = Mathf.Round(abundance * 100f) / 100f,
                });
            }

            // Reward: a native resource for this depth, bigger for deeper tiers
            if (spec.forecast.Count > 0)
            {
                var pick = spec.forecast[rng.Next(spec.forecast.Count)];
                spec.rewardResourceKey = pick.resourceKey;
                spec.rewardAmount = (8 + tier * 6) + rng.Next(0, 5);
            }

            WriteCopy(spec, tier, rng);
            return spec;
        }

        // -------------------------------------------------------
        // Rolls & helpers
        // -------------------------------------------------------

        /** Retrieval is the bread-and-butter offer; the others season the board. */
        private static MissionType RollType(System.Random rng)
        {
            int roll = rng.Next(100);
            if (roll < 50) return MissionType.Retrieval;
            return roll < 75 ? MissionType.Neutralize : MissionType.Research;
        }

        /**
         * Triangular abundance across a depth band: 1 at the band center, 0 at
         * (and beyond) its edges. Example: band (0.2, 0.6), depth 0.4 → 1.0;
         * depth 0.55 → 0.25.
         */
        private static float BandAbundance(float depth, Vector2 band)
        {
            float center = (band.x + band.y) * 0.5f;
            float halfWidth = Mathf.Max(0.01f, (band.y - band.x) * 0.5f);
            return Mathf.Clamp01(1f - Mathf.Abs(depth - center) / halfWidth);
        }

        private static float Lerp(System.Random rng, float min, float max) =>
            min + (float)rng.NextDouble() * (max - min);

        /** Title + flavor per mission type and tier — the scanner-report voice. */
        private static void WriteCopy(MissionSpec spec, int tier, System.Random rng)
        {
            string[] tierNames = { "Shallow Contract", "Rated Dive", "Deep Stretch" };
            switch (spec.type)
            {
                case MissionType.Retrieval:
                    spec.title = $"{tierNames[tier]}: Recovery";
                    spec.flavor = $"A sealed cargo pod is transmitting a locator ping at ~{spec.targetDepth:0} m. Bring it home intact.";
                    break;
                case MissionType.Neutralize:
                    spec.title = $"{tierNames[tier]}: Neutralize";
                    spec.flavor = $"Long-range sonar shows a large aggressive contact holding at ~{spec.targetDepth:0} m. Put it down and harvest what's left.";
                    break;
                default:
                    spec.title = $"{tierNames[tier]}: Survey";
                    spec.flavor = $"Anomalous readings near ~{spec.targetDepth:0} m. Get the sensor suite close to {spec.researchTargetCount} sites and hold position.";
                    break;
            }
        }
    }
}
