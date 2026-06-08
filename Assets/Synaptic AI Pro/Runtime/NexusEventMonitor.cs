using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using System.IO;
using System.Linq;
#if UNITY_2019_1_OR_NEWER
using UnityEditor.Compilation;
#endif
#else
using System.IO;
using System.Linq;
#endif

namespace SynapticPro
{
    /// <summary>
    /// Unity Real-time Event Monitoring System
    /// Monitors Play state, file changes, and compilation state in real-time
    /// </summary>
#if UNITY_EDITOR
    public class NexusEventMonitor : EditorWindow
#else
    public class NexusEventMonitor
#endif
    {
        private static NexusEventMonitor instance;
        public static NexusEventMonitor Instance
        {
            get
            {
                if (instance == null)
                {
#if UNITY_EDITOR
                    instance = GetWindow<NexusEventMonitor>();
                    instance.titleContent = new GUIContent("Nexus Event Monitor");
#else
                    instance = new NexusEventMonitor();
#endif
                    instance.Initialize();
                }
                return instance;
            }
        }

        // Monitoring state
        private bool isMonitoringPlayState = false;
        private bool isMonitoringFileChanges = false;
        private bool isMonitoringCompile = false;

        // Previous state
#if UNITY_EDITOR
        private PlayModeStateChange lastPlayState;
#endif
        private Dictionary<string, DateTime> lastFileModificationTimes = new Dictionary<string, DateTime>();
        private List<string> monitoredFileExtensions = new List<string> { ".cs", ".js", ".ts", ".shader", ".hlsl" };
        
        // Event buffer
        private Queue<EventData> eventQueue = new Queue<EventData>();
        private const int maxEventQueueSize = 100;

        // Custom event subscriptions
        private Dictionary<string, HashSet<string>> eventSubscriptions = new Dictionary<string, HashSet<string>>();

        [Serializable]
        public class EventData
        {
            public string type;
            public string category;
            public DateTime timestamp;
            public Dictionary<string, object> data;
            public string description;
        }

        public static event Action<EventData> OnEventDetected;

        private void Initialize()
        {
#if UNITY_EDITOR
            // Subscribe to editor events
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            
#if UNITY_2019_1_OR_NEWER
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
#endif
#endif // UNITY_EDITOR

            // Initialize file watcher
            InitializeFileWatcher();
            
            Debug.Log("[Nexus Event Monitor] Initialized");
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            // Unsubscribe from events
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            
#if UNITY_2019_1_OR_NEWER
            CompilationPipeline.compilationStarted -= OnCompilationStarted;
            CompilationPipeline.compilationFinished -= OnCompilationFinished;
#endif
#endif // UNITY_EDITOR
        }

        #region Play State Monitoring

        /// <summary>
        /// Start/stop Play state monitoring
        /// </summary>
        public bool StartPlayStateMonitoring(bool enable = true)
        {
            isMonitoringPlayState = enable;
            
            if (enable)
            {
                BroadcastEvent(new EventData
                {
                    type = "play_state_monitoring",
                    category = "monitoring",
                    timestamp = DateTime.Now,
                    description = "Play state monitoring started",
                    data = new Dictionary<string, object>
                    {
#if UNITY_EDITOR
                        ["current_state"] = EditorApplication.isPlaying ? "playing" : "stopped",
                        ["is_paused"] = EditorApplication.isPaused
#else
                        ["current_state"] = Application.isPlaying ? "playing" : "stopped",
                        ["is_paused"] = false
#endif
                    }
                });
            }
            
            return true;
        }

#if UNITY_EDITOR
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!isMonitoringPlayState) return;

            var eventData = new EventData
            {
                type = "play_state_changed",
                category = "editor",
                timestamp = DateTime.Now,
                description = $"Play mode changed to {state}",
                data = new Dictionary<string, object>
                {
                    ["previous_state"] = lastPlayState.ToString(),
                    ["current_state"] = state.ToString(),
                    ["is_playing"] = EditorApplication.isPlaying,
                    ["is_paused"] = EditorApplication.isPaused,
                    ["frame_count"] = Time.frameCount
                }
            };

            BroadcastEvent(eventData);
            lastPlayState = state;
        }
#endif

        #endregion

        #region File Change Monitoring

        private FileSystemWatcher fileWatcher;

        private void InitializeFileWatcher()
        {
            try
            {
                string projectPath = Directory.GetParent(Application.dataPath).FullName;
                
                fileWatcher = new FileSystemWatcher(projectPath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName
                };

                fileWatcher.Changed += OnFileChanged;
                fileWatcher.Created += OnFileCreated;
                fileWatcher.Deleted += OnFileDeleted;
                fileWatcher.Renamed += OnFileRenamed;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Nexus Event Monitor] Failed to initialize file watcher: {e.Message}");
            }
        }

        /// <summary>
        /// Start/stop file change monitoring
        /// </summary>
        public bool StartFileChangeMonitoring(bool enable = true)
        {
            isMonitoringFileChanges = enable;
            
            if (fileWatcher != null)
            {
                fileWatcher.EnableRaisingEvents = enable;
            }
            
            if (enable)
            {
                BroadcastEvent(new EventData
                {
                    type = "file_monitoring",
                    category = "monitoring",
                    timestamp = DateTime.Now,
                    description = "File change monitoring started",
                    data = new Dictionary<string, object>
                    {
                        ["monitored_extensions"] = monitoredFileExtensions,
                        ["project_path"] = Directory.GetParent(Application.dataPath).FullName
                    }
                });
            }
            
            return true;
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            if (!ShouldMonitorFile(e.FullPath)) return;

            var eventData = new EventData
            {
                type = "file_changed",
                category = "filesystem",
                timestamp = DateTime.Now,
                description = $"File modified: {Path.GetFileName(e.FullPath)}",
                data = new Dictionary<string, object>
                {
                    ["file_path"] = e.FullPath,
                    ["file_name"] = Path.GetFileName(e.FullPath),
                    ["extension"] = Path.GetExtension(e.FullPath),
                    ["relative_path"] = GetRelativePath(e.FullPath)
                }
            };

            BroadcastEvent(eventData);
        }

        private void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            if (!ShouldMonitorFile(e.FullPath)) return;

            BroadcastEvent(new EventData
            {
                type = "file_created",
                category = "filesystem",
                timestamp = DateTime.Now,
                description = $"File created: {Path.GetFileName(e.FullPath)}",
                data = new Dictionary<string, object>
                {
                    ["file_path"] = e.FullPath,
                    ["file_name"] = Path.GetFileName(e.FullPath),
                    ["extension"] = Path.GetExtension(e.FullPath),
                    ["relative_path"] = GetRelativePath(e.FullPath)
                }
            });
        }

        private void OnFileDeleted(object sender, FileSystemEventArgs e)
        {
            if (!ShouldMonitorFile(e.FullPath)) return;

            BroadcastEvent(new EventData
            {
                type = "file_deleted",
                category = "filesystem",
                timestamp = DateTime.Now,
                description = $"File deleted: {Path.GetFileName(e.FullPath)}",
                data = new Dictionary<string, object>
                {
                    ["file_path"] = e.FullPath,
                    ["file_name"] = Path.GetFileName(e.FullPath),
                    ["extension"] = Path.GetExtension(e.FullPath),
                    ["relative_path"] = GetRelativePath(e.FullPath)
                }
            });
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            if (!ShouldMonitorFile(e.FullPath)) return;

            BroadcastEvent(new EventData
            {
                type = "file_renamed",
                category = "filesystem",
                timestamp = DateTime.Now,
                description = $"File renamed: {Path.GetFileName(e.OldFullPath)} → {Path.GetFileName(e.FullPath)}",
                data = new Dictionary<string, object>
                {
                    ["old_path"] = e.OldFullPath,
                    ["new_path"] = e.FullPath,
                    ["old_name"] = Path.GetFileName(e.OldFullPath),
                    ["new_name"] = Path.GetFileName(e.FullPath),
                    ["relative_path"] = GetRelativePath(e.FullPath)
                }
            });
        }

        private bool ShouldMonitorFile(string filePath)
        {
            if (!isMonitoringFileChanges) return false;
            
            string extension = Path.GetExtension(filePath).ToLower();
            return monitoredFileExtensions.Contains(extension);
        }

        private string GetRelativePath(string fullPath)
        {
            string projectPath = Directory.GetParent(Application.dataPath).FullName;
            if (fullPath.StartsWith(projectPath))
            {
                return fullPath.Substring(projectPath.Length + 1).Replace('\\', '/');
            }
            return fullPath;
        }

        #endregion

        #region Compilation Monitoring

        /// <summary>
        /// Start/stop compilation state monitoring
        /// </summary>
        public bool StartCompileMonitoring(bool enable = true)
        {
            isMonitoringCompile = enable;
            
            if (enable)
            {
                BroadcastEvent(new EventData
                {
                    type = "compile_monitoring",
                    category = "monitoring",
                    timestamp = DateTime.Now,
                    description = "Compilation monitoring started",
                    data = new Dictionary<string, object>
                    {
#if UNITY_EDITOR
                        ["is_compiling"] = EditorApplication.isCompiling
#else
                        ["is_compiling"] = false
#endif
                    }
                });
            }
            
            return true;
        }

#if UNITY_EDITOR && UNITY_2019_1_OR_NEWER
        private void OnCompilationStarted(object context)
        {
            if (!isMonitoringCompile) return;

            BroadcastEvent(new EventData
            {
                type = "compilation_started",
                category = "compiler",
                timestamp = DateTime.Now,
                description = "Script compilation started",
                data = new Dictionary<string, object>
                {
                    ["context"] = context?.ToString(),
                    ["assemblies_building"] = CompilationPipeline.GetAssemblies().Length
                }
            });
        }

        private void OnCompilationFinished(object context)
        {
            if (!isMonitoringCompile) return;

            BroadcastEvent(new EventData
            {
                type = "compilation_finished",
                category = "compiler",
                timestamp = DateTime.Now,
                description = "Script compilation finished",
                data = new Dictionary<string, object>
                {
                    ["context"] = context?.ToString(),
                    ["has_errors"] = EditorApplication.isCompiling == false && EditorUtility.scriptCompilationFailed
                }
            });
        }
#endif

        #endregion

        #region Custom Event Subscription

        /// <summary>
        /// Subscribe to custom event
        /// </summary>
        public bool SubscribeToEvent(string eventType, string subscriberId)
        {
            if (!eventSubscriptions.ContainsKey(eventType))
            {
                eventSubscriptions[eventType] = new HashSet<string>();
            }
            
            eventSubscriptions[eventType].Add(subscriberId);
            
            BroadcastEvent(new EventData
            {
                type = "event_subscription",
                category = "monitoring",
                timestamp = DateTime.Now,
                description = $"Subscribed to event: {eventType}",
                data = new Dictionary<string, object>
                {
                    ["event_type"] = eventType,
                    ["subscriber_id"] = subscriberId,
                    ["total_subscribers"] = eventSubscriptions[eventType].Count
                }
            });
            
            return true;
        }

        /// <summary>
        /// Unsubscribe from custom event
        /// </summary>
        public bool UnsubscribeFromEvent(string eventType, string subscriberId)
        {
            if (eventSubscriptions.ContainsKey(eventType))
            {
                eventSubscriptions[eventType].Remove(subscriberId);
                if (eventSubscriptions[eventType].Count == 0)
                {
                    eventSubscriptions.Remove(eventType);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Fire custom event
        /// </summary>
        public void TriggerCustomEvent(string eventType, Dictionary<string, object> data, string description = "")
        {
            var eventData = new EventData
            {
                type = eventType,
                category = "custom",
                timestamp = DateTime.Now,
                description = description.Length > 0 ? description : $"Custom event: {eventType}",
                data = data ?? new Dictionary<string, object>()
            };

            BroadcastEvent(eventData);
        }

        #endregion

        #region Event Broadcasting

        private void BroadcastEvent(EventData eventData)
        {
            // Add to event queue
            eventQueue.Enqueue(eventData);
            while (eventQueue.Count > maxEventQueueSize)
            {
                eventQueue.Dequeue();
            }

            // Notify external event handlers
            OnEventDetected?.Invoke(eventData);

            // Send to MCP Client
            if (NexusMCPClient.Instance != null)
            {
                var mcpMessage = new
                {
                    type = "unity_event",
                    event_type = eventData.type,
                    category = eventData.category,
                    timestamp = eventData.timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    description = eventData.description,
                    data = eventData.data
                };

                // Send to MCP asynchronously
                if (UnityMainThreadDispatcher.Exists())
                {
                    UnityMainThreadDispatcher.Instance().Enqueue(() =>
                    {
                        NexusMCPClient.Instance.SendMessage(Newtonsoft.Json.JsonConvert.SerializeObject(mcpMessage));
                    });
                }
            }

            Debug.Log($"[Nexus Event Monitor] {eventData.type}: {eventData.description}");
        }

        /// <summary>
        /// Get recent event history
        /// </summary>
        public string GetRecentEvents(int count = 10)
        {
            var recentEvents = eventQueue.TakeLast(count).Reverse();
            
            var result = new System.Text.StringBuilder();
            result.AppendLine($"📊 Recent Events (Last {count}):");
            
            foreach (var evt in recentEvents)
            {
                result.AppendLine($"  [{evt.timestamp:HH:mm:ss}] {evt.category}/{evt.type}: {evt.description}");
            }
            
            return result.ToString();
        }

        /// <summary>
        /// Get monitoring state
        /// </summary>
        public string GetMonitoringStatus()
        {
            return $@"📡 Nexus Event Monitor Status:
Play State Monitoring: {(isMonitoringPlayState ? "🟢 Active" : "🔴 Inactive")}
File Change Monitoring: {(isMonitoringFileChanges ? "🟢 Active" : "🔴 Inactive")}
Compile Monitoring: {(isMonitoringCompile ? "🟢 Active" : "🔴 Inactive")}
Custom Events: {eventSubscriptions.Count} subscriptions
Recent Events: {eventQueue.Count} in queue";
        }

        #endregion

        #region GUI

#if UNITY_EDITOR
        void OnGUI()
        {
            GUILayout.Label("Nexus Event Monitor", EditorStyles.boldLabel);

            EditorGUILayout.Space();

            // Display monitoring state
            isMonitoringPlayState = EditorGUILayout.Toggle("Monitor Play State", isMonitoringPlayState);
            isMonitoringFileChanges = EditorGUILayout.Toggle("Monitor File Changes", isMonitoringFileChanges);
            isMonitoringCompile = EditorGUILayout.Toggle("Monitor Compilation", isMonitoringCompile);

            EditorGUILayout.Space();

            // Statistics information
            EditorGUILayout.LabelField("Event Queue Size", eventQueue.Count.ToString());
            EditorGUILayout.LabelField("Active Subscriptions", eventSubscriptions.Count.ToString());

            EditorGUILayout.Space();

            if (GUILayout.Button("Clear Event Queue"))
            {
                eventQueue.Clear();
            }

            if (GUILayout.Button("Test Custom Event"))
            {
                TriggerCustomEvent("test_event", new Dictionary<string, object>
                {
                    ["test_data"] = "Hello from Nexus!",
                    ["timestamp"] = DateTime.Now
                }, "Manual test event triggered");
            }
        }
#endif

        #endregion
    }
}