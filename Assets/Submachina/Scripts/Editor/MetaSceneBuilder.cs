using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Submachina.Core;
using Submachina.Meta;

namespace Submachina.EditorTools
{
    /**
     * Builds the hub/mission scene pair for the core game loop and registers
     * them in Build Settings. Idempotent: existing scenes are updated, not
     * recreated (Mission_Descent is only copied from Ore testing on first run).
     *
     * Menu: Tools/Submachina/Build Meta Scenes
     */
    public static class MetaSceneBuilder
    {
        private const string MissionScenePath = "Assets/Scenes/Mission_Descent.unity";
        private const string HubScenePath = "Assets/Scenes/Hub.unity";
        private const string SourceScenePath = "Assets/Scenes/Ore testing.unity";
        private const string CatalogPath = "Assets/Submachina/Data/Meta/UpgradeCatalog.asset";

        [MenuItem("Tools/Submachina/Build Meta Scenes")]
        public static void Build()
        {
            // No modal prompts — this runs from automation. Refuse to stomp unsaved work instead.
            if (EditorSceneManager.GetActiveScene().isDirty)
            {
                Debug.LogWarning("[MetaSceneBuilder] Active scene has unsaved changes — save it first, then re-run.");
                return;
            }

            BuildMissionScene();
            BuildHubScene();
            RegisterBuildScenes();
            Debug.Log("[MetaSceneBuilder] Scenes built and registered in Build Settings.");
        }

        // -------------------------------------------------------
        // Mission scene
        // -------------------------------------------------------

        /**
         * Mission_Descent = the Ore testing sandbox (cleanest full gameplay
         * scene) plus the mission rig: MissionController with objective prefabs,
         * and a LoadoutApplier on each submarine.
         */
        private static void BuildMissionScene()
        {
            // First run: clone the sandbox as our mission playground
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MissionScenePath) == null &&
                !AssetDatabase.CopyAsset(SourceScenePath, MissionScenePath))
            {
                Debug.LogError("[MetaSceneBuilder] Could not copy Ore testing → Mission_Descent.");
                return;
            }

            var scene = EditorSceneManager.OpenScene(MissionScenePath, OpenSceneMode.Single);
            var catalog = AssetDatabase.LoadAssetAtPath<UpgradeCatalog>(CatalogPath);

            // Mission controller with objective prefabs wired
            var controllerGO = GameObject.Find("MissionController") ?? new GameObject("MissionController");
            var controller = controllerGO.GetComponent<MissionController>() ?? controllerGO.AddComponent<MissionController>();
            var so = new SerializedObject(controller);
            SetRef(so, "cargoPodPrefab", "Assets/Submachina/Prefabs/Missions/MissionCargoPod.prefab", typeof(MissionCargo));
            SetRef(so, "researchTargetPrefab", "Assets/Submachina/Prefabs/Missions/ResearchSite.prefab", typeof(ResearchTarget));
            SetRef(so, "hostilePrefab", "Assets/Submachina/Prefabs/World/Enemy/RammerEnemy.prefab", typeof(GameObject));
            so.ApplyModifiedPropertiesWithoutUndo();

            // Every sub gets the profile applier
            foreach (var sub in Object.FindObjectsByType<Submarine>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var applier = sub.GetComponent<LoadoutApplier>() ?? sub.gameObject.AddComponent<LoadoutApplier>();
                var applierSO = new SerializedObject(applier);
                applierSO.FindProperty("catalog").objectReferenceValue = catalog;
                applierSO.ApplyModifiedPropertiesWithoutUndo();
            }

            // Mission spawn profile: forecast-driven resources instead of fixed rules
            AssignMissionSpawnProfile();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        /**
         * Points the mission scene's ChunkSpawner at a dedicated MissionProfile:
         * a copy of the sandbox profile with the fixed resource rules (generic
         * Resource / OreCluster) swapped for the mission-aware MissionResources
         * rule, so the level contains exactly what the scanner forecast promises.
         * The sandbox scene keeps its original profile untouched. Idempotent —
         * an existing MissionProfile is refreshed in place (rule swap re-checked).
         */
        private static void AssignMissionSpawnProfile()
        {
            const string missionProfilePath = "Assets/Submachina/Data/Meta/MissionProfile.asset";
            const string missionRulePath = "Assets/Submachina/Data/Meta/MissionResources.asset";

            var spawner = Object.FindFirstObjectByType<ChunkSpawner>(FindObjectsInactive.Include);
            if (spawner == null) { Debug.LogWarning("[MetaSceneBuilder] No ChunkSpawner in mission scene — profile not assigned."); return; }

            var missionRule = AssetDatabase.LoadAssetAtPath<MissionResourceRule>(missionRulePath);
            if (missionRule == null) { Debug.LogWarning("[MetaSceneBuilder] MissionResources rule missing — run Build Meta Content first."); return; }

            var spawnerSO = new SerializedObject(spawner);
            var profileProp = spawnerSO.FindProperty("spawnProfile");
            var current = profileProp.objectReferenceValue as SpawnProfile;

            // Create the mission profile from the scene's current profile on first run
            var missionProfile = AssetDatabase.LoadAssetAtPath<SpawnProfile>(missionProfilePath);
            if (missionProfile == null)
            {
                if (current == null) { Debug.LogWarning("[MetaSceneBuilder] ChunkSpawner has no profile to derive from."); return; }
                missionProfile = Object.Instantiate(current);
                AssetDatabase.CreateAsset(missionProfile, missionProfilePath);
            }

            // Swap resource rules for the mission rule inside the shared list
            var profileSO = new SerializedObject(missionProfile);
            var shared = profileSO.FindProperty("sharedRules");
            bool hasMissionRule = false;
            for (int i = shared.arraySize - 1; i >= 0; i--)
            {
                var element = shared.GetArrayElementAtIndex(i).objectReferenceValue;
                if (element == missionRule) { hasMissionRule = true; continue; }
                if (element != null && (element.name == "Resource" || element.name == "OreCluster"))
                    shared.DeleteArrayElementAtIndex(i);
            }
            if (!hasMissionRule)
            {
                shared.InsertArrayElementAtIndex(shared.arraySize);
                shared.GetArrayElementAtIndex(shared.arraySize - 1).objectReferenceValue = missionRule;
            }
            profileSO.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(missionProfile);

            // Point the spawner at the mission profile
            profileProp.objectReferenceValue = missionProfile;
            spawnerSO.ApplyModifiedPropertiesWithoutUndo();
        }

        /** Loads a prefab (optionally as a specific component) into a serialized slot. */
        private static void SetRef(SerializedObject so, string property, string assetPath, System.Type type)
        {
            var prop = so.FindProperty(property);
            if (prop == null) { Debug.LogWarning($"[MetaSceneBuilder] Property '{property}' not found."); return; }

            Object asset = type == typeof(GameObject)
                ? AssetDatabase.LoadAssetAtPath<GameObject>(assetPath)
                : (AssetDatabase.LoadAssetAtPath<GameObject>(assetPath)?.GetComponent(type));
            if (asset == null) { Debug.LogWarning($"[MetaSceneBuilder] Asset missing for '{property}': {assetPath}"); return; }
            prop.objectReferenceValue = asset;
        }

        // -------------------------------------------------------
        // Hub scene
        // -------------------------------------------------------

        /**
         * The hub is a pure-UI scene: camera, event system, and the runtime-built
         * HubScreenController (resolved by name so this builder compiles even
         * before that script lands).
         */
        private static void BuildHubScene()
        {
            var hubType = System.Type.GetType("Submachina.Meta.HubScreenController, Assembly-CSharp");
            if (hubType == null) { Debug.LogWarning("[MetaSceneBuilder] HubScreenController not compiled yet — hub scene skipped."); return; }

            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(HubScenePath) != null
                ? EditorSceneManager.OpenScene(HubScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Camera — reuse ANY existing scene camera (whatever its name) so
            // re-runs never create a duplicate; dark harbor-water backdrop
            var cam = Object.FindFirstObjectByType<Camera>();
            var camGO = cam != null ? cam.gameObject : new GameObject("HubCamera");
            if (cam == null) cam = camGO.AddComponent<Camera>();
            camGO.tag = "MainCamera";
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.07f, 0.12f);

            // Event system for uGUI clicks (Input System UI module)
            var esGO = GameObject.Find("EventSystem") ?? new GameObject("EventSystem");
            if (esGO.GetComponent<UnityEngine.EventSystems.EventSystem>() == null)
                esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
            if (esGO.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() == null)
                esGO.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

            // The hub controller, wired to the catalog + resource types
            var hubGO = GameObject.Find("Hub") ?? new GameObject("Hub");
            var hub = hubGO.GetComponent(hubType) ?? hubGO.AddComponent(hubType);
            var so = new SerializedObject(hub);

            var catalogProp = so.FindProperty("catalog");
            if (catalogProp != null) catalogProp.objectReferenceValue = AssetDatabase.LoadAssetAtPath<UpgradeCatalog>(CatalogPath);

            var typesProp = so.FindProperty("resourceTypes");
            if (typesProp != null)
            {
                var typeNames = new[] { "FerriteNodules", "VentBrass", "ClathrateIce", "Luminite", "Abyssite" };
                typesProp.arraySize = typeNames.Length;
                for (int i = 0; i < typeNames.Length; i++)
                    typesProp.GetArrayElementAtIndex(i).objectReferenceValue =
                        AssetDatabase.LoadAssetAtPath<ResourceType>($"Assets/Submachina/Data/Resources/{typeNames[i]}.asset");
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, HubScenePath);
        }

        // -------------------------------------------------------
        // Build settings
        // -------------------------------------------------------

        /** Hub first (startup scene), then the mission scene; replaces the stale SampleScene entry. */
        private static void RegisterBuildScenes()
        {
            var scenes = new List<EditorBuildSettingsScene>();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(HubScenePath) != null)
                scenes.Add(new EditorBuildSettingsScene(HubScenePath, true));
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MissionScenePath) != null)
                scenes.Add(new EditorBuildSettingsScene(MissionScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
