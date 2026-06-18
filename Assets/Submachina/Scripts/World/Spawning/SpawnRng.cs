using System;

namespace Submachina.Core
{
    /**
     * Extension helpers for sampling from a deterministic System.Random.
     *
     * The spawn system uses System.Random (seeded per chunk) rather than
     * UnityEngine.Random so worlds are reproducible: the same world seed +
     * cell coordinate always produces the same chunk. These helpers give it
     * the same convenience surface as UnityEngine.Random (float ranges, bools).
     */
    public static class SpawnRng
    {
        /** Returns a uniform float in [min, max). */
        public static float NextFloat(this Random rng, float min, float max)
            => min + (float)rng.NextDouble() * (max - min);

        /** Returns a uniform float in [0, 1). */
        public static float NextFloat01(this Random rng)
            => (float)rng.NextDouble();

        /** Returns true ~50% of the time — equivalent to Random.value > 0.5f. */
        public static bool NextBool(this Random rng)
            => rng.NextDouble() >= 0.5;
    }
}
