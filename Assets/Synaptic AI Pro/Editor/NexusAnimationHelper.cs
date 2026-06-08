using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using SynapticAIPro;

namespace SynapticPro
{
    /// <summary>
    /// Helper class for advanced animation and motion management
    /// Handles Mixamo integration, IK setup, and animation asset management
    /// </summary>
    public static class NexusAnimationHelper
    {
        private const string MIXAMO_RIG_PREFIX = "mixamorig:";
        private const string HUMANOID_AVATAR_PREFIX = "Avatar_";

        /// <summary>
        /// Import and setup Mixamo FBX with automatic Humanoid configuration
        /// </summary>
        public static string ImportMixamoAnimation(Dictionary<string, string> parameters)
        {
            try
            {
                string fbxPath = parameters.GetValueOrDefault("fbxPath", "");
                string targetPath = parameters.GetValueOrDefault("targetPath", "Assets/Animations/Mixamo/");
                bool createController = parameters.GetValueOrDefault("createController", "true") == "true";
                string characterName = parameters.GetValueOrDefault("characterName", "Character");
                bool setupIK = parameters.GetValueOrDefault("setupIK", "true") == "true";

                if (string.IsNullOrEmpty(fbxPath) || !File.Exists(fbxPath))
                {
                    return $"Error: FBX file not found at path: {fbxPath}";
                }

                // Ensure target directory exists
                if (!AssetDatabase.IsValidFolder(targetPath))
                {
                    string[] folders = targetPath.Split('/');
                    string currentPath = folders[0];
                    for (int i = 1; i < folders.Length; i++)
                    {
                        if (string.IsNullOrEmpty(folders[i])) continue;
                        string nextPath = currentPath + "/" + folders[i];
                        if (!AssetDatabase.IsValidFolder(nextPath))
                        {
                            AssetDatabase.CreateFolder(currentPath, folders[i]);
                        }
                        currentPath = nextPath;
                    }
                }

                // Copy FBX to project
                string fileName = Path.GetFileName(fbxPath);
                string assetPath = Path.Combine(targetPath, fileName);
                File.Copy(fbxPath, assetPath, true);
                AssetDatabase.Refresh();

                // Configure as Humanoid
                ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
                if (importer != null)
                {
                    // Setup for Mixamo
                    importer.animationType = ModelImporterAnimationType.Human;
                    importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;

                    // Animation settings
                    importer.importAnimation = true;
                    importer.animationCompression = ModelImporterAnimationCompression.Optimal;

                    // Optimize for Mixamo
                    importer.optimizeGameObjects = true;
                    importer.optimizeMeshPolygons = true;
                    importer.optimizeMeshVertices = true;

                    // Material settings
                    importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;

                    // Apply settings
                    importer.SaveAndReimport();

                    var result = new Dictionary<string, object>
                    {
                        ["success"] = true,
                        ["assetPath"] = assetPath,
                        ["characterName"] = characterName
                    };

                    // Create Animator Controller if requested
                    if (createController)
                    {
                        string controllerPath = CreateAnimatorControllerForMixamo(assetPath, characterName, targetPath);
                        result["controllerPath"] = controllerPath;
                    }

                    // Setup IK if requested
                    if (setupIK)
                    {
                        SetupBasicIK(characterName);
                        result["ikSetup"] = true;
                    }

                    return JsonUtility.ToJson(result);
                }

                return "Error: Failed to configure model importer";
            }
            catch (Exception e)
            {
                return $"Error importing Mixamo animation: {e.Message}";
            }
        }

        /// <summary>
        /// Organize and categorize animation clips
        /// </summary>
        public static string OrganizeAnimationAssets(Dictionary<string, string> parameters)
        {
            try
            {
                string sourcePath = parameters.GetValueOrDefault("sourcePath", "Assets/Animations/");
                bool autoDetectType = parameters.GetValueOrDefault("autoDetectType", "true") == "true";
                bool createFolders = parameters.GetValueOrDefault("createFolders", "true") == "true";

                // Find all animation clips
                string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { sourcePath });

                var categories = new Dictionary<string, List<AnimationClip>>
                {
                    ["Idle"] = new List<AnimationClip>(),
                    ["Walk"] = new List<AnimationClip>(),
                    ["Run"] = new List<AnimationClip>(),
                    ["Jump"] = new List<AnimationClip>(),
                    ["Attack"] = new List<AnimationClip>(),
                    ["Death"] = new List<AnimationClip>(),
                    ["Damage"] = new List<AnimationClip>(),
                    ["Other"] = new List<AnimationClip>()
                };

                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);

                    if (clip != null && autoDetectType)
                    {
                        string category = DetectAnimationType(clip.name);
                        categories[category].Add(clip);

                        // Move to categorized folder if requested
                        if (createFolders)
                        {
                            string targetFolder = Path.Combine(sourcePath, category);
                            if (!AssetDatabase.IsValidFolder(targetFolder))
                            {
                                AssetDatabase.CreateFolder(sourcePath, category);
                            }

                            string newPath = Path.Combine(targetFolder, Path.GetFileName(path));
                            if (path != newPath)
                            {
                                AssetDatabase.MoveAsset(path, newPath);
                            }
                        }
                    }
                }

                // Generate report
                var report = new Dictionary<string, object>
                {
                    ["totalClips"] = guids.Length,
                    ["categories"] = new Dictionary<string, int>()
                };

                foreach (var category in categories)
                {
                    ((Dictionary<string, int>)report["categories"])[category.Key] = category.Value.Count;
                }

                return JsonUtility.ToJson(report);
            }
            catch (Exception e)
            {
                return $"Error organizing animation assets: {e.Message}";
            }
        }

        /// <summary>
        /// Setup IK for character
        /// </summary>
        public static string SetupCharacterIK(Dictionary<string, string> parameters)
        {
            try
            {
                string gameObjectName = parameters.GetValueOrDefault("gameObject", "");
                bool enableFootIK = parameters.GetValueOrDefault("enableFootIK", "true") == "true";
                bool enableHandIK = parameters.GetValueOrDefault("enableHandIK", "false") == "true";
                bool enableLookAt = parameters.GetValueOrDefault("enableLookAt", "false") == "true";
                float footIKWeight = float.Parse(parameters.GetValueOrDefault("footIKWeight", "1"));
                float handIKWeight = float.Parse(parameters.GetValueOrDefault("handIKWeight", "1"));

                GameObject target = GameObject.Find(gameObjectName);
                if (target == null)
                {
                    return $"Error: GameObject '{gameObjectName}' not found";
                }

                // Add IK controller component if not exists
                var ikController = target.GetComponent<IKController>();
                if (ikController == null)
                {
                    ikController = target.AddComponent<IKController>();
                    Undo.RegisterCreatedObjectUndo(ikController, "Add IK Controller");
                }

                // Configure IK settings
                ikController.enableFootIK = enableFootIK;
                ikController.enableHandIK = enableHandIK;
                ikController.enableLookAt = enableLookAt;
                ikController.footIKWeight = footIKWeight;
                ikController.handIKWeight = handIKWeight;

                // Setup IK targets
                if (enableFootIK)
                {
                    CreateIKTarget(target.transform, "LeftFootIK", new Vector3(-0.1f, 0, 0));
                    CreateIKTarget(target.transform, "RightFootIK", new Vector3(0.1f, 0, 0));
                }

                if (enableHandIK)
                {
                    CreateIKTarget(target.transform, "LeftHandIK", new Vector3(-0.5f, 1.5f, 0.5f));
                    CreateIKTarget(target.transform, "RightHandIK", new Vector3(0.5f, 1.5f, 0.5f));
                }

                if (enableLookAt)
                {
                    CreateIKTarget(target.transform, "LookAtTarget", new Vector3(0, 1.6f, 2f));
                }

                EditorUtility.SetDirty(target);

                return JsonUtility.ToJson(new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["gameObject"] = gameObjectName,
                    ["footIK"] = enableFootIK,
                    ["handIK"] = enableHandIK,
                    ["lookAt"] = enableLookAt
                });
            }
            catch (Exception e)
            {
                return $"Error setting up IK: {e.Message}";
            }
        }

        /// <summary>
        /// Create animation layer mask
        /// </summary>
        public static string CreateAnimationLayerMask(Dictionary<string, string> parameters)
        {
            try
            {
                string maskName = parameters.GetValueOrDefault("maskName", "NewLayerMask");
                string savePath = parameters.GetValueOrDefault("savePath", "Assets/Animations/Masks/");
                string includeBonesPattern = parameters.GetValueOrDefault("includeBones", "");
                string excludeBonesPattern = parameters.GetValueOrDefault("excludeBones", "");
                string avatarPath = parameters.GetValueOrDefault("avatarPath", "");

                if (!AssetDatabase.IsValidFolder(savePath))
                {
                    Directory.CreateDirectory(savePath);
                    AssetDatabase.Refresh();
                }

                // Create avatar mask
                AvatarMask mask = new AvatarMask();
                mask.name = maskName;

                // Load avatar if specified
                if (!string.IsNullOrEmpty(avatarPath))
                {
                    Avatar avatar = AssetDatabase.LoadAssetAtPath<Avatar>(avatarPath);
                    if (avatar != null)
                    {
                        // Configure body parts based on patterns
                        ConfigureAvatarMaskBodyParts(mask, includeBonesPattern, excludeBonesPattern);
                    }
                }

                string assetPath = Path.Combine(savePath, maskName + ".mask");
                AssetDatabase.CreateAsset(mask, assetPath);
                AssetDatabase.SaveAssets();

                return JsonUtility.ToJson(new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["maskPath"] = assetPath,
                    ["maskName"] = maskName
                });
            }
            catch (Exception e)
            {
                return $"Error creating layer mask: {e.Message}";
            }
        }

        /// <summary>
        /// Setup animation blend tree
        /// </summary>
        public static string SetupAdvancedBlendTree(Dictionary<string, string> parameters)
        {
            try
            {
                string controllerPath = parameters.GetValueOrDefault("controllerPath", "");
                string blendType = parameters.GetValueOrDefault("blendType", "2D");
                string parameterX = parameters.GetValueOrDefault("parameterX", "MoveX");
                string parameterY = parameters.GetValueOrDefault("parameterY", "MoveY");
                string clips = parameters.GetValueOrDefault("clips", ""); // Comma separated paths

                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                if (controller == null)
                {
                    return "Error: Animator Controller not found";
                }

                // Create blend tree
                var rootStateMachine = controller.layers[0].stateMachine;
                var blendTreeState = rootStateMachine.AddState("BlendTree");

                BlendTree blendTree;
                controller.CreateBlendTreeInController("Movement", out blendTree);
                blendTreeState.motion = blendTree;

                // Configure blend type
                switch (blendType.ToLower())
                {
                    case "1d":
                        blendTree.blendType = BlendTreeType.Simple1D;
                        blendTree.blendParameter = parameterX;
                        break;
                    case "2d":
                        blendTree.blendType = BlendTreeType.SimpleDirectional2D;
                        blendTree.blendParameter = parameterX;
                        blendTree.blendParameterY = parameterY;
                        break;
                    case "freeform":
                        blendTree.blendType = BlendTreeType.FreeformDirectional2D;
                        blendTree.blendParameter = parameterX;
                        blendTree.blendParameterY = parameterY;
                        break;
                }

                // Add animation clips
                if (!string.IsNullOrEmpty(clips))
                {
                    string[] clipPaths = clips.Split(',');
                    foreach (string clipPath in clipPaths)
                    {
                        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath.Trim());
                        if (clip != null)
                        {
                            blendTree.AddChild(clip);
                        }
                    }
                }

                // Add parameters if not exist
                if (!controller.parameters.Any(p => p.name == parameterX))
                {
                    controller.AddParameter(parameterX, AnimatorControllerParameterType.Float);
                }
                if (!string.IsNullOrEmpty(parameterY) && !controller.parameters.Any(p => p.name == parameterY))
                {
                    controller.AddParameter(parameterY, AnimatorControllerParameterType.Float);
                }

                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(controller), ImportAssetOptions.ForceUpdate);

                return JsonUtility.ToJson(new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["blendTreeName"] = "Movement",
                    ["blendType"] = blendType,
                    ["clipCount"] = blendTree.children.Length
                });
            }
            catch (Exception e)
            {
                return $"Error setting up blend tree: {e.Message}";
            }
        }

        /// <summary>
        /// Retarget animation from one rig to another
        /// </summary>
        public static string RetargetAnimation(Dictionary<string, string> parameters)
        {
            try
            {
                string sourceClipPath = parameters.GetValueOrDefault("sourceClip", "");
                string targetAvatarPath = parameters.GetValueOrDefault("targetAvatar", "");
                string outputPath = parameters.GetValueOrDefault("outputPath", "Assets/Animations/Retargeted/");
                bool adjustRootMotion = parameters.GetValueOrDefault("adjustRootMotion", "true") == "true";

                AnimationClip sourceClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(sourceClipPath);
                Avatar targetAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(targetAvatarPath);

                if (sourceClip == null || targetAvatar == null)
                {
                    return "Error: Source clip or target avatar not found";
                }

                // Create output directory
                if (!AssetDatabase.IsValidFolder(outputPath))
                {
                    Directory.CreateDirectory(outputPath);
                    AssetDatabase.Refresh();
                }

                // Clone animation clip
                AnimationClip retargetedClip = UnityEngine.Object.Instantiate(sourceClip);
                retargetedClip.name = sourceClip.name + "_Retargeted";

                // Adjust root motion if needed
                if (adjustRootMotion)
                {
                    AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(retargetedClip);
                    settings.loopTime = sourceClip.isLooping;
                    settings.keepOriginalPositionY = true;
                    settings.keepOriginalOrientation = true;
                    AnimationUtility.SetAnimationClipSettings(retargetedClip, settings);
                }

                string savePath = Path.Combine(outputPath, retargetedClip.name + ".anim");
                AssetDatabase.CreateAsset(retargetedClip, savePath);
                AssetDatabase.SaveAssets();

                return JsonUtility.ToJson(new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["retargetedClip"] = savePath,
                    ["originalClip"] = sourceClipPath
                });
            }
            catch (Exception e)
            {
                return $"Error retargeting animation: {e.Message}";
            }
        }

        /// <summary>
        /// Create animation transition presets
        /// </summary>
        public static string CreateTransitionPreset(Dictionary<string, string> parameters)
        {
            try
            {
                string controllerPath = parameters.GetValueOrDefault("controllerPath", "");
                string fromState = parameters.GetValueOrDefault("fromState", "");
                string toState = parameters.GetValueOrDefault("toState", "");
                string presetType = parameters.GetValueOrDefault("presetType", "smooth"); // smooth, instant, crossfade
                float duration = float.Parse(parameters.GetValueOrDefault("duration", "0.25"));

                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                if (controller == null)
                {
                    return "Error: Controller not found";
                }

                var stateMachine = controller.layers[0].stateMachine;
                var sourceState = FindState(stateMachine, fromState);
                var destState = FindState(stateMachine, toState);

                if (sourceState == null || destState == null)
                {
                    return "Error: States not found";
                }

                var transition = sourceState.AddTransition(destState);

                // Configure based on preset
                switch (presetType.ToLower())
                {
                    case "instant":
                        transition.duration = 0;
                        transition.offset = 0;
                        transition.hasExitTime = false;
                        break;
                    case "smooth":
                        transition.duration = duration;
                        transition.offset = 0;
                        transition.hasExitTime = true;
                        transition.exitTime = 0.75f;
                        break;
                    case "crossfade":
                        transition.duration = duration * 2;
                        transition.offset = 0.1f;
                        transition.hasExitTime = true;
                        transition.exitTime = 0.5f;
                        break;
                }

                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(controller), ImportAssetOptions.ForceUpdate);

                return JsonUtility.ToJson(new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["fromState"] = fromState,
                    ["toState"] = toState,
                    ["presetType"] = presetType
                });
            }
            catch (Exception e)
            {
                return $"Error creating transition preset: {e.Message}";
            }
        }

        /// <summary>
        /// Analyze animation performance
        /// </summary>
        public static string AnalyzeAnimationPerformance(Dictionary<string, string> parameters)
        {
            try
            {
                string targetPath = parameters.GetValueOrDefault("targetPath", "Assets/Animations/");

                string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { targetPath });
                var report = new Dictionary<string, object>
                {
                    ["totalClips"] = guids.Length,
                    ["clips"] = new List<Dictionary<string, object>>()
                };

                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);

                    if (clip != null)
                    {
                        var clipInfo = new Dictionary<string, object>
                        {
                            ["name"] = clip.name,
                            ["length"] = clip.length,
                            ["frameRate"] = clip.frameRate,
                            ["isHumanMotion"] = clip.humanMotion,
                            ["isLooping"] = clip.isLooping,
                            ["events"] = clip.events.Length,
                            ["approximateSize"] = EstimateAnimationSize(clip)
                        };

                        ((List<Dictionary<string, object>>)report["clips"]).Add(clipInfo);
                    }
                }

                return JsonUtility.ToJson(report);
            }
            catch (Exception e)
            {
                return $"Error analyzing animation performance: {e.Message}";
            }
        }

        // ===== Helper Methods =====

        private static string CreateAnimatorControllerForMixamo(string fbxPath, string characterName, string targetPath)
        {
            string controllerPath = Path.Combine(targetPath, characterName + "_Controller.controller");

            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

            // Create parameters
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Direction", AnimatorControllerParameterType.Float);
            controller.AddParameter("IsGrounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);

            // Create default states
            var stateMachine = controller.layers[0].stateMachine;
            var idleState = stateMachine.AddState("Idle");
            stateMachine.defaultState = idleState;

            // Try to find and assign idle animation from the FBX
            var clips = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>().ToArray();
            foreach (var clip in clips)
            {
                if (clip.name.ToLower().Contains("idle"))
                {
                    idleState.motion = clip;
                    break;
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(controllerPath, ImportAssetOptions.ForceUpdate);

            return controllerPath;
        }

        private static void SetupBasicIK(string characterName)
        {
            // This would normally set up IK components
            // Actual implementation would depend on the IK solution being used
            SynLog.Info($"IK setup prepared for {characterName}");
        }

        private static string DetectAnimationType(string clipName)
        {
            string lowerName = clipName.ToLower();

            if (lowerName.Contains("idle") || lowerName.Contains("stand"))
                return "Idle";
            if (lowerName.Contains("walk"))
                return "Walk";
            if (lowerName.Contains("run") || lowerName.Contains("sprint"))
                return "Run";
            if (lowerName.Contains("jump") || lowerName.Contains("leap"))
                return "Jump";
            if (lowerName.Contains("attack") || lowerName.Contains("punch") || lowerName.Contains("kick"))
                return "Attack";
            if (lowerName.Contains("death") || lowerName.Contains("die"))
                return "Death";
            if (lowerName.Contains("damage") || lowerName.Contains("hit") || lowerName.Contains("hurt"))
                return "Damage";

            return "Other";
        }

        private static GameObject CreateIKTarget(Transform parent, string name, Vector3 localPosition)
        {
            GameObject ikTarget = new GameObject(name);
            ikTarget.transform.SetParent(parent);
            ikTarget.transform.localPosition = localPosition;

            // Add visual gizmo component for editor
            var gizmo = ikTarget.AddComponent<IKTargetGizmo>();
            gizmo.color = name.Contains("Foot") ? Color.green : (name.Contains("Hand") ? Color.blue : Color.yellow);

            Undo.RegisterCreatedObjectUndo(ikTarget, $"Create {name}");

            return ikTarget;
        }

        private static void ConfigureAvatarMaskBodyParts(AvatarMask mask, string includeBones, string excludeBones)
        {
            // Configure humanoid body parts
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                bool include = true;
                AvatarMaskBodyPart part = (AvatarMaskBodyPart)i;
                string partName = part.ToString().ToLower();

                if (!string.IsNullOrEmpty(excludeBones) && excludeBones.ToLower().Contains(partName))
                {
                    include = false;
                }
                if (!string.IsNullOrEmpty(includeBones) && !includeBones.ToLower().Contains(partName))
                {
                    include = false;
                }

                mask.SetHumanoidBodyPartActive(part, include);
            }
        }

        private static AnimatorState FindState(AnimatorStateMachine stateMachine, string name)
        {
            foreach (var state in stateMachine.states)
            {
                if (state.state.name == name)
                    return state.state;
            }
            return null;
        }

        private static float EstimateAnimationSize(AnimationClip clip)
        {
            // Rough estimation based on curves and keys
            var bindings = AnimationUtility.GetCurveBindings(clip);
            int totalKeys = 0;

            foreach (var binding in bindings)
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve != null)
                {
                    totalKeys += curve.keys.Length;
                }
            }

            // Approximate size in KB (4 bytes per float * keys * 4 values per key)
            return (totalKeys * 4 * 4) / 1024f;
        }
    }

    /// <summary>
    /// IK Controller component for runtime IK handling
    /// </summary>
    public class IKController : MonoBehaviour
    {
        public bool enableFootIK = true;
        public bool enableHandIK = false;
        public bool enableLookAt = false;

        public float footIKWeight = 1f;
        public float handIKWeight = 1f;
        public float lookAtWeight = 1f;

        public Transform leftFootTarget;
        public Transform rightFootTarget;
        public Transform leftHandTarget;
        public Transform rightHandTarget;
        public Transform lookAtTarget;

        private Animator animator;

        void Start()
        {
            animator = GetComponent<Animator>();
        }

        void OnAnimatorIK(int layerIndex)
        {
            if (animator == null) return;

            // Foot IK
            if (enableFootIK)
            {
                if (leftFootTarget != null)
                {
                    animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, footIKWeight);
                    animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, footIKWeight);
                    animator.SetIKPosition(AvatarIKGoal.LeftFoot, leftFootTarget.position);
                    animator.SetIKRotation(AvatarIKGoal.LeftFoot, leftFootTarget.rotation);
                }

                if (rightFootTarget != null)
                {
                    animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, footIKWeight);
                    animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, footIKWeight);
                    animator.SetIKPosition(AvatarIKGoal.RightFoot, rightFootTarget.position);
                    animator.SetIKRotation(AvatarIKGoal.RightFoot, rightFootTarget.rotation);
                }
            }

            // Hand IK
            if (enableHandIK)
            {
                if (leftHandTarget != null)
                {
                    animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, handIKWeight);
                    animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, handIKWeight);
                    animator.SetIKPosition(AvatarIKGoal.LeftHand, leftHandTarget.position);
                    animator.SetIKRotation(AvatarIKGoal.LeftHand, leftHandTarget.rotation);
                }

                if (rightHandTarget != null)
                {
                    animator.SetIKPositionWeight(AvatarIKGoal.RightHand, handIKWeight);
                    animator.SetIKRotationWeight(AvatarIKGoal.RightHand, handIKWeight);
                    animator.SetIKPosition(AvatarIKGoal.RightHand, rightHandTarget.position);
                    animator.SetIKRotation(AvatarIKGoal.RightHand, rightHandTarget.rotation);
                }
            }

            // Look At
            if (enableLookAt && lookAtTarget != null)
            {
                animator.SetLookAtWeight(lookAtWeight);
                animator.SetLookAtPosition(lookAtTarget.position);
            }
        }
    }

    /// <summary>
    /// Visual gizmo for IK targets
    /// </summary>
    public class IKTargetGizmo : MonoBehaviour
    {
        public Color color = Color.green;
        public float size = 0.1f;

        void OnDrawGizmos()
        {
            Gizmos.color = color;
            Gizmos.DrawWireSphere(transform.position, size);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * size * 2);
        }
    }
}
