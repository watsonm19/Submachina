using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SynapticPro
{
    /// <summary>
    /// Nexus MCP Client - Connects to MCP server and handles AI communication
    /// Supports multiple AI providers through MCP protocol
    /// </summary>
    public class NexusMCPClient : MonoBehaviour
    {
        private static NexusMCPClient instance;
        public static NexusMCPClient Instance
        {
            get
            {
                if (instance == null)
                {
                    if (Application.isPlaying)
                    {
                        // Runtime only: Create GameObject only in PlayMode
                        var go = new GameObject("NexusMCPClient_Runtime");
                        instance = go.AddComponent<NexusMCPClient>();
                        DontDestroyOnLoad(go);
                    }
                    else
                    {
                        // Return null in Editor mode (use NexusEditorMCPService)
                        return null;
                    }
                }
                return instance;
            }
        }

        private ClientWebSocket webSocket;
        private Queue<MCPMessage> messageQueue = new Queue<MCPMessage>();
        private bool isConnected = false;
        private string serverUrl = "ws://localhost:8090";
        
        public event Action<string> OnMessageReceived;
        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<string> OnError;

        [Serializable]
        public class MCPMessage
        {
            public string type;
            public string id;
            public string provider;
            public string content;
            public Dictionary<string, object> parameters;
            public string tool;
            public string command;
            public object data;
        }

        [Serializable]
        public class MCPResponse
        {
            public string id;
            public bool success;
            public string content;
            public string error;
            public string provider;
            public int tokensUsed;
        }

        private void Start()
        {
            Debug.Log($"[Nexus MCP] Starting MCP Client: {gameObject.name}");

            // Monitor Play mode and Editor mode switching
#if UNITY_EDITOR
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
            _ = Task.Run(async () => await ConnectToMCPServer());
        }

        private void OnDestroy()
        {
            Debug.Log($"[Nexus MCP] Destroying MCP Client: {gameObject.name}");

#if UNITY_EDITOR
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
            DisconnectFromMCPServer();

            // Clear instance
            if (instance == this)
            {
                instance = null;
            }
        }

#if UNITY_EDITOR
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // Reconnect when switching between Play mode and Editor mode
            switch (state)
            {
                case PlayModeStateChange.EnteredPlayMode:
                case PlayModeStateChange.EnteredEditMode:
                    if (!isConnected)
                    {
                        Debug.Log($"[Nexus MCP] Reconnecting due to play mode change: {state}");
                        _ = Task.Run(async () => await ConnectToMCPServer());
                    }
                    break;
            }
        }
#endif

        public async Task ConnectToMCPServer()
        {
            try
            {
                webSocket = new ClientWebSocket();
                
                await webSocket.ConnectAsync(new Uri(serverUrl), CancellationToken.None);
                
                isConnected = true;
                OnConnected?.Invoke();
                
                Debug.Log("[Nexus MCP] Connected to MCP Server");
                
                // Start listening for messages
                _ = Task.Run(async () => await ListenForMessages());
            }
            catch (Exception e)
            {
                Debug.LogError($"[Nexus MCP] Failed to connect: {e.Message}");
                OnError?.Invoke(e.Message);
                isConnected = false;
            }
        }

        private async Task ListenForMessages()
        {
            Debug.Log("[Nexus MCP] Starting message listener");
            
            var buffer = new byte[1024 * 4];
            
            while (webSocket.State == WebSocketState.Open)
            {
                try
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        
                        // Queue message for main thread processing
                        var mcpMessage = JsonConvert.DeserializeObject<MCPMessage>(message);
                        messageQueue.Enqueue(mcpMessage);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Nexus MCP] Listen error: {e.Message}");
                    break;
                }
            }
            
            isConnected = false;
            OnDisconnected?.Invoke();
        }

        private void Update()
        {
            // Process queued messages on main thread
            if (messageQueue.Count > 0)
            {
                Debug.Log($"[Nexus MCP] Processing {messageQueue.Count} queued messages");
                while (messageQueue.Count > 0)
                {
                    var message = messageQueue.Dequeue();
                    ProcessMessage(message);
                }
            }
        }

        private void ProcessMessage(MCPMessage message)
        {
            Debug.Log($"[Nexus MCP] Processing message type: {message.type}, tool: {message.tool}, command: {message.command}");
            
            switch (message.type)
            {
                case "unity_operation":
                    ExecuteUnityOperation(message);
                    break;
                    
                case "ai_response":
                    OnMessageReceived?.Invoke(message.content);
                    break;
                    
                case "tool_call":
                    HandleToolCall(message);
                    break;
                    
                default:
                    Debug.Log($"[Nexus MCP] Unknown message type: {message.type}");
                    break;
            }
        }

        public async Task<string> SendChatMessage(string message, string provider = "claude")
        {
            if (!isConnected)
            {
                await ConnectToMCPServer();
                if (!isConnected)
                    return "Failed to connect to MCP server";
            }

            var mcpMessage = new MCPMessage
            {
                type = "chat",
                id = Guid.NewGuid().ToString(),
                provider = provider,
                content = message,
                parameters = new Dictionary<string, object>()
            };

            try
            {
                var json = JsonConvert.SerializeObject(mcpMessage);
                var buffer = Encoding.UTF8.GetBytes(json);
                
                await webSocket.SendAsync(
                    new ArraySegment<byte>(buffer), 
                    WebSocketMessageType.Text, 
                    true, 
                    CancellationToken.None
                );

                // Wait for response (simplified - in practice you'd use proper async pattern)
                return await WaitForResponse(mcpMessage.id);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Nexus MCP] Send error: {e.Message}");
                return $"Error: {e.Message}";
            }
        }

        public async Task<bool> ExecuteUnityTool(string toolName, Dictionary<string, object> parameters)
        {
            if (!isConnected)
                return false;

            var mcpMessage = new MCPMessage
            {
                type = "tool_call",
                id = Guid.NewGuid().ToString(),
                tool = toolName,
                parameters = parameters
            };

            try
            {
                var json = JsonConvert.SerializeObject(mcpMessage);
                var buffer = Encoding.UTF8.GetBytes(json);
                
                await webSocket.SendAsync(
                    new ArraySegment<byte>(buffer), 
                    WebSocketMessageType.Text, 
                    true, 
                    CancellationToken.None
                );

                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Nexus MCP] Tool call error: {e.Message}");
                return false;
            }
        }

        private async Task<string> WaitForResponse(string messageId)
        {
            // Simplified response waiting - in practice use proper async/await pattern
            float timeout = 30f;
            string response = null;
            
            System.Action<string> responseHandler = (content) => response = content;
            OnMessageReceived += responseHandler;
            
            while (timeout > 0 && response == null)
            {
                await Task.Delay(100);
                timeout -= 0.1f;
            }
            
            OnMessageReceived -= responseHandler;
            
            return response ?? "Timeout waiting for response";
        }

        private void ExecuteUnityOperation(MCPMessage message)
        {
            Debug.Log($"[Nexus MCP] Executing Unity operation: {message.tool} with command: {message.command}");

            // Must be executed on Unity main thread
            if (UnityMainThreadDispatcher.Exists())
            {
                UnityMainThreadDispatcher.Instance().Enqueue(() => {
                    ExecuteUnityOperationOnMainThread(message);
                });
            }
            else
            {
                // Execute directly if main thread dispatcher not available (risky)
                ExecuteUnityOperationOnMainThread(message);
            }
        }

        private void ExecuteUnityOperationOnMainThread(MCPMessage message)
        {
            try
            {
                Debug.Log($"[Nexus MCP] Executing on main thread: {message.tool}");

                // Map MCP tool names to Unity operations
                string operationType = message.command ?? message.tool ?? "";

                // Convert tool name to existing operation type
                operationType = ConvertMCPToolToOperation(operationType);
                
                Debug.Log($"[Nexus MCP] Converted operation type: {operationType}");
                
                var operation = new NexusUnityOperation
                {
                    type = operationType,
                    parameters = new Dictionary<string, string>()
                };

                // Convert parameters
                if (message.parameters != null)
                {
                    foreach (var kvp in message.parameters)
                    {
                        if (kvp.Value != null)
                        {
                            // Handle nested objects
                            if (kvp.Value is Dictionary<string, object> dict)
                            {
                                // Handle structures like Vector3
                                if (dict.ContainsKey("x") && dict.ContainsKey("y") && dict.ContainsKey("z"))
                                {
                                    operation.parameters[kvp.Key] = $"{dict["x"]},{dict["y"]},{dict["z"]}";
                                }
                                else if (dict.ContainsKey("x") && dict.ContainsKey("y"))
                                {
                                    operation.parameters[kvp.Key] = $"{dict["x"]},{dict["y"]}";
                                }
                                else if (dict.ContainsKey("r") && dict.ContainsKey("g") && dict.ContainsKey("b"))
                                {
                                    operation.parameters[kvp.Key] = $"{dict["r"]},{dict["g"]},{dict["b"]}";
                                }
                                else
                                {
                                    // Save other objects as JSON strings
                                    operation.parameters[kvp.Key] = JsonConvert.SerializeObject(dict);
                                }
                            }
                            else
                            {
                                operation.parameters[kvp.Key] = kvp.Value.ToString();
                            }
                        }
                    }
                }

                Debug.Log($"[Nexus MCP] About to execute operation with parameters: {operation.parameters.Count}");
                foreach (var param in operation.parameters)
                {
                    Debug.Log($"[Nexus MCP] Parameter: {param.Key} = {param.Value}");
                }

                // Execute Unity operation (already running on main thread)
                string result;
                bool success;

#if UNITY_EDITOR
                // Only executable in Editor
                try
                {
                    // Use reflection to call Executor in Editor assembly
                    var executorType = System.Type.GetType("SynapticPro.NexusUnityExecutor, Synaptic.MCP.Unity.Editor");
                    if (executorType == null)
                    {
                        result = "Error: NexusUnityExecutor not found in Editor assembly";
                        success = false;
                    }
                    else
                    {
                        var executor = Activator.CreateInstance(executorType);
                        var executeMethod = executorType.GetMethod("ExecuteOperation");
                        if (executeMethod == null)
                        {
                            result = "Error: ExecuteOperation method not found";
                            success = false;
                        }
                        else
                        {
                            var task = (Task<string>)executeMethod.Invoke(executor, new object[] { operation });
                            result = task.Result;
                            success = !result.StartsWith("Error:") && !result.StartsWith("Failed:") && !result.Contains("Tool execution failed");
                        }
                    }
                }
                catch (Exception ex)
                {
                    result = $"Exception during execution: {ex.Message}";
                    success = false;
                    Debug.LogError($"[Nexus MCP] Exception: {ex}");
                }
#else
                // Not executable at runtime
                result = "MCP operations are only available in Unity Editor";
                success = false;
#endif
                
                Debug.Log($"[Nexus MCP] Operation result: {result}");
                Debug.Log($"[Nexus MCP] Operation success: {success}");

                // Send result to MCP server
                _ = SendOperationResult(message.id, success, result);

                // Output result to log
                if (success)
                {
                    Debug.Log($"[Nexus MCP] SUCCESS: {result}");
                }
                else
                {
                    Debug.LogError($"[Nexus MCP] FAILED: {result}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Nexus MCP] Unity operation error: {e.Message}");
            }
        }

        private void HandleToolCall(MCPMessage message)
        {
            // Handle all unity_* tools uniformly
            if (message.tool.StartsWith("unity_"))
            {
                ExecuteUnityOperation(message);
            }
            else
            {
                Debug.LogWarning($"[Nexus MCP] Unknown tool: {message.tool}");
            }
        }

        public bool IsConnected => IsConnectedToServer();

        public void SetServerUrl(string url)
        {
            serverUrl = url;
        }

        // Unity-specific MCP tools
        public async Task<bool> CreateGameObject(string name, Vector3 position = default)
        {
            var parameters = new Dictionary<string, object>
            {
                ["type"] = "CREATE_GAMEOBJECT",
                ["parameters"] = new Dictionary<string, object>
                {
                    ["name"] = name,
                    ["position"] = $"{position.x},{position.y},{position.z}"
                }
            };

            return await ExecuteUnityTool("unity_create", parameters);
        }

        public async Task<bool> AddComponent(string targetName, string componentType)
        {
            var parameters = new Dictionary<string, object>
            {
                ["type"] = "ADD_COMPONENT",
                ["parameters"] = new Dictionary<string, object>
                {
                    ["target"] = targetName,
                    ["type"] = componentType
                }
            };

            return await ExecuteUnityTool("unity_create", parameters);
        }

        public async Task<bool> CreateUI(string uiType, string name, Dictionary<string, string> properties = null)
        {
            var parameters = new Dictionary<string, object>
            {
                ["type"] = "CREATE_UI",
                ["parameters"] = new Dictionary<string, object>
                {
                    ["type"] = uiType,
                    ["name"] = name
                }
            };

            if (properties != null)
            {
                foreach (var kvp in properties)
                {
                    ((Dictionary<string, object>)parameters["parameters"])[kvp.Key] = kvp.Value;
                }
            }

            return await ExecuteUnityTool("unity_create", parameters);
        }
        
        private async Task SendOperationResult(string messageId, bool success, string result)
        {
            object structuredData = null;
            string displayContent = result;

            // Attempt to parse JSON and send as structured data
            try
            {
                // If result is JSON, send as structured data
                if (result.TrimStart().StartsWith("{") || result.TrimStart().StartsWith("["))
                {
                    structuredData = JsonConvert.DeserializeObject(result);
                    displayContent = success ? "Retrieved structured data" : result;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Nexus MCP] JSON parse failed: {e.Message}");
            }

            // Store result in content field according to MCP protocol
            var response = new MCPMessage
            {
                type = "operation_result",
                id = messageId,
                content = result, // Return original result (JSON string) as is
                data = new { success = success }
            };

            try
            {
                var json = JsonConvert.SerializeObject(response, Formatting.Indented);
                var buffer = Encoding.UTF8.GetBytes(json);
                
                await webSocket.SendAsync(
                    new ArraySegment<byte>(buffer), 
                    WebSocketMessageType.Text, 
                    true, 
                    CancellationToken.None
                );
                
                Debug.Log($"[Nexus MCP] Sent operation result: {success}");
                Debug.Log($"[Nexus MCP] Response JSON: {json}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Nexus MCP] Failed to send operation result: {e.Message}");
            }
        }
        
        private string ConvertMCPToolToOperation(string mcpTool)
        {
            switch (mcpTool)
            {
                // GameObject operations
                case "unity_create_gameobject":
                case "create_gameobject":
                    return "CREATE_GAMEOBJECT";

                case "unity_update_gameobject":
                case "update_gameobject":
                    return "SET_PROPERTY";

                case "unity_delete_gameobject":
                case "delete_gameobject":
                    return "DELETE_GAMEOBJECT";

                case "unity_set_transform":
                case "set_transform":
                    return "SET_PROPERTY";

                // Components
                case "unity_add_component":
                case "add_component":
                    return "ADD_COMPONENT";
                    
                case "unity_update_component":
                case "update_component":
                    return "UPDATE_COMPONENT";

                // Package management
                case "unity_list_packages":
                case "list_packages":
                    return "LIST_PACKAGES";
                    
                case "unity_install_package":
                case "install_package":
                    return "INSTALL_PACKAGE";
                    
                case "unity_remove_package":
                case "remove_package":
                    return "REMOVE_PACKAGE";
                    
                case "unity_check_package":
                case "check_package":
                    return "CHECK_PACKAGE";

                // UI
                case "unity_create_ui":
                case "create_ui":
                    return "CREATE_UI";

                // Terrain
                case "unity_create_terrain":
                case "create_terrain":
                    return "CREATE_TERRAIN";
                    
                case "unity_modify_terrain":
                case "modify_terrain":
                    return "MODIFY_TERRAIN";

                // Camera
                case "unity_setup_camera":
                case "setup_camera":
                    return "SETUP_CAMERA";

                // Cinemachine
                case "unity_create_virtual_camera":
                case "create_virtual_camera":
                    return "CREATE_VIRTUAL_CAMERA";

                case "unity_create_freelook_camera":
                case "create_freelook_camera":
                    return "CREATE_FREELOOK_CAMERA";

                case "unity_setup_cinemachine_brain":
                case "setup_cinemachine_brain":
                    return "SETUP_CINEMACHINE_BRAIN";

                case "unity_update_virtual_camera":
                case "update_virtual_camera":
                    return "UPDATE_VIRTUAL_CAMERA";

                case "unity_create_dolly_track":
                case "create_dolly_track":
                    return "CREATE_DOLLY_TRACK";

                case "unity_add_collider_extension":
                case "add_collider_extension":
                    return "ADD_COLLIDER_EXTENSION";

                case "unity_add_confiner_extension":
                case "add_confiner_extension":
                    return "ADD_CONFINER_EXTENSION";

                case "unity_create_state_driven_camera":
                case "create_state_driven_camera":
                    return "CREATE_STATE_DRIVEN_CAMERA";

                case "unity_create_clear_shot_camera":
                case "create_clear_shot_camera":
                    return "CREATE_CLEAR_SHOT_CAMERA";

                case "unity_create_impulse_source":
                case "create_impulse_source":
                    return "CREATE_IMPULSE_SOURCE";

                case "unity_add_impulse_listener":
                case "add_impulse_listener":
                    return "ADD_IMPULSE_LISTENER";

                case "unity_create_blend_list_camera":
                case "create_blend_list_camera":
                    return "CREATE_BLEND_LIST_CAMERA";

                case "unity_create_target_group":
                case "create_target_group":
                    return "CREATE_TARGET_GROUP";

                case "unity_add_target_to_group":
                case "add_target_to_group":
                    return "ADD_TARGET_TO_GROUP";

                case "unity_set_camera_priority":
                case "set_camera_priority":
                    return "SET_CAMERA_PRIORITY";

                case "unity_set_camera_enabled":
                case "set_camera_enabled":
                    return "SET_CAMERA_ENABLED";

                case "unity_create_mixing_camera":
                case "create_mixing_camera":
                    return "CREATE_MIXING_CAMERA";

                case "unity_update_camera_target":
                case "update_camera_target":
                    return "UPDATE_CAMERA_TARGET";

                case "unity_update_brain_blend_settings":
                case "update_brain_blend_settings":
                    return "UPDATE_BRAIN_BLEND_SETTINGS";

                case "unity_get_active_camera_info":
                case "get_active_camera_info":
                    return "GET_ACTIVE_CAMERA_INFO";

                // Placement
                case "unity_place_objects":
                case "place_objects":
                    return "PLACE_OBJECTS";

                // Lighting
                case "unity_setup_lighting":
                case "setup_lighting":
                    return "SETUP_LIGHTING";

                // Material
                case "unity_create_material":
                case "create_material":
                    return "CREATE_MATERIAL";

                // Prefab
                case "unity_create_prefab":
                case "create_prefab":
                    return "CREATE_PREFAB";

                // Script
                case "unity_create_script":
                case "create_script":
                    return "CREATE_SCRIPT";

                // Scene
                case "unity_manage_scene":
                case "manage_scene":
                    return "MANAGE_SCENE";

                // Animation
                case "unity_create_animation":
                case "create_animation":
                    return "CREATE_ANIMATION";

                // Physics
                case "unity_setup_physics":
                case "setup_physics":
                    return "SETUP_PHYSICS";

                // Other
                case "unity_search":
                case "search_objects":
                    return "SEARCH_OBJECTS";
                    
                case "unity_console":
                case "console_operation":
                    return "CONSOLE_OPERATION";

                // Operation history / Undo/Redo
                case "unity_get_operation_history":
                    return "GET_OPERATION_HISTORY";
                    
                case "unity_undo_operation":
                    return "UNDO_OPERATION";
                    
                case "unity_redo_operation":
                    return "REDO_OPERATION";
                    
                case "unity_create_checkpoint":
                    return "CREATE_CHECKPOINT";
                    
                case "unity_restore_checkpoint":
                    return "RESTORE_CHECKPOINT";

                // Real-time event monitoring
                case "unity_monitor_play_state":
                    return "MONITOR_PLAY_STATE";
                    
                case "unity_monitor_file_changes":
                    return "MONITOR_FILE_CHANGES";
                    
                case "unity_monitor_compile":
                    return "MONITOR_COMPILE";
                    
                case "unity_subscribe_events":
                    return "SUBSCRIBE_EVENTS";
                    
                case "unity_get_events":
                    return "GET_EVENTS";
                    
                case "unity_get_monitoring_status":
                    return "GET_MONITORING_STATUS";

                // Project settings
                case "unity_get_build_settings":
                    return "GET_BUILD_SETTINGS";
                    
                case "unity_get_player_settings":
                    return "GET_PLAYER_SETTINGS";
                    
                case "unity_get_quality_settings":
                    return "GET_QUALITY_SETTINGS";
                    
                case "unity_get_input_settings":
                    return "GET_INPUT_SETTINGS";
                    
                case "unity_get_physics_settings":
                    return "GET_PHYSICS_SETTINGS";
                    
                case "unity_get_project_summary":
                    return "GET_PROJECT_SUMMARY";
                    
                default:
                    // Strip unity_ prefix if present and convert to uppercase
                    if (mcpTool.StartsWith("unity_"))
                        return mcpTool.Substring(6).ToUpper();
                    return mcpTool.ToUpper();
            }
        }

        /// <summary>
        /// Disconnect from MCP server
        /// </summary>
        public void DisconnectFromMCPServer()
        {
            try
            {
                if (webSocket != null && isConnected)
                {
                    isConnected = false;
                    webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", CancellationToken.None);
                    webSocket.Dispose();
                    webSocket = null;
                    
                    OnDisconnected?.Invoke();
                    Debug.Log("[Nexus MCP] Disconnected from MCP Server");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Nexus MCP] Error during disconnect: {e.Message}");
            }
        }

        /// <summary>
        /// Check connection status
        /// </summary>
        public bool IsConnectedToServer()
        {
            return isConnected && webSocket != null && webSocket.State == WebSocketState.Open;
        }

        /// <summary>
        /// Retry connection
        /// </summary>
        public async void ReconnectToMCPServer()
        {
            DisconnectFromMCPServer();
            await Task.Delay(1000); // Wait 1 second
            await ConnectToMCPServer();
        }
    }
}