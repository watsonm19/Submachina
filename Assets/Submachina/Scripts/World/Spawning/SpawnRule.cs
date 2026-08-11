using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

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
        [HideLabel, InlineProperty]
        [SerializeField] private SpawnRuleData rule = new SpawnRuleData();

        /** The underlying rule data executed by WorldChunk. */
        public SpawnRuleData Rule => rule;

        /** Every rule data this asset contributes (base: just the one authored rule). */
        public virtual IEnumerable<SpawnRuleData> Rules
        {
            get { yield return rule; }
        }
    }
}
