using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Type-safe identity for a toggleable submarine feature.
     *
     * This ScriptableObject holds (almost) no data — its purpose is to be a
     * unique, drag-droppable identity that cannot be misspelled. Create one
     * asset per feature (e.g. "DashRam", "ReinforcedHull") via
     * Create > Submachina > Upgrade Feature.
     *
     * Two places reference the asset, and they are matched by object reference
     * (not by string), so renaming the asset never breaks the link:
     *   - UpgradeToggleTarget  — the marker on a child object in the sub hierarchy.
     *   - UpgradeDef.toggles   — the upgrade that switches matching objects on/off.
     */
    [CreateAssetMenu(menuName = "Submachina/Upgrade Feature")]
    public class UpgradeFeature : ScriptableObject
    {
        [Tooltip("Optional human-readable label. Purely for editor clarity — the " +
                 "asset's identity (not this string) is what gets matched at runtime.")]
        public string displayName;

        [TextArea(2, 4)]
        [Tooltip("Optional designer notes describing what objects this feature tags.")]
        public string notes;
    }
}
