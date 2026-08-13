using System;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /** How a rule decides how many instances to spawn in a chunk. */
    public enum CountKind
    {
        /** One roll against a probability — 0 or 1 instance (rare encounters). */
        SingleRoll,

        /** A flat random integer in [min, max] (uniform variation). */
        Range,

        /** A count that ramps with depth between two endpoints (density curves). */
        CurveRange,

        /** A float average per chunk — the fraction becomes a weighted coin flip (exact sub-1 rates). */
        Expected
    }

    /** Optional post-split applied to the evaluated count. */
    public enum CountSplit
    {
        /** Use the count as-is. */
        None,

        /** Take floor(count / 2) — pairs with CeilHalf to split one budget across two rules. */
        FloorHalf,

        /** Take ceil(count / 2). */
        CeilHalf
    }

    /**
     * Describes how many instances of a rule spawn in a single chunk.
     *
     * Four models cover every existing behavior:
     *   - SingleRoll  → passive creature / ramming enemy (probability gate).
     *   - Range       → passive O2 (flat 1–2 per chunk).
     *   - CurveRange  → rocks / resources / enemies (count ramps with depth).
     *   - Expected    → mission resources (float average; fractions stay exact,
     *                   so 0.3/chunk really is one node every ~3 chunks).
     *
     * The optional Split lets two rules share one rounded budget: the original
     * rock generator computed totalCount once then split it half wall / half
     * center. Giving the wall rule FloorHalf and the center rule CeilHalf over
     * the same CurveRange reproduces that exactly (both round the identical
     * deterministic curve, then floor/ceil the half).
     */
    [Serializable]
    public class CountModel
    {
        [InfoBox("How many instances spawn per chunk:\n" +
                 "• Single Roll — flip ONE weighted coin: spawn 1 (at 'Spawn Chance') or 0. For rare, " +
                 "at-most-one things (a mini-boss, a special creature).\n" +
                 "• Range — a flat random whole number between Min and Max, every value equally likely. " +
                 "For steady, depth-independent variety (e.g. 1–2 bubbles).\n" +
                 "• Curve Range — the count RAMPS WITH DEPTH: it equals 'Count At Min Depth' at the shallow " +
                 "end and 'Count At Max Depth' at the deep end, blending linearly between (and clamping past " +
                 "Max Depth). For density that grows as you descend (rocks, enemies). The live preview below " +
                 "shows the actual counts at sample depths.\n" +
                 "• Expected — averages a FLOAT count: the whole part always spawns, the fraction is a " +
                 "weighted coin flip (0.3 → about one every 3 chunks; 2.5 → 2 or 3 each chunk). The only " +
                 "model with exact sub-1-per-chunk rates.",
            InfoMessageType.None, "@Submachina.Core.SpawnDocs.ShowHelp")]
        [Tooltip("Which counting model this rule uses.")]
        [EnumToggleButtons]
        public CountKind kind = CountKind.CurveRange;

        // ---- SingleRoll ----

        [ShowIf(nameof(kind), CountKind.SingleRoll)]
        [Tooltip("Probability (0–1) that one instance spawns. Scaled by global density × prevalence.")]
        [Range(0f, 1f)]
        public float spawnChance = 0.3f;

        // ---- Range ----

        [ShowIf(nameof(kind), CountKind.Range)]
        [HorizontalGroup("Range"), LabelWidth(40)]
        [Tooltip("Minimum instances (inclusive).")]
        public int min = 1;

        [ShowIf(nameof(kind), CountKind.Range)]
        [HorizontalGroup("Range"), LabelWidth(40)]
        [Tooltip("Maximum instances (inclusive).")]
        public int max = 2;

        // ---- CurveRange ----

        [ShowIf(nameof(kind), CountKind.CurveRange)]
        [HorizontalGroup("Depths"), LabelWidth(110)]
        [Tooltip("Depth (m) at which 'Count At Min Depth' applies (the shallow end of the ramp).")]
        public float refMinDepth = 0f;

        [ShowIf(nameof(kind), CountKind.CurveRange)]
        [HorizontalGroup("Depths"), LabelWidth(110)]
        [Tooltip("Depth (m) at which 'Count At Max Depth' applies. Deeper than this the count is clamped.")]
        public float refMaxDepth = 300f;

        [ShowIf(nameof(kind), CountKind.CurveRange)]
        [HorizontalGroup("Counts"), LabelWidth(110)]
        [Tooltip("Count at (and shallower than) refMinDepth.")]
        public float countAtMinDepth = 2f;

        [ShowIf(nameof(kind), CountKind.CurveRange)]
        [HorizontalGroup("Counts"), LabelWidth(110)]
        [Tooltip("Count at refMaxDepth and deeper.")]
        [InfoBox("$CurveRangePreview", InfoMessageType.None)]
        public float countAtMaxDepth = 9f;

        // ---- Expected ----

        [ShowIf(nameof(kind), CountKind.Expected)]
        [Tooltip("Average instances per chunk — fractions are exact over time: the whole part always " +
                 "spawns, the fraction is a weighted coin flip. 0.3 → about one every 3 chunks; " +
                 "2.5 → 2 or 3 each chunk. Scaled by global density × prevalence before rolling.")]
        [Min(0f)] public float expectedCount = 0.5f;

        // ---- Split ----

        [InfoBox("Split divides the evaluated count in half — used to spread ONE budget across TWO rules.\n" +
                 "Example: the rock budget ramps to 9 deep down. The wall-rock rule takes Floor Half (rounds " +
                 "DOWN → 4) and the center-rock rule takes Ceil Half (rounds UP → 5); together exactly 9, " +
                 "never lost or double-counted. Use this only when two rules intentionally share a budget — " +
                 "otherwise leave it None.",
            InfoMessageType.None, "@Submachina.Core.SpawnDocs.ShowHelp")]
        [Tooltip("Optionally halve the evaluated count. Pair FloorHalf + CeilHalf on two rules to divide one budget.")]
        [EnumToggleButtons]
        [InfoBox("$EffectiveSummary", InfoMessageType.None)]
        public CountSplit split = CountSplit.None;

        // -------------------------------------------------------
        // Evaluation
        // -------------------------------------------------------

        /**
         * Returns the number of instances to spawn for the given depth.
         *
         * densityMultiplier folds in the profile's global density and the
         * rule's prevalence curve. For SingleRoll it scales the probability;
         * for count models it scales the float count before rounding.
         *
         * Example (CurveRange 2→9 over 0–300m, depth=150, density=1, FloorHalf):
         *   t = 150/300 = 0.5 → raw = lerp(2,9,0.5) = 5.5 → round = 6 → floor(6/2) = 3
         */
        public int Evaluate(float depth, System.Random rng, float densityMultiplier)
        {
            int count;

            switch (kind)
            {
                // Single probability gate — 0 or 1
                case CountKind.SingleRoll:
                    count = rng.NextFloat01() < spawnChance * densityMultiplier ? 1 : 0;
                    break;

                // Flat uniform range, then density-scaled
                case CountKind.Range:
                    int rolled = rng.Next(Mathf.Min(min, max), Mathf.Max(min, max) + 1);
                    count = Mathf.RoundToInt(rolled * densityMultiplier);
                    break;

                // Float expectation — the whole part is guaranteed, the fraction
                // is a Bernoulli roll. Example: 2.3 × density 1 → 2 always, +1 at 30%.
                case CountKind.Expected:
                    float scaled = expectedCount * densityMultiplier;
                    int whole = Mathf.FloorToInt(scaled);
                    count = whole + (rng.NextFloat01() < scaled - whole ? 1 : 0);
                    break;

                // Depth-ramped count
                default:
                    count = Mathf.RoundToInt(RawCurveCount(depth) * densityMultiplier);
                    break;
            }

            // Apply optional half-split (used to divide one budget across two rules)
            switch (split)
            {
                case CountSplit.FloorHalf: count /= 2; break;
                case CountSplit.CeilHalf:  count -= count / 2; break;
            }

            return Mathf.Max(0, count);
        }

        /** The un-rounded, un-scaled CurveRange count at a depth (deterministic). */
        private float RawCurveCount(float depth)
        {
            float t = Mathf.Approximately(refMaxDepth, refMinDepth)
                ? 1f
                : Mathf.Clamp01((depth - refMinDepth) / (refMaxDepth - refMinDepth));
            return Mathf.Lerp(countAtMinDepth, countAtMaxDepth, t);
        }

        // -------------------------------------------------------
        // Editor previews
        // -------------------------------------------------------

        // Shows the resolved counts at sample depths for the CurveRange model
        private string CurveRangePreview
        {
            get
            {
                float q1 = Mathf.Lerp(refMinDepth, refMaxDepth, 0.25f);
                float mid = Mathf.Lerp(refMinDepth, refMaxDepth, 0.5f);
                float q3 = Mathf.Lerp(refMinDepth, refMaxDepth, 0.75f);
                float[] depths = { refMinDepth, q1, mid, q3, refMaxDepth };
                string s = "Counts by depth (before split/density):";
                foreach (float d in depths)
                    s += $"\n  {d:F0}m → {Mathf.RoundToInt(RawCurveCount(d))}";
                return s;
            }
        }

        // One-line plain-language summary of the effective behavior, including split
        private string EffectiveSummary
        {
            get
            {
                string body;
                switch (kind)
                {
                    case CountKind.SingleRoll:
                        float pct = Mathf.Clamp01(spawnChance) * 100f;
                        string freq = spawnChance > 0.0001f ? $"~1 every {(1f / spawnChance):F1} chunks" : "never";
                        body = $"≈{pct:F0}% chance to spawn 1 ({freq})";
                        break;
                    case CountKind.Range:
                        body = $"random {Mathf.Min(min, max)}–{Mathf.Max(min, max)} per chunk";
                        break;
                    case CountKind.Expected:
                        string cadence = expectedCount > 0.0001f && expectedCount < 1f
                            ? $" (~1 every {1f / expectedCount:F1} chunks)" : "";
                        body = $"averages {expectedCount:F2} per chunk{cadence}";
                        break;
                    default:
                        body = $"ramps {Mathf.RoundToInt(RawCurveCount(refMinDepth))} → " +
                               $"{Mathf.RoundToInt(RawCurveCount(refMaxDepth))} with depth";
                        break;
                }

                if (split != CountSplit.None) body += $"  →  then {split} (count halved)";
                return "Effective: " + body;
            }
        }
    }
}
