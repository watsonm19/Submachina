using System;
using System.Collections.Generic;

namespace Submachina.Meta
{
    /**
     * The player's persistent meta-progression state — everything that survives
     * between missions and sessions. Serialized to JSON by ProfileService.
     *
     * JsonUtility can't serialize dictionaries, so collections are lists of
     * small serializable entries; ProfileService provides keyed accessors.
     *
     * Resources are keyed by ResourceType.Key (the asset name) and upgrades by
     * UpgradeDef name — assets are resolved back through the UpgradeCatalog /
     * resource assets at load time, keeping the JSON free of Unity references.
     */
    [Serializable]
    public class PlayerProfile
    {
        /** Banked resource wallet: one entry per ResourceType the player has ever held. */
        public List<ResourceEntry> resources = new();

        /** Permanently purchased upgrades and their levels. */
        public List<OwnedUpgrade> upgrades = new();

        /** Loadout picks, one per slot: "slotName:defName". Re-picked at the hub. */
        public List<string> loadoutSelections = new();

        /** Lifetime statistics — shown at the hub, used for mission generation. */
        public int missionsCompleted;
        public int missionsFailed;
        public float deepestDepthReached;

        [Serializable]
        public struct ResourceEntry
        {
            public string key;      // ResourceType.Key (asset name)
            public int amount;
        }

        [Serializable]
        public struct OwnedUpgrade
        {
            public string defName;  // UpgradeDef asset name
            public int level;
        }
    }
}
