using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Reflection;
using System.Linq;

namespace SynapticPro
{
    /// <summary>
    /// Real-time Execution State Monitoring Class
    /// Monitors Unity Editor execution state, performance, and memory usage
    /// </summary>
    public static class NexusRuntimeMonitor
    {
        private static float lastFPS = 0f;
        private static long lastGCMemory = 0L;
        private static DateTime lastUpdateTime = DateTime.Now;
        
        /// <summary>
        /// Get Unity execution state information
        /// </summary>
        public static string GetRuntimeStatus(Dictionary<string, string> parameters)
        {
            try
            {
                var includePerformance = parameters.GetValueOrDefault("includePerformance", "true") == "true";
                var includeMemory = parameters.GetValueOrDefault("includeMemory", "true") == "true";
                var includeErrors = parameters.GetValueOrDefault("includeErrors", "true") == "true";
                
                var status = new Dictionary<string, object>
                {
                    ["playMode"] = Application.isPlaying,
                    ["isEditor"] = Application.isEditor,
                    ["isPaused"] = GetEditorPausedState(),
                    ["isCompiling"] = GetEditorCompilingState(),
                    ["platform"] = Application.platform.ToString(),
                    ["unityVersion"] = Application.unityVersion,
                    ["timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };
                
                if (includePerformance)
                {
                    status["performance"] = GetPerformanceData();
                }
                
                if (includeMemory)
                {
                    status["memory"] = GetMemoryData();
                }
                
                if (includeErrors)
                {
                    status["errors"] = GetErrorStatus();
                }
                
                return FormatStatusReport(status);
            }
            catch (Exception e)
            {
                return $"Error getting runtime status: {e.Message}";
            }
        }
        
        /// <summary>
        /// Get performance metrics
        /// </summary>
        public static string GetPerformanceMetrics(Dictionary<string, string> parameters)
        {
            try
            {
                var duration = float.Parse(parameters.GetValueOrDefault("duration", "5"));
                var includeFrameTime = parameters.GetValueOrDefault("includeFrameTime", "true") == "true";
                var includeGPUUsage = parameters.GetValueOrDefault("includeGPUUsage", "true") == "true";
                var includeBatches = parameters.GetValueOrDefault("includeBatches", "true") == "true";
                
                var metrics = new Dictionary<string, object>();
                
                // FPS calculation
                if (Application.isPlaying)
                {
                    var currentFPS = 1.0f / Time.unscaledDeltaTime;
                    lastFPS = currentFPS;
                    metrics["fps"] = Math.Round(currentFPS, 1);
                    metrics["deltaTime"] = Math.Round(Time.unscaledDeltaTime * 1000, 2); // ms
                }
                else
                {
                    metrics["fps"] = "Editor Mode";
                    metrics["deltaTime"] = "N/A";
                }
                
                if (includeFrameTime)
                {
                    metrics["frameTime"] = Application.isPlaying ? 
                        Math.Round(Time.unscaledDeltaTime * 1000, 2) : 0;
                    metrics["timeScale"] = Time.timeScale;
                    metrics["fixedDeltaTime"] = Time.fixedDeltaTime;
                }
                
                if (includeGPUUsage)
                {
                    // Use Profiler API (Editor only)
                    if (Application.isEditor)
                    {
                        metrics["gpuMemory"] = SystemInfo.graphicsMemorySize + " MB";
                        metrics["graphicsDevice"] = SystemInfo.graphicsDeviceName;
                        metrics["graphicsAPI"] = SystemInfo.graphicsDeviceType.ToString();
                    }
                }
                
                if (includeBatches)
                {
                    // Rendering statistics (game runtime only)
                    if (Application.isPlaying)
                    {
                        metrics["triangles"] = "Available in Game View";
                        metrics["batches"] = "Available in Game View";
                    }
                    else
                    {
                        metrics["triangles"] = "N/A (Editor Mode)";
                        metrics["batches"] = "N/A (Editor Mode)";
                    }
                }
                
                // System information
                metrics["systemMemory"] = SystemInfo.systemMemorySize + " MB";
                metrics["processorCount"] = SystemInfo.processorCount;
                metrics["processorType"] = SystemInfo.processorType;
                
                return FormatPerformanceReport(metrics);
            }
            catch (Exception e)
            {
                return $"Error getting performance metrics: {e.Message}";
            }
        }
        
        /// <summary>
        /// Get detailed memory usage
        /// </summary>
        public static string GetMemoryUsage(Dictionary<string, string> parameters)
        {
            try
            {
                var includeBreakdown = parameters.GetValueOrDefault("includeBreakdown", "true") == "true";
                var includeGC = parameters.GetValueOrDefault("includeGC", "true") == "true";
                var includeProfiler = parameters.GetValueOrDefault("includeProfiler", "false") == "true";
                
                var memory = new Dictionary<string, object>();
                
                // Basic memory information
                var gcMemory = GC.GetTotalMemory(false);
                memory["gcMemory"] = FormatBytes(gcMemory);
                memory["gcGeneration0"] = GC.CollectionCount(0);
                memory["gcGeneration1"] = GC.CollectionCount(1);
                memory["gcGeneration2"] = GC.CollectionCount(2);
                
                if (includeGC)
                {
                    var memoryDiff = gcMemory - lastGCMemory;
                    memory["memoryDelta"] = FormatBytes(memoryDiff);
                    lastGCMemory = gcMemory;
                }
                
                // Unity-specific memory information
                if (Application.isEditor)
                {
                    memory["unityReserved"] = "Available via Profiler";
                    memory["gfxDriver"] = SystemInfo.graphicsMemorySize + " MB";
                }
                
                if (includeBreakdown)
                {
                    // Memory usage by asset type (estimated)
                    var textures = Resources.FindObjectsOfTypeAll<Texture>();
                    var meshes = Resources.FindObjectsOfTypeAll<Mesh>();
                    var audioClips = Resources.FindObjectsOfTypeAll<AudioClip>();
                    
                    memory["textureCount"] = textures.Length;
                    memory["meshCount"] = meshes.Length;
                    memory["audioClipCount"] = audioClips.Length;
                    
                    // Estimated memory size
                    long textureMemory = 0;
                    foreach (var tex in textures)
                    {
                        if (tex != null)
                        {
                            textureMemory += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex);
                        }
                    }
                    memory["textureMemory"] = FormatBytes(textureMemory);
                }
                
                if (includeProfiler && Application.isPlaying)
                {
                    memory["profilerMemory"] = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
                    memory["profilerReserved"] = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong();
                }
                
                return FormatMemoryReport(memory);
            }
            catch (Exception e)
            {
                return $"Error getting memory usage: {e.Message}";
            }
        }
        
        /// <summary>
        /// Get current error status
        /// </summary>
        public static string GetErrorStatus(Dictionary<string, string> parameters)
        {
            try
            {
                var includeWarnings = parameters.GetValueOrDefault("includeWarnings", "true") == "true";
                var includeStackTrace = parameters.GetValueOrDefault("includeStackTrace", "false") == "true";
                var maxErrors = int.Parse(parameters.GetValueOrDefault("maxErrors", "20"));
                
                var isCompiling = GetEditorCompilingState();
                var hasCompileErrors = GetEditorCompileErrorState();
                
                var errors = new Dictionary<string, object>
                {
                    ["isCompiling"] = isCompiling,
                    ["hasCompileErrors"] = hasCompileErrors,
                    ["timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };
                
                // Console log retrieval is limited, so only basic information
                errors["status"] = isCompiling ? "Compiling..." : 
                                 hasCompileErrors ? "Compile Errors" : "No Errors";
                
                return FormatErrorReport(errors);
            }
            catch (Exception e)
            {
                return $"Error getting error status: {e.Message}";
            }
        }
        
        /// <summary>
        /// Start monitoring Play state changes
        /// </summary>
        public static string MonitorPlayState(Dictionary<string, string> parameters)
        {
            try
            {
                var enableNotifications = parameters.GetValueOrDefault("enableNotifications", "true") == "true";
                var includeTimestamp = parameters.GetValueOrDefault("includeTimestamp", "true") == "true";
                
                if (enableNotifications)
                {
#if UNITY_EDITOR
                    // Set up EditorApplication event handlers
                    EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
                }
                
                var currentState = new Dictionary<string, object>
                {
                    ["isPlaying"] = Application.isPlaying,
#if UNITY_EDITOR
                    ["isPaused"] = EditorApplication.isPaused,
                    ["isCompiling"] = EditorApplication.isCompiling,
#else
                    ["isPaused"] = false,
                    ["isCompiling"] = false,
#endif
                    ["monitoringEnabled"] = enableNotifications
                };
                
                if (includeTimestamp)
                {
                    currentState["timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                }
                
                return FormatPlayStateReport(currentState);
            }
            catch (Exception e)
            {
                return $"Error setting up play state monitoring: {e.Message}";
            }
        }
        
        /// <summary>
        /// Get build status
        /// </summary>
        public static string GetBuildStatus(Dictionary<string, string> parameters)
        {
            try
            {
                var includeSettings = parameters.GetValueOrDefault("includeSettings", "true") == "true";
                var includeErrors = parameters.GetValueOrDefault("includeErrors", "true") == "true";
                
                var buildInfo = new Dictionary<string, object>();
                
                if (includeSettings)
                {
#if UNITY_EDITOR
                    buildInfo["targetPlatform"] = EditorUserBuildSettings.activeBuildTarget.ToString();
                    buildInfo["developmentBuild"] = EditorUserBuildSettings.development;
                    buildInfo["scriptDebugging"] = EditorUserBuildSettings.allowDebugging;
                    buildInfo["buildAppBundle"] = EditorUserBuildSettings.buildAppBundle;
#else
                    buildInfo["targetPlatform"] = Application.platform.ToString();
                    buildInfo["developmentBuild"] = UnityEngine.Debug.isDebugBuild;
                    buildInfo["scriptDebugging"] = false;
                    buildInfo["buildAppBundle"] = false;
#endif
                }
                
#if UNITY_EDITOR
                // Scene information
                var scenes = EditorBuildSettings.scenes;
                buildInfo["sceneCount"] = scenes.Length;
                buildInfo["enabledScenes"] = scenes.Count(s => s.enabled);
#else
                buildInfo["sceneCount"] = SceneManager.sceneCountInBuildSettings;
                buildInfo["enabledScenes"] = SceneManager.sceneCountInBuildSettings;
#endif
                
                if (includeErrors)
                {
#if UNITY_EDITOR
                    buildInfo["canBuild"] = !EditorApplication.isCompiling && !EditorUtility.scriptCompilationFailed;
                    buildInfo["compilationStatus"] = EditorApplication.isCompiling ? "Compiling" : 
                                                   EditorUtility.scriptCompilationFailed ? "Error" : "Ready";
#else
                    buildInfo["canBuild"] = true;
                    buildInfo["compilationStatus"] = "Ready";
#endif
                }
                
                return FormatBuildReport(buildInfo);
            }
            catch (Exception e)
            {
                return $"Error getting build status: {e.Message}";
            }
        }
        
        // ===== Helper Methods =====
        
#if UNITY_EDITOR
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            var message = $"Play Mode State Changed: {state} at {DateTime.Now:HH:mm:ss}";
            UnityEngine.Debug.Log($"[Nexus Monitor] {message}");
            
            // Real-time notification via WebSocket is also possible
            // NexusWebSocketClient.Instance?.SendMessage(new { type = "play_state_changed", state = state.ToString() });
        }
#endif
        
        private static Dictionary<string, object> GetPerformanceData()
        {
            return new Dictionary<string, object>
            {
                ["fps"] = Application.isPlaying ? Math.Round(1.0f / Time.unscaledDeltaTime, 1) : 0,
                ["frameTime"] = Application.isPlaying ? Math.Round(Time.unscaledDeltaTime * 1000, 2) : 0,
                ["timeScale"] = Time.timeScale
            };
        }
        
        private static Dictionary<string, object> GetMemoryData()
        {
            var gcMemory = GC.GetTotalMemory(false);
            return new Dictionary<string, object>
            {
                ["gcMemory"] = FormatBytes(gcMemory),
                ["systemMemory"] = SystemInfo.systemMemorySize + " MB",
                ["graphicsMemory"] = SystemInfo.graphicsMemorySize + " MB"
            };
        }
        
        private static Dictionary<string, object> GetErrorStatus()
        {
            var isCompiling = GetEditorCompilingState();
            var hasErrors = GetEditorCompileErrorState();
            
            return new Dictionary<string, object>
            {
                ["isCompiling"] = isCompiling,
                ["hasErrors"] = hasErrors,
                ["status"] = isCompiling ? "Compiling" : 
                           hasErrors ? "Errors" : "OK"
            };
        }
        
        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int counter = 0;
            decimal number = (decimal)bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number = number / 1024;
                counter++;
            }
            return string.Format("{0:n1} {1}", number, suffixes[counter]);
        }
        
        private static string FormatStatusReport(Dictionary<string, object> status)
        {
            var report = "=== Unity Runtime Status ===\n";
            report += $"Play Mode: {status["playMode"]}\n";
            report += $"Paused: {status["isPaused"]}\n";
            report += $"Compiling: {status["isCompiling"]}\n";
            report += $"Platform: {status["platform"]}\n";
            report += $"Unity Version: {status["unityVersion"]}\n";
            report += $"Timestamp: {status["timestamp"]}\n";
            
            if (status.ContainsKey("performance"))
            {
                var perf = (Dictionary<string, object>)status["performance"];
                report += $"\n--- Performance ---\n";
                report += $"FPS: {perf["fps"]}\n";
                report += $"Frame Time: {perf["frameTime"]} ms\n";
                report += $"Time Scale: {perf["timeScale"]}\n";
            }
            
            if (status.ContainsKey("memory"))
            {
                var mem = (Dictionary<string, object>)status["memory"];
                report += $"\n--- Memory ---\n";
                report += $"GC Memory: {mem["gcMemory"]}\n";
                report += $"System Memory: {mem["systemMemory"]}\n";
                report += $"Graphics Memory: {mem["graphicsMemory"]}\n";
            }
            
            if (status.ContainsKey("errors"))
            {
                var err = (Dictionary<string, object>)status["errors"];
                report += $"\n--- Status ---\n";
                report += $"Compilation: {err["status"]}\n";
            }
            
            return report;
        }
        
        private static string FormatPerformanceReport(Dictionary<string, object> metrics)
        {
            var report = "=== Performance Metrics ===\n";
            
            foreach (var kvp in metrics)
            {
                report += $"{kvp.Key}: {kvp.Value}\n";
            }
            
            return report;
        }
        
        private static string FormatMemoryReport(Dictionary<string, object> memory)
        {
            var report = "=== Memory Usage ===\n";
            
            foreach (var kvp in memory)
            {
                report += $"{kvp.Key}: {kvp.Value}\n";
            }
            
            return report;
        }
        
        private static string FormatErrorReport(Dictionary<string, object> errors)
        {
            var report = "=== Error Status ===\n";
            
            foreach (var kvp in errors)
            {
                report += $"{kvp.Key}: {kvp.Value}\n";
            }
            
            return report;
        }
        
        private static string FormatPlayStateReport(Dictionary<string, object> state)
        {
            var report = "=== Play State Monitoring ===\n";
            
            foreach (var kvp in state)
            {
                report += $"{kvp.Key}: {kvp.Value}\n";
            }
            
            return report;
        }
        
        private static string FormatBuildReport(Dictionary<string, object> buildInfo)
        {
            var report = "=== Build Status ===\n";
            
            foreach (var kvp in buildInfo)
            {
                report += $"{kvp.Key}: {kvp.Value}\n";
            }
            
            return report;
        }
        
        /// <summary>
        /// Safely access Editor-only APIs using Reflection
        /// </summary>
        private static bool GetEditorPausedState()
        {
            try
            {
                var editorAppType = Type.GetType("UnityEditor.EditorApplication, UnityEditor");
                if (editorAppType != null)
                {
                    var isPausedProperty = editorAppType.GetProperty("isPaused");
                    if (isPausedProperty != null)
                    {
                        return (bool)isPausedProperty.GetValue(null);
                    }
                }
            }
            catch
            {
                // Return false if Editor API is not available
            }
            return false;
        }
        
        private static bool GetEditorCompilingState()
        {
            try
            {
                var editorAppType = Type.GetType("UnityEditor.EditorApplication, UnityEditor");
                if (editorAppType != null)
                {
                    var isCompilingProperty = editorAppType.GetProperty("isCompiling");
                    if (isCompilingProperty != null)
                    {
                        return (bool)isCompilingProperty.GetValue(null);
                    }
                }
            }
            catch
            {
                // Return false if Editor API is not available
            }
            return false;
        }
        
        private static bool GetEditorCompileErrorState()
        {
            try
            {
                var editorUtilityType = Type.GetType("UnityEditor.EditorUtility, UnityEditor");
                if (editorUtilityType != null)
                {
                    var scriptCompilationFailedProperty = editorUtilityType.GetProperty("scriptCompilationFailed");
                    if (scriptCompilationFailedProperty != null)
                    {
                        return (bool)scriptCompilationFailedProperty.GetValue(null);
                    }
                }
            }
            catch
            {
                // Return false if Editor API is not available
            }
            return false;
        }
    }
}