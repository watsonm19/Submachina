using System;
using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;
using Submachina.Meta;

namespace Submachina.Core
{
    /**
     * Optional mission-trait gating for a spawn rule: which MissionFlags must /
     * must not be present on the active mission, plus per-flag rate multipliers.
     * One flag (e.g. Infested) can quietly enable, disable, or retune many
     * rules at once — resources, creatures, and enemies alike.
     *
     * Resolution contract: Resolve returns a rate scale — 1 = unaffected,
     * 0 = fully suppressed, anything else multiplies the rule's count model.
     * A null spec (sandbox / direct scene play / edit mode) deactivates the
     * gate entirely so test scenes always see the rule.
     *
     * NOTE: reads the Meta layer's MissionFlags from the spawning layer — part
     * of the sanctioned upward reference documented in Meta/context.md
     * (alongside MissionResourceRule's MissionContext read).
     */
    [Serializable]
    public class MissionGate
    {
        [Tooltip("Spawn only when the active mission has AT LEAST ONE of these flags (None = no requirement).")]
        public MissionFlags requireAny = MissionFlags.None;

        [Tooltip("Suppress the rule entirely when the active mission has ANY of these flags.")]
        public MissionFlags excludeAny = MissionFlags.None;

        [Tooltip("Rate multipliers applied when the mission carries the flag (multiple matches stack multiplicatively). " +
                 "Example: Infested × 2.5 on the enemy rule makes flagged sites feel dangerous without new rules.")]
        public List<FlagRateScale> rateScales = new();

        /** One flag → rate multiplier pairing. */
        [Serializable]
        public class FlagRateScale
        {
            [HorizontalGroup, LabelWidth(50)]
            public MissionFlags flag = MissionFlags.None;

            [HorizontalGroup, LabelWidth(70)]
            [Tooltip("Count-model multiplier while the flag is present (0 = suppress).")]
            [Min(0f)] public float multiplier = 1f;
        }

        /** True when this gate can never change anything (the default state) — lets callers skip resolution. */
        public bool IsNeutral
            => requireAny == MissionFlags.None && excludeAny == MissionFlags.None
               && (rateScales == null || rateScales.Count == 0);

        /**
         * Rate scale for a mission spec: 1 = unaffected, 0 = suppressed, else
         * the product of every matching flag multiplier.
         */
        public float Resolve(MissionSpec spec)
        {
            // No mission (sandbox / editor) → gate inactive so everything stays testable
            if (spec == null) return 1f;

            // Hard gates first: exclusion wins, then the any-of requirement
            if (excludeAny != MissionFlags.None && (spec.flags & excludeAny) != 0) return 0f;
            if (requireAny != MissionFlags.None && (spec.flags & requireAny) == 0) return 0f;

            // Soft scaling: every matching flag multiplies the rate
            float scale = 1f;
            if (rateScales != null)
                foreach (var entry in rateScales)
                    if (entry != null && (spec.flags & entry.flag) != 0) scale *= entry.multiplier;
            return scale;
        }
    }
}
