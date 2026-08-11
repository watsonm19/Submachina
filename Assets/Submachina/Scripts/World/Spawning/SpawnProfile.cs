using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * The full set of spawn rules a level uses, referenced by ChunkSpawner.
     *
     * Rules come from two places, both executed identically:
     *   - sharedRules: references to reusable SpawnRule assets (edit once,
     *     reuse across levels).
     *   - inlineRules: one-off rules authored directly on this profile.
     *
     * A global density multiplier scales every rule's count at once for quick
     * world-wide tuning. ChunkSpawner iterates AllRules per chunk.
     *
     * Create via: Assets → Create → Submachina → Spawning → Spawn Profile
     */
    [CreateAssetMenu(fileName = "SpawnProfile", menuName = "Submachina/Spawning/Spawn Profile")]
    public class SpawnProfile : ScriptableObject
    {
        // =====================
        // Global
        // =====================

        [FoldoutGroup("Global")]
        [ShowInInspector, ToggleLeft, PropertyOrder(-1)]
        [LabelText("Show Help Text")]
        [Tooltip("Toggles the verbose explanation boxes throughout the spawn inspectors. " +
                 "Hover tooltips and live previews stay regardless.")]
        private bool ShowHelpToggle
        {
            get => SpawnDocs.ShowHelp;
            set => SpawnDocs.ShowHelp = value;
        }

        [FoldoutGroup("Global")]
        [Tooltip("Scales every rule's count/probability at once. 1 = authored values, " +
                 "2 = twice as dense, 0.5 = half. Great for quickly tuning a whole level.")]
        [Range(0f, 5f)]
        [SerializeField]
        private float globalDensityMultiplier = 1f;

        [FoldoutGroup("Global")]
        [Title("Profile Notes")]
        [HideLabel]
        [Tooltip("Notes about this profile as a whole — what level/biome it's for, design intent, etc.")]
        [MultiLineProperty(3)]
        [SerializeField]
        private string profileNotes = "";

        // =====================
        // Rules
        // =====================

        [InfoBox("This profile has no rules — chunks will spawn empty. " +
                 "Add SpawnRule assets below, author inline rules, or click " +
                 "'Generate Default Rules'.", InfoMessageType.Warning, nameof(IsEmpty))]
        [InfoBox("One or more shared rule slots is empty (null) and will be skipped.",
            InfoMessageType.Warning, nameof(HasNullSharedRule))]
        [FoldoutGroup("Shared Rules (reusable assets)")]
        [Tooltip("References to reusable SpawnRule assets shared across levels.")]
        [ListDrawerSettings(ShowFoldout = true)]
        [SerializeField]
        private List<SpawnRule> sharedRules = new List<SpawnRule>();

        [FoldoutGroup("Inline Rules (this profile only)")]
        [Tooltip("Rules authored directly on this profile for one-off, level-specific spawns.")]
        [ListDrawerSettings(ShowFoldout = true)]
        [SerializeField]
        private List<SpawnRuleData> inlineRules = new List<SpawnRuleData>();

        // =====================
        // Public API
        // =====================

        /** Global density multiplier applied to every rule. */
        public float GlobalDensity => globalDensityMultiplier;

        /**
         * Every rule this profile contributes, shared then inline. Null entries
         * (empty asset slots) are skipped so callers can iterate safely.
         * Shared assets enumerate through SpawnRule.Rules so composite rules
         * (e.g. MissionResourceRule) can contribute several datas each.
         */
        public IEnumerable<SpawnRuleData> AllRules
        {
            get
            {
                foreach (SpawnRule shared in sharedRules)
                {
                    if (shared == null) continue;
                    foreach (SpawnRuleData data in shared.Rules)
                        if (data != null)
                            yield return data;
                }

                foreach (SpawnRuleData inline in inlineRules)
                    if (inline != null)
                        yield return inline;
            }
        }

        // =====================
        // Validation helpers
        // =====================

        private bool IsEmpty => (sharedRules == null || sharedRules.Count == 0)
                                && (inlineRules == null || inlineRules.Count == 0);

        private bool HasNullSharedRule
        {
            get
            {
                if (sharedRules == null) return false;
                foreach (SpawnRule r in sharedRules)
                    if (r == null)
                        return true;
                return false;
            }
        }

        // =====================
        // Editor Tools
        // =====================

#if UNITY_EDITOR
        [FoldoutGroup("Editor Tools")] [Tooltip("Depth (m) used by the Test Spawn button below.")] [SerializeField]
        private float testDepth = 150f;

        [FoldoutGroup("Editor Tools")]
        [Tooltip("ON: each Test Spawn uses a fresh random seed, so you see different layouts every click.\n" +
                 "OFF: uses the World Seed from the scene's ChunkSpawner, matching what the real world generates.")]
        [SerializeField]
        private bool testUseRandomSeed = true;

        /**
         * Spawns a throwaway chunk in the scene at testDepth so rules can be
         * tuned without entering Play mode. Uses representative cell geometry
         * (20m tall, 10-unit half-width). The seed is either random (to preview
         * variety) or the scene ChunkSpawner's World Seed (to match the real
         * world). Select it in the Hierarchy to inspect; remove with Clear Test Chunks.
         */
        [FoldoutGroup("Editor Tools")]
        [Button("Test Spawn Chunk", ButtonSizes.Medium), GUIColor(0.6f, 0.9f, 0.6f)]
        private void TestSpawnChunk()
        {
            const float height = 20f;
            const float halfWidth = 10f;
            float topY = -Mathf.Max(0f, testDepth);

            // Random seed for previewing variety, or the live world seed for true-to-game output
            int seed;
            string seedLabel;
            if (testUseRandomSeed)
            {
                seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                seedLabel = "random";
            }
            else
            {
                ChunkSpawner spawner = Object.FindFirstObjectByType<ChunkSpawner>();
                seed = spawner != null ? spawner.WorldSeed : 0;
                seedLabel = spawner != null ? $"world seed {seed}" : "world seed 0 (no ChunkSpawner in scene)";
            }

            GameObject go = new GameObject($"TEST_Chunk_d{testDepth:F0}");
            go.transform.position = new Vector3(0f, topY, 0f);
            WorldChunk chunk = go.AddComponent<WorldChunk>();
            chunk.Initialize(topY, height, halfWidth, 0f, testDepth, this, seed);

            Debug.Log(
                $"[SpawnProfile] '{name}' test chunk at {testDepth:F0}m ({seedLabel}) → {go.transform.childCount} objects.");
            UnityEditor.Selection.activeGameObject = go;
        }

        /** Destroys every TEST_Chunk_* object left in the active scene. */
        [FoldoutGroup("Editor Tools")]
        [Button("Clear Test Chunks"), GUIColor(0.95f, 0.7f, 0.6f)]
        private void ClearTestChunks()
        {
            int cleared = 0;
            foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (!root.name.StartsWith("TEST_Chunk")) continue;
                DestroyImmediate(root);
                cleared++;
            }

            Debug.Log($"[SpawnProfile] Cleared {cleared} test chunk(s).");
        }

        /**
         * Scaffolds the seven inline rules that reproduce the original
         * hardcoded WorldChunk behavior. Prefabs are left null — assign them
         * per each rule's developer notes after generating.
         */
        [FoldoutGroup("Editor Tools")]
        [Button("Generate Default Rules (legacy parity)"), GUIColor(0.6f, 0.8f, 1f)]
        private void GenerateDefaultRules()
        {
            inlineRules = SpawnProfileDefaults.BuildLegacyRules();
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log("[SpawnProfile] Generated 7 default rules. Assign each rule's prefab " +
                      "(see its Developer Notes).");
        }

        /**
         * Promotes every inline rule into its own reusable SpawnRule asset
         * (saved to a "Rules" folder next to this profile), moves them into the
         * shared list, and clears the inline list. Lets you author quickly
         * inline, then graduate rules to shareable assets when they stabilize.
         */
        [FoldoutGroup("Editor Tools")]
        [Button("Export Inline Rules → Shared Assets"), GUIColor(0.8f, 0.7f, 1f)]
        private void ExportInlineRulesToSharedAssets()
        {
            if (inlineRules == null || inlineRules.Count == 0)
            {
                Debug.Log("[SpawnProfile] No inline rules to export.");
                return;
            }

            // Put the rule assets in a "Rules" subfolder beside this profile
            string profilePath = UnityEditor.AssetDatabase.GetAssetPath(this);
            string dir = System.IO.Path.GetDirectoryName(profilePath).Replace("\\", "/");
            string rulesDir = dir + "/Rules";
            if (!UnityEditor.AssetDatabase.IsValidFolder(rulesDir))
                UnityEditor.AssetDatabase.CreateFolder(dir, "Rules");

            // Reflect the SpawnRule's private rule field so we can inject each inline rule
            var ruleField = typeof(SpawnRule).GetField("rule",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            int exported = 0;
            foreach (SpawnRuleData data in inlineRules)
            {
                if (data == null) continue;

                // Wrap the inline data in a new SpawnRule asset with a unique, readable name
                SpawnRule asset = ScriptableObject.CreateInstance<SpawnRule>();
                ruleField.SetValue(asset, data);
                string safeName = string.IsNullOrEmpty(data.ruleName) ? "SpawnRule" : data.ruleName;
                string path = UnityEditor.AssetDatabase.GenerateUniqueAssetPath(rulesDir + "/" + safeName + ".asset");
                UnityEditor.AssetDatabase.CreateAsset(asset, path);

                sharedRules.Add(asset);
                exported++;
            }

            // Inline rules have been moved into shared assets — clear them to avoid duplicates
            inlineRules.Clear();
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssets();
            Debug.Log($"[SpawnProfile] Exported {exported} inline rule(s) to shared assets in '{rulesDir}'.");
        }
#endif
    }
}