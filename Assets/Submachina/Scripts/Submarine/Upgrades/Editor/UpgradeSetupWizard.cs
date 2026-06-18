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
                SetPrivateField(draftUI, "draftPool", pool);

                // Add UpgradeDebugPanel if missing (requires UIDocument)
                var debugPanel = sub.GetComponentInChildren<UpgradeDebugPanel>();
                if (debugPanel == null)
                {
                    var debugGO = new GameObject("UpgradeDebugPanel");
                    debugGO.transform.SetParent(sub.transform, false);
                    debugPanel = debugGO.AddComponent<UpgradeDebugPanel>();
                    Undo.RegisterCreatedObjectUndo(debugGO, "Add UpgradeDebugPanel");
                }

                // Assign the full catalog to the debug panel
                SetPrivateField(debugPanel, "catalog", pool);

                // Ensure UIDocument has PanelSettings
                var uiDoc = debugPanel.GetComponent<UnityEngine.UIElements.UIDocument>();
                if (uiDoc != null && uiDoc.panelSettings == null)
                {
                    var panelSettings = FindOrCreatePanelSettings();
                    uiDoc.panelSettings = panelSettings;
                    EditorUtility.SetDirty(uiDoc);
                }

                wired++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[UpgradeSetupWizard] Created 8 sample upgrades + 1 draft pool at {AssetFolder}");
            Debug.Log($"[UpgradeSetupWizard] Wired UpgradeManager + UpgradeDraftUI + UpgradeDebugPanel on {wired} submarine(s)");
            Debug.Log($"[UpgradeSetupWizard] Press Tab in play mode to open the debug upgrade panel");

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

        private static UnityEngine.UIElements.PanelSettings FindOrCreatePanelSettings()
        {
            // Reuse our own PanelSettings if it already exists
            string settingsPath = $"{AssetFolder}/UpgradeDebugPanelSettings.asset";
            var existing = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>(settingsPath);
            if (existing != null) return existing;

            // Create a dedicated one — don't grab random demo assets
            var settings = ScriptableObject.CreateInstance<UnityEngine.UIElements.PanelSettings>();
            settings.scaleMode = UnityEngine.UIElements.PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = UnityEngine.UIElements.PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            settings.sortingOrder = 200;
            AssetDatabase.CreateAsset(settings, settingsPath);
            return settings;
        }

        private static void SetPrivateField(Object target, string fieldName, Object value)
        {
            var field = target.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(target, value);
                EditorUtility.SetDirty(target);
            }
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
