#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Submachina.Core.Editor
{
    /**
     * One-time setup wizard that creates sample UpgradeDef assets, an
     * UpgradeDraftPool, and wires the UpgradeManager + UpgradeDraftUI
     * onto the submarine in the active scene.
     *
     * Run from the menu: Tools > Submachina > Setup Upgrade System
     *
     * Safe to run multiple times — skips assets that already exist and
     * only adds components if they're not already present.
     */
    public static class UpgradeSetupWizard
    {
        private const string AssetFolder = "Assets/Submachina/Data/Upgrades";

        [MenuItem("Tools/Submachina/Setup Upgrade System")]
        public static void Run()
        {
            EnsureFolder(AssetFolder);

            // ── Create sample UpgradeDef assets ──
            var increaseO2 = CreateStatUpgrade("IncreaseO2Capacity",
                "Increase O2 Capacity",
                "Expands the air tank, giving you more time between pumps.",
                SubStats.MaxAirPressure, 15f, 0f, 3);

            var increasePumpGain = CreateStatUpgrade("IncreasePumpSweetGain",
                "Stronger Pump",
                "Perfect pumps restore more air pressure.",
                SubStats.PerfectPumpAir, 5f, 0f, 3);

            var increaseWeaponDmg = CreateStatUpgrade("IncreaseWeaponDamage",
                "Sharper Blades",
                "Each melee swing deals more damage.",
                SubStats.AttackDamage, 1f, 0f, 3);

            var decreaseDashCost = CreateStatUpgrade("DecreaseDashCost",
                "Efficient Dash",
                "Cavitation bursts consume less O2.",
                SubStats.DashAirCost, -3f, 0f, 3);

            var fasterLateral = CreateStatUpgrade("FasterLateral",
                "Lateral Thrusters",
                "Increases side-to-side thrust force.",
                SubStats.LateralThrustForce, 2f, 0f, 3);

            var fasterDescent = CreateStatUpgrade("FasterCounterThrust",
                "Counter Thrusters",
                "Increases upward thrust against the current.",
                SubStats.CounterThrustForce, 3f, 0f, 3);

            var decreaseDashCooldown = CreateStatUpgrade("DecreaseDashCooldown",
                "Quick Recovery Dash",
                "Reduces the cooldown between dashes.",
                SubStats.DashCooldown, -0.3f, 0f, 3);

            var lessCollisionDmg = CreateStatUpgrade("LessCollisionDamage",
                "Reinforced Hull",
                "Reduces damage taken from collisions with terrain.",
                SubStats.CollisionDamagePerImpact, -1f, 0f, 1);

            // ── Create UpgradeDraftPool ──
            var pool = CreateOrLoadAsset<UpgradeDraftPool>($"{AssetFolder}/SampleDraftPool.asset");
            pool.draftsPerLevelUp = 3;
            pool.upgrades.Clear();
            pool.upgrades.Add(increaseO2);
            pool.upgrades.Add(increasePumpGain);
            pool.upgrades.Add(increaseWeaponDmg);
            pool.upgrades.Add(decreaseDashCost);
            pool.upgrades.Add(fasterLateral);
            pool.upgrades.Add(fasterDescent);
            pool.upgrades.Add(decreaseDashCooldown);
            pool.upgrades.Add(lessCollisionDmg);
            EditorUtility.SetDirty(pool);

            // ── Wire components onto the submarine in the scene ──
            var submarines = Object.FindObjectsByType<Submarine>(FindObjectsSortMode.None);
            int wired = 0;

            foreach (var sub in submarines)
            {
                // Add UpgradeManager if missing
                if (sub.GetComponentInChildren<UpgradeManager>() == null)
                {
                    var mgrGO = new GameObject("UpgradeManager");
                    mgrGO.transform.SetParent(sub.transform, false);
                    mgrGO.AddComponent<UpgradeManager>();
                    Undo.RegisterCreatedObjectUndo(mgrGO, "Add UpgradeManager");
                }

                // Add UpgradeDraftUI if missing
                var draftUI = sub.GetComponentInChildren<UpgradeDraftUI>();
                if (draftUI == null)
                {
                    var uiGO = new GameObject("UpgradeDraftUI");
                    uiGO.transform.SetParent(sub.transform, false);
                    draftUI = uiGO.AddComponent<UpgradeDraftUI>();
                    Undo.RegisterCreatedObjectUndo(uiGO, "Add UpgradeDraftUI");
                }

                // Assign the draft pool to the UI
                var poolField = typeof(UpgradeDraftUI).GetField("draftPool",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (poolField != null)
                {
                    poolField.SetValue(draftUI, pool);
                    EditorUtility.SetDirty(draftUI);
                }

                wired++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[UpgradeSetupWizard] Created 8 sample upgrades + 1 draft pool at {AssetFolder}");
            Debug.Log($"[UpgradeSetupWizard] Wired UpgradeManager + UpgradeDraftUI on {wired} submarine(s)");

            if (wired == 0)
                Debug.LogWarning("[UpgradeSetupWizard] No Submarine found in the active scene. " +
                    "Open your game scene and run again, or add the components manually.");
        }

        // -------------------------------------------------------
        // Asset Helpers
        // -------------------------------------------------------

        private static UpgradeDef CreateStatUpgrade(string fileName, string displayName,
            string description, StatId stat, float additive, float multiplier, int maxLevel)
        {
            string path = $"{AssetFolder}/{fileName}.asset";

            var def = CreateOrLoadAsset<UpgradeDef>(path);
            def.upgradeName = displayName;
            def.description = description;
            def.maxLevel = maxLevel;
            def.tags = new string[0];
            def.prerequisites = new UpgradeDef[0];

            def.statModifiers = new StatModifierEntry[]
            {
                new StatModifierEntry
                {
                    stat = stat,
                    additivePerLevel = additive,
                    multiplierPerLevel = multiplier
                }
            };

            EditorUtility.SetDirty(def);
            return def;
        }

        private static T CreateOrLoadAsset<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;

            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif
