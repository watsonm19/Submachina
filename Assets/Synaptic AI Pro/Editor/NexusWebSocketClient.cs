using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json;
using System.Collections.Generic;
using SynapticAIPro;

namespace SynapticPro
{
    /// <summary>
    /// WebSocket client for editor mode
    /// Manages communication with MCP server
    /// </summary>
    public class NexusWebSocketClient
    {
        private ClientWebSocket webSocket;
        private CancellationTokenSource cancellationTokenSource;
        private bool isConnected = false;
        private Queue<string> messageQueue = new Queue<string>();
        private bool shouldReconnect = true;
        private int reconnectAttempts = 0;
        private const int maxReconnectAttempts = 30;
        private const int reconnectDelay = 2000; // 2 seconds between attempts
        private string serverUrl = "ws://127.0.0.1:8090";
        private const int CONNECT_TIMEOUT_SECONDS = 5;
        private readonly List<Task> backgroundTasks = new List<Task>();

        public bool IsConnected => isConnected;
        public event Action<string> OnMessageReceived;
        public event Action OnConnected;
        public event Action OnDisconnected;

        private static NexusWebSocketClient instance;
        public static NexusWebSocketClient Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new NexusWebSocketClient();
                }
                return instance;
            }
        }

        private static void LogTaskFault(Task t, string label)
        {
            t.ContinueWith(
                x => SynLog.Warn($"[Nexus WebSocket] {label} faulted: {x.Exception?.GetBaseException().Message}"),
                TaskContinuationOptions.OnlyOnFaulted);
        }

        public async Task<bool> Connect(string url = "ws://127.0.0.1:8090")
        {
            shouldReconnect = true;
            reconnectAttempts = 0;
            
            while (shouldReconnect && reconnectAttempts < maxReconnectAttempts)
            {
                try
                {
                    SynLog.Info($"[Nexus WebSocket] Connecting to {url}... (Attempt {reconnectAttempts + 1})");
                    
                    webSocket = new ClientWebSocket();
                    cancellationTokenSource = new CancellationTokenSource();
                    
                    using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationTokenSource.Token))
                    {
                        connectCts.CancelAfter(TimeSpan.FromSeconds(CONNECT_TIMEOUT_SECONDS));
                        await webSocket.ConnectAsync(new Uri(url), connectCts.Token);
                    }
                    isConnected = true;
                    reconnectAttempts = 0; // Reset on success

                    SynLog.Info("[Nexus WebSocket] Connected successfully!");
                    OnConnected?.Invoke();

                    // Start message receive loop (tracked so Disconnect can await)
                    var receiveTask = Task.Run(async () => await ReceiveLoop());
                    LogTaskFault(receiveTask, "ReceiveLoop");
                    backgroundTasks.Add(receiveTask);

                    // Start heartbeat
                    var heartbeatTask = Task.Run(async () => await HeartbeatLoop());
                    LogTaskFault(heartbeatTask, "HeartbeatLoop");
                    backgroundTasks.Add(heartbeatTask);

                    // Notify Unity is ready
                    await SendMessage(new { type = "unity_ready", version = NexusVersion.Current });

                    return true;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Nexus WebSocket] Connection failed: {e.Message}");
                    isConnected = false;
                    reconnectAttempts++;
                    
                    if (reconnectAttempts < maxReconnectAttempts && shouldReconnect)
                    {
                        SynLog.Info($"[Nexus WebSocket] Retrying in {reconnectDelay / 1000} seconds...");
                        await Task.Delay(reconnectDelay);
                    }
                }
            }
            
            return false;
        }
        
        private async Task ReceiveLoop()
        {
            var buffer = new byte[4096];
            var messageBuilder = new StringBuilder();

            try
            {
                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        cancellationTokenSource.Token
                    );

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        // Accumulate fragments until EndOfMessage. Without this,
                        // any message larger than 4096B (e.g. tool args / scene
                        // dumps) gets truncated mid-chunk and fails JSON parse —
                        // root cause of ESC-0102 (Win + Unity 6.3 MCP receive
                        // appears to "drop" large messages).
                        messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                        if (result.EndOfMessage)
                        {
                            var message = messageBuilder.ToString();
                            messageBuilder.Clear();

                            SynLog.Info($"[Nexus WebSocket] Received ({message.Length} chars): {message.Substring(0, System.Math.Min(message.Length, 200))}");

                            lock (messageQueue)
                            {
                                messageQueue.Enqueue(message);
                            }
                        }
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Nexus WebSocket] Receive error: {e.Message}");
            }
            finally
            {
                isConnected = false;
                OnDisconnected?.Invoke();
            }
        }
        
        public void ProcessMessages()
        {
            lock (messageQueue)
            {
                while (messageQueue.Count > 0)
                {
                    var message = messageQueue.Dequeue();
                    OnMessageReceived?.Invoke(message);
                    
                    try
                    {
                        var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(message);
                        if (data != null && data.ContainsKey("type"))
                        {
                            ProcessUnityCommand(data);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[Nexus WebSocket] Message processing error: {e.Message}");
                    }
                }
            }
        }
        
        public event Action<string, string> OnClaudeResponse; // sessionId, message
        public event Action<string> OnChatStatusUpdate; // status message
        
        private void ProcessUnityCommand(Dictionary<string, object> data)
        {
            var type = data["type"].ToString();
            
            if (type == "unity_operation")
            {
                var command = data.ContainsKey("command") ? data["command"].ToString() : "";
                var parameters = data.ContainsKey("parameters") ? data["parameters"] as Newtonsoft.Json.Linq.JObject : null;
                
                SynLog.Info($"[Nexus WebSocket] Executing Unity command: {command}");

                // Execute Unity operation in editor mode
                EditorApplication.delayCall += () =>
                {
                    ExecuteUnityOperation(command, parameters);
                };
            }
            else if (type == "claude_response")
            {
                // Real-time response from Claude Desktop
                var responseData = data.ContainsKey("data") ? data["data"] as Newtonsoft.Json.Linq.JObject : null;
                if (responseData != null)
                {
                    var message = responseData.Value<string>("message") ?? "";
                    var sessionId = responseData.Value<string>("sessionId") ?? "";
                    var responseType = responseData.Value<string>("responseType") ?? "response";

                    SynLog.Info($"[Nexus WebSocket] Claude response received: {message}");

                    // Fire event on main thread
                    EditorApplication.delayCall += () =>
                    {
                        OnClaudeResponse?.Invoke(sessionId, message);
                    };
                }
            }
            else if (type == "chat_initiated")
            {
                // Chat initiated notification
                var chatData = data.ContainsKey("data") ? data["data"] as Newtonsoft.Json.Linq.JObject : null;
                if (chatData != null)
                {
                    var status = chatData.Value<string>("status") ?? "Processing...";

                    SynLog.Info($"[Nexus WebSocket] Chat initiated: {status}");

                    EditorApplication.delayCall += () =>
                    {
                        OnChatStatusUpdate?.Invoke(status);
                    };
                }
            }
        }
        
        private async void ExecuteUnityOperation(string command, Newtonsoft.Json.Linq.JObject parameters)
        {
            SynLog.Info($"[Nexus WebSocket] Executing Unity operation: {command}");
            SynLog.Info($"[Nexus WebSocket] Parameters: {parameters?.ToString()}");
            
            var operationId = parameters?.Value<string>("operationId") ?? Guid.NewGuid().ToString();

            try
            {
                var operation = new NexusUnityOperation
                {
                    type = ConvertCommandToOperationType(command),
                    parameters = new Dictionary<string, string>()
                };

                // Convert parameters
                if (parameters != null)
                {
                    foreach (var prop in parameters.Properties())
                    {
                        if (prop.Name == "operationId") continue;

                        var value = prop.Value;

                        if (value is Newtonsoft.Json.Linq.JObject jObj)
                        {
                            // Process nested objects (Vector3, etc.)
                            if (jObj.ContainsKey("x") && jObj.ContainsKey("y") && jObj.ContainsKey("z"))
                            {
                                operation.parameters[prop.Name] = $"{jObj["x"]},{jObj["y"]},{jObj["z"]}";
                            }
                            else if (jObj.ContainsKey("x") && jObj.ContainsKey("y"))
                            {
                                operation.parameters[prop.Name] = $"{jObj["x"]},{jObj["y"]}";
                            }
                            else
                            {
                                operation.parameters[prop.Name] = value.ToString();
                            }
                        }
                        else
                        {
                            operation.parameters[prop.Name] = value.ToString();
                        }
                    }
                }

                // Execute
                string result = "";
                bool success = true;

                // Process information retrieval commands
                switch (operation.type)
                {
                    case "GET_SCENE_INFO":
                        result = NexusStateInspector.GetSceneInformation();
                        break;
                    case "GET_CAMERA_INFO":
                        result = NexusStateInspector.GetCameraInformation();
                        break;
                    case "GET_TERRAIN_INFO":
                        result = NexusStateInspector.GetTerrainInformation();
                        break;
                    case "GET_LIGHTING_INFO":
                        result = NexusStateInspector.GetLightingInformation();
                        break;
                    case "GET_MATERIAL_INFO":
                        result = NexusStateInspector.GetMaterialInformation();
                        break;
                    case "GET_UI_INFO":
                        result = NexusStateInspector.GetUIInformation();
                        break;
                    case "GET_PHYSICS_INFO":
                        result = NexusStateInspector.GetPhysicsInformation();
                        break;
                    case "GET_GAMEOBJECT_DETAILS":
                        var name = operation.parameters.GetValueOrDefault("name", "");
                        result = NexusStateInspector.GetGameObjectDetails(name);
                        break;
                    case "GET_PROJECT_STATS":
                        result = NexusStateInspector.GetProjectStatistics();
                        break;
                    default:
                        // Normal operation.
                        // ESC-0107 fix: previously used `.Result` which blocks the
                        // main thread (delayCall) on a Task whose continuation may
                        // need to repost via UnitySynchronizationContext → classic
                        // SyncContext deadlock. ConfigureAwait(false) drops the
                        // captured context so the continuation can run on any
                        // thread; the body of ExecuteOperation is itself sync
                        // for most cases (e.g. RUN_CSHARP returns immediately).
                        var executor = new NexusUnityExecutor();
                        result = await executor.ExecuteOperation(operation).ConfigureAwait(false);

                        // Error check
                        if (result.StartsWith("Error") || result.Contains("not found") || result.Contains("failed"))
                        {
                            success = false;
                        }
                        break;
                }

                // 既存のMCP通信と同じフォーマットを使用
                var response = new Dictionary<string, object>
                {
                    ["type"] = "operation_result",
                    ["id"] = operationId,
                    ["content"] = result,
                    ["data"] = new Dictionary<string, object> { ["success"] = success }
                };

                SynLog.Info($"[Nexus WebSocket] Operation result: {result}");

                // Send result to MCP server
                await SendMessage(response);
            }
            catch (Exception e)
            {
                var errorResponse = new Dictionary<string, object>
                {
                    ["type"] = "operation_result",
                    ["id"] = operationId,
                    ["content"] = e.Message,
                    ["data"] = new Dictionary<string, object> { ["success"] = false }
                };

                Debug.LogError($"[Nexus WebSocket] Operation execution error: {e.Message}\n{e.StackTrace}");

                // Send error response to MCP server
                await SendMessage(errorResponse);
            }
        }
        
        private string ConvertCommandToOperationType(string command)
        {
            switch (command)
            {
                case "create_ui":
                    return "CREATE_UI";
                case "create_gameobject":
                    return "CREATE_GAMEOBJECT";
                case "instantiate_prefab":
                    return "INSTANTIATE_PREFAB";
                case "set_transform":
                    return "SET_PROPERTY";
                case "setup_camera":
                    return "SETUP_CAMERA";
                case "create_particle_system":
                    return "CREATE_PARTICLE_SYSTEM";
                case "setup_navmesh":
                    return "SETUP_NAVMESH";
                case "create_audio_mixer":
                    return "CREATE_AUDIO_MIXER";
                case "undo":
                    return "UNDO";
                case "redo":
                    return "REDO";
                case "get_history":
                    return "GET_HISTORY";

                // Information retrieval
                case "get_scene_info":
                    return "GET_SCENE_INFO";
                case "get_camera_info":
                    return "GET_CAMERA_INFO";
                case "get_terrain_info":
                    return "GET_TERRAIN_INFO";
                case "get_lighting_info":
                    return "GET_LIGHTING_INFO";
                case "get_material_info":
                    return "GET_MATERIAL_INFO";
                case "get_ui_info":
                    return "GET_UI_INFO";
                case "get_physics_info":
                    return "GET_PHYSICS_INFO";
                case "get_gameobject_details":
                    return "GET_GAMEOBJECT_DETAILS";
                case "list_assets":
                    return "LIST_ASSETS";
                case "get_project_stats":
                    return "GET_PROJECT_STATS";
                    
                default:
                    return command.ToUpper();
            }
        }
        
        public async Task SendMessage(object data)
        {
            if (!isConnected || webSocket.State != WebSocketState.Open)
            {
                SynLog.Warn("[Nexus WebSocket] Cannot send message: not connected");
                return;
            }
            
            try
            {
                var json = JsonConvert.SerializeObject(data);
                var bytes = Encoding.UTF8.GetBytes(json);
                
                await webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    cancellationTokenSource.Token
                );
            }
            catch (Exception e)
            {
                Debug.LogError($"[Nexus WebSocket] Send error: {e.Message}");
            }
        }
        
        private async Task HeartbeatLoop()
        {
            while (isConnected && !cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    // Send heartbeat every 30 seconds
                    await Task.Delay(30000, cancellationTokenSource.Token);

                    if (webSocket.State == WebSocketState.Open)
                    {
                        await SendMessage(new { type = "heartbeat", timestamp = DateTime.Now.Ticks });
                    }
                    else
                    {
                        SynLog.Warn("[Nexus WebSocket] Connection lost during heartbeat");
                        isConnected = false;
                        OnDisconnected?.Invoke();

                        // Attempt reconnection
                        if (shouldReconnect)
                        {
                            var reconnectTask = Task.Run(async () => await Connect());
                            LogTaskFault(reconnectTask, "HeartbeatReconnect");
                        }
                        break;
                    }
                }
                catch (Exception e)
                {
                    if (!cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        Debug.LogError($"[Nexus WebSocket] Heartbeat error: {e.Message}");
                    }
                    break;
                }
            }
        }
        
        public async Task Disconnect()
        {
            shouldReconnect = false; // Disable auto-reconnect
            
            if (webSocket != null && webSocket.State == WebSocketState.Open)
            {
                try
                {
                    await webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure, 
                        "Closing", 
                        cancellationTokenSource.Token
                    );
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Nexus WebSocket] Disconnect error: {e.Message}");
                }
            }
            
            cancellationTokenSource?.Cancel();
            cancellationTokenSource?.Dispose();
            isConnected = false;

            // Give background tasks a bounded window to unwind before disposing resources.
            if (backgroundTasks.Count > 0)
            {
                try { Task.WhenAll(backgroundTasks).Wait(TimeSpan.FromSeconds(2)); } catch { }
                backgroundTasks.Clear();
            }
        }

        /// <summary>
        /// Set server URL
        /// </summary>
        public void SetServerUrl(string url)
        {
            serverUrl = url;
            SynLog.Info($"[Nexus WebSocket] Server URL changed to: {url}");
        }
    }
    
    /// <summary>
    /// Manages WebSocket client updates
    /// </summary>
    [InitializeOnLoad]
    public static class NexusWebSocketUpdater
    {
        static NexusWebSocketUpdater()
        {
            EditorApplication.update += Update;
        }

        private static void Update()
        {
            NexusWebSocketClient.Instance?.ProcessMessages();
            NexusHTTPWebSocketClient.Instance?.ProcessMessages();
        }
    }

    /// <summary>
    /// WebSocket client for HTTP Server connection
    /// Separate from MCP WebSocket to allow both to run simultaneously
    /// </summary>
    public class NexusHTTPWebSocketClient
    {
        private ClientWebSocket webSocket;
        private CancellationTokenSource cancellationTokenSource;
        private bool isConnected = false;
        private Queue<string> messageQueue = new Queue<string>();
        private bool shouldReconnect = true;
        private int reconnectAttempts = 0;
        private const int maxReconnectAttempts = 10;
        private const int reconnectDelay = 1000;
        private const int CONNECT_TIMEOUT_SECONDS = 5;
        private readonly List<Task> backgroundTasks = new List<Task>();

        // Reentrancy guard. Connect() can be invoked from three paths (UI button,
        // NexusEditorMCPService HTTP auto-connect, and ReceiveLoop's finally auto-reconnect);
        // without this gate, concurrent calls clobber `webSocket`/`cancellationTokenSource`
        // and produce duplicate "Connecting/Connected/Disconnected" log spam.
        private volatile bool connectInFlight = false;
        private string serverUrl = "ws://127.0.0.1:8086";
        private int port = 8086;

        public bool IsConnected => isConnected;
        public int Port => port;

        private static NexusHTTPWebSocketClient instance;
        public static NexusHTTPWebSocketClient Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new NexusHTTPWebSocketClient();
                }
                return instance;
            }
        }

        private static void LogTaskFault(Task t, string label)
        {
            t.ContinueWith(
                x => SynLog.Warn($"[HTTP WebSocket] {label} faulted: {x.Exception?.GetBaseException().Message}"),
                TaskContinuationOptions.OnlyOnFaulted);
        }

        public async Task<bool> Connect(int httpPort)
        {
            // Reentrancy guard — silently drop concurrent Connect calls.
            if (connectInFlight)
            {
                return isConnected;
            }
            if (isConnected && port == httpPort)
            {
                return true;
            }

            connectInFlight = true;
            try
            {
                port = httpPort;
                serverUrl = $"ws://127.0.0.1:{port}";
                shouldReconnect = true;
                reconnectAttempts = 0;

                while (shouldReconnect && reconnectAttempts < maxReconnectAttempts)
                {
                    try
                    {
                        SynLog.Info($"[HTTP WebSocket] Connecting to {serverUrl}... (Attempt {reconnectAttempts + 1})");

                        var ws = new ClientWebSocket();
                        ws.Options.SetRequestHeader("X-Client-Type", "unity");
                        var cts = new CancellationTokenSource();
                        webSocket = ws;
                        cancellationTokenSource = cts;

                        using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token))
                        {
                            connectCts.CancelAfter(TimeSpan.FromSeconds(CONNECT_TIMEOUT_SECONDS));
                            await ws.ConnectAsync(new Uri(serverUrl), connectCts.Token);
                        }
                        isConnected = true;
                        reconnectAttempts = 0;

                        SynLog.Info($"[HTTP WebSocket] Connected to HTTP Server on port {port}!");

                        // Start message receive loop — snapshot ws+cts so a stale loop whose
                        // fields were overwritten by a subsequent Connect bails out cleanly.
                        var receiveTask = Task.Run(async () => await ReceiveLoop(ws, cts));
                        LogTaskFault(receiveTask, "ReceiveLoop");
                        backgroundTasks.Add(receiveTask);

                        // Notify Unity is ready
                        await SendMessage(new { type = "unity_ready", version = NexusVersion.Current });

                        return true;
                    }
                    catch (Exception e)
                    {
                        SynLog.Warn($"[HTTP WebSocket] Connection failed: {e.Message}");
                        isConnected = false;
                        reconnectAttempts++;

                        if (reconnectAttempts < maxReconnectAttempts && shouldReconnect)
                        {
                            await Task.Delay(reconnectDelay);
                        }
                    }
                }

                SynLog.Warn($"[HTTP WebSocket] Could not connect after {maxReconnectAttempts} attempts");
                return false;
            }
            finally
            {
                connectInFlight = false;
            }
        }

        private async Task ReceiveLoop(ClientWebSocket ws, CancellationTokenSource cts)
        {
            var buffer = new byte[8192];
            var messageBuilder = new StringBuilder();

            try
            {
                while (ws.State == WebSocketState.Open && !cts.IsCancellationRequested)
                {
                    var result = await ws.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        cts.Token
                    );

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                        if (result.EndOfMessage)
                        {
                            var message = messageBuilder.ToString();
                            messageBuilder.Clear();

                            lock (messageQueue)
                            {
                                messageQueue.Enqueue(message);
                            }
                        }
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                if (!cts.IsCancellationRequested)
                {
                    SynLog.Warn($"[HTTP WebSocket] Receive error: {e.Message}");
                }
            }
            finally
            {
                // Only mutate shared state + auto-reconnect if this loop is STILL the active
                // session. A later Connect() overwrites webSocket/cancellationTokenSource, so
                // reference-equality on the snapshots tells us whether we're stale.
                bool stillActive = ReferenceEquals(webSocket, ws)
                                && ReferenceEquals(cancellationTokenSource, cts);

                if (stillActive)
                {
                    isConnected = false;
                }

                if (shouldReconnect
                    && stillActive
                    && !cts.IsCancellationRequested
                    && !connectInFlight)
                {
                    var reconnectTask = Task.Run(async () =>
                    {
                        await Task.Delay(reconnectDelay);
                        if (shouldReconnect && !connectInFlight)
                            await Connect(port);
                    });
                    LogTaskFault(reconnectTask, "AutoReconnect");
                }
            }
        }

        public void ProcessMessages()
        {
            if (!isConnected) return;

            lock (messageQueue)
            {
                while (messageQueue.Count > 0)
                {
                    var message = messageQueue.Dequeue();

                    try
                    {
                        var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(message);
                        if (data != null && data.ContainsKey("type"))
                        {
                            ProcessOperation(data);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[HTTP WebSocket] Message processing error: {e.Message}");
                    }
                }
            }
        }

        private void ProcessOperation(Dictionary<string, object> data)
        {
            var type = data["type"].ToString();

            if (type == "operation")
            {
                var operationType = data.ContainsKey("operationType") ? data["operationType"].ToString() : "";
                var operationId = data.ContainsKey("operationId") ? data["operationId"].ToString() : "";
                var parameters = data.ContainsKey("parameters") ? data["parameters"] as Newtonsoft.Json.Linq.JObject : null;

                // Execute immediately (ProcessMessages is called from main thread via EditorApplication.update)
                _ = ExecuteOperation(operationType, operationId, parameters);
            }
        }

        private async Task ExecuteOperation(string operationType, string operationId, Newtonsoft.Json.Linq.JObject parameters)
        {
            try
            {
                var operation = new NexusUnityOperation
                {
                    type = operationType,
                    parameters = new Dictionary<string, string>()
                };

                // Convert parameters
                if (parameters != null)
                {
                    foreach (var prop in parameters.Properties())
                    {
                        var value = prop.Value;
                        if (value is Newtonsoft.Json.Linq.JObject jObj)
                        {
                            if (jObj.ContainsKey("x") && jObj.ContainsKey("y") && jObj.ContainsKey("z"))
                            {
                                operation.parameters[prop.Name] = $"{jObj["x"]},{jObj["y"]},{jObj["z"]}";
                            }
                            else
                            {
                                operation.parameters[prop.Name] = value.ToString();
                            }
                        }
                        else
                        {
                            operation.parameters[prop.Name] = value?.ToString() ?? "";
                        }
                    }
                }

                // Execute
                var executor = new NexusUnityExecutor();
                var result = await executor.ExecuteOperation(operation);
                var success = !result.StartsWith("Error") && !result.Contains("failed");

                // Send response
                var response = new Dictionary<string, object>
                {
                    ["operationId"] = operationId,
                    ["success"] = success,
                    ["result"] = result
                };

                await SendMessage(response);
            }
            catch (Exception e)
            {
                var errorResponse = new Dictionary<string, object>
                {
                    ["operationId"] = operationId,
                    ["success"] = false,
                    ["result"] = $"Error: {e.Message}"
                };

                await SendMessage(errorResponse);
            }
        }

        public async Task SendMessage(object data)
        {
            if (!isConnected || webSocket?.State != WebSocketState.Open)
            {
                return;
            }

            try
            {
                var json = JsonConvert.SerializeObject(data);
                var bytes = Encoding.UTF8.GetBytes(json);

                await webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    cancellationTokenSource.Token
                );
            }
            catch (Exception e)
            {
                Debug.LogError($"[HTTP WebSocket] Send error: {e.Message}");
            }
        }

        public async Task Disconnect()
        {
            shouldReconnect = false;
            isConnected = false;

            try
            {
                if (webSocket != null && webSocket.State == WebSocketState.Open)
                {
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                    {
                        await webSocket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Closing",
                            cts.Token
                        );
                    }
                }
            }
            catch { }

            try
            {
                cancellationTokenSource?.Cancel();
                cancellationTokenSource?.Dispose();
            }
            catch { }

            cancellationTokenSource = null;
            webSocket = null;

            if (backgroundTasks.Count > 0)
            {
                try { Task.WhenAll(backgroundTasks).Wait(TimeSpan.FromSeconds(2)); } catch { }
                backgroundTasks.Clear();
            }

            SynLog.Info("[HTTP WebSocket] Disconnected");
        }
    }
}