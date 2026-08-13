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

        /** A flat random count in [min, max] — fractions read as odds (uniform variation). */
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
     * EVERY model preserves fractions rather than rounding them away: a count
     * below 1 is a per-chunk probability (0.5 = one every 2 chunks), and a
     * count like 5.5 spawns 5 or 6. See StochasticRound.
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
                 "• Range — a flat random number between Min and Max, every whole value equally likely. " +
                 "For steady, depth-independent variety (e.g. 1–2 bubbles). Fractions are allowed and read " +
                 "as ODDS: a range of 0–0.5 means about a 25% chance of one per chunk.\n" +
                 "• Curve Range — the count RAMPS WITH DEPTH: it equals 'Count At Min Depth' at the shallow " +
                 "end and 'Count At Max Depth' at the deep end, blending linearly between (and clamping past " +
                 "Max Depth). For density that grows as you descend (rocks, enemies). Fractional counts are " +
                 "odds too — 0.5 → one every 2 chunks, 5.5 → 5 or 6. 'Midpoint Bias' bends the ramp: 0.5 is " +
                 "a straight line, higher front-loads the growth into the shallows, lower keeps it flat then " +
                 "spikes deep. The live preview below shows the actual counts at sample depths.\n" +
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
        [Tooltip("Minimum instances (inclusive). Fractions are odds, not zero — 0.5 means 'half an instance', " +
                 "i.e. a 50% chance of one.")]
        [Min(0f)] public float min = 1f;

        [ShowIf(nameof(kind), CountKind.Range)]
        [HorizontalGroup("Range"), LabelWidth(40)]
        [Tooltip("Maximum instances (inclusive). Fractions are odds — a 0–0.5 range averages 0.25 per chunk, " +
                 "about one every 4 chunks.")]
        [Min(0f)] public float max = 2f;

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
        [Tooltip("Count at (and shallower than) refMinDepth. Fractions are odds — 0.5 = one every 2 chunks.")]
        [Min(0f)] public float countAtMinDepth = 2f;

        [ShowIf(nameof(kind), CountKind.CurveRange)]
        [HorizontalGroup("Counts"), LabelWidth(110)]
        [Tooltip("Count at refMaxDepth and deeper. Fractions are odds — 0.5 = one every 2 chunks.")]
        [Min(0f)] public float countAtMaxDepth = 9f;

        [ShowIf(nameof(kind), CountKind.CurveRange)]
        [LabelText("$MidpointBiasLabel"), LabelWidth(160)]
        [Tooltip("Bends the ramp without needing a curve: how far the count has travelled from the shallow " +
                 "value to the deep value by the HALFWAY depth.\n\n" +
                 "0.5 = straight line (the default).\n" +
                 "Above 0.5 = front-loaded — most of the change happens shallow, then it flattens out.\n" +
                 "Below 0.5 = back-loaded — it stays near the shallow value, then ramps hard at depth.\n\n" +
                 "Example (2 → 9 over 0–300m): 0.5 gives 5.5 at 150m, 0.8 gives ~7.6, 0.2 gives ~3.4. " +
                 "The endpoints never move.")]
        [PropertyRange(0.05f, 0.95f)]
        [InfoBox("$CurveRangePreview", InfoMessageType.None)]
        public float midpointBias = 0.5f;

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
         *   t = 150/300 = 0.5 → raw = lerp(2,9,0.5) = 5.5 → 5 or 6 → floor(/2) = 2 or 3
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

                // Flat range, then density-scaled. Whole endpoints keep the classic
                // uniform integer roll (1–2 → 1 or 2, equally likely); fractional
                // endpoints sample a real number instead so sub-1 ranges survive as
                // odds (0–0.5 → averages 0.25/chunk) rather than rounding to nothing.
                case CountKind.Range:
                {
                    float lo = Mathf.Min(min, max);
                    float hi = Mathf.Max(min, max);
                    float rolled = IsWhole(lo) && IsWhole(hi)
                        ? rng.Next(Mathf.RoundToInt(lo), Mathf.RoundToInt(hi) + 1)
                        : rng.NextFloat(lo, hi);
                    count = StochasticRound(rolled * densityMultiplier, rng);
                    break;
                }

                // Float expectation — the whole part is guaranteed, the fraction
                // is a Bernoulli roll. Example: 2.3 × density 1 → 2 always, +1 at 30%.
                case CountKind.Expected:
                    count = StochasticRound(expectedCount * densityMultiplier, rng);
                    break;

                // Depth-ramped count — fractions along the ramp stay meaningful,
                // so a curve that ends at 0.5 really is one every other chunk
                default:
                    count = StochasticRound(RawCurveCount(depth) * densityMultiplier, rng);
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

        /**
         * Rounds a float count to an int WITHOUT discarding the fraction: the
         * whole part always spawns and the fraction becomes a weighted coin
         * flip. This is what lets sub-1 counts read as a per-chunk percentage
         * instead of collapsing to zero.
         *
         * Examples: 0.5 → 1 half the time (one every 2 chunks); 0.25 → one
         * every ~4 chunks; 5.5 → 5 or 6. Averages out to the exact input.
         *
         * Whole values consume no RNG draw, so rules authored with whole
         * numbers keep the exact random sequence (and worlds) they had before.
         */
        private static int StochasticRound(float value, System.Random rng)
        {
            if (value <= 0f) return 0;

            int whole = Mathf.FloorToInt(value);
            float frac = value - whole;
            if (frac <= 0f) return whole;

            return whole + (rng.NextFloat01() < frac ? 1 : 0);
        }

        /** True when a value is (near enough) a whole number — picks the Range roll style. */
        private static bool IsWhole(float v) => Mathf.Approximately(v, Mathf.Round(v));

        /** The un-rounded, un-scaled CurveRange count at a depth (deterministic). */
        private float RawCurveCount(float depth)
        {
            float t = Mathf.Approximately(refMaxDepth, refMinDepth)
                ? 1f
                : Mathf.Clamp01((depth - refMinDepth) / (refMaxDepth - refMinDepth));
            return Mathf.Lerp(countAtMinDepth, countAtMaxDepth, ApplyMidpointBias(t));
        }

        /**
         * Warps normalized depth so the ramp can bow toward either end — a
         * one-slider stand-in for authoring a full AnimationCurve.
         *
         * Uses a power curve t^k with k solved so the halfway depth lands
         * exactly on midpointBias: 0.5^k = bias → k = ln(bias)/ln(0.5).
         *   bias 0.5  → k = 1    → straight line
         *   bias 0.8  → k ≈ 0.32 → front-loaded (climbs fast, then flattens)
         *   bias 0.2  → k ≈ 2.32 → back-loaded (flat, then climbs hard deep)
         *
         * Monotonic and endpoint-preserving in every case: t=0 and t=1 always
         * map to themselves, so the authored min/max counts stay exact.
         */
        private float ApplyMidpointBias(float t)
        {
            // Straight line — skip the pow entirely (the overwhelmingly common
            // case). A 0 also means "unset" (the slider floor is 0.05), so old
            // assets predating this field stay linear rather than bending hard.
            if (midpointBias <= 0f || Mathf.Approximately(midpointBias, 0.5f)) return t;

            // Clamp guards against a 1.0 bias (zero exponent) from bad data
            float bias = Mathf.Clamp(midpointBias, 0.01f, 0.99f);
            return Mathf.Pow(t, Mathf.Log(bias) / Mathf.Log(0.5f));
        }

        // -------------------------------------------------------
        // Editor previews
        // -------------------------------------------------------

        // Names the bias slider with the shape it produces and the resolved
        // midpoint count, e.g. "Midpoint Bias — front-loaded (7.6 @ 150m)"
        private string MidpointBiasLabel
        {
            get
            {
                float mid = Mathf.Lerp(refMinDepth, refMaxDepth, 0.5f);
                string shape = Mathf.Approximately(midpointBias, 0.5f) ? "linear"
                    : midpointBias > 0.5f ? "front-loaded"
                    : "back-loaded";
                return $"Midpoint Bias — {shape} ({RawCurveCount(mid):0.##} @ {mid:F0}m)";
            }
        }

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
                    s += $"\n  {d:F0}m → {DescribeCount(RawCurveCount(d))}";
                return s;
            }
        }

        /**
         * Plain-language rendering of a fractional count, so designers can see
         * that 0.4 is "40% chance of 1" and 5.5 is "5 or 6" rather than a
         * number that looks like it rounds away.
         */
        private static string DescribeCount(float raw)
        {
            if (raw <= 0.0001f) return "0";

            int whole = Mathf.FloorToInt(raw);
            float frac = raw - whole;

            // Effectively a whole number — show it bare
            if (frac < 0.005f) return whole.ToString();

            // Sub-1 counts are odds: report both the chance and the cadence
            if (whole == 0) return $"{frac * 100f:F0}% chance of 1 (~1 every {1f / raw:F1} chunks)";

            return $"{raw:0.##} ({whole} or {whole + 1})";
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
                        float lo = Mathf.Min(min, max), hi = Mathf.Max(min, max);
                        float avg = (lo + hi) * 0.5f;
                        body = $"random {lo:0.##}–{hi:0.##} per chunk";
                        // Fractional endpoints don't read as counts, so spell out the average
                        if (!IsWhole(lo) || !IsWhole(hi)) body += $" → averages {DescribeCount(avg)}";
                        break;
                    case CountKind.Expected:
                        string cadence = expectedCount > 0.0001f && expectedCount < 1f
                            ? $" (~1 every {1f / expectedCount:F1} chunks)" : "";
                        body = $"averages {expectedCount:F2} per chunk{cadence}";
                        break;
                    default:
                        body = $"ramps {DescribeCount(RawCurveCount(refMinDepth))} → " +
                               $"{DescribeCount(RawCurveCount(refMaxDepth))} with depth";
                        // Only worth naming the shape when it isn't a straight line
                        if (!Mathf.Approximately(midpointBias, 0.5f))
                            body += midpointBias > 0.5f ? ", front-loaded" : ", back-loaded";
                        break;
                }

                if (split != CountSplit.None) body += $"  →  then {split} (count halved)";
                return "Effective: " + body;
            }
        }
    }
}
