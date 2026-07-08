using System;
using System.Collections.Generic;
using UnityEngine;

namespace Submachina.Core
{
    /**
     * Tuning for bad-luck protection on a probability roll.
     *
     * The effective chance stays at the base chance for the first graceMisses
     * consecutive failures, then ramps toward a guaranteed hit over the next
     * rampMisses failures:
     *
     *   effective(m) = !enabled || m <= graceMisses
     *                ? baseChance
     *                : Lerp(baseChance, 1, shape((m - graceMisses) / rampMisses))
     *
     * Example — base 10%, grace 5, ramp 5, linear shape:
     *   miss streak 0–5 → 10%, 6 → 28%, 7 → 46%, 8 → 64%, 9 → 82%, 10 → 100%.
     *   Worst-case drought is 10 misses (the 11th roll always hits), and the
     *   long-run effective rate works out to ~17% instead of the 10% base.
     */
    [Serializable]
    public struct PitySettings
    {
        [Tooltip("Off = pure base chance, no bad-luck protection.")]
        public bool enabled;

        [Tooltip("Consecutive misses forgiven at pure base chance before the ramp starts.")]
        [Min(0)] public int graceMisses;

        [Tooltip("Additional misses over which the chance ramps from base to 100%.")]
        [Min(1)] public int rampMisses;

        [Tooltip("Optional shaping of the ramp. X = ramp progress 0-1, Y = blend base→guaranteed 0-1. " +
                 "Leave as a straight diagonal (or empty) for a linear ramp.")]
        public AnimationCurve rampShape;

        /** Sensible starting values: protection on, 5 forgiven misses, 5-miss ramp, linear. */
        public static PitySettings Default => new PitySettings
        {
            enabled = true,
            graceMisses = 5,
            rampMisses = 5,
            rampShape = AnimationCurve.Linear(0f, 0f, 1f, 1f)
        };
    }

    /**
     * Generic, keyed bad-luck protection ("pity") for probability rolls.
     *
     * Tracks consecutive misses per string key and boosts the effective chance
     * according to a PitySettings ramp, guaranteeing a hit once the streak is
     * unlucky enough. Any system that rolls a chance can adopt it — ore cluster
     * value spawns use it now; drop rolls (e.g. MiningResource scrap) can call
     * the float overload with UnityEngine.Random.value later.
     *
     * State is in-memory and per play session: the streak table is cleared on
     * play start via [RuntimeInitializeOnLoadMethod] (same statics-reset pattern
     * as SpecularLight2DManager) so domain-reload settings can't leak streaks
     * between sessions. Nothing persists to disk yet.
     *
     * Note for deterministic callers: a roll consumes exactly one 0-1 sample
     * regardless of pity state, so feeding it from a seeded System.Random never
     * perturbs how many draws the caller's stream makes. Only the OUTCOME is
     * pity-dependent (and therefore play-order-dependent).
     */
    public static class SpawnLuck
    {
        // Consecutive-miss streak per key. Missing key = streak 0.
        private static readonly Dictionary<string, int> MissStreaks = new Dictionary<string, int>();

        /** Fresh luck every play session — clears all streaks before the first scene loads. */
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetAll() => MissStreaks.Clear();

        // -------------------------------------------------------
        // Rolling
        // -------------------------------------------------------

        /** Convenience overload for deterministic spawn-side callers — draws one sample from rng. */
        public static bool Roll(string key, float baseChance, in PitySettings pity, System.Random rng)
            => Roll(key, baseChance, in pity, rng.NextFloat01());

        /**
         * Rolls the pity-adjusted chance against a uniform 0-1 sample and advances
         * the streak: success resets it to 0, failure increments it. The caller
         * supplies the sample so any RNG source works (seeded System.Random,
         * UnityEngine.Random.value, ...).
         *
         * Example: key "OreCluster", base 0.1, streak currently 7, grace 5, ramp 5
         *   → effective chance 46%; roll01 = 0.3 → HIT, streak resets to 0.
         */
        public static bool Roll(string key, float baseChance, in PitySettings pity, float roll01)
        {
            bool hit = roll01 < EffectiveChance(key, baseChance, in pity);

            // Advance the streak — a hit clears it, a miss deepens it
            MissStreaks[key] = hit ? 0 : MissStreak(key) + 1;
            return hit;
        }

        // -------------------------------------------------------
        // Inspection / control
        // -------------------------------------------------------

        /** The chance the next Roll for this key would use — peek only, no state change. */
        public static float EffectiveChance(string key, float baseChance, in PitySettings pity)
            => EffectiveChanceAtStreak(MissStreak(key), baseChance, in pity);

        /**
         * The pity math for an arbitrary streak, exposed so editor previews can
         * tabulate the ramp without touching live state. See PitySettings for the
         * formula and a worked example.
         */
        public static float EffectiveChanceAtStreak(int missStreak, float baseChance, in PitySettings pity)
        {
            // Inside the grace window (or protection off) — pure base chance
            if (!pity.enabled || missStreak <= pity.graceMisses) return baseChance;

            // Ramp progress 0-1 across rampMisses, optionally reshaped by the curve
            float t = Mathf.Clamp01((missStreak - pity.graceMisses) / (float)Mathf.Max(1, pity.rampMisses));
            if (pity.rampShape != null && pity.rampShape.length > 0) t = pity.rampShape.Evaluate(t);

            return Mathf.Lerp(baseChance, 1f, t);
        }

        /** Current consecutive-miss streak for a key (0 if never rolled or last roll hit). */
        public static int MissStreak(string key)
            => MissStreaks.TryGetValue(key, out int streak) ? streak : 0;

        /** Manually clears one key's streak (editor preview tooling, debugging). */
        public static void Reset(string key) => MissStreaks.Remove(key);
    }
}
