using System.IO;
using Core.ProceduralAnimation;
using UnityEditor;
using UnityEngine;

namespace Submachina.Core.EditorTools
{
    /**
     * Builds the sample procedural creatures from code — materials, prefabs,
     * ink VFX texture, and spawn rule assets — so the whole system is one menu
     * click away from playable, with no hand-assembly.
     *
     * Idempotent like MetaSceneBuilder: prefabs are rebuilt in place (same path,
     * same GUID, references survive), materials/rules are created once and then
     * updated. Automation-safe: no modal dialogs.
     *
     * Menu:
     *   Tools/Submachina/Creatures/Build Sample Creatures        — everything below
     *   Tools/Submachina/Creatures/Add Creature Rules To Default Profile — opt-in balance change
     *   Tools/Submachina/Creatures/Spawn Samples In Scene        — drop one of each into the open scene
     */
    public static class CreatureBuilder
    {
        private const string ArtFolder = "Assets/Submachina/Art/Creatures";
        private const string PrefabFolder = "Assets/Submachina/Prefabs/World/Enemy/Procedural";
        private const string RuleFolder = "Assets/Submachina/Data/SpawnProfiles/SpawnRules";
        private const string ShaderName = "Submachina/2D/ProcCreature";
        private const string O2BubblePath = "Assets/Submachina/Prefabs/World/O2Bubble.prefab";
        private const string DefaultProfilePath = "Assets/Submachina/Data/SpawnProfiles/DefaultSpawnProfile.asset";

        // =====================================================================
        // Menu entry points
        // =====================================================================

        [MenuItem("Tools/Submachina/Creatures/Build Sample Creatures")]
        public static void BuildAll()
        {
            EnsureFolders();

            // Materials first — prefabs reference them.
            Material matEel = BuildCreatureMaterial("Mat_Eel",
                fill: new Color(0.16f, 0.42f, 0.36f, 1f), outline: new Color(0.03f, 0.10f, 0.09f, 1f),
                flash: new Color(1f, 0.85f, 0.6f, 1f), emission: Color.black, rimEmission: 0f);
            Material matJelly = BuildCreatureMaterial("Mat_Jelly",
                fill: new Color(0.95f, 0.45f, 0.75f, 0.42f), outline: new Color(1f, 0.6f, 0.9f, 0.55f),
                flash: Color.white, emission: Color.black, rimEmission: 2.2f);
            Material matSquid = BuildCreatureMaterial("Mat_Squid",
                fill: new Color(0.62f, 0.19f, 0.16f, 1f), outline: new Color(0.12f, 0.03f, 0.05f, 1f),
                flash: new Color(0.1f, 0.06f, 0.12f, 1f), emission: Color.black, rimEmission: 0f);
            Material matFish = BuildCreatureMaterial("Mat_Fish",
                fill: new Color(0.62f, 0.72f, 0.82f, 1f), outline: new Color(0.10f, 0.16f, 0.24f, 1f),
                flash: Color.white, emission: Color.black, rimEmission: 0f);

            GameObject o2Bubble = AssetDatabase.LoadAssetAtPath<GameObject>(O2BubblePath);
            if (o2Bubble == null) Debug.LogWarning($"[CreatureBuilder] O2 bubble prefab not found at {O2BubblePath} — death drops left empty.");

            // Prefabs (each rebuilt in place).
            GameObject eel = BuildEelPrefab(matEel, o2Bubble);
            GameObject jelly = BuildJellyfishPrefab(matJelly, o2Bubble);
            GameObject squid = BuildSquidPrefab(matSquid, o2Bubble);
            GameObject fish = BuildSchoolFishPrefab(matFish);
            GameObject school = BuildFishSchoolPrefab(fish);

            // Spawn rules (created/updated, NOT auto-added to any profile — balance stays a human call).
            BuildSpawnRule("CreatureEel", eel, minDepth: 60f,
                notes: "Procedural eel — telegraphed lunge attack, vulnerable in recovery. Deeper water hunter.",
                configure: so =>
                {
                    SetEnum(so, "rule.count.kind", (int)CountKind.SingleRoll);
                    SetFloat(so, "rule.count.spawnChance", 0.35f);
                });
            BuildSpawnRule("CreatureJellyfish", jelly, minDepth: 30f,
                notes: "Procedural jellyfish — slow pulse drifter, sting on contact. Density ramps with depth.",
                configure: so =>
                {
                    SetEnum(so, "rule.count.kind", (int)CountKind.CurveRange);
                    SetFloat(so, "rule.count.refMinDepth", 30f);
                    SetFloat(so, "rule.count.refMaxDepth", 300f);
                    SetFloat(so, "rule.count.countAtMinDepth", 0.5f);
                    SetFloat(so, "rule.count.countAtMaxDepth", 2f);
                    SetFloat(so, "rule.minSpacing", 4f);
                });
            BuildSpawnRule("CreatureSquid", squid, minDepth: 150f,
                notes: "Procedural squid — jet ambusher with ink escape. Rare, deep, flashy.",
                configure: so =>
                {
                    SetEnum(so, "rule.count.kind", (int)CountKind.SingleRoll);
                    SetFloat(so, "rule.count.spawnChance", 0.25f);
                });
            BuildSpawnRule("CreatureFishSchool", school, minDepth: 5f,
                notes: "Ambient boid fish school — pure atmosphere, scatters from the sub. Shallow-biased.",
                configure: so =>
                {
                    SetEnum(so, "rule.count.kind", (int)CountKind.SingleRoll);
                    SetFloat(so, "rule.count.spawnChance", 0.4f);
                    SetBool(so, "rule.depth.hasMax", true);
                    SetFloat(so, "rule.depth.maxDepth", 260f);
                    SetFloat(so, "rule.minSpacing", 12f);
                });

            AssetDatabase.SaveAssets();
            Debug.Log("[CreatureBuilder] Sample creatures built: materials, prefabs, and spawn rules are up to date. " +
                      "Use 'Add Creature Rules To Default Profile' to enable them in normal play.");
        }

        [MenuItem("Tools/Submachina/Creatures/Add Creature Rules To Default Profile")]
        public static void AddRulesToDefaultProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<SpawnProfile>(DefaultProfilePath);
            if (profile == null) { Debug.LogError($"[CreatureBuilder] Profile not found at {DefaultProfilePath}"); return; }

            var so = new SerializedObject(profile);
            var list = so.FindProperty("sharedRules");
            if (list == null) { Debug.LogError("[CreatureBuilder] SpawnProfile.sharedRules not found — field renamed?"); return; }

            int added = 0;
            foreach (string name in new[] { "CreatureEel", "CreatureJellyfish", "CreatureSquid", "CreatureFishSchool" })
            {
                var rule = AssetDatabase.LoadAssetAtPath<SpawnRule>($"{RuleFolder}/{name}.asset");
                if (rule == null) { Debug.LogWarning($"[CreatureBuilder] Rule {name} missing — run Build Sample Creatures first."); continue; }

                // Skip if already referenced.
                bool present = false;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == rule) { present = true; break; }
                if (present) continue;

                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = rule;
                added++;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log($"[CreatureBuilder] Added {added} creature rule(s) to {DefaultProfilePath}.");
        }

        [MenuItem("Tools/Submachina/Creatures/Spawn Samples In Scene")]
        public static void SpawnSamplesInScene()
        {
            Vector3 center = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            center.z = 0f;

            string[] names = { "EelCreature", "JellyfishCreature", "SquidCreature", "FishSchool" };
            for (int i = 0; i < names.Length; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{names[i]}.prefab");
                if (prefab == null) { Debug.LogWarning($"[CreatureBuilder] {names[i]} prefab missing — run Build Sample Creatures first."); continue; }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.transform.position = center + new Vector3((i - 1.5f) * 7f, 0f, 0f);
                Undo.RegisterCreatedObjectUndo(instance, "Spawn Sample Creatures");
            }
        }

        // =====================================================================
        // Materials
        // =====================================================================

        /** Creates or updates one ProcCreature material with the given palette. */
        private static Material BuildCreatureMaterial(string name, Color fill, Color outline, Color flash, Color emission, float rimEmission)
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null) { Debug.LogError($"[CreatureBuilder] Shader '{ShaderName}' not found — did ProcCreature2D.shader compile?"); return null; }

            string path = $"{ArtFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.shader = shader;
            mat.SetColor("_Color", fill);
            mat.SetColor("_OutlineColor", outline);
            mat.SetFloat("_OutlineWidth", 0.05f);
            mat.SetColor("_FlashColor", flash);
            mat.SetColor("_EmissionColor", emission);
            mat.SetFloat("_RimEmission", rimEmission);
            mat.SetFloat("_RimWidth", 0.18f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // =====================================================================
        // Prefab: Eel
        // =====================================================================

        private static GameObject BuildEelPrefab(Material mat, GameObject o2Bubble)
        {
            GameObject root = NewCreatureRoot("EelCreature", out Rigidbody2D rb, out Health health, out HitReceiver hit);
            rb.linearDamping = 1f;
            SetInt(health, "maxHP", 5);

            var col = root.AddComponent<CapsuleCollider2D>();
            col.direction = CapsuleDirection2D.Horizontal;
            col.size = new Vector2(1.1f, 0.5f);

            // Body child: spine simulator + strip renderer anchored to the root.
            GameObject body = NewChild(root, "Body");
            var sim = body.AddComponent<ChainSimulator>();
            var strip = body.AddComponent<ChainStripRenderer>();
            Configure(sim, so =>
            {
                SetInt(so, "pointCount", 14);
                SetFloat(so, "segmentLength", 0.28f);
                SetFloat(so, "maxBendDegrees", 26f);
                SetFloat(so, "straightenSpeed", 4f);
                SetRef(so, "anchor", root.transform);
                SetFloat(so, "idleWaveAmplitude", 0.03f);
                SetFloat(so, "waveAmplitudePerSpeed", 0.03f);
                SetFloat(so, "maxWaveAmplitude", 0.3f);
                SetFloat(so, "waveFrequency", 2.2f);
                SetFloat(so, "waveLength", 2.4f);
                SetFloat(so, "swayAmplitude", 0.04f);
                SetFloat(so, "swayFrequency", 0.3f);
            });
            Configure(strip, so =>
            {
                SetRef(so, "chain", sim);
                SetFloat(so, "maxWidth", 0.55f);
                SetCurve(so, "widthProfile", new AnimationCurve(
                    new Keyframe(0f, 0.55f), new Keyframe(0.15f, 1f), new Keyframe(0.55f, 0.75f), new Keyframe(1f, 0.05f)));
                SetInt(so, "capSegments", 5);
            });
            strip.GetComponent<MeshRenderer>().sharedMaterial = mat;

            var brain = root.AddComponent<EelEnemy>();
            Configure(brain, so =>
            {
                SetRef(so, "body", sim);
                SetRef(so, "bodyRenderer", strip);
            });
            WireDeathDrops(brain, o2Bubble, count: 2);
            AddCulling(root, brain, sim, strip);

            return SavePrefab(root);
        }

        // =====================================================================
        // Prefab: Jellyfish
        // =====================================================================

        private static GameObject BuildJellyfishPrefab(Material mat, GameObject o2Bubble)
        {
            GameObject root = NewCreatureRoot("JellyfishCreature", out Rigidbody2D rb, out Health health, out HitReceiver hit);
            rb.linearDamping = 1.4f;
            SetInt(health, "maxHP", 4);

            var col = root.AddComponent<CircleCollider2D>();
            col.radius = 0.55f;

            // Bell — dome-shaped radial blob (wide top, tucked-in underside).
            GameObject bellGo = NewChild(root, "Bell");
            var bell = bellGo.AddComponent<RadialMeshRenderer>();
            Configure(bell, so =>
            {
                SetInt(so, "ringSegments", 28);
                SetFloat(so, "baseRadius", 0.62f);
                SetCurve(so, "radiusProfile", new AnimationCurve(
                    new Keyframe(0f, 0.9f), new Keyframe(0.25f, 1f), new Keyframe(0.5f, 0.9f),
                    new Keyframe(0.75f, 0.55f), new Keyframe(1f, 0.9f)));
                SetColor(so, "centerColor", new Color(1f, 1f, 1f, 0.7f));
                SetColor(so, "rimColor", Color.white);
                SetInt(so, "sortingOrder", 1);
            });
            bell.GetComponent<MeshRenderer>().sharedMaterial = mat;

            // Trailing tentacles hung from the bell's underside.
            var tentacles = new ChainSimulator[4];
            for (int i = 0; i < tentacles.Length; i++)
            {
                GameObject t = NewChild(root, $"Tentacle_{i}");
                float x = Mathf.Lerp(-0.32f, 0.32f, tentacles.Length > 1 ? i / (float)(tentacles.Length - 1) : 0.5f);
                var sim = t.AddComponent<ChainSimulator>();
                var strip = t.AddComponent<ChainStripRenderer>();
                Configure(sim, so =>
                {
                    SetInt(so, "pointCount", 9);
                    SetFloat(so, "segmentLength", 0.22f);
                    SetFloat(so, "maxBendDegrees", 60f);
                    SetFloat(so, "straightenSpeed", 1.2f);
                    SetRef(so, "anchor", root.transform);
                    SetVec2(so, "anchorOffset", new Vector2(x, -0.3f));
                    SetEnum(so, "facing", (int)ChainSimulator.FacingMode.None);
                    SetFloat(so, "idleWaveAmplitude", 0.01f);
                    SetFloat(so, "waveAmplitudePerSpeed", 0.02f);
                    SetFloat(so, "waveFrequency", 1.2f);
                    SetFloat(so, "swayAmplitude", 0.06f);
                    SetFloat(so, "swayFrequency", 0.35f);
                    SetVec2(so, "constantForce", new Vector2(0f, -0.5f));
                });
                Configure(strip, so =>
                {
                    SetRef(so, "chain", sim);
                    SetFloat(so, "maxWidth", 0.11f);
                    SetCurve(so, "widthProfile", AnimationCurve.Linear(0f, 1f, 1f, 0.15f));
                    SetInt(so, "capSegments", 3);
                    SetInt(so, "sortingOrder", 0);
                });
                strip.GetComponent<MeshRenderer>().sharedMaterial = mat;
                tentacles[i] = sim;
            }

            var brain = root.AddComponent<JellyfishEnemy>();
            Configure(brain, so =>
            {
                SetRef(so, "bell", bell);
                SetRefArray(so, "tentacles", tentacles);
            });
            WireDeathDrops(brain, o2Bubble, count: 2);

            var suspend = new Behaviour[tentacles.Length + 2];
            suspend[0] = brain; suspend[1] = bell;
            for (int i = 0; i < tentacles.Length; i++) suspend[i + 2] = tentacles[i];
            AddCulling(root, suspend);

            return SavePrefab(root);
        }

        // =====================================================================
        // Prefab: Squid
        // =====================================================================

        private static GameObject BuildSquidPrefab(Material mat, GameObject o2Bubble)
        {
            GameObject root = NewCreatureRoot("SquidCreature", out Rigidbody2D rb, out Health health, out HitReceiver hit);
            rb.linearDamping = 0.9f;
            SetInt(health, "maxHP", 6);

            var col = root.AddComponent<CircleCollider2D>();
            col.radius = 0.5f;

            // Visual child rotates toward travel; the physics root never rotates.
            GameObject visual = NewChild(root, "Visual");

            // Mantle — teardrop pointing +X (the squid's "nose"; travel is -X of this).
            GameObject mantleGo = NewChild(visual, "Mantle");
            var mantle = mantleGo.AddComponent<RadialMeshRenderer>();
            Configure(mantle, so =>
            {
                SetInt(so, "ringSegments", 24);
                SetFloat(so, "baseRadius", 0.55f);
                SetCurve(so, "radiusProfile", new AnimationCurve(
                    new Keyframe(0f, 1.5f), new Keyframe(0.2f, 0.85f), new Keyframe(0.5f, 0.7f),
                    new Keyframe(0.8f, 0.85f), new Keyframe(1f, 1.5f)));
                SetInt(so, "sortingOrder", 1);
            });
            mantle.GetComponent<MeshRenderer>().sharedMaterial = mat;

            // Tentacles cluster at the mantle's open (-X) end and trail behind.
            var tentacles = new ChainSimulator[6];
            for (int i = 0; i < tentacles.Length; i++)
            {
                GameObject t = NewChild(visual, $"Tentacle_{i}");
                float y = Mathf.Lerp(-0.22f, 0.22f, tentacles.Length > 1 ? i / (float)(tentacles.Length - 1) : 0.5f);
                var sim = t.AddComponent<ChainSimulator>();
                var strip = t.AddComponent<ChainStripRenderer>();
                Configure(sim, so =>
                {
                    SetInt(so, "pointCount", 8);
                    SetFloat(so, "segmentLength", 0.17f);
                    SetFloat(so, "maxBendDegrees", 70f);
                    SetFloat(so, "straightenSpeed", 2f);
                    SetRef(so, "anchor", visual.transform);
                    SetVec2(so, "anchorOffset", new Vector2(-0.42f, y));
                    SetEnum(so, "facing", (int)ChainSimulator.FacingMode.None);
                    SetFloat(so, "idleWaveAmplitude", 0.015f);
                    SetFloat(so, "waveAmplitudePerSpeed", 0.03f);
                    SetFloat(so, "waveFrequency", 1.6f);
                    SetFloat(so, "swayAmplitude", 0.05f);
                    SetFloat(so, "swayFrequency", 0.5f);
                });
                Configure(strip, so =>
                {
                    SetRef(so, "chain", sim);
                    SetFloat(so, "maxWidth", 0.09f);
                    SetCurve(so, "widthProfile", AnimationCurve.Linear(0f, 1f, 1f, 0.12f));
                    SetInt(so, "capSegments", 3);
                    SetInt(so, "sortingOrder", 0);
                });
                strip.GetComponent<MeshRenderer>().sharedMaterial = mat;
                tentacles[i] = sim;
            }

            // Ink burst rig.
            ParticleSystem ink = BuildInkParticles(root);

            var brain = root.AddComponent<SquidEnemy>();
            Configure(brain, so =>
            {
                SetRef(so, "visualRoot", visual.transform);
                SetRef(so, "mantle", mantle);
                SetRefArray(so, "tentacles", tentacles);
                SetRef(so, "inkParticles", ink);
            });
            WireDeathDrops(brain, o2Bubble, count: 3);

            var suspend = new Behaviour[tentacles.Length + 2];
            suspend[0] = brain; suspend[1] = mantle;
            for (int i = 0; i < tentacles.Length; i++) suspend[i + 2] = tentacles[i];
            AddCulling(root, suspend);

            return SavePrefab(root);
        }

        /** Dark billowing ink cloud — soft generated blob sprite, world-space, burst-driven. */
        private static ParticleSystem BuildInkParticles(GameObject root)
        {
            GameObject go = NewChild(root, "InkBurst");
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.5f);
            main.startColor = new Color(0.05f, 0.04f, 0.1f, 0.85f);
            main.maxParticles = 64;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 22) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.3f;

            // Ink billows out then dissolves.
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));

            var colOl = ps.colorOverLifetime;
            colOl.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            colOl.color = new ParticleSystem.MinMaxGradient(grad);

            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.drag = 1.5f;

            // Soft blob sprite on a 2D-renderer-friendly unlit material.
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = BuildInkMaterial();
            renderer.sortingLayerName = "Default";
            renderer.sortingOrder = 5;

            return ps;
        }

        /** Generates the soft radial ink blob texture + material (idempotent). */
        private static Material BuildInkMaterial()
        {
            string texPath = $"{ArtFolder}/InkBlob.png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(texPath) == null)
            {
                // Radial falloff blob: solid core, feathered edge, slight noise for texture.
                const int size = 64;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - size / 2f) / (size / 2f);
                    float dy = (y - size / 2f) / (size / 2f);
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float noise = Mathf.PerlinNoise(x * 0.15f, y * 0.15f) * 0.25f;
                    float a = Mathf.Clamp01(1f - r - noise);
                    a = a * a * (3f - 2f * a); // smoothstep feather
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
                tex.Apply();
                File.WriteAllBytes(texPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(texPath);

                if (AssetImporter.GetAtPath(texPath) is TextureImporter importer)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.SaveAndReimport();
                }
            }

            string matPath = $"{ArtFolder}/Mat_Ink.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // =====================================================================
        // Prefabs: Fish school
        // =====================================================================

        /** The individual fish — a tiny chain strip the school controller instantiates and drives. */
        private static GameObject BuildSchoolFishPrefab(Material mat)
        {
            var root = new GameObject("SchoolFish");
            var sim = root.AddComponent<ChainSimulator>();
            var strip = root.AddComponent<ChainStripRenderer>();
            Configure(sim, so =>
            {
                SetInt(so, "pointCount", 5);
                SetFloat(so, "segmentLength", 0.09f);
                SetFloat(so, "maxBendDegrees", 40f);
                SetFloat(so, "straightenSpeed", 7f);
                SetFloat(so, "idleWaveAmplitude", 0.008f);
                SetFloat(so, "waveAmplitudePerSpeed", 0.02f);
                SetFloat(so, "maxWaveAmplitude", 0.05f);
                SetFloat(so, "waveFrequency", 6f);
                SetFloat(so, "waveLength", 0.5f);
                SetFloat(so, "swayAmplitude", 0.01f);
            });
            Configure(strip, so =>
            {
                SetRef(so, "chain", sim);
                SetFloat(so, "maxWidth", 0.13f);
                SetCurve(so, "widthProfile", new AnimationCurve(
                    new Keyframe(0f, 0.4f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.08f)));
                SetInt(so, "capSegments", 3);
            });
            strip.GetComponent<MeshRenderer>().sharedMaterial = mat;

            return SavePrefab(root);
        }

        private static GameObject BuildFishSchoolPrefab(GameObject fishPrefab)
        {
            var root = new GameObject("FishSchool");
            var school = root.AddComponent<FishSchoolController>();
            Configure(school, so => SetRef(so, "fishPrefab", fishPrefab));
            AddCulling(root, school);
            return SavePrefab(root);
        }

        // =====================================================================
        // Shared prefab plumbing
        // =====================================================================

        /** Root skeleton every combat creature shares: Enemy layer, Rigidbody2D, Health, HitReceiver. */
        private static GameObject NewCreatureRoot(string name, out Rigidbody2D rb, out Health health, out HitReceiver hit)
        {
            var root = new GameObject(name) { layer = LayerMask.NameToLayer("Enemy") };
            rb = root.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            health = root.AddComponent<Health>();
            hit = root.AddComponent<HitReceiver>();
            return root;
        }

        private static GameObject NewChild(GameObject parent, string name)
        {
            var go = new GameObject(name) { layer = parent.layer };
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        /** Wires an O2DropConfig into EnemyBase.deathDrops ([SerializeReference] array). */
        private static void WireDeathDrops(EnemyBase brain, GameObject o2Bubble, int count)
        {
            if (o2Bubble == null) return;
            var so = new SerializedObject(brain);
            var drops = so.FindProperty("deathDrops");
            if (drops == null) { Debug.LogError("[CreatureBuilder] EnemyBase.deathDrops not found — field renamed?"); return; }
            drops.arraySize = 1;
            drops.GetArrayElementAtIndex(0).managedReferenceValue = new O2DropConfig
            {
                prefab = o2Bubble, count = count, scatterRadius = 0.8f, sizeMin = 1f, sizeMax = 2f
            };
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /** Adds the DistanceCullable wired with the creature's expensive parts. */
        private static void AddCulling(GameObject root, params Behaviour[] suspend)
        {
            var cull = root.AddComponent<DistanceCullable>();
            var so = new SerializedObject(cull);
            var list = so.FindProperty("suspendBehaviours");
            if (list == null) { Debug.LogError("[CreatureBuilder] DistanceCullable.suspendBehaviours not found."); return; }
            list.arraySize = suspend.Length;
            for (int i = 0; i < suspend.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = suspend[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /** Saves the built hierarchy over the prefab path (same GUID) and removes the temp object. */
        private static GameObject SavePrefab(GameObject root)
        {
            string path = $"{PrefabFolder}/{root.name}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // =====================================================================
        // Spawn rules
        // =====================================================================

        private static void BuildSpawnRule(string name, GameObject prefab, float minDepth, string notes,
            System.Action<SerializedObject> configure)
        {
            if (prefab == null) return;
            string path = $"{RuleFolder}/{name}.asset";
            var rule = AssetDatabase.LoadAssetAtPath<SpawnRule>(path);
            if (rule == null)
            {
                rule = ScriptableObject.CreateInstance<SpawnRule>();
                AssetDatabase.CreateAsset(rule, path);
            }

            var so = new SerializedObject(rule);
            SetString(so, "rule.ruleName", name);
            SetString(so, "rule.developerNotes", notes + " (Generated by CreatureBuilder — safe to hand-tune; " +
                                                 "the builder only resets these notes and the prefab reference.)");
            SetRef(so, "rule.prefab", prefab);
            SetFloat(so, "rule.depth.minDepth", minDepth);
            configure?.Invoke(so);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rule);
        }

        // =====================================================================
        // SerializedObject helpers — loud failures over silent drift
        // =====================================================================

        private static void EnsureFolders()
        {
            EnsureFolder("Assets/Submachina/Art", "Creatures");
            EnsureFolder("Assets/Submachina/Prefabs/World/Enemy", "Procedural");
        }

        private static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{child}"))
                AssetDatabase.CreateFolder(parent, child);
        }

        private static void Configure(Component c, System.Action<SerializedObject> edit)
        {
            var so = new SerializedObject(c);
            edit(so);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static SerializedProperty Find(SerializedObject so, string path)
        {
            var p = so.FindProperty(path);
            if (p == null) Debug.LogError($"[CreatureBuilder] Missing serialized property '{path}' on {so.targetObject.GetType().Name} — field renamed?");
            return p;
        }

        private static void SetInt(SerializedObject so, string path, int v) { var p = Find(so, path); if (p != null) p.intValue = v; }
        private static void SetFloat(SerializedObject so, string path, float v) { var p = Find(so, path); if (p != null) p.floatValue = v; }
        private static void SetBool(SerializedObject so, string path, bool v) { var p = Find(so, path); if (p != null) p.boolValue = v; }
        private static void SetString(SerializedObject so, string path, string v) { var p = Find(so, path); if (p != null) p.stringValue = v; }
        private static void SetEnum(SerializedObject so, string path, int v) { var p = Find(so, path); if (p != null) p.enumValueIndex = v; }
        private static void SetVec2(SerializedObject so, string path, Vector2 v) { var p = Find(so, path); if (p != null) p.vector2Value = v; }
        private static void SetColor(SerializedObject so, string path, Color v) { var p = Find(so, path); if (p != null) p.colorValue = v; }
        private static void SetCurve(SerializedObject so, string path, AnimationCurve v) { var p = Find(so, path); if (p != null) p.animationCurveValue = v; }
        private static void SetRef(SerializedObject so, string path, Object v) { var p = Find(so, path); if (p != null) p.objectReferenceValue = v; }

        private static void SetRefArray(SerializedObject so, string path, Object[] values)
        {
            var p = Find(so, path);
            if (p == null) return;
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        /** Direct int set on a component's private serialized field (e.g. Health.maxHP). */
        private static void SetInt(Component c, string field, int v)
        {
            var so = new SerializedObject(c);
            SetInt(so, field, v);
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
