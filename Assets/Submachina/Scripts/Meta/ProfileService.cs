using System.IO;
using UnityEngine;
using Submachina.Core;

namespace Submachina.Meta
{
    /**
     * Static persistence service for the player profile (no scene object, no
     * singleton GameObject — plain code, safe to call from any scene).
     *
     * Load-on-demand: the first Current access loads (or creates) the profile
     * from persistentDataPath/submachina_profile.json. Mutating helpers save
     * immediately — profile writes are rare (purchases, mission end), so
     * write-through keeps things simple and crash-safe.
     */
    public static class ProfileService
    {
        private const string FileName = "submachina_profile.json";
        private static PlayerProfile _current;

        private static string SavePath => Path.Combine(Application.persistentDataPath, FileName);

        /** The active profile, loaded on first access. */
        public static PlayerProfile Current => _current ??= LoadOrCreate();

        // -------------------------------------------------------
        // Load / save
        // -------------------------------------------------------

        /** Reads the profile from disk, or starts a fresh one when none exists / parsing fails. */
        private static PlayerProfile LoadOrCreate()
        {
            try
            {
                if (File.Exists(SavePath))
                {
                    var loaded = JsonUtility.FromJson<PlayerProfile>(File.ReadAllText(SavePath));
                    if (loaded != null) return loaded;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[ProfileService] Failed to read profile, starting fresh: {e.Message}");
            }
            return new PlayerProfile();
        }

        /** Writes the current profile to disk. */
        public static void Save()
        {
            if (_current == null) return;
            try { File.WriteAllText(SavePath, JsonUtility.ToJson(_current, prettyPrint: true)); }
            catch (System.Exception e) { Debug.LogError($"[ProfileService] Failed to save profile: {e.Message}"); }
        }

        /** Deletes the save and resets in-memory state (debug / new game). */
        public static void ResetProfile()
        {
            _current = new PlayerProfile();
            if (File.Exists(SavePath)) File.Delete(SavePath);
        }

        // -------------------------------------------------------
        // Resource wallet
        // -------------------------------------------------------

        /** Banked amount for a resource key (0 when never held). */
        public static int GetResource(string key)
        {
            foreach (var entry in Current.resources)
                if (entry.key == key) return entry.amount;
            return 0;
        }

        public static int GetResource(ResourceType type) => type != null ? GetResource(type.Key) : 0;

        /** Adds (or subtracts) banked resources, clamped at zero, and saves. */
        public static void AddResource(string key, int delta)
        {
            if (string.IsNullOrEmpty(key) || delta == 0) return;

            var list = Current.resources;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].key != key) continue;
                var entry = list[i];
                entry.amount = Mathf.Max(0, entry.amount + delta);
                list[i] = entry;
                Save();
                return;
            }

            // First time this resource is banked
            list.Add(new PlayerProfile.ResourceEntry { key = key, amount = Mathf.Max(0, delta) });
            Save();
        }

        /**
         * Attempts to pay a multi-resource cost atomically: verifies every line
         * first, then deducts all of them. Returns false (nothing spent) if any
         * single resource is short.
         */
        public static bool TrySpend(ResourceCost[] costs)
        {
            if (costs == null) return true;

            // Verify affordability before touching anything
            foreach (var cost in costs)
                if (cost.type != null && GetResource(cost.type) < cost.amount) return false;

            foreach (var cost in costs)
                if (cost.type != null) AddResource(cost.type.Key, -cost.amount);
            return true;
        }

        // -------------------------------------------------------
        // Owned upgrades
        // -------------------------------------------------------

        /** Owned level of an upgrade (0 = not owned). */
        public static int GetUpgradeLevel(string defName)
        {
            foreach (var owned in Current.upgrades)
                if (owned.defName == defName) return owned.level;
            return 0;
        }

        /** Records a purchased level (sets absolute level, saves). */
        public static void SetUpgradeLevel(string defName, int level)
        {
            var list = Current.upgrades;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].defName != defName) continue;
                var owned = list[i];
                owned.level = level;
                list[i] = owned;
                Save();
                return;
            }
            list.Add(new PlayerProfile.OwnedUpgrade { defName = defName, level = level });
            Save();
        }

        // -------------------------------------------------------
        // Loadout selections
        // -------------------------------------------------------

        /** All chosen upgrade names for a slot (slots like Tools allow multiple picks). */
        public static System.Collections.Generic.List<string> GetLoadoutChoices(string slotName)
        {
            string prefix = slotName + ":";
            var picks = new System.Collections.Generic.List<string>();
            foreach (var selection in Current.loadoutSelections)
                if (selection.StartsWith(prefix)) picks.Add(selection.Substring(prefix.Length));
            return picks;
        }

        /** True when the def is currently picked in the slot. */
        public static bool IsLoadoutChoice(string slotName, string defName) =>
            Current.loadoutSelections.Contains(slotName + ":" + defName);

        /**
         * Toggles a pick within a slot, enforcing the slot's pick limit by evicting
         * the oldest pick when full (so single-pick slots behave like radio buttons).
         * Saves after every change.
         */
        public static void ToggleLoadoutChoice(string slotName, string defName, int maxPicks)
        {
            string key = slotName + ":" + defName;
            var list = Current.loadoutSelections;

            // Picked already → unpick
            if (list.Remove(key)) { Save(); return; }

            // Evict oldest picks until there's room, then add
            var existing = GetLoadoutChoices(slotName);
            while (existing.Count >= Mathf.Max(1, maxPicks))
            {
                list.Remove(slotName + ":" + existing[0]);
                existing.RemoveAt(0);
            }
            list.Add(key);
            Save();
        }

        // -------------------------------------------------------
        // Mission results
        // -------------------------------------------------------

        /** Banks a cargo hold's contents into the wallet (mission extraction). */
        public static void BankCargo(CargoHold cargo)
        {
            if (cargo == null) return;
            foreach (var kvp in cargo.Contents)
                AddResource(kvp.Key.Key, kvp.Value);
        }

        /** Records the outcome of a mission and any new depth record. */
        public static void RecordMission(bool success, float deepestDepth)
        {
            if (success) Current.missionsCompleted++;
            else Current.missionsFailed++;
            Current.deepestDepthReached = Mathf.Max(Current.deepestDepthReached, deepestDepth);
            Save();
        }
    }
}
