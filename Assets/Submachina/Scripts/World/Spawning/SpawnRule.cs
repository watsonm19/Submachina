using System;
using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;
using Submachina.Meta;

namespace Submachina.Core
{
    /**
     * A reusable, named spawn rule stored as its own asset.
     *
     * This is a thin wrapper around SpawnRuleData so the same rule (e.g.
     * "Rock_Wall" or "Enemy") can be referenced by multiple SpawnProfiles and
     * edited in one place. For one-off rules specific to a single profile,
     * author them inline on the SpawnProfile instead — both paths feed the
     * same execution code.
     *
     * Every rule asset also carries an optional MissionGate: mission trait
     * flags can suppress the rule or scale its spawn rate, so a single flag
     * (e.g. Infested) retunes any number of rules without new assets. The gate
     * is neutral by default and inert without an active mission.
     *
     * Subclasses can override Rules to contribute MULTIPLE (even runtime-built)
     * rule datas from one asset — see MissionResourceRule, which expands the
     * active mission's resource forecast into concrete per-type rules. This is
     * the composition hook for future biome/mission rule packs.
     *
     * Create via: Assets → Create → Submachina → Spawning → Spawn Rule
     */
    [CreateAssetMenu(fileName = "SpawnRule", menuName = "Submachina/Spawning/Spawn Rule")]
    public class SpawnRule : ScriptableObject
    {
        [ShowIf(nameof(UsesAuthoredRule)), HideLabel, InlineProperty]
        [SerializeField] private SpawnRuleData rule = new SpawnRuleData();

        [FoldoutGroup("Mission Gate")]
        [Tooltip("Optional mission-trait gating: require/exclude MissionFlags and per-flag rate scaling. " +
                 "Neutral by default; ignored entirely when no mission is active (sandbox play).")]
        [HideLabel, InlineProperty]
        [SerializeField] private MissionGate missionGate = new MissionGate();

        // Cached gate resolution — rebuilt whenever the active mission spec
        // changes (reference compare; specs are immutable once launched)
        [NonSerialized] private SpawnRuleData _gated;
        [NonSerialized] private MissionSpec _gatedFor;
        [NonSerialized] private bool _gatedOnce;

        /** The underlying rule data executed by WorldChunk (ungated — editor/preview use). */
        public SpawnRuleData Rule => rule;

        /**
         * Subclasses that replace the authored rule entirely (e.g.
         * MissionResourceRule's template expansion) override this to false so
         * the dead rule block disappears from their inspector instead of
         * inviting edits that do nothing.
         */
        protected virtual bool UsesAuthoredRule => true;

        /**
         * Inspector edits drop the cached gate resolution. Vital with Enter
         * Play Mode domain reload OFF: [NonSerialized] caches survive play
         * sessions there, so without this an edit would never take effect
         * (the cache is only re-keyed when the mission spec reference changes).
         */
        protected virtual void OnValidate()
        {
            _gated = null;
            _gatedFor = null;
            _gatedOnce = false;
        }

        /** Rate scale the mission gate resolves for a spec (1 = neutral, 0 = suppressed). For subclasses. */
        protected float MissionGateScale(MissionSpec spec) => missionGate?.Resolve(spec) ?? 1f;

        /** Every rule data this asset contributes (base: the one authored rule, mission-gated). */
        public virtual IEnumerable<SpawnRuleData> Rules
        {
            get
            {
                // Fast path — a neutral gate yields the raw asset data, no cloning or caching
                if (missionGate == null || missionGate.IsNeutral) { yield return rule; yield break; }

                // Re-resolve the gate when the active mission changes
                var spec = MissionContext.Current;
                if (!_gatedOnce || !ReferenceEquals(spec, _gatedFor))
                {
                    _gated = BuildGated(spec);
                    _gatedFor = spec;
                    _gatedOnce = true;
                }
                if (_gated != null) yield return _gated;
            }
        }

        /** The authored rule after gating: null = suppressed, a rate-scaled shallow copy when scale ≠ 1. */
        private SpawnRuleData BuildGated(MissionSpec spec)
        {
            float scale = missionGate.Resolve(spec);
            if (scale <= 0f) return null;
            if (Mathf.Approximately(scale, 1f)) return rule;

            // Scale rides on a copy so the asset itself is never mutated
            SpawnRuleData scaled = rule.ShallowCopy();
            scaled.runtimeRateScale = scale;
            return scaled;
        }
    }
}
