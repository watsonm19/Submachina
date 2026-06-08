using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using SynapticAIPro;
using UnityEditor;
using System.Text;
using Newtonsoft.Json;

namespace SynapticPro
{
    /// <summary>
    /// Unity project settings detailed retrieval and manipulation system
    /// Provides complete information for Build Settings, Player Settings, Quality Settings, etc.
    /// </summary>
    public static class NexusProjectSettings
    {
        #region Build Settings

        /// <summary>
        /// Get detailed information for Build Settings
        /// </summary>
        public static string GetBuildSettings()
        {
            try
            {
                var buildSettings = new Dictionary<string, object>();

                // Basic settings
                buildSettings["target_group"] = EditorUserBuildSettings.selectedBuildTargetGroup.ToString();
                buildSettings["build_target"] = EditorUserBuildSettings.activeBuildTarget.ToString();
                buildSettings["development_build"] = EditorUserBuildSettings.development;
                buildSettings["auto_connect_profiler"] = EditorUserBuildSettings.connectProfiler;
                buildSettings["deep_profiling"] = EditorUserBuildSettings.buildWithDeepProfilingSupport;
                buildSettings["script_debugging"] = EditorUserBuildSettings.allowDebugging;

                // Scene settings
                var scenes = EditorBuildSettings.scenes;
                var sceneList = new List<Dictionary<string, object>>();
                
                for (int i = 0; i < scenes.Length; i++)
                {
                    var scene = scenes[i];
                    sceneList.Add(new Dictionary<string, object>
                    {
                        ["index"] = i,
                        ["path"] = scene.path,
                        ["enabled"] = scene.enabled,
                        ["guid"] = scene.guid.ToString()
                    });
                }
                buildSettings["scenes"] = sceneList;

                // Platform-specific settings
                var platformSettings = new Dictionary<string, object>();

                // Android settings
                if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
                {
                    platformSettings["android"] = new Dictionary<string, object>
                    {
                        ["build_system"] = EditorUserBuildSettings.androidBuildSystem.ToString(),
                        ["export_project"] = EditorUserBuildSettings.exportAsGoogleAndroidProject,
                        ["build_app_bundle"] = EditorUserBuildSettings.buildAppBundle
                    };
                }

                // iOS settings
                if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS)
                {
                    platformSettings["ios"] = new Dictionary<string, object>
                    {
                        ["build_number"] = PlayerSettings.iOS.buildNumber
                    };
                }

                // WebGL settings
                if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL)
                {
                    platformSettings["webgl"] = new Dictionary<string, object>
                    {
                        // WebGL specific settings would go here
                    };
                }

                buildSettings["platform_settings"] = platformSettings;

                return JsonConvert.SerializeObject(buildSettings, Formatting.Indented);
            }
            catch (Exception e)
            {
                return $"Error getting build settings: {e.Message}";
            }
        }

        #endregion

        #region Player Settings

        /// <summary>
        /// Get detailed information for Player Settings
        /// </summary>
        public static string GetPlayerSettings()
        {
            try
            {
                var playerSettings = new Dictionary<string, object>();

                // Basic information
                playerSettings["company_name"] = PlayerSettings.companyName;
                playerSettings["product_name"] = PlayerSettings.productName;
                playerSettings["version"] = PlayerSettings.bundleVersion;
                playerSettings["bundle_identifier"] = PlayerSettings.GetApplicationIdentifier(EditorUserBuildSettings.selectedBuildTargetGroup);

                // Icon & Splash
                // Default icon handling varies by platform
                playerSettings["has_default_icon"] = true;
                playerSettings["use_animated_autorotation"] = PlayerSettings.useAnimatedAutorotation;

                // Resolution & Display
                var resolutionSettings = new Dictionary<string, object>
                {
                    ["default_is_fullscreen"] = PlayerSettings.defaultIsNativeResolution,
                    ["default_screen_width"] = PlayerSettings.defaultScreenWidth,
                    ["default_screen_height"] = PlayerSettings.defaultScreenHeight,
                    ["run_in_background"] = PlayerSettings.runInBackground,
                    ["capture_single_screen"] = PlayerSettings.captureSingleScreen,
                    // Display resolution dialog removed in newer Unity versions
                    ["use_player_log"] = PlayerSettings.usePlayerLog,
                    ["resize_with_window"] = PlayerSettings.resizableWindow,
                    ["visible_in_background"] = PlayerSettings.visibleInBackground
                };
                playerSettings["resolution_presentation"] = resolutionSettings;

                // Splash Screen
                var splashSettings = new Dictionary<string, object>
                {
                    ["show_unity_logo"] = PlayerSettings.SplashScreen.showUnityLogo,
                    ["animation_mode"] = PlayerSettings.SplashScreen.animationMode.ToString(),
                    ["background_color"] = ColorToHex(PlayerSettings.SplashScreen.backgroundColor),
                    ["logo_style"] = PlayerSettings.SplashScreen.unityLogoStyle.ToString()
                };
                playerSettings["splash_screen"] = splashSettings;

                // XR Settings
                var xrSettings = new Dictionary<string, object>
                {
                    // VR support moved to XR Management package
                };
                playerSettings["xr_settings"] = xrSettings;

                // Publishing Settings
                var publishingSettings = new Dictionary<string, object>
                {
                    ["use_mac_app_store_validation"] = PlayerSettings.useMacAppStoreValidation,
                    // Mac App Store category setting
                };
                playerSettings["publishing_settings"] = publishingSettings;

                // Configuration
                var configurationSettings = new Dictionary<string, object>
                {
                    ["scripting_backend"] = PlayerSettings.GetScriptingBackend(EditorUserBuildSettings.selectedBuildTargetGroup).ToString(),
                    ["api_compatibility_level"] = PlayerSettings.GetApiCompatibilityLevel(EditorUserBuildSettings.selectedBuildTargetGroup).ToString(),
                    // Input handling setting varies by Unity version
                    ["il2cpp_compiler_configuration"] = PlayerSettings.GetIl2CppCompilerConfiguration(EditorUserBuildSettings.selectedBuildTargetGroup).ToString()
                };
                playerSettings["configuration"] = configurationSettings;

                // Platform-specific settings
                var platformSpecific = GetPlatformSpecificPlayerSettings();
                playerSettings["platform_specific"] = platformSpecific;

                return JsonConvert.SerializeObject(playerSettings, Formatting.Indented);
            }
            catch (Exception e)
            {
                return $"Error getting player settings: {e.Message}";
            }
        }

        private static Dictionary<string, object> GetPlatformSpecificPlayerSettings()
        {
            var platformSettings = new Dictionary<string, object>();

            // Android-specific settings
            var androidSettings = new Dictionary<string, object>
            {
                ["bundle_version_code"] = PlayerSettings.Android.bundleVersionCode,
                ["min_sdk_version"] = PlayerSettings.Android.minSdkVersion.ToString(),
                ["target_sdk_version"] = PlayerSettings.Android.targetSdkVersion.ToString(),
                ["preferred_install_location"] = PlayerSettings.Android.preferredInstallLocation.ToString(),
                ["force_internet_permission"] = PlayerSettings.Android.forceInternetPermission,
                ["force_sd_card_permission"] = PlayerSettings.Android.forceSDCardPermission,
                ["keystore_name"] = PlayerSettings.Android.keystoreName,
                ["keystore_pass"] = "[PROTECTED]",
                ["keyalias_name"] = PlayerSettings.Android.keyaliasName,
                ["use_custom_keystore"] = PlayerSettings.Android.useCustomKeystore
            };
            platformSettings["android"] = androidSettings;

            // iOS-specific settings
            var iosSettings = new Dictionary<string, object>
            {
                ["build_number"] = PlayerSettings.iOS.buildNumber,
                ["target_os_version"] = PlayerSettings.iOS.targetOSVersionString,
                ["camera_usage_description"] = PlayerSettings.iOS.cameraUsageDescription,
                ["location_usage_description"] = PlayerSettings.iOS.locationUsageDescription,
                ["microphone_usage_description"] = PlayerSettings.iOS.microphoneUsageDescription,
                ["requires_persistent_wifi"] = PlayerSettings.iOS.requiresPersistentWiFi,
                // Exit on suspend deprecated, use appInBackgroundBehavior instead
                ["app_in_background_behavior"] = PlayerSettings.iOS.appInBackgroundBehavior.ToString()
            };
            platformSettings["ios"] = iosSettings;

            return platformSettings;
        }

        #endregion

        #region Quality Settings

        /// <summary>
        /// Get detailed information for Quality Settings
        /// </summary>
        public static string GetQualitySettings()
        {
            try
            {
                var qualitySettings = new Dictionary<string, object>();

                // Current quality level
                qualitySettings["current_level"] = QualitySettings.GetQualityLevel();
                qualitySettings["current_level_name"] = QualitySettings.names[QualitySettings.GetQualityLevel()];

                // All quality levels
                var levels = new List<Dictionary<string, object>>();
                string[] names = QualitySettings.names;

                for (int i = 0; i < names.Length; i++)
                {
                    // Temporarily switch level to retrieve settings
                    int currentLevel = QualitySettings.GetQualityLevel();
                    QualitySettings.SetQualityLevel(i, false);

                    var levelSettings = new Dictionary<string, object>
                    {
                        ["index"] = i,
                        ["name"] = names[i],
                        ["pixel_light_count"] = QualitySettings.pixelLightCount,
#if UNITY_2022_2_OR_NEWER
                        ["texture_quality"] = QualitySettings.globalTextureMipmapLimit,
#else
                        ["texture_quality"] = QualitySettings.masterTextureLimit,
#endif
                        ["anisotropic_textures"] = QualitySettings.anisotropicFiltering.ToString(),
                        ["anti_aliasing"] = QualitySettings.antiAliasing,
                        ["soft_particles"] = QualitySettings.softParticles,
                        ["realtime_reflection_probes"] = QualitySettings.realtimeReflectionProbes,
                        ["billboard_face_camera_position"] = QualitySettings.billboardsFaceCameraPosition,
                        ["resolution_scaling_fixed_dpi_factor"] = QualitySettings.resolutionScalingFixedDPIFactor,
                        ["texture_streaming_enabled"] = QualitySettings.streamingMipmapsActive,
                        ["texture_streaming_memory_budget"] = QualitySettings.streamingMipmapsMemoryBudget,
                        ["maximum_lod_bias"] = QualitySettings.maximumLODLevel,
                        ["particle_raycast_budget"] = QualitySettings.particleRaycastBudget,
                        ["async_upload_time_slice"] = QualitySettings.asyncUploadTimeSlice,
                        ["async_upload_buffer_size"] = QualitySettings.asyncUploadBufferSize,
                        ["async_upload_persistent_buffer"] = QualitySettings.asyncUploadPersistentBuffer,
                        ["realtime_gi_cpu_usage"] = QualitySettings.realtimeGICPUUsage.ToString(),
                        ["skinned_mesh_max_bone_count"] = QualitySettings.skinWeights.ToString()
                    };

                    levels.Add(levelSettings);

                    // Restore original level
                    QualitySettings.SetQualityLevel(currentLevel, false);
                }

                qualitySettings["levels"] = levels;

                // Detail settings
                var detailSettings = new Dictionary<string, object>
                {
                    ["blend_weights"] = QualitySettings.skinWeights.ToString(),
                    ["vsync_count"] = QualitySettings.vSyncCount,
                    ["lod_bias"] = QualitySettings.lodBias,
                    ["maximum_lod_level"] = QualitySettings.maximumLODLevel,
                    ["particle_raycast_budget"] = QualitySettings.particleRaycastBudget,
                    ["soft_vegetation"] = QualitySettings.softVegetation
                };
                qualitySettings["detail_settings"] = detailSettings;

                return JsonConvert.SerializeObject(qualitySettings, Formatting.Indented);
            }
            catch (Exception e)
            {
                return $"Error getting quality settings: {e.Message}";
            }
        }

        #endregion

        #region Input Settings

        /// <summary>
        /// Get detailed information for Input Settings
        /// </summary>
        public static string GetInputSettings()
        {
            try
            {
                var inputSettings = new Dictionary<string, object>();

                // Input system settings
                // Input handling setting varies by Unity version
                inputSettings["input_system_available"] = true;

                // Legacy Input Manager settings
                var axes = new List<Dictionary<string, object>>();

                // Get Axes settings from Input Manager (using Reflection)
                var inputManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset");
                if (inputManagerAssets == null || inputManagerAssets.Length == 0)
                {
                    SynLog.Warn("[GetInputSettings] InputManager asset not found");
                    inputSettings["input_axes"] = new List<Dictionary<string, object>>();
                }
                else
                {
                    var inputManagerAsset = inputManagerAssets[0];
                    if (inputManagerAsset == null)
                    {
                        SynLog.Warn("[GetInputSettings] InputManager asset is null");
                        inputSettings["input_axes"] = new List<Dictionary<string, object>>();
                    }
                    else
                    {
                        var serializedObject = new SerializedObject(inputManagerAsset);
                        var axesProperty = serializedObject.FindProperty("m_Axes");
                        
                        if (axesProperty == null)
                        {
                            SynLog.Warn("[GetInputSettings] m_Axes property not found");
                            inputSettings["input_axes"] = new List<Dictionary<string, object>>();
                        }
                        else
                        {

                            for (int i = 0; i < axesProperty.arraySize; i++)
                            {
                                try
                                {
                                    var axis = axesProperty.GetArrayElementAtIndex(i);
                                    if (axis != null)
                                    {
                                        var axisData = new Dictionary<string, object>
                                        {
                                            ["name"] = axis.FindPropertyRelative("m_Name")?.stringValue ?? "",
                                            ["descriptive_name"] = axis.FindPropertyRelative("m_DescriptiveName")?.stringValue ?? "",
                                            ["descriptive_negative_name"] = axis.FindPropertyRelative("m_DescriptiveNegativeName")?.stringValue ?? "",
                                            ["negative_button"] = axis.FindPropertyRelative("m_NegativeButton")?.stringValue ?? "",
                                            ["positive_button"] = axis.FindPropertyRelative("m_PositiveButton")?.stringValue ?? "",
                                            ["alt_negative_button"] = axis.FindPropertyRelative("m_AltNegativeButton")?.stringValue ?? "",
                                            ["alt_positive_button"] = axis.FindPropertyRelative("m_AltPositiveButton")?.stringValue ?? "",
                                            ["gravity"] = axis.FindPropertyRelative("m_Gravity")?.floatValue ?? 0f,
                                            ["dead"] = axis.FindPropertyRelative("m_Dead")?.floatValue ?? 0f,
                                            ["sensitivity"] = axis.FindPropertyRelative("m_Sensitivity")?.floatValue ?? 1f,
                                            ["snap"] = axis.FindPropertyRelative("m_Snap")?.boolValue ?? false,
                                            ["invert"] = axis.FindPropertyRelative("m_Invert")?.boolValue ?? false,
                                            ["type"] = axis.FindPropertyRelative("m_Type")?.intValue ?? 0,
                                            ["axis"] = axis.FindPropertyRelative("m_Axis")?.intValue ?? 0,
                                            ["joy_num"] = axis.FindPropertyRelative("m_JoyNum")?.intValue ?? 0
                                        };
                                        axes.Add(axisData);
                                    }
                                }
                                catch (Exception axisEx)
                                {
                                    SynLog.Warn($"[GetInputSettings] Failed to read axis {i}: {axisEx.Message}");
                                }
                            }
                            
                            inputSettings["input_axes"] = axes;
                        }
                    }
                }

                // New Input System information (if available)
#if UNITY_INPUT_SYSTEM_MODULE_ENABLED
                try
                {
                    inputSettings["new_input_system"] = new Dictionary<string, object>
                    {
                        ["enabled"] = true,
                        ["version"] = UnityEngine.InputSystem.InputSystem.version
                    };
                }
                catch
                {
                    inputSettings["new_input_system"] = new Dictionary<string, object>
                    {
                        ["enabled"] = false,
                        ["error"] = "Input System package not available"
                    };
                }
#else
                inputSettings["new_input_system"] = new Dictionary<string, object>
                {
                    ["enabled"] = false,
                    ["note"] = "Input System module not enabled"
                };
#endif

                return JsonConvert.SerializeObject(inputSettings, Formatting.Indented);
            }
            catch (Exception e)
            {
                return $"Error getting input settings: {e.Message}";
            }
        }

        #endregion

        #region Physics Settings

        /// <summary>
        /// Get detailed information for Physics Settings
        /// </summary>
        public static string GetPhysicsSettings()
        {
            try
            {
                var physicsSettings = new Dictionary<string, object>();

                // Basic physics settings
                physicsSettings["gravity"] = new Dictionary<string, object>
                {
                    ["x"] = Physics.gravity.x,
                    ["y"] = Physics.gravity.y,
                    ["z"] = Physics.gravity.z
                };

                // Default physics material varies by Unity version
                physicsSettings["has_default_material"] = true;

                physicsSettings["bounce_threshold"] = Physics.bounceThreshold;
                physicsSettings["sleep_threshold"] = Physics.sleepThreshold;
                physicsSettings["default_contact_offset"] = Physics.defaultContactOffset;
                physicsSettings["default_solver_iterations"] = Physics.defaultSolverIterations;
                physicsSettings["default_solver_velocity_iterations"] = Physics.defaultSolverVelocityIterations;

                // Query settings
                physicsSettings["queries_hit_backfaces"] = Physics.queriesHitBackfaces;
                physicsSettings["queries_hit_triggers"] = Physics.queriesHitTriggers;
                physicsSettings["auto_sync_transforms"] = Physics.autoSyncTransforms;
                physicsSettings["reuse_collision_callbacks"] = Physics.reuseCollisionCallbacks;

                // Layer collision matrix
                var layerCollisionMatrix = new Dictionary<string, object>();
                for (int i = 0; i < 32; i++)
                {
                    var layerName = LayerMask.LayerToName(i);
                    if (!string.IsNullOrEmpty(layerName))
                    {
                        var collisions = new List<string>();
                        for (int j = 0; j < 32; j++)
                        {
                            if (!Physics.GetIgnoreLayerCollision(i, j))
                            {
                                var otherLayerName = LayerMask.LayerToName(j);
                                if (!string.IsNullOrEmpty(otherLayerName))
                                {
                                    collisions.Add(otherLayerName);
                                }
                            }
                        }
                        layerCollisionMatrix[layerName] = collisions;
                    }
                }
                physicsSettings["layer_collision_matrix"] = layerCollisionMatrix;

                // 2D Physics settings
                var physics2DSettings = new Dictionary<string, object>
                {
                    ["gravity"] = new Dictionary<string, object>
                    {
                        ["x"] = Physics2D.gravity.x,
                        ["y"] = Physics2D.gravity.y
                    },
                    // Default physics material varies by Unity version
                    ["has_default_material"] = true,
                    ["velocity_iterations"] = Physics2D.velocityIterations,
                    ["position_iterations"] = Physics2D.positionIterations,
                    ["velocity_threshold"] = Physics2D.bounceThreshold,
                    ["max_linear_correction"] = Physics2D.maxLinearCorrection,
                    ["max_angular_correction"] = Physics2D.maxAngularCorrection,
                    ["max_translation_speed"] = Physics2D.maxTranslationSpeed,
                    ["max_rotation_speed"] = Physics2D.maxRotationSpeed,
                    ["baumgarte_scale"] = Physics2D.baumgarteScale,
                    ["baumgarte_time_of_impact_scale"] = Physics2D.baumgarteTOIScale,
                    ["time_to_sleep"] = Physics2D.timeToSleep,
                    ["linear_sleep_tolerance"] = Physics2D.linearSleepTolerance,
                    ["angular_sleep_tolerance"] = Physics2D.angularSleepTolerance,
                    ["auto_sync_transforms"] = Physics2D.autoSyncTransforms,
                    ["reuse_collision_callbacks"] = Physics2D.reuseCollisionCallbacks,
                    // Auto simulation deprecated, use simulationMode instead
                    ["queries_hit_triggers"] = Physics2D.queriesHitTriggers,
                    ["queries_start_in_colliders"] = Physics2D.queriesStartInColliders,
                    ["callbacks_on_disable"] = Physics2D.callbacksOnDisable
                };
                physicsSettings["physics_2d"] = physics2DSettings;

                return JsonConvert.SerializeObject(physicsSettings, Formatting.Indented);
            }
            catch (Exception e)
            {
                return $"Error getting physics settings: {e.Message}";
            }
        }

        #endregion

        #region Utility Methods

        private static string ColorToHex(Color color)
        {
            return $"#{ColorUtility.ToHtmlStringRGBA(color)}";
        }

        /// <summary>
        /// Get summary of all project settings
        /// </summary>
        public static string GetProjectSettingsSummary()
        {
            try
            {
                var summary = new Dictionary<string, object>
                {
                    ["project_info"] = new Dictionary<string, object>
                    {
                        ["product_name"] = PlayerSettings.productName,
                        ["bundle_version"] = PlayerSettings.bundleVersion,
                        ["company_name"] = PlayerSettings.companyName,
                        ["bundle_id"] = PlayerSettings.GetApplicationIdentifier(EditorUserBuildSettings.selectedBuildTargetGroup)
                    },
                    ["build_settings"] = new Dictionary<string, object>
                    {
                        ["target_platform"] = EditorUserBuildSettings.activeBuildTarget.ToString(),
                        ["development_build"] = EditorUserBuildSettings.development,
                        ["scripting_backend"] = PlayerSettings.GetScriptingBackend(EditorUserBuildSettings.selectedBuildTargetGroup).ToString()
                    },
                    ["quality_settings"] = new Dictionary<string, object>
                    {
                        ["quality_level"] = QualitySettings.names[QualitySettings.GetQualityLevel()],
                        ["vsync_count"] = QualitySettings.vSyncCount,
                        ["anti_aliasing"] = QualitySettings.antiAliasing,
                        ["aniso_filtering"] = QualitySettings.anisotropicFiltering.ToString()
                    },
                    ["physics_settings"] = new Dictionary<string, object>
                    {
                        ["gravity_3d"] = new Dictionary<string, float>
                        {
                            ["x"] = Physics.gravity.x,
                            ["y"] = Physics.gravity.y,
                            ["z"] = Physics.gravity.z
                        },
                        ["gravity_2d"] = new Dictionary<string, float>
                        {
                            ["x"] = Physics2D.gravity.x,
                            ["y"] = Physics2D.gravity.y
                        }
                    },
                    ["statistics"] = GetProjectStatistics()
                };

                return JsonConvert.SerializeObject(summary, Formatting.Indented);
            }
            catch (Exception e)
            {
                return $"Error getting project settings summary: {e.Message}";
            }
        }
        
        /// <summary>
        /// Get project statistics information
        /// </summary>
        private static Dictionary<string, object> GetProjectStatistics()
        {
            var stats = new Dictionary<string, object>();
            
            try
            {
                // Number of GameObjects (active scene only)
                var rootObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
                int totalGameObjects = 0;
                foreach (var root in rootObjects)
                {
                    totalGameObjects += CountGameObjectsRecursive(root.transform);
                }
                
                // Asset statistics
                string[] allAssets = AssetDatabase.GetAllAssetPaths();
                var assetTypes = new Dictionary<string, int>();
                
                foreach (string assetPath in allAssets)
                {
                    if (assetPath.StartsWith("Assets/"))
                    {
                        string extension = System.IO.Path.GetExtension(assetPath).ToLower();
                        if (!string.IsNullOrEmpty(extension))
                        {
                            if (assetTypes.ContainsKey(extension))
                                assetTypes[extension]++;
                            else
                                assetTypes[extension] = 1;
                        }
                    }
                }
                
                stats["gameobject_count"] = totalGameObjects;
                stats["script_count"] = assetTypes.ContainsKey(".cs") ? assetTypes[".cs"] : 0;
                stats["total_assets"] = allAssets.Where(p => p.StartsWith("Assets/")).Count();
                stats["asset_types"] = assetTypes;
                stats["memory_usage"] = new Dictionary<string, object>
                {
                    ["allocated_memory"] = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong(),
                    ["reserved_memory"] = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong(),
                    ["mono_heap_size"] = UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong(),
                    ["mono_used_size"] = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong()
                };
            }
            catch (Exception e)
            {
                stats["error"] = e.Message;
            }
            
            return stats;
        }
        
        /// <summary>
        /// Count GameObjects recursively
        /// </summary>
        private static int CountGameObjectsRecursive(Transform transform)
        {
            int count = 1; // Self
            for (int i = 0; i < transform.childCount; i++)
            {
                count += CountGameObjectsRecursive(transform.GetChild(i));
            }
            return count;
        }

        #endregion
    }
}
