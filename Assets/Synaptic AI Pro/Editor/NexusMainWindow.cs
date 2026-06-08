using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using SynapticAIPro;
using System.Net.Http;
using Newtonsoft.Json;

namespace SynapticPro
{
    /// <summary>
    /// Synaptic Pro Main Window - MCP-based AI integration for Unity
    /// Provides centralized control for multiple AI providers through MCP
    /// </summary>
    public class NexusMainWindow : EditorWindow
    {
        // [MenuItem("Window/Synaptic Pro/AI Chat & Control")]
        public static void ShowWindow()
        {
            var window = GetWindow<NexusMainWindow>("Synaptic Pro");
            window.minSize = new Vector2(900, 700);
            window.Show();
        }

        private NexusMCPClient mcpClient;
        private Vector2 scrollPosition;
        private string currentInput = "";
        private List<ChatMessage> messages = new List<ChatMessage>();
        private bool isProcessing = false;
        private bool mcpServerStatus = false;

        // Repaint throttling: during a reconnect storm status/response events can fire dozens of
        // times per second; an unthrottled Repaint() on each locks the editor window.
        private double lastRepaintTime;
        private const double RepaintThrottleSeconds = 0.1; // ~10 Hz

        private void ThrottledRepaint()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - lastRepaintTime < RepaintThrottleSeconds) return;
            lastRepaintTime = now;
            Repaint();
        }
        
        // Tabs
        private int selectedTab = 0;
        private string[] tabNames = new string[] { "💬 Chat", "🛠️ Tools", "📊 Status", "⚙️ Settings" };
        
        // AI Providers
        private string selectedProvider = "claude";
        private string[] availableProviders = new string[] { "claude", "gemini", "openai", "ollama" };
        
        // Tool categories
        private bool showUnityTools = true;
        private bool showScriptTools = true;
        private bool showUITools = true;
        
        private class ChatMessage
        {
            public string content;
            public bool isUser;
            public DateTime timestamp;
            public string provider;
            public bool hasError;
        }
        
        private GUIStyle headerStyle;
        private GUIStyle chatUserStyle;
        private GUIStyle chatAIStyle;
        private GUIStyle toolButtonStyle;
        private GUIStyle statusStyle;
        
        private void OnEnable()
        {
            // MCPClient is not used in editor window
            // Actual MCP functionality only works in play mode
            if (Application.isPlaying)
            {
                mcpClient = NexusMCPClient.Instance;
                if (mcpClient != null)
                {
                    mcpClient.OnMessageReceived += OnAIResponse;
                    mcpClient.OnConnected += OnMCPConnected;
                    mcpClient.OnDisconnected += OnMCPDisconnected;
                    mcpClient.OnError += OnMCPError;
                }
            }
            
            // Set up WebSocket client event listeners
            var webSocketClient = NexusWebSocketClient.Instance;
            if (webSocketClient != null)
            {
                webSocketClient.OnClaudeResponse += OnClaudeResponseReceived;
                webSocketClient.OnChatStatusUpdate += OnChatStatusReceived;
            }

            _ = CheckMCPServerStatus();
        }
        
        private void OnClaudeResponseReceived(string sessionId, string message)
        {
            // Add real-time response from Claude Desktop to chat
            messages.Add(new ChatMessage
            {
                content = message,
                isUser = false,
                timestamp = DateTime.Now,
                provider = "claude",
                hasError = false
            });
            
            // Repaint window
            ThrottledRepaint();
            
            SynLog.Info($"[Synaptic] Claude response received: {message}");
        }
        
        private void OnChatStatusReceived(string status)
        {
            // Display status update in chat
            messages.Add(new ChatMessage
            {
                content = $"🔄 {status}",
                isUser = false,
                timestamp = DateTime.Now,
                provider = "system",
                hasError = false
            });
            
            ThrottledRepaint();
        }

        private void OnDisable()
        {
            if (mcpClient != null)
            {
                mcpClient.OnMessageReceived -= OnAIResponse;
                mcpClient.OnConnected -= OnMCPConnected;
                mcpClient.OnDisconnected -= OnMCPDisconnected;
                mcpClient.OnError -= OnMCPError;
            }
            
            // Disconnect WebSocket connection
            _ = NexusWebSocketClient.Instance.Disconnect();
        }
        
        private void InitializeStyles()
        {
            headerStyle = new GUIStyle(EditorStyles.largeLabel)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.2f, 0.7f, 1f) }
            };
            
            chatUserStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(15, 15, 10, 10),
                fontSize = 14,
                wordWrap = true,
                normal = { 
                    textColor = Color.white,
                    background = CreateColorTexture(new Color(0.2f, 0.6f, 1f, 0.3f))
                }
            };
            
            chatAIStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(15, 15, 10, 10),
                fontSize = 14,
                wordWrap = true,
                normal = { 
                    textColor = new Color(0.9f, 0.9f, 0.9f),
                    background = CreateColorTexture(new Color(0.3f, 0.3f, 0.3f, 0.5f))
                }
            };
            
            toolButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                padding = new RectOffset(10, 10, 8, 8),
                margin = new RectOffset(2, 2, 2, 2)
            };
            
            statusStyle = new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = 12,
                padding = new RectOffset(10, 10, 5, 5)
            };
        }
        
        private Texture2D CreateColorTexture(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }
        
        private void OnGUI()
        {
            if (headerStyle == null)
                InitializeStyles();
            
            DrawHeader();
            DrawTabs();
            
            switch (selectedTab)
            {
                case 0:
                    DrawChatTab();
                    break;
                case 1:
                    DrawToolsTab();
                    break;
                case 2:
                    DrawStatusTab();
                    break;
                case 3:
                    DrawSettingsTab();
                    break;
            }
        }
        
        private void DrawHeader()
        {
            EditorGUILayout.Space(10);
            GUILayout.Label("🔗 Synaptic Pro", headerStyle, GUILayout.Height(30));
            
            // Status bar
            EditorGUILayout.BeginHorizontal(statusStyle);
            
            var statusIcon = mcpServerStatus ? "🟢" : "🔴";
            var statusText = mcpServerStatus ? "MCP Server Connected" : "MCP Server Disconnected";
            var statusColor = mcpServerStatus ? Color.green : Color.red;
            
            var oldColor = GUI.contentColor;
            GUI.contentColor = statusColor;
            GUILayout.Label($"{statusIcon} {statusText}");
            GUI.contentColor = oldColor;
            
            GUILayout.FlexibleSpace();
            
            // Provider selection
            GUILayout.Label("Provider:");
            selectedProvider = EditorGUILayout.Popup(
                Array.IndexOf(availableProviders, selectedProvider),
                availableProviders,
                GUILayout.Width(100)
            ) >= 0 ? availableProviders[EditorGUILayout.Popup(Array.IndexOf(availableProviders, selectedProvider), availableProviders, GUILayout.Width(100))] : selectedProvider;
            
            if (GUILayout.Button("🔄", GUILayout.Width(30)))
            {
                _ = CheckMCPServerStatus();
            }
            
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(5);
        }
        
        private void DrawTabs()
        {
            selectedTab = GUILayout.Toolbar(selectedTab, tabNames, GUILayout.Height(25));
            EditorGUILayout.Space(5);
        }
        
        private void DrawChatTab()
        {
            EditorGUILayout.BeginVertical();
            
            // Chat area
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
            
            if (messages.Count == 0)
            {
                DrawWelcomeMessage();
            }
            else
            {
                foreach (var message in messages)
                {
                    DrawChatMessage(message);
                }
            }
            
            if (isProcessing)
            {
                DrawProcessingMessage();
            }
            
            EditorGUILayout.EndScrollView();
            
            // Input area
            DrawChatInput();
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawWelcomeMessage()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            GUILayout.Label("🚀 Welcome to Synaptic Pro!", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            GUILayout.Label("Nexus provides unified access to multiple AI providers through MCP (Model Context Protocol).");
            EditorGUILayout.Space(5);
            
            GUILayout.Label("Features:");
            GUILayout.Label("• Multi-AI support (Claude, Gemini, OpenAI, Ollama)");
            GUILayout.Label("• Direct Unity integration");
            GUILayout.Label("• Advanced tool system");
            GUILayout.Label("• Real-time collaboration");
            
            EditorGUILayout.Space(10);
            
            if (!mcpServerStatus)
            {
                EditorGUILayout.HelpBox("MCP Server is not running. Please start the server from the Setup tab.", MessageType.Warning);
                
                if (GUILayout.Button("🚀 Open MCP Setup"))
                {
                    NexusMCPSetupWindow.ShowWindow();
                }
            }
            else
            {
                GUILayout.Label("Start by typing a message below or use the Tools tab for quick actions.");
            }
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawChatMessage(ChatMessage message)
        {
            EditorGUILayout.BeginHorizontal();
            
            if (message.isUser)
            {
                GUILayout.FlexibleSpace();
                
                EditorGUILayout.BeginVertical();
                GUILayout.Label($"👤 You - {message.timestamp:HH:mm}", EditorStyles.miniLabel);
                GUILayout.Label(message.content, chatUserStyle, GUILayout.MaxWidth(position.width * 0.7f));
                EditorGUILayout.EndVertical();
            }
            else
            {
                EditorGUILayout.BeginVertical();
                
                var providerIcon = GetProviderIcon(message.provider);
                var timeText = message.timestamp.ToString("HH:mm");
                var statusIcon = message.hasError ? "❌" : "✅";
                
                GUILayout.Label($"{providerIcon} {message.provider} {statusIcon} - {timeText}", EditorStyles.miniLabel);
                GUILayout.Label(message.content, chatAIStyle, GUILayout.MaxWidth(position.width * 0.7f));
                EditorGUILayout.EndVertical();
                
                GUILayout.FlexibleSpace();
            }
            
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(5);
        }
        
        private void DrawProcessingMessage()
        {
            EditorGUILayout.BeginHorizontal();
            
            var dots = new string('.', ((int)(EditorApplication.timeSinceStartup * 2) % 4));
            var providerIcon = GetProviderIcon(selectedProvider);
            
            GUILayout.Label($"{providerIcon} {selectedProvider} is thinking{dots}", chatAIStyle, GUILayout.MaxWidth(position.width * 0.7f));
            GUILayout.FlexibleSpace();
            
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(5);
        }
        
        private void DrawChatInput()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            GUILayout.Label("💬 Message", EditorStyles.boldLabel);
            
            GUI.SetNextControlName("ChatInput");
            currentInput = EditorGUILayout.TextArea(currentInput, GUILayout.MinHeight(60));
            
            EditorGUILayout.BeginHorizontal();
            
            GUILayout.FlexibleSpace();
            
            EditorGUI.BeginDisabledGroup(!mcpServerStatus || isProcessing || string.IsNullOrWhiteSpace(currentInput));
            
            if (GUILayout.Button($"📤 Send to {selectedProvider}", GUILayout.Width(150), GUILayout.Height(30)))
            {
                SendChatMessage();
            }
            
            EditorGUI.EndDisabledGroup();
            
            EditorGUILayout.EndHorizontal();
            
            // Keyboard shortcuts
            if (Event.current.type == EventType.KeyDown && Event.current.control && Event.current.keyCode == KeyCode.Return)
            {
                if (mcpServerStatus && !isProcessing && !string.IsNullOrWhiteSpace(currentInput))
                {
                    SendChatMessage();
                    Event.current.Use();
                }
            }
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawToolsTab()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            
            // Unity Tools
            showUnityTools = EditorGUILayout.Foldout(showUnityTools, "🎯 Unity Tools", true);
            if (showUnityTools)
            {
                EditorGUI.indentLevel++;
                DrawUnityTools();
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.Space(10);
            
            // Script Tools
            showScriptTools = EditorGUILayout.Foldout(showScriptTools, "📝 Script Tools", true);
            if (showScriptTools)
            {
                EditorGUI.indentLevel++;
                DrawScriptTools();
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.Space(10);
            
            // UI Tools
            showUITools = EditorGUILayout.Foldout(showUITools, "🖼️ UI Tools", true);
            if (showUITools)
            {
                EditorGUI.indentLevel++;
                DrawUITools();
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.EndScrollView();
        }
        
        private void DrawUnityTools()
        {
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("🎲 Create GameObject", toolButtonStyle))
            {
                CreateGameObjectTool();
            }
            
            if (GUILayout.Button("🔧 Add Component", toolButtonStyle))
            {
                AddComponentTool();
            }
            
            if (GUILayout.Button("🏗️ Create Prefab", toolButtonStyle))
            {
                CreatePrefabTool();
            }
            
            EditorGUILayout.EndHorizontal();
        }
        
        private void DrawScriptTools()
        {
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("📄 Generate Script", toolButtonStyle))
            {
                GenerateScriptTool();
            }
            
            if (GUILayout.Button("🔍 Analyze Code", toolButtonStyle))
            {
                AnalyzeCodeTool();
            }
            
            if (GUILayout.Button("🚨 Fix Errors", toolButtonStyle))
            {
                FixErrorsTool();
            }
            
            EditorGUILayout.EndHorizontal();
        }
        
        private void DrawUITools()
        {
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("🖼️ Create UI Panel", toolButtonStyle))
            {
                CreateUIPanelTool();
            }
            
            if (GUILayout.Button("🔘 Create Button", toolButtonStyle))
            {
                CreateButtonTool();
            }
            
            if (GUILayout.Button("📝 Create Text", toolButtonStyle))
            {
                CreateTextTool();
            }
            
            EditorGUILayout.EndHorizontal();
        }
        
        private void DrawStatusTab()
        {
            EditorGUILayout.BeginVertical();
            
            // MCP Server Status
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("🖥️ MCP Server Status", EditorStyles.boldLabel);
            
            var serverIcon = mcpServerStatus ? "🟢" : "🔴";
            var serverText = mcpServerStatus ? "Connected" : "Disconnected";
            GUILayout.Label($"{serverIcon} Status: {serverText}");
            
            if (mcpClient != null)
            {
                GUILayout.Label($"🔗 WebSocket: {(mcpClient.IsConnected ? "Connected" : "Disconnected")}");
            }
            
            GUILayout.Label("📡 Server URL: ws://127.0.0.1:8090");
            GUILayout.Label("🌐 HTTP API: http://localhost:3000");
            
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(10);
            
            // Supported AI Applications
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("🤖 Supported AI Applications", EditorStyles.boldLabel);

            GUILayout.Label("You can control Unity with the following AI apps:", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(5);
            
            foreach (var provider in availableProviders)
            {
                var icon = GetProviderIcon(provider);
                var appName = provider == "claude" ? "Claude Desktop" :
                             provider == "gemini" ? "Gemini Desktop" :
                             provider == "openai" ? "ChatGPT Desktop" :
                             provider == "ollama" ? "Ollama" : provider;
                
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"  {icon} {appName}", GUILayout.Width(150));
                GUILayout.Label("✓ Auto-configured", EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }
            
            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox("💡 Please select Unity MCP Server in your AI app", MessageType.Info);

            EditorGUILayout.Space(5);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("🎯 Correct Usage", EditorStyles.boldLabel);

            GUILayout.Label("Example questions in Claude Desktop:", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(3);

            GUILayout.Label("❌ Wrong: \"I want to make a 3D watermelon game\"", EditorStyles.miniLabel);
            GUILayout.Label("✅ Correct: \"Use available Unity tools to create a 3D watermelon game\"", EditorStyles.miniLabel);

            EditorGUILayout.Space(3);
            GUILayout.Label("Or first ask \"Tell me about available Unity tools\"", EditorStyles.wordWrappedLabel);

            EditorGUILayout.EndVertical();
            
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(10);
            
            // Actions
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("🔧 Actions", EditorStyles.boldLabel);
            
            if (GUILayout.Button("🔄 Refresh Status"))
            {
                _ = CheckMCPServerStatus();
            }
            
            if (GUILayout.Button("🚀 Open MCP Setup"))
            {
                NexusMCPSetupWindow.ShowWindow();
            }
            
            if (GUILayout.Button("📋 View Logs"))
            {
                // Open console or log window
                EditorUtility.DisplayDialog("Logs", "Check Unity Console for Nexus logs", "OK");
            }
            
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawSettingsTab()
        {
            EditorGUILayout.BeginVertical();
            
            GUILayout.Label("⚙️ Nexus Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space(10);
            
            // Server Settings
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("🖥️ Server Configuration", EditorStyles.boldLabel);
            
            var currentUrl = mcpClient?.IsConnected == true ? "ws://127.0.0.1:8090" : "ws://127.0.0.1:8090";
            var newUrl = EditorGUILayout.TextField("WebSocket URL:", currentUrl);
            
            if (newUrl != currentUrl && mcpClient != null)
            {
                mcpClient.SetServerUrl(newUrl);
            }
            
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(10);
            
            // Provider Settings
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("🤖 Default Provider", EditorStyles.boldLabel);
            
            var currentProviderIndex = Array.IndexOf(availableProviders, selectedProvider);
            var newProviderIndex = EditorGUILayout.Popup("Default AI:", currentProviderIndex, availableProviders);
            
            if (newProviderIndex != currentProviderIndex && newProviderIndex >= 0)
            {
                selectedProvider = availableProviders[newProviderIndex];
            }
            
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(10);
            
            // Advanced Settings
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("🔧 Advanced", EditorStyles.boldLabel);
            
            if (GUILayout.Button("🗑️ Clear Chat History"))
            {
                if (EditorUtility.DisplayDialog("Clear History", "Are you sure you want to clear all chat history?", "Yes", "Cancel"))
                {
                    messages.Clear();
                }
            }
            
            if (GUILayout.Button("📁 Open Project Folder"))
            {
                EditorUtility.RevealInFinder(Application.dataPath);
            }
            
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.EndVertical();
        }
        
        private async void SendChatMessage()
        {
            if (string.IsNullOrWhiteSpace(currentInput) || isProcessing)
                return;
            
            // Check WebSocket connection
            if (!NexusWebSocketClient.Instance.IsConnected)
            {
                SynLog.Warn("[Synaptic] WebSocket not connected. Attempting to connect...");
                await CheckMCPServerStatus();
                if (!mcpServerStatus)
                {
                    AddMessage("❗ Cannot connect to MCP Server. Please verify that the MCP Server is running.", false, true);
                    return;
                }
            }
            
            var userMessage = currentInput.Trim();
            AddMessage(userMessage, true);
            
            currentInput = "";
            isProcessing = true;
            
            try
            {
                // MCPClient only works in Play Mode, so send HTTP requests directly in Editor
                if (Application.isPlaying && mcpClient != null)
                {
                    var response = await mcpClient.SendChatMessage(userMessage, selectedProvider);
                    AddMessage(response, false);
                }
                else if (NexusWebSocketClient.Instance?.IsConnected == true)
                {
                    // Send message to desktop app via WebSocket
                    var chatMessage = new
                    {
                        type = "chat_message",
                        message = userMessage,
                        projectName = Application.productName
                    };
                    
                    await NexusWebSocketClient.Instance.SendMessage(chatMessage);
                }
                else
                {
                    // Use legacy HTTP API if WebSocket is not connected
                    using (var client = new System.Net.Http.HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(30);
                        var content = new System.Net.Http.StringContent(
                            JsonConvert.SerializeObject(new { message = userMessage, provider = selectedProvider }),
                            System.Text.Encoding.UTF8,
                            "application/json"
                        );
                        
                        // Try port range 3000-3010
                        for (int port = 3000; port <= 3010; port++)
                        {
                            try
                            {
                                var response = await client.PostAsync($"http://localhost:{port}/api/chat", content);
                                if (response.IsSuccessStatusCode)
                                {
                                    var json = await response.Content.ReadAsStringAsync();
                                    var result = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                                    AddMessage(result["response"], false);
                                    return;
                                }
                            }
                            catch
                            {
                                // Try next port
                                continue;
                            }
                        }

                        throw new Exception("Cannot connect to MCP Server. Please verify that the server is running.");
                    }
                }
            }
            catch (Exception e)
            {
                AddMessage($"Error: {e.Message}", false, true);
                Debug.LogError($"[Synaptic] Chat error: {e}");
            }
            finally
            {
                isProcessing = false;
                ThrottledRepaint();
            }
        }
        
        private void AddMessage(string content, bool isUser, bool hasError = false)
        {
            messages.Add(new ChatMessage
            {
                content = content,
                isUser = isUser,
                timestamp = DateTime.Now,
                provider = isUser ? "user" : selectedProvider,
                hasError = hasError
            });
            
            // Auto-scroll to bottom
            scrollPosition.y = float.MaxValue;
            ThrottledRepaint();
        }
        
        private string GetProviderIcon(string provider)
        {
            switch (provider.ToLower())
            {
                case "claude": return "🤖";
                case "gemini": return "💎";
                case "openai": return "🧠";
                case "ollama": return "🦙";
                default: return "🤖";
            }
        }
        
        private async Task CheckMCPServerStatus()
        {
            try
            {
                // Check MCP server status via WebSocket connection
                if (!NexusWebSocketClient.Instance.IsConnected)
                {
                    SynLog.Info("[Synaptic] Starting WebSocket connection...");
                    var connected = await NexusWebSocketClient.Instance.Connect("ws://127.0.0.1:8090");
                    if (connected)
                    {
                        SynLog.Info("[Synaptic] WebSocket connection successful!");
                        mcpServerStatus = true;
                    }
                    else
                    {
                        // If connection fails, try other ports
                        for (int port = 8090; port <= 8084; port++)
                        {
                            connected = await NexusWebSocketClient.Instance.Connect($"ws://localhost:{port}");
                            if (connected)
                            {
                                SynLog.Info($"[Synaptic] WebSocket connection successful! Port: {port}");
                                mcpServerStatus = true;
                                break;
                            }
                        }
                    }
                    mcpServerStatus = connected;
                }
                else
                {
                    // Already connected
                    mcpServerStatus = true;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] MCP server status check error: {e.Message}");
                mcpServerStatus = false;
            }
            
            ThrottledRepaint();
        }
        
        private async Task<bool> CheckServerRunning()
        {
            try
            {
                // Check MCP server in port range 3000-3010
                for (int port = 3000; port <= 3010; port++)
                {
                    try
                    {
                        using (var client = new System.Net.Http.HttpClient())
                        {
                            client.Timeout = TimeSpan.FromSeconds(1);
                            var response = await client.GetAsync($"http://localhost:{port}/");
                            if (response != null)
                            {
                                SynLog.Info($"[Synaptic] MCP server detected on port {port}");
                                return true;
                            }
                        }
                    }
                    catch
                    {
                        // Not found on this port
                        continue;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
        
        // Tool methods
        private void CreateGameObjectTool()
        {
            var name = "NewGameObject";

            // Execute directly in Unity Editor
            var go = new GameObject(name);
            Selection.activeGameObject = go;
            AddMessage($"Created GameObject: {name}", false);
        }
        
        private void AddComponentTool()
        {
            if (Selection.activeGameObject == null)
            {
                AddMessage("Please select a GameObject first", false, true);
                return;
            }
            
            var target = Selection.activeGameObject.name;
            Selection.activeGameObject.AddComponent<Rigidbody>();
            AddMessage($"Added Rigidbody to {target}", false);
        }
        
        private void CreatePrefabTool()
        {
            AddMessage("Creating prefab from selected object...", false);
        }
        
        private void GenerateScriptTool()
        {
            currentInput = "Generate a MonoBehaviour script for ";
            GUI.FocusControl("ChatInput");
        }
        
        private void AnalyzeCodeTool()
        {
            currentInput = "Analyze the code in my project and suggest improvements";
            GUI.FocusControl("ChatInput");
        }
        
        private void FixErrorsTool()
        {
            currentInput = "Fix any compilation errors in my project";
            GUI.FocusControl("ChatInput");
        }
        
        private void CreateUIPanelTool()
        {
            // Create Canvas if it doesn't exist
            GameObject canvas = GameObject.Find("Canvas");
            if (canvas == null)
            {
                canvas = new GameObject("Canvas", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
                canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            }
            
            var panel = new GameObject("UIPanel", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            panel.transform.SetParent(canvas.transform, false);
            panel.GetComponent<UnityEngine.UI.Image>().color = new Color(0, 0, 0, 0.5f);
            panel.GetComponent<RectTransform>().sizeDelta = new Vector2(300, 200);
            
            Selection.activeGameObject = panel;
            AddMessage("Created UI Panel", false);
        }
        
        private void CreateButtonTool()
        {
            // Create Canvas if it doesn't exist
            GameObject canvas = GameObject.Find("Canvas");
            if (canvas == null)
            {
                canvas = new GameObject("Canvas", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
                canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            }
            
            var button = new GameObject("Button", typeof(RectTransform), typeof(UnityEngine.UI.Button), typeof(UnityEngine.UI.Image));
            button.transform.SetParent(canvas.transform, false);
            button.GetComponent<RectTransform>().sizeDelta = new Vector2(160, 30);
            
            var text = new GameObject("Text", typeof(RectTransform), typeof(UnityEngine.UI.Text));
            text.transform.SetParent(button.transform, false);
            text.GetComponent<UnityEngine.UI.Text>().text = "Click Me";
            text.GetComponent<UnityEngine.UI.Text>().alignment = TextAnchor.MiddleCenter;
            text.GetComponent<UnityEngine.UI.Text>().color = Color.black;
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            
            Selection.activeGameObject = button;
            AddMessage("Created UI Button", false);
        }
        
        private void CreateTextTool()
        {
            // Create Canvas if it doesn't exist
            GameObject canvas = GameObject.Find("Canvas");
            if (canvas == null)
            {
                canvas = new GameObject("Canvas", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
                canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            }
            
            var text = new GameObject("Text", typeof(RectTransform), typeof(UnityEngine.UI.Text));
            text.transform.SetParent(canvas.transform, false);
            text.GetComponent<UnityEngine.UI.Text>().text = "Hello World";
            text.GetComponent<UnityEngine.UI.Text>().alignment = TextAnchor.MiddleCenter;
            text.GetComponent<RectTransform>().sizeDelta = new Vector2(200, 50);
            
            Selection.activeGameObject = text;
            AddMessage("Created UI Text", false);
        }
        
        // Event handlers
        private void OnAIResponse(string response)
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                AddMessage(response, false);
            };
        }
        
        private void OnMCPConnected()
        {
            mcpServerStatus = true;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                AddMessage("🟢 Connected to MCP Server", false);
            };
        }
        
        private void OnMCPDisconnected()
        {
            mcpServerStatus = false;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                AddMessage("🔴 Disconnected from MCP Server", false, true);
            };
        }
        
        private void OnMCPError(string error)
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                AddMessage($"❌ MCP Error: {error}", false, true);
            };
        }
    }
}
