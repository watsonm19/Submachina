using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Submachina.Core;
using Submachina.Meta;

namespace Submachina.EditorTools
{
    /**
     * One-shot builder for the hub meta content: the ballast unlock feature,
     * the new hub-only UpgradeDefs, the loadout slots, the UpgradeCatalog, and
     * the mission objective prefabs. Idempotent — re-running updates existing
     * assets in place, so tuning numbers here and re-running is safe.
     *
     * Menu: Tools/Submachina/Build Meta Content
     */
    public static class MetaContentBuilder
    {
        private const string UpgradeDir = "Assets/Submachina/Data/Upgrades";
        private const string FeatureDir = "Assets/Submachina/Data/UpgradeFeatureIds";
        private const string MetaDir = "Assets/Submachina/Data/Meta";
        private const string ResourceDir = "Assets/Submachina/Data/Resources";
        private const string MissionPrefabDir = "Assets/Submachina/Prefabs/Missions";

        [MenuItem("Tools/Submachina/Build Meta Content")]
        public static void Build()
        {
            EnsureFolder(MetaDir);
            EnsureFolder(MissionPrefabDir);

            // -- Ballast unlock feature + marker-driven upgrade --
            var ballastFeature = LoadOrCreate<UpgradeFeature>(FeatureDir + "/BallastTankFeature.asset");

            var ballastDef = LoadOrCreate<UpgradeDef>(UpgradeDir + "/BallastTankUnlock.asset");
            ballastDef.upgradeName = "Ballast Tank";
            ballastDef.description = "Flood to sink, blow stored air to rise — vertical traversal without burning thrust. Hold C to flood, X to blow, V to retarget the pumps.";
            ballastDef.maxLevel = 1;
            ballastDef.toggles = new[] { new UpgradeToggleEntry { feature = ballastFeature, setActive = true } };
            EditorUtility.SetDirty(ballastDef);

            // -- New hub-only stat upgrades --
            var doubleO2 = LoadOrCreate<UpgradeDef>(UpgradeDir + "/DoubleO2.asset");
            doubleO2.upgradeName = "Double O2 Reserve";
            doubleO2.description = "A second air cell doubles maximum O2 pressure. Long hauls stop being a countdown.";
            doubleO2.maxLevel = 1;
            doubleO2.statModifiers = new[] { new StatModifierEntry { stat = SubStats.MaxAirPressure, multiplierPerLevel = 1f } };
            EditorUtility.SetDirty(doubleO2);

            var cargoExp = LoadOrCreate<UpgradeDef>(UpgradeDir + "/CargoHoldExpansion.asset");
            cargoExp.upgradeName = "Expanded Cargo Hold";
            cargoExp.description = "+10 cargo units per level. Bring more home — if you can haul the weight.";
            cargoExp.maxLevel = 3;
            cargoExp.statModifiers = new[] { new StatModifierEntry { stat = SubStats.CargoCapacity, additivePerLevel = 10f } };
            EditorUtility.SetDirty(cargoExp);

            // -- Rename the display-name collision (both said "Reinforced Hull") --
            var lessCollision = AssetDatabase.LoadAssetAtPath<UpgradeDef>(UpgradeDir + "/LessCollisionDamage.asset");
            if (lessCollision != null && lessCollision.upgradeName == "Reinforced Hull")
            {
                lessCollision.upgradeName = "Dampened Frame";
                EditorUtility.SetDirty(lessCollision);
            }

            // -- Loadout slots --
            var hullSlot = LoadOrCreate<LoadoutSlotDef>(MetaDir + "/Slot_HullFeature.asset");
            hullSlot.slotName = "Hull Feature";
            hullSlot.description = "One structural feature per dive — the frame can only take so much modification.";
            hullSlot.maxPicks = 1;
            hullSlot.choices = new List<UpgradeDef>
            {
                ballastDef, doubleO2,
                AssetDatabase.LoadAssetAtPath<UpgradeDef>(UpgradeDir + "/PressureReinforcement.asset"),
                AssetDatabase.LoadAssetAtPath<UpgradeDef>(UpgradeDir + "/ImpactReinforcement.asset"),
            };
            EditorUtility.SetDirty(hullSlot);

            var computeSlot = LoadOrCreate<LoadoutSlotDef>(MetaDir + "/Slot_Computerized.asset");
            computeSlot.slotName = "Computerized System";
            computeSlot.description = "The onboard computer runs exactly one suite. (More suites arrive later.)";
            computeSlot.maxPicks = 1;
            computeSlot.choices = new List<UpgradeDef>
            {
                AssetDatabase.LoadAssetAtPath<UpgradeDef>(UpgradeDir + "/UnlockSonarPresence.asset"),
            };
            EditorUtility.SetDirty(computeSlot);

            // -- Catalog --
            var catalog = LoadOrCreate<UpgradeCatalog>(MetaDir + "/UpgradeCatalog.asset");
            catalog.loadoutSlots = new List<LoadoutSlotDef> { hullSlot, computeSlot };
            catalog.entries = BuildShopEntries(ballastDef, doubleO2, cargoExp);
            EditorUtility.SetDirty(catalog);

            // -- Ballast toggle marker on the sub prefab --
            AddBallastToggleMarker(ballastFeature);

            // -- Mission objective prefabs --
            BuildMissionPrefabs();

            // -- Ballast/cargo economy wiring (bubble venting, cargo dumping) --
            BuildCargoPickupPrefab();
            WireBallastAndCargoPrefabs();

            // -- Mission resource spawning (forecast → real world spawns) --
            BuildMissionResourceRule();

            AssetDatabase.SaveAssets();
            Debug.Log($"[MetaContentBuilder] Built meta content: catalog with {catalog.entries.Count} entries, {catalog.loadoutSlots.Count} slots.");
        }

        // -------------------------------------------------------
        // Shop stock
        // -------------------------------------------------------

        /**
         * The hub shop's stock with typed-resource prices. Costs follow each
         * upgrade's thematic domain (hull = Ferrite, machinery = Vent Brass,
         * O2/ballast = Clathrate, sensors = Luminite, deep-rated = Abyssite).
         */
        private static List<ShopEntry> BuildShopEntries(UpgradeDef ballast, UpgradeDef doubleO2, UpgradeDef cargoExp)
        {
            var ferrite = LoadResource("FerriteNodules");
            var brass = LoadResource("VentBrass");
            var clathrate = LoadResource("ClathrateIce");
            var luminite = LoadResource("Luminite");
            var abyssite = LoadResource("Abyssite");

            var entries = new List<ShopEntry>();
            void Stock(string defAsset, UpgradeDef direct, float growth, params (ResourceType type, int amount)[] costs)
            {
                var def = direct != null ? direct : AssetDatabase.LoadAssetAtPath<UpgradeDef>(UpgradeDir + "/" + defAsset + ".asset");
                if (def == null) { Debug.LogWarning($"[MetaContentBuilder] Missing upgrade def '{defAsset}' — skipped."); return; }

                var lines = new ResourceCost[costs.Length];
                for (int i = 0; i < costs.Length; i++)
                    lines[i] = new ResourceCost { type = costs[i].type, amount = costs[i].amount };
                entries.Add(new ShopEntry { def = def, costs = lines, costGrowth = growth });
            }

            // Hull & structure (Ferrite-led)
            Stock("ReinforcedHull", null, 1.6f, (ferrite, 10));
            Stock("PressureReinforcement", null, 1.6f, (ferrite, 12), (abyssite, 4));
            Stock("ImpactReinforcement", null, 1.6f, (ferrite, 10), (brass, 4));
            Stock("LessCollisionDamage", null, 1.5f, (ferrite, 8));
            Stock(null, cargoExp, 1.5f, (ferrite, 8), (brass, 4));

            // O2 & ballast (Clathrate-led)
            Stock("IncreaseO2Capacity", null, 1.5f, (clathrate, 6));
            Stock(null, doubleO2, 1.5f, (clathrate, 10), (brass, 6));
            Stock(null, ballast, 1.5f, (clathrate, 8), (ferrite, 6));

            // Machinery (Vent Brass-led)
            Stock("FasterLateral", null, 1.5f, (brass, 8));
            Stock("FasterCounterThrust", null, 1.5f, (brass, 8));
            Stock("IncreaseWeaponDamage", null, 1.5f, (brass, 6));

            // Sensor suite ladder (Luminite-led, deep tiers want Abyssite)
            Stock("UnlockSonarPresence", null, 1f, (luminite, 8));
            Stock("UpgradeSonarDirectionality", null, 1f, (luminite, 10));
            Stock("UpgradeSonarSizeReadout", null, 1f, (luminite, 12), (abyssite, 3));
            Stock("UpgradeSonarIdentification", null, 1f, (luminite, 14), (abyssite, 6));

            return entries;
        }

        // -------------------------------------------------------
        // Prefab wiring
        // -------------------------------------------------------

        /** Tags the sub prefab's BallastTank child with the unlock feature marker. */
        private static void AddBallastToggleMarker(UpgradeFeature feature)
        {
            const string subPath = "Assets/Submachina/Prefabs/Player/Submarine - juiced.prefab";
            var root = PrefabUtility.LoadPrefabContents(subPath);
            if (root == null) { Debug.LogWarning("[MetaContentBuilder] Sub prefab not found."); return; }

            var ballast = root.transform.Find("BallastTank");
            if (ballast != null)
            {
                var marker = ballast.GetComponent<UpgradeToggleTarget>() ?? ballast.gameObject.AddComponent<UpgradeToggleTarget>();
                var so = new SerializedObject(marker);
                var featureProp = so.FindProperty("feature");
                if (featureProp != null) { featureProp.objectReferenceValue = feature; so.ApplyModifiedPropertiesWithoutUndo(); }
                else Debug.LogWarning("[MetaContentBuilder] UpgradeToggleTarget has no 'feature' property — marker left unassigned.");
                // NOTE: authored state stays ACTIVE for now so test scenes keep ballast;
                // flip the child inactive once the hub loop is the main path.
                PrefabUtility.SaveAsPrefabAsset(root, subPath);
            }
            PrefabUtility.UnloadPrefabContents(root);
        }

        /**
         * Mission objective prefabs, derived from ScrapMetal's visuals so they
         * read as physical salvage: the retrieval cargo pod (gold) and the
         * research survey site (cyan).
         */
        private static void BuildMissionPrefabs()
        {
            BuildMissionPrefab("MissionCargoPod", new Color(1f, 0.82f, 0.3f), typeof(MissionCargo));
            BuildMissionPrefab("ResearchSite", new Color(0.45f, 0.9f, 1f), typeof(ResearchTarget));
        }

        private static void BuildMissionPrefab(string name, Color tint, System.Type objectiveType)
        {
            string path = MissionPrefabDir + "/" + name + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;   // keep manual tweaks

            const string sourcePath = "Assets/Submachina/Prefabs/World/ScrapMetal.prefab";
            if (!AssetDatabase.CopyAsset(sourcePath, path)) { Debug.LogWarning($"[MetaContentBuilder] Could not copy {sourcePath}."); return; }

            var root = PrefabUtility.LoadPrefabContents(path);

            // Strip pickup/mining behaviors — these are objective markers, not loot
            foreach (var mono in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mono == null) continue;
                string typeName = mono.GetType().Name;
                if (typeName == "ScrapPickup" || typeName == "MiningResource") Object.DestroyImmediate(mono, true);
            }

            // Objectives latch/parent to things — physics bodies would fight that
            var rb = root.GetComponent<Rigidbody2D>();
            if (rb != null) Object.DestroyImmediate(rb, true);

            // Tint so objectives read differently from ordinary scrap
            foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
                renderer.color = tint;

            // Objective behavior + a generous trigger for the interaction
            root.AddComponent(objectiveType);
            var collider = root.GetComponent<CircleCollider2D>() ?? root.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            if (collider.radius < 1f) collider.radius = 1f;

            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        /**
         * The jettisoned-cargo parcel: ScrapMetal visuals with a CargoPickup
         * component (tinted per resource at runtime by CargoHold when dumped).
         */
        private static void BuildCargoPickupPrefab()
        {
            const string path = "Assets/Submachina/Prefabs/World/CargoPickup.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;   // keep manual tweaks

            const string sourcePath = "Assets/Submachina/Prefabs/World/ScrapMetal.prefab";
            if (!AssetDatabase.CopyAsset(sourcePath, path)) { Debug.LogWarning("[MetaContentBuilder] Could not copy ScrapMetal for CargoPickup."); return; }

            var root = PrefabUtility.LoadPrefabContents(path);

            // Strip scrap behavior — this parcel is typed cargo, not heal-scrap
            foreach (var mono in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mono == null) continue;
                string typeName = mono.GetType().Name;
                if (typeName == "ScrapPickup" || typeName == "MiningResource") Object.DestroyImmediate(mono, true);
            }
            var rb = root.GetComponent<Rigidbody2D>();
            if (rb != null) Object.DestroyImmediate(rb, true);

            root.AddComponent<CargoPickup>();
            var collider = root.GetComponent<CircleCollider2D>() ?? root.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            if (collider.radius < 0.8f) collider.radius = 0.8f;

            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        /**
         * Wires the shared-O2 economy references into the subsystem prefabs:
         * BallastTank gets the O2Bubble prefab for overflow venting; CargoHold
         * gets the dump action + CargoPickup prefab for jettisoning.
         */
        private static void WireBallastAndCargoPrefabs()
        {
            // BallastTank.o2BubblePrefab → the world O2 bubble
            WirePrefabReference("Assets/Submachina/Prefabs/SubSystems/BallastTank.prefab", "o2BubblePrefab",
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Submachina/Prefabs/World/O2Bubble.prefab")?.GetComponent<O2Pickup>());

            // CargoHold.cargoPickupPrefab → the jettison parcel
            WirePrefabReference("Assets/Submachina/Prefabs/SubSystems/CargoHold.prefab", "cargoPickupPrefab",
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Submachina/Prefabs/World/CargoPickup.prefab")?.GetComponent<CargoPickup>());

            // CargoHold.dumpAction → the DumpCargo InputActionReference sub-asset
            Object dumpRef = null;
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath("Assets/Submachina/Input/PlayerControls.inputactions"))
                if (sub is UnityEngine.InputSystem.InputActionReference iar && iar.action != null && iar.action.name == "DumpCargo") { dumpRef = sub; break; }
            if (dumpRef != null)
                WirePrefabReference("Assets/Submachina/Prefabs/SubSystems/CargoHold.prefab", "dumpAction", dumpRef);
            else
                Debug.LogWarning("[MetaContentBuilder] DumpCargo action not found in PlayerControls — reimport the asset and re-run.");
        }

        /** Sets one serialized object-reference field on a prefab's root component set. */
        private static void WirePrefabReference(string prefabPath, string property, Object value)
        {
            if (value == null) { Debug.LogWarning($"[MetaContentBuilder] Null value for {prefabPath}.{property} — skipped."); return; }

            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null) { Debug.LogWarning($"[MetaContentBuilder] Missing prefab {prefabPath}."); return; }

            bool set = false;
            foreach (var mono in root.GetComponents<MonoBehaviour>())
            {
                var so = new SerializedObject(mono);
                var prop = so.FindProperty(property);
                if (prop == null) continue;
                prop.objectReferenceValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
                set = true;
                break;
            }

            if (set) PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            else Debug.LogWarning($"[MetaContentBuilder] Property '{property}' not found on {prefabPath}.");
            PrefabUtility.UnloadPrefabContents(root);
        }

        /**
         * The mission-aware resource rule: one template per ResourceType mapping
         * it to its world prefabs and a base density. Counts are tuned so a
         * DETECTED (≈1.0) forecast feels like today's resource frequency and
         * TRACE/RICH clearly bracket it.
         */
        private static void BuildMissionResourceRule()
        {
            var rule = LoadOrCreate<MissionResourceRule>(MetaDir + "/MissionResources.asset");
            rule.templates = new List<MissionResourceRule.ResourceTemplate>();

            void Template(string typeName, float countAtFull, params string[] prefabPaths)
            {
                var type = LoadResource(typeName);
                if (type == null) { Debug.LogWarning($"[MetaContentBuilder] ResourceType '{typeName}' missing — template skipped."); return; }

                var prefabs = new List<GameObject>();
                foreach (var path in prefabPaths)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null) prefabs.Add(prefab);
                    else Debug.LogWarning($"[MetaContentBuilder] Prefab missing for {typeName}: {path}");
                }
                if (prefabs.Count == 0) return;

                rule.templates.Add(new MissionResourceRule.ResourceTemplate
                {
                    type = type,
                    prefabVariants = prefabs.ToArray(),
                    countPerChunkAtFull = countAtFull,
                    minSpacing = 2f,
                });
            }

            const string res = "Assets/Submachina/Prefabs/World/Resources";
            const string nug = "Assets/Submachina/Prefabs/World/Cluster/OreNugget";
            Template("FerriteNodules", 3f, res + "/CopperResource.prefab", nug + "/OreMetalNugget.prefab");
            Template("VentBrass", 2.5f, res + "/Glitter Metal.prefab", nug + "/OreGoldNugget.prefab");
            Template("ClathrateIce", 2f, res + "/ClathrateIce.prefab");
            Template("Luminite", 2f, res + "/GreenCrystal.prefab");
            Template("Abyssite", 1.5f, res + "/RockPinkCrystals_0_albedo.prefab");

            EditorUtility.SetDirty(rule);
        }

        // -------------------------------------------------------
        // Helpers
        // -------------------------------------------------------

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static ResourceType LoadResource(string name) =>
            AssetDatabase.LoadAssetAtPath<ResourceType>(ResourceDir + "/" + name + ".asset");

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }
    }
}
