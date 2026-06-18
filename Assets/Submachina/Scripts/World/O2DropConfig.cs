using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Drop configuration for O2 bubble pickups.
     *
     * Extends DropConfig with a size range and a distribution curve that
     * controls how likely each size is. Each spawned bubble gets a random
     * size via O2Pickup.SetSize(), which handles visual scale and the
     * non-linear reward multiplier.
     *
     * Size selection works in two steps:
     *   1. Roll a uniform random value [0, 1].
     *   2. Remap it through sizeDistribution to get a normalized position,
     *      then lerp between sizeMin and sizeMax.
     *
     * A straight diagonal curve = uniform distribution.
     * A curve that stays low then rises steeply = most drops are small,
     * rare drops are large.
     */
    [System.Serializable]
    public class O2DropConfig : DropConfig
    {
        [Tooltip("Minimum bubble size passed to O2Pickup.SetSize(). " +
                 "1.0 = the prefab's default visual size.")]
        [Min(0.1f)] public float sizeMin = 1f;

        [Tooltip("Maximum bubble size. Each bubble rolls independently " +
                 "for natural variety.")]
        [Min(0.1f)] public float sizeMax = 1f;

        [Tooltip("Remaps a uniform random roll [0,1] (X) to a normalized " +
                 "position [0,1] (Y) between sizeMin and sizeMax.\n\n" +
                 "Straight diagonal = uniform distribution (equal chance of all sizes).\n" +
                 "Flat at bottom, steep rise at end = most drops small, rare large.\n\n" +
                 "Example: sizeMin=1, sizeMax=5. Curve stays at Y=0.25 from X=0–0.8, " +
                 "then rises to Y=1.0 → 80% of drops land near size 2, the other 20% " +
                 "spread across 2–5.")]
        [InfoBox("$DistributionPreview")]
        public AnimationCurve sizeDistribution = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        // -------------------------------------------------------
        // Editor Preview
        // -------------------------------------------------------

        private string DistributionPreview
        {
            get
            {
                if (sizeDistribution == null) return "No curve assigned.";
                if (sizeMin >= sizeMax) return $"All drops: size {sizeMin:F2} (min ≥ max)";

                float p10 = Mathf.Lerp(sizeMin, sizeMax, sizeDistribution.Evaluate(0.1f));
                float p25 = Mathf.Lerp(sizeMin, sizeMax, sizeDistribution.Evaluate(0.25f));
                float p50 = Mathf.Lerp(sizeMin, sizeMax, sizeDistribution.Evaluate(0.5f));
                float p75 = Mathf.Lerp(sizeMin, sizeMax, sizeDistribution.Evaluate(0.75f));
                float p90 = Mathf.Lerp(sizeMin, sizeMax, sizeDistribution.Evaluate(0.9f));

                return $"Median: {p50:F2}   |   " +
                       $"Middle 50%: {p25:F2}–{p75:F2}   |   " +
                       $"Rare (top 10%): ≥ {p90:F2}   |   " +
                       $"Small (bottom 10%): ≤ {p10:F2}";
            }
        }

        // -------------------------------------------------------
        // Spawning
        // -------------------------------------------------------

        /**
         * Finds the O2Pickup on the spawned instance and assigns a random
         * size sampled through the distribution curve.
         */
        protected override void Configure(GameObject instance)
        {
            O2Pickup pickup = instance.GetComponent<O2Pickup>();
            if (pickup == null) return;

            // Remap a uniform roll through the distribution curve, then lerp into the size range
            float t = sizeDistribution.Evaluate(Random.value);
            float size = Mathf.Lerp(sizeMin, sizeMax, t);
            pickup.SetSize(size);
        }
    }
}
