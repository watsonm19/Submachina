using Core.Audio;
using Core.Modulation;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering.Universal;

namespace Submachina.EditorTools
{
    /**
     * One-shot scene builder for the HorrorScene descent sequence. Creates (or reuses) the
     * audio/parameter definition assets, then rebuilds a "Descent Direction" hierarchy wiring
     * the full experience: depth → Darkness/Dread parameters → light + ambience routes, plus
     * flicker/moan/stinger/jump-scare rules, the wreck encounter, and the big-bang finale.
     *
     * Re-runnable: deletes and rebuilds the scene hierarchy each time, but never overwrites
     * definition assets that already exist (so hand-tuned values survive rebuilds).
     */
    public static class DescentSequenceSceneBuilder
    {
        private const string RootName = "Descent Direction";
        private const string AudioClipRoot = "Assets/Audio/horror/";
        private const string DataRoot = "Assets/Submachina/Data";

        [MenuItem("Tools/Submachina/Build Descent Horror Sequence (active scene)")]
        public static void Build()
        {
            // Depth staging derives from the scene's LevelBounds so the sequence fits any level:
            // Darkness saturates at 80% of max depth and Dread tops out at 78% — the traversable
            // seabed usually sits well above the bounds bottom (HorrorScene: floor ~-167 vs bounds
            // -199), so staging must saturate early enough that the encounter trigger (Dread 0.97)
            // is reachable before the player grounds out. Falls back to 700 when unbounded.
            float maxDepth = 700f;
            var levelBounds = Object.FindFirstObjectByType<Submachina.Core.LevelBounds>();
            if (levelBounds != null && levelBounds.Bottom.bounded) maxDepth = Mathf.Abs(levelBounds.Bottom.value);
            float darknessFullDepth = 0.8f * maxDepth;
            float dreadStartDepth = 0.15f * maxDepth;
            float dreadFullDepth = 0.78f * maxDepth;
            Debug.Log($"[DescentSequence] Depth staging: maxDepth={maxDepth}, darknessFull={darknessFullDepth}, dread={dreadStartDepth}..{dreadFullDepth}");
            // ---------------------------------------------------------------- assets
            var darkness = LoadRequired<DirectorParameterDef>(DataRoot + "/Director/Parameters/Darkness.asset");
            var dread = LoadRequired<DirectorParameterDef>(DataRoot + "/Director/Parameters/Dread.asset");
            var intensity = LoadRequired<DirectorParameterDef>(DataRoot + "/Director/Parameters/Intensity.asset");

            EnsureFolder(DataRoot + "/Audio");
            EnsureFolder(DataRoot + "/Audio/Ambience");
            EnsureFolder(DataRoot + "/Audio/OneShots");
            EnsureFolder(DataRoot + "/Audio/Stingers");

            // Ambience beds: base → deep → eerie → bassy build → encounter swells.
            var ambBase = AmbienceDef("Amb_BaseUnderwater", "Assets/ThirdParty/Universal Sound FX/AMBIENCES/Nature/AMBIENCE_Under_Water_Active_loop_stereo.wav", 0.65f, 3f, 4f);
            var ambDeep = AmbienceDef("Amb_DeepDark", "Assets/ThirdParty/Universal Sound FX/AMBIENCES/Nature/AMBIENCE_Under_Water_Deep_Dark_loop_stereo.wav", 0.7f, 4f, 5f);
            var ambEerie = AmbienceDef("Amb_EerieGurgle", AudioClipRoot + "ambience/ESM_SGAL_cinematic_fx_ambience_horror_loops_dark_night_layer_c_water_eerie_suspense_gurgling_stream_cm.wav", 0.75f, 4f, 5f);
            var ambBassy = AmbienceDef("Amb_BassyPressure", AudioClipRoot + "ambience/ESM_SGAL_cinematic_fx_ambience_horror_loops_trapped_underwater_layer_b_bubbling_bassy_submarine_dm.wav", 0.7f, 4f, 5f);
            var ambSwells = AmbienceDef("Amb_BuildSwells", AudioClipRoot + "ambience/ESM_SGAL_cinematic_fx_ambience_horror_loops_analog_landscape_full_gritty_electric_swells_am.wav", 0.8f, 3f, 4f);

            // One-shots.
            var osBell = OneShotDef("OS_CreepyBell", new[] { AudioClipRoot + "FF_ET_120_pulse_creepy_bell_C.wav" }, 0.45f, 0.65f, 30f);
            var osMoans = OneShotDef("OS_CreatureMoans", new[]
            {
                AudioClipRoot + "stingers/creature/SeaCreatureMoan_S011HO.385.wav",
                AudioClipRoot + "stingers/creature/SeaCreatureMoan_S011HO.387.wav",
                AudioClipRoot + "stingers/creature/FF_HFFX_sfx_creature_growl_alter.wav"
            }, 0.5f, 0.8f, 20f);
            var osWhoosh = OneShotDef("OS_SkitterWhoosh", new[]
            {
                AudioClipRoot + "stingers/creature/SeaCreatureWhoosh_S011HO.39.wav",
                AudioClipRoot + "stingers/creature/SeaCreatureMove_S011HO.389.wav",
                AudioClipRoot + "stingers/creature/SeaCreatureMove_S011HO.390.wav",
                AudioClipRoot + "stingers/creature/SeaCreatureMove_S011HO.391.wav"
            }, 0.35f, 0.6f, 3f);
            var osRiser = OneShotDef("OS_Riser", new[] { AudioClipRoot + "build/FF_ET_reverse_horribly_wrong_D.wav" }, 0.9f, 1f, 0f);
            var osExplosion = OneShotDef("OS_Explosion", new[] { "Assets/Audio/EXPLOSION_Underwater_Implode_Bubbles_01_mono_01_TRIMMED.wav" }, 1f, 1f, 0f);
            var osDrone = OneShotDef("OS_DroneAtmos", new[] { AudioClipRoot + "ambience/ESM_PFFX_Cinematic_FX_drone_one_shot_atmos_dark_steady_05.wav" }, 0.65f, 0.75f, 0f);

            // Stingers.
            var stWaterphone = StingerDef("ST_Waterphone", new[] { AudioClipRoot + "stingers/MNT_SLA_perc_waterphone_scrape_metallic_dream_tansistion.wav" },
                "Eerie", 55f, 25f, 0.45f, 0.3f, 3f, 4f);
            var stJumpScare = StingerDef("ST_JumpScare", new[] { AudioClipRoot + "stingers/creature/FF_HFFX_sfx_creature_scream_killer.wav" },
                "Scare", 120f, 0f, 0.85f, 0.05f, 2.5f, 3f);

            // ---------------------------------------------------------------- scene root
            var old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject(RootName);

            var directorGo = Child(root, "Environment Director");
            var envDirector = directorGo.AddComponent<EnvironmentDirector>();

            var audioGo = Child(root, "Audio Director");
            var audioDirector = audioGo.AddComponent<AudioDirector>();
            SetFloat(audioDirector, "globalStingerCooldownSeconds", 6f);

            // ---------------------------------------------------------------- signals → parameters
            var signalsGo = Child(root, "Signals");
            var depthSignal = signalsGo.AddComponent<TransformDepthSignal>();
            SetBool(depthSignal, "useMainCameraFallback", true);
            SetFloat(depthSignal, "surfaceY", 0f);

            // Depth → Darkness: eases in slowly near the surface, steep after mid descent.
            var darknessContribution = signalsGo.AddComponent<SignalContribution>();
            ConfigureContribution(darknessContribution, envDirector, depthSignal, darkness,
                new Vector2(0f, darknessFullDepth), AnimationCurve.EaseInOut(0f, 0f, 1f, 1f), new Vector2(0f, 0.85f));

            // Depth → Dread: linear creep from dreadStartDepth to the target depth.
            var dreadContribution = signalsGo.AddComponent<SignalContribution>();
            ConfigureContribution(dreadContribution, envDirector, depthSignal, dread,
                new Vector2(dreadStartDepth, dreadFullDepth), AnimationCurve.Linear(0f, 0f, 1f, 1f), new Vector2(0f, 1f));

            // ---------------------------------------------------------------- lights
            var lightRoutesGo = Child(root, "Light Routes");
            var mainLightGo = GameObject.Find("GlobalLight2D_OceanAmbient");
            var transpLightGo = GameObject.Find("TranspFX GlobalLight2D_OceanAmbient (1)");
            Light2DFloatTarget mainLightTarget = null;

            if (mainLightGo != null)
            {
                float origMain = mainLightGo.GetComponent<Light2D>().intensity;
                mainLightTarget = EnsureLightTarget(mainLightGo);
                AddRoute(lightRoutesGo, "Route_MainLight", envDirector, darkness,
                    new Vector2(0f, 1f), AnimationCurve.Linear(0f, 0f, 1f, 1f), new Vector2(0.30f, 0.015f), mainLightTarget);

                if (transpLightGo != null)
                {
                    float scale = origMain > 0.0001f ? transpLightGo.GetComponent<Light2D>().intensity / origMain : 1f;
                    var transpTarget = EnsureLightTarget(transpLightGo);
                    AddRoute(lightRoutesGo, "Route_TranspLight", envDirector, darkness,
                        new Vector2(0f, 1f), AnimationCurve.Linear(0f, 0f, 1f, 1f), new Vector2(0.30f * scale, 0.015f * scale), transpTarget);
                }
            }
            else Debug.LogWarning("[DescentSequence] GlobalLight2D_OceanAmbient not found — light routes skipped.");

            // Red pulse light: global red glow that ramps with Intensity during the encounter.
            var redGo = Child(root, "Red Pulse Light");
            var redLight = redGo.AddComponent<Light2D>();
            CopyLightShape(mainLightGo, redLight);
            redLight.color = new Color(0.9f, 0.08f, 0.06f);
            redLight.intensity = 0f;
            var redTarget = redGo.AddComponent<Light2DFloatTarget>();
            SetObj(redTarget, "light2D", redLight);
            AddRoute(lightRoutesGo, "Route_RedLight", envDirector, intensity,
                new Vector2(0f, 1f), AnimationCurve.EaseInOut(0f, 0f, 1f, 1f), new Vector2(0f, 0.8f), redTarget);
            var redPulser = redGo.AddComponent<FloatTargetPulser>();
            SetObj(redPulser, "target", redTarget);
            SetFloat(redPulser, "amplitude", 0.3f);
            SetFloat(redPulser, "frequency", 0.7f);

            // ---------------------------------------------------------------- ambience routes
            var ambRoutesGo = Child(root, "Ambience Routes");
            AddAmbienceRoute(ambRoutesGo, "Route_BaseUnderwater", envDirector, audioDirector, ambBase, dread, new Vector2(0f, 1f), new Vector2(1f, 0.35f));
            AddAmbienceRoute(ambRoutesGo, "Route_DeepDark", envDirector, audioDirector, ambDeep, darkness, new Vector2(0.15f, 0.9f), new Vector2(0f, 1f));
            AddAmbienceRoute(ambRoutesGo, "Route_EerieGurgle", envDirector, audioDirector, ambEerie, dread, new Vector2(0.15f, 0.7f), new Vector2(0f, 0.9f));
            AddAmbienceRoute(ambRoutesGo, "Route_BassyPressure", envDirector, audioDirector, ambBassy, dread, new Vector2(0.45f, 0.9f), new Vector2(0f, 0.85f));
            AddAmbienceRoute(ambRoutesGo, "Route_BuildSwells", envDirector, audioDirector, ambSwells, intensity, new Vector2(0f, 1f), new Vector2(0f, 1f));

            // ---------------------------------------------------------------- flicker + skitters
            var flickerGo = Child(root, "Light Flicker");
            var flicker = flickerGo.AddComponent<Submachina.Core.LightFlicker>();
            if (mainLightTarget != null) SetObj(flicker, "target", mainLightTarget);

            var skittersGo = Child(root, "Skitters");
            var skitters = skittersGo.AddComponent<Submachina.Core.SkitterSpawner>();
            SetObj(skitters, "director", envDirector);
            SetObj(skitters, "parameter", dread);
            SetFloat(skitters, "minParameterValue", 0.6f);
            SetVec2(skitters, "intervalRange", new Vector2(18f, 40f));
            SetVec2(skitters, "speedRange", new Vector2(10f, 22f));
            SetVec2(skitters, "scaleRange", new Vector2(0.6f, 1.6f));
            SetInt(skitters, "sortingOrder", 60);
            AssignSkitterSprites(skitters);
            UnityEventTools.AddObjectPersistentListener<AudioOneShotDef>(skitters.onSkitter, audioDirector.TriggerOneShot, osWhoosh);

            // ---------------------------------------------------------------- wreck + finale
            var wreck = BuildWreckPlaceholder(root);

            var encounterGo = Child(root, "Wreck Encounter");
            var encounter = encounterGo.AddComponent<Submachina.Core.WreckEncounter>();
            SetObj(encounter, "wreckObject", wreck);
            SetFloat(encounter, "horizontalOffset", 32f);
            SetFloat(encounter, "reachRadius", 7f);

            var intensityTrigger = encounterGo.AddComponent<ParameterModifierTrigger>();
            SetObj(intensityTrigger, "director", envDirector);
            SetObj(intensityTrigger, "parameter", intensity);
            SetEnum(intensityTrigger, "blendMode", (int)ParameterBlendMode.Add);
            SetFloat(intensityTrigger, "value", 1f);
            SetFloat(intensityTrigger, "attackSeconds", 25f);
            SetFloat(intensityTrigger, "holdSeconds", -1f);

            var finaleGo = Child(root, "Finale");
            var finale = finaleGo.AddComponent<Submachina.Core.DescentFinale>();
            SetObj(finale, "director", envDirector);
            SetObj(finale, "darknessParameter", darkness);
            if (mainLightTarget != null) SetObj(finale, "lightTarget", mainLightTarget);
            SetFloat(finale, "blackoutFadeSeconds", 0.7f);
            SetFloat(finale, "bangDelaySeconds", 4.6f); // riser is 5.0s — bang lands right at its peak
            var finaleShake = finaleGo.AddComponent<Submachina.Core.CameraShakeTrigger>();
            SetFloat(finaleShake, "duration", 1.1f);
            SetFloat(finaleShake, "amplitude", 2.2f);

            // Encounter wiring: reveal wreck → drone hit + intensity ramp + red pulse.
            UnityEventTools.AddPersistentListener(encounter.onEncounterBegan, intensityTrigger.Apply);
            UnityEventTools.AddObjectPersistentListener<AudioOneShotDef>(encounter.onEncounterBegan, audioDirector.TriggerOneShot, osDrone);
            UnityEventTools.AddPersistentListener(encounter.onEncounterBegan, redPulser.Activate);

            // Reaching the wreck: riser leads in, finale detonates 4.6s later.
            UnityEventTools.AddObjectPersistentListener<AudioOneShotDef>(encounter.onWreckReached, audioDirector.TriggerOneShot, osRiser);
            UnityEventTools.AddPersistentListener(encounter.onWreckReached, finale.TriggerFinale);

            // The bang: explosion + shake, all ambience dies, effect emitters shut down.
            UnityEventTools.AddObjectPersistentListener<AudioOneShotDef>(finale.onBang, audioDirector.TriggerOneShot, osExplosion);
            UnityEventTools.AddPersistentListener(finale.onBang, finaleShake.Shake);
            UnityEventTools.AddFloatPersistentListener(finale.onBang, audioDirector.StopAllAmbience, 2.5f);
            UnityEventTools.AddBoolPersistentListener(finale.onBang, ambRoutesGo.SetActive, false);
            UnityEventTools.AddBoolPersistentListener(finale.onBang, skittersGo.SetActive, false);
            UnityEventTools.AddBoolPersistentListener(finale.onBang, redGo.SetActive, false);
            UnityEventTools.AddBoolPersistentListener(finale.onBang, flickerGo.SetActive, false);

            // ---------------------------------------------------------------- rules
            var rulesGo = Child(root, "Rules");
            UnityEventTools.AddBoolPersistentListener(finale.onBang, rulesGo.SetActive, false);

            // Repeating rules use resetThreshold 2 (above max) so they auto re-arm on monotonic depth,
            // with cooldown + probability providing the actual pacing.
            var ruleFlicker = AddRule(rulesGo, "Rule_Flicker", envDirector, dread, 0.35f, 2f, 0f, 12f, 0.65f, false);
            UnityEventTools.AddPersistentListener(ruleFlicker.onTriggered, flicker.PlayFlicker);
            UnityEventTools.AddObjectPersistentListener<AudioOneShotDef>(ruleFlicker.onTriggered, audioDirector.TriggerOneShot, osBell);

            var ruleMoan = AddRule(rulesGo, "Rule_CreatureMoans", envDirector, dread, 0.55f, 2f, 0f, 15f, 0.6f, false);
            UnityEventTools.AddObjectPersistentListener<AudioOneShotDef>(ruleMoan.onTriggered, audioDirector.TriggerOneShot, osMoans);

            var ruleEerie = AddRule(rulesGo, "Rule_EerieStinger", envDirector, dread, 0.5f, 2f, 0f, 30f, 0.5f, false);
            UnityEventTools.AddObjectPersistentListener<AudioStingerDef>(ruleEerie.onTriggered, audioDirector.TriggerStinger, stWaterphone);

            var ruleScare = AddRule(rulesGo, "Rule_JumpScare", envDirector, dread, 0.82f, 0.5f, 0.4f, 0f, 1f, true);
            UnityEventTools.AddObjectPersistentListener<AudioStingerDef>(ruleScare.onTriggered, audioDirector.TriggerStinger, stJumpScare);
            UnityEventTools.AddFloatPersistentListener(ruleScare.onTriggered, flicker.PlayFlicker, 1.6f);
            var scareShake = ruleScare.gameObject.AddComponent<Submachina.Core.CameraShakeTrigger>();
            SetFloat(scareShake, "duration", 0.6f);
            SetFloat(scareShake, "amplitude", 1.4f);
            UnityEventTools.AddPersistentListener(ruleScare.onTriggered, scareShake.Shake);

            var ruleEncounter = AddRule(rulesGo, "Rule_BeginEncounter", envDirector, dread, 0.97f, 0.5f, 2f, 0f, 1f, true);
            UnityEventTools.AddPersistentListener(ruleEncounter.onTriggered, encounter.BeginEncounter);

            // ---------------------------------------------------------------- hand off legacy audio
            DisableLegacyAudio("Sound Manager/AMBIENCE_Under_Water_Active_loop_stereo");
            DisableLegacyAudio("Sound Manager/Music");

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log("[DescentSequence] Build complete. Descent Direction hierarchy rebuilt and scene saved.");
        }

        // -------------------------------------------------------------------- asset helpers

        private static T LoadRequired<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) Debug.LogError($"[DescentSequence] Missing required asset: {path}");
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }

        /** Creates an ambience layer def once; existing assets are loaded untouched so tuning survives rebuilds. */
        private static AmbienceLayerDef AmbienceDef(string name, string clipPath, float baseVolume, float fadeIn, float fadeOut)
        {
            string assetPath = $"{DataRoot}/Audio/Ambience/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<AmbienceLayerDef>(assetPath);
            if (existing != null) return existing;

            var def = ScriptableObject.CreateInstance<AmbienceLayerDef>();
            def.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
            if (def.clip == null) Debug.LogWarning($"[DescentSequence] Ambience clip missing: {clipPath}");
            def.baseVolume = baseVolume;
            def.fadeInSeconds = fadeIn;
            def.fadeOutSeconds = fadeOut;
            AssetDatabase.CreateAsset(def, assetPath);
            return def;
        }

        private static AudioOneShotDef OneShotDef(string name, string[] clipPaths, float volMin, float volMax, float cooldown)
        {
            string assetPath = $"{DataRoot}/Audio/OneShots/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<AudioOneShotDef>(assetPath);
            if (existing != null) return existing;

            var def = ScriptableObject.CreateInstance<AudioOneShotDef>();
            def.clips = LoadClips(clipPaths);
            def.volumeRange = new Vector2(volMin, volMax);
            def.cooldownSeconds = cooldown;
            AssetDatabase.CreateAsset(def, assetPath);
            return def;
        }

        private static AudioStingerDef StingerDef(string name, string[] clipPaths, string category,
            float cooldown, float categoryCooldown, float duckAmount, float duckAttack, float duckHold, float duckRelease)
        {
            string assetPath = $"{DataRoot}/Audio/Stingers/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<AudioStingerDef>(assetPath);
            if (existing != null) return existing;

            var def = ScriptableObject.CreateInstance<AudioStingerDef>();
            def.clips = LoadClips(clipPaths);
            def.category = category;
            def.cooldownSeconds = cooldown;
            def.categoryCooldownSeconds = categoryCooldown;
            def.duckAmount = duckAmount;
            def.duckAttackSeconds = duckAttack;
            def.duckHoldSeconds = duckHold;
            def.duckReleaseSeconds = duckRelease;
            AssetDatabase.CreateAsset(def, assetPath);
            return def;
        }

        private static AudioClip[] LoadClips(string[] paths)
        {
            var clips = new AudioClip[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                clips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(paths[i]);
                if (clips[i] == null) Debug.LogWarning($"[DescentSequence] Clip missing: {paths[i]}");
            }
            return clips;
        }

        // -------------------------------------------------------------------- scene helpers

        private static GameObject Child(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        private static void ConfigureContribution(SignalContribution c, EnvironmentDirector director, FloatSignal signal,
            DirectorParameterDef parameter, Vector2 input, AnimationCurve curve, Vector2 output)
        {
            SetObj(c, "director", director);
            SetObj(c, "signal", signal);
            SetObj(c, "parameter", parameter);
            SetVec2(c, "inputRange", input);
            SetCurve(c, "responseCurve", curve);
            SetVec2(c, "outputRange", output);
        }

        private static FloatRoute AddRoute(GameObject parent, string name, EnvironmentDirector director,
            DirectorParameterDef parameter, Vector2 input, AnimationCurve curve, Vector2 output, ModulatedFloatTarget target)
        {
            var go = Child(parent, name);
            var route = go.AddComponent<FloatRoute>();
            SetObj(route, "director", director);
            SetObj(route, "parameter", parameter);
            SetVec2(route, "inputRange", input);
            SetCurve(route, "responseCurve", curve);
            SetVec2(route, "outputRange", output);
            SetObj(route, "target", target);
            return route;
        }

        /** One ambience layer = one child GO carrying the influence target + the route that feeds it. */
        private static void AddAmbienceRoute(GameObject parent, string name, EnvironmentDirector envDirector,
            AudioDirector audioDirector, AmbienceLayerDef layer, DirectorParameterDef parameter, Vector2 input, Vector2 output)
        {
            var go = Child(parent, name);
            var target = go.AddComponent<AmbienceInfluenceTarget>();
            SetObj(target, "audioDirector", audioDirector);
            SetObj(target, "layer", layer);
            AddRoute(go, name + "_Route", envDirector, parameter, input, AnimationCurve.Linear(0f, 0f, 1f, 1f), output, target);
        }

        private static DirectorRule AddRule(GameObject parent, string name, EnvironmentDirector director,
            DirectorParameterDef parameter, float trigger, float reset, float sustain, float cooldown, float probability, bool oneShot)
        {
            var go = Child(parent, name);
            var rule = go.AddComponent<DirectorRule>();
            SetObj(rule, "director", director);
            SetObj(rule, "parameter", parameter);
            SetFloat(rule, "triggerThreshold", trigger);
            SetFloat(rule, "resetThreshold", reset);
            SetFloat(rule, "sustainSeconds", sustain);
            SetFloat(rule, "cooldownSeconds", cooldown);
            SetFloat(rule, "probability", probability);
            SetBool(rule, "oneShot", oneShot);
            return rule;
        }

        private static Light2DFloatTarget EnsureLightTarget(GameObject lightGo)
        {
            var target = lightGo.GetComponent<Light2DFloatTarget>();
            if (target == null) target = lightGo.AddComponent<Light2DFloatTarget>();
            SetObj(target, "light2D", lightGo.GetComponent<Light2D>());
            return target;
        }

        /** Copies light type + sorting-layer coverage from the scene's global light so the red light affects the same layers. */
        private static void CopyLightShape(GameObject sourceLightGo, Light2D destination)
        {
            if (sourceLightGo == null) return;
            var source = sourceLightGo.GetComponent<Light2D>();
            if (source == null) return;

            var srcSer = new SerializedObject(source);
            var dstSer = new SerializedObject(destination);
            dstSer.FindProperty("m_LightType").intValue = srcSer.FindProperty("m_LightType").intValue;
            dstSer.CopyFromSerializedProperty(srcSer.FindProperty("m_ApplyToSortingLayers"));
            dstSer.ApplyModifiedPropertiesWithoutUndo();
        }

        /** Wreck placeholder: dark rock sprite + trigger collider + sonar target, inactive until the encounter begins. */
        private static GameObject BuildWreckPlaceholder(GameObject root)
        {
            var wreck = Child(root, "Wreck (Placeholder)");

            var sr = wreck.AddComponent<SpriteRenderer>();
            var rockGo = GameObject.Find("FossilRock_0_albedo");
            if (rockGo != null && rockGo.TryGetComponent<SpriteRenderer>(out var rockSr)) sr.sprite = rockSr.sprite;
            sr.color = new Color(0.35f, 0.32f, 0.3f);
            sr.sortingOrder = 10;
            wreck.transform.localScale = new Vector3(3f, 3f, 1f);

            var col = wreck.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 2f;

            var sonarTarget = wreck.AddComponent<Submachina.Core.SonarTarget>();
            var signature = AssetDatabase.LoadAssetAtPath<Submachina.Core.SonarSignature>("Assets/Submachina/Data/Sonar/RockSignature.asset");
            if (signature != null) SetObj(sonarTarget, "signature", signature);
            else Debug.LogWarning("[DescentSequence] RockSignature.asset not found — wreck has no sonar signature.");

            wreck.SetActive(false);
            return wreck;
        }

        private static void DisableLegacyAudio(string path)
        {
            var go = GameObject.Find(path);
            if (go == null) { Debug.LogWarning($"[DescentSequence] Legacy audio object not found: {path}"); return; }
            go.SetActive(false);
        }

        // -------------------------------------------------------------------- serialized-field helpers

        private static void SetObj(Component c, string field, Object value) => Apply(c, field, p => p.objectReferenceValue = value);
        private static void SetFloat(Component c, string field, float value) => Apply(c, field, p => p.floatValue = value);
        private static void SetInt(Component c, string field, int value) => Apply(c, field, p => p.intValue = value);
        private static void SetEnum(Component c, string field, int value) => Apply(c, field, p => p.enumValueIndex = value);
        private static void SetBool(Component c, string field, bool value) => Apply(c, field, p => p.boolValue = value);
        private static void SetVec2(Component c, string field, Vector2 value) => Apply(c, field, p => p.vector2Value = value);
        private static void SetCurve(Component c, string field, AnimationCurve value) => Apply(c, field, p => p.animationCurveValue = value);

        private static void Apply(Component c, string field, System.Action<SerializedProperty> setter)
        {
            var so = new SerializedObject(c);
            var prop = so.FindProperty(field);
            if (prop == null) { Debug.LogError($"[DescentSequence] Field '{field}' not found on {c.GetType().Name}"); return; }
            setter(prop);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /** Pulls silhouette sprites for skitters from existing creature prefabs so no new art is needed. */
        private static void AssignSkitterSprites(Component skitters)
        {
            string[] prefabPaths =
            {
                "Assets/Submachina/Prefabs/World/Enemy/SeaCreature.prefab",
                "Assets/Submachina/Prefabs/World/Enemy/PassiveCreature.prefab",
                "Assets/Submachina/Prefabs/World/Enemy/RammerEnemy.prefab"
            };

            var sprites = new System.Collections.Generic.List<Sprite>();
            foreach (var path in prefabPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                var sr = prefab.GetComponentInChildren<SpriteRenderer>();
                if (sr != null && sr.sprite != null) sprites.Add(sr.sprite);
            }
            if (sprites.Count == 0) { Debug.LogWarning("[DescentSequence] No skitter sprites found in creature prefabs."); return; }

            var so = new SerializedObject(skitters);
            var arr = so.FindProperty("sprites");
            arr.arraySize = sprites.Count;
            for (int i = 0; i < sprites.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
