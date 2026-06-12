using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using Newtonsoft.Json;
using SynapticAIPro;

namespace SynapticPro
{
    /// <summary>
    /// MCP Server Setup and Local CLI Management Window
    /// One-touch setup for MCP server and configuration of various AI tools
    /// </summary>
    public class NexusMCPSetupWindow : EditorWindow
    {
        [MenuItem("Tools/Synaptic Pro/Synaptic Setup", false, 0)]
        public static void ShowWindow()
        {
            var window = GetWindow<NexusMCPSetupWindow>("Synaptic Pro Setup");
            // 小さい画面でも使えるようminSizeを緩和（旧: 800x800で固定気味）
            window.minSize = new Vector2(480, 480);
            window.Show();
        }

        private NexusMCPSetupManager mcpSetupManager;
        private NexusMCPSetupManager.SetupStatus mcpStatus;
        private Vector2 scrollPosition;
        private bool mcpServerRunning = false;

        // Tabs
        private int selectedTab = 0;
        private string[] tabNames = new string[] { "AI Connection", "HTTP Server", "Diagnostics", "Help" };

        // MCP Settings
        private int mcpPort = 8090;
        private int wsPort = 8090;

        // HTTP Server Settings
        private int httpPort = 8086;
        private bool httpServerRunning = false;

        // Use project-specific keys for HTTP settings
        private static string ProjectKey => Application.dataPath.GetHashCode().ToString("X8");
        private static string PREF_HTTP_PORT => $"SynapticPro_HTTP_Port_{ProjectKey}";
        private static string PREF_HTTP_AUTO_START => $"SynapticPro_HTTP_AutoStart_{ProjectKey}";

        // External HTTP Server Process
        private static System.Diagnostics.Process externalHttpProcess = null;
        private static bool externalHttpRunning = false;
        private static bool externalAutoStartAttempted = false;

        // Port-probe throttle. Probing runs on a background thread; UI reads the cached
        // `externalHttpRunning` so OnGUI never blocks on a TCP connect.
        private static double lastPortCheckTime = -100;
        private static bool portCheckInFlight = false;
        private const double PORT_CHECK_INTERVAL_SEC = 2.0;
        private const int PORT_CHECK_TIMEOUT_MS = 500;

        // Auto-Update
        private static bool updateCheckDone = false;
        private static bool updateAvailable = false;
        private static string latestVersion = "";
        private static string updateUrl = "";
        private static string updateMethod = "browser"; // "browser" or "auto"
        private static bool isDownloadingUpdate = false;

        // Distribution type: "assetstore" or "booth"
        // Asset Store版はfalse（ブラウザでAsset Storeに飛ばす）、BOOTH/サイト版はtrue（自動ダウンロード）
        private static readonly bool ENABLE_SELF_UPDATE = false; // Asset Store版リリース時にfalseに変更

        public static bool HttpAutoStartEnabled
        {
            get => EditorPrefs.GetBool(PREF_HTTP_AUTO_START, false);
            set => EditorPrefs.SetBool(PREF_HTTP_AUTO_START, value);
        }

        // Animation
        private bool isConnecting = false;
        private float animationTime = 0f;
        private const float CONNECTION_TIMEOUT = 60f; // 60 second timeout

        // Setup state management
        private bool mcpConfigured = false;

        // AI Client selection (v1.1.3)
        private enum AIClientType
        {
            ClaudeDesktopOrCursor,      // Full mode (index.js) - 246 tools
            CursorOrLMStudioEssential,  // Essential mode (index-essential.js) - 80 tools
            GitHubCopilot,              // Dynamic mode (hub-server.js) - 8→dynamic tools
            TokenSuperSaveMode          // Experimental: 3 meta-tools only (index-supersave.js)
        }
        private AIClientType selectedAIClient = AIClientType.TokenSuperSaveMode;

        private string[] connectingMessages = new string[] 
        {
            "Preparing AI Connection",
            "Starting MCP Server",
            "Establishing connection with desktop AI apps",
            "Auto-generating configuration files",
            "AI connection almost ready"
        };
        
        private GUIStyle headerStyle;
        private GUIStyle setupButtonStyle;
        private GUIStyle statusStyle;
        
        private void OnEnable()
        {
            mcpSetupManager = NexusMCPSetupManager.Instance;

            // Load HTTP Server settings first (no blocking)
            httpPort = EditorPrefs.GetInt(PREF_HTTP_PORT, 8086);

            // Check if server is still running
            CheckExternalHttpServerStatus();

            // Try auto-start
            TryHttpAutoStart();

            // Check for updates (background, 1日1回)
            CheckForUpdates();

            // Refresh MCP status in background thread (completely non-blocking)
            System.Threading.ThreadPool.QueueUserWorkItem(async _ =>
            {
                try
                {
                    mcpStatus = await mcpSetupManager.CheckSetupStatus();
                    // UI更新はメインスレッドで
                    EditorApplication.delayCall += () => { if (this) Repaint(); };
                }
                catch { }
            });
        }

        private void OnDisable()
        {
            // Save HTTP Server settings
            EditorPrefs.SetInt(PREF_HTTP_PORT, httpPort);
            // サーバーはここで止めない（ドメインリロードで毎回止まるため）
            // Unity終了時はRegisterQuitHandlerで停止する
        }

        // Unity終了時にNode.jsプロセスを確実に停止
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterQuitHandler()
        {
            EditorApplication.quitting += () =>
            {
                var port = EditorPrefs.GetInt(PREF_HTTP_PORT, 8086);
                try
                {
                    // ポートを使っているプロセスを強制終了
                    if (Application.platform == RuntimePlatform.WindowsEditor)
                    {
                        var p = new System.Diagnostics.Process
                        {
                            StartInfo = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "cmd.exe",
                                Arguments = $"/c \"for /f \"tokens=5\" %a in ('netstat -aon ^| findstr :{port} ^| findstr LISTENING') do taskkill /PID %a /F\"",
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };
                        p.Start();
                        p.WaitForExit(3000);
                    }
                    else
                    {
                        var p = new System.Diagnostics.Process
                        {
                            StartInfo = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "/bin/bash",
                                Arguments = $"-c \"lsof -ti:{port} | xargs kill -9 2>/dev/null\"",
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };
                        p.Start();
                        p.WaitForExit(3000);
                    }
                    SynLog.Info($"[Synaptic] Killed HTTP server process on port {port} (Unity quitting)");
                }
                catch { }
            };
        }

        // Unity起動時にアップデートチェック（Setup Windowを開かなくても通知）
        /// <summary>
        /// After domain reload, recover the previously spawned detached
        /// node.exe by re-attaching to its PID stored in SessionState.
        /// Only the WebSocket needs reconnecting — the HTTP process itself
        /// was started detached and survives across reloads.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void RestoreDetachedHttpServerOnReload()
        {
            EditorApplication.delayCall += () =>
            {
                // Windows detached path: recover by PID stored in SessionState.
                if (Application.platform == RuntimePlatform.WindowsEditor &&
                    SynapticDetachedProcess.IsStoredProcessAlive(out int pid, out int wPort))
                {
                    externalHttpRunning = true;
                    EditorPrefs.SetInt(PREF_HTTP_PORT, wPort);
                    SynLog.Info($"[Synaptic] Recovered detached HTTP server PID={pid} on port {wPort}. Reconnecting WebSocket.");
                    _ = ReconnectWebSocketOnlyAsync(wPort);
                    return;
                }

                // Generic path (Mac/Linux, or Windows fallback): probe last-used
                // port; if a server is listening, just reconnect WS.
                int port = EditorPrefs.GetInt(PREF_HTTP_PORT, 8086);
                if (IsPortListeningStatic(port))
                {
                    externalHttpRunning = true;
                    SynLog.Info($"[Synaptic] HTTP server alive on port {port} after reload. Reconnecting WebSocket.");
                    _ = ReconnectWebSocketOnlyAsync(port);
                    return;
                }

                // Server is dead. If the user opted into HTTP auto-start,
                // bring the Setup window up so its OnEnable → TryHttpAutoStart
                // path can re-spawn the node process. Without this nudge the
                // NexusHTTPWebSocketClient retry loop just hammers a closed
                // port forever (the client cannot start its own server).
                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    // Stored PID is stale and port dead — clear for fresh spawn.
                    SynapticDetachedProcess.ClearStoredPid();
                }
                if (HttpAutoStartEnabled)
                {
                    SynLog.Info("[Synaptic] HTTP server not running after reload; opening Setup window to auto-restart.");
                    // GetWindow opens existing or creates new; OnEnable fires
                    // TryHttpAutoStart which spawns the node process.
                    EditorWindow.GetWindow<NexusMCPSetupWindow>("Synaptic Setup", true);
                }
            };
        }

        /// <summary>
        /// Static port-alive probe usable from [InitializeOnLoadMethod].
        /// Lightweight TCP connect with short timeout.
        /// </summary>
        private static bool IsPortListeningStatic(int port)
        {
            try
            {
                using (var tcp = new System.Net.Sockets.TcpClient())
                {
                    var ar = tcp.BeginConnect("127.0.0.1", port, null, null);
                    bool ok = ar.AsyncWaitHandle.WaitOne(500);
                    if (!ok) return false;
                    try { tcp.EndConnect(ar); return true; } catch { return false; }
                }
            }
            catch { return false; }
        }

        /// <summary>
        /// Reconnect WebSocket to an already-running HTTP server after domain
        /// reload. Does not start the server or open any UI.
        /// </summary>
        private static async System.Threading.Tasks.Task ReconnectWebSocketOnlyAsync(int port)
        {
            // Light delay so the editor finishes loading other systems first.
            await System.Threading.Tasks.Task.Delay(500);
            for (int i = 0; i < 3; i++)
            {
                bool ok = await NexusHTTPWebSocketClient.Instance.Connect(port);
                if (ok)
                {
                    SynLog.Info($"[Synaptic] WebSocket re-attached to port {port} after reload.");
                    return;
                }
                await System.Threading.Tasks.Task.Delay(1500);
            }
            SynLog.Warn($"[Synaptic] Could not reconnect WebSocket to port {port}. Use Setup window to reconnect manually.");
        }

        [InitializeOnLoadMethod]
        private static void CheckForUpdatesOnStartup()
        {
            // エディタ起動直後は少し待つ
            EditorApplication.delayCall += () =>
            {
                // 1日1回チェック
                var lastCheck = EditorPrefs.GetString("SynapticPro_LastStartupUpdateCheck", "");
                var today = DateTime.Now.ToString("yyyy-MM-dd");
                if (lastCheck == today) return;
                EditorPrefs.SetString("SynapticPro_LastStartupUpdateCheck", today);

                var currentVersion = NexusVersion.Current;
                var dist = ENABLE_SELF_UPDATE ? "booth" : "assetstore";

                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        using (var client = new System.Net.WebClient())
                        {
                            client.Headers.Add("User-Agent", "SynapticAIPro-Unity");
                            var url = $"https://kawaii-agent-backend.vercel.app/api/synaptic/unity-version?v={currentVersion}&dist={dist}";
                            var json = client.DownloadString(url);

                            if (json.Contains("\"updateAvailable\":true") || json.Contains("\"updateAvailable\": true"))
                            {
                                var vMatch = System.Text.RegularExpressions.Regex.Match(json, "\"latestVersion\"\\s*:\\s*\"([^\"]+)\"");
                                if (vMatch.Success)
                                {
                                    var newVersion = vMatch.Groups[1].Value;
                                    EditorApplication.delayCall += () =>
                                    {
                                        var result = EditorUtility.DisplayDialog(
                                            "Synaptic AI Pro - Update Available",
                                            $"A new version is available!\n\n" +
                                            $"Current: v{currentVersion}\n" +
                                            $"Latest: v{newVersion}\n\n" +
                                            (ENABLE_SELF_UPDATE
                                                ? "Would you like to update now?"
                                                : "Would you like to open the Asset Store?"),
                                            ENABLE_SELF_UPDATE ? "Update Now" : "Open Store",
                                            "Later"
                                        );

                                        if (result)
                                        {
                                            if (ENABLE_SELF_UPDATE)
                                            {
                                                // Setup Windowを開いてアップデート実行
                                                var window = GetWindow<NexusMCPSetupWindow>("Synaptic Pro Setup");
                                                window.Show();
                                                updateAvailable = true;
                                                latestVersion = newVersion;
                                                var urlMatch = System.Text.RegularExpressions.Regex.Match(json, "\"updateUrl\"\\s*:\\s*\"([^\"]+)\"");
                                                updateUrl = urlMatch.Success ? urlMatch.Groups[1].Value : "";
                                                updateMethod = "auto";
                                                window.StartAutoUpdate();
                                            }
                                            else
                                            {
                                                Application.OpenURL("https://assetstore.unity.com/packages/tools/generative-ai/synaptic-ai-pro-natural-language-control-for-unity-336030");
                                            }
                                        }
                                    };
                                }
                            }
                        }
                    }
                    catch { /* フェイルサイレント */ }
                });
            };
        }

        private void TryHttpAutoStart()
        {
            if (externalAutoStartAttempted) return;
            externalAutoStartAttempted = true;

            if (HttpAutoStartEnabled && !externalHttpRunning)
            {
                // まず既存サーバーが生きてるかチェック
                if (IsPortListening(httpPort))
                {
                    // 既にサーバーが動いてる（前回のプロセスが残ってる）→ 再接続のみ
                    externalHttpRunning = true;
                    SynLog.Info("[Synaptic] Auto-start: Existing server detected, reconnecting...");
                    _ = ConnectToHttpServerAsync(httpPort);
                }
                else
                {
                    // サーバーがない → 新規起動
                    SynLog.Info("[Synaptic] Auto-starting HTTP Server...");
                    StartExternalHttpServer();
                }
            }
        }
        
        private void InitializeStyles()
        {
            headerStyle = new GUIStyle(EditorStyles.largeLabel)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.2f, 0.6f, 1f) }
            };
            
            setupButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(20, 20, 10, 10),
                normal = { background = CreateColorTexture(new Color(0.2f, 0.6f, 1f)) },
                hover = { background = CreateColorTexture(new Color(0.3f, 0.7f, 1f)) },
                active = { background = CreateColorTexture(new Color(0.1f, 0.5f, 0.9f)) }
            };
            
            statusStyle = new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = 14,
                padding = new RectOffset(10, 10, 10, 10),
                wordWrap = true
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
            DrawUpdateBanner();

            // Tab rendering
            selectedTab = GUILayout.Toolbar(selectedTab, tabNames, GUILayout.Height(30));

            // Animation update (non-blocking)
            if (isConnecting)
            {
                animationTime += Time.deltaTime;
                // Use delayed call instead of direct Repaint to avoid "Hold on" blocking
                EditorApplication.delayCall += () => { if (this) Repaint(); };
            }
            EditorGUILayout.Space(10);
            
            switch (selectedTab)
            {
                case 0:
                    DrawAIConnectionTab();
                    break;
                case 1:
                    DrawHTTPServerTab();
                    break;
                case 2:
                    DrawDiagnosticsTab();
                    break;
                case 3:
                    DrawHelpTab();
                    break;
            }
        }

        /// <summary>
        /// v1.2.24: Diagnostics タブ。旧 Tools/Synaptic Pro 配下に散在していた
        /// MCP Server Start/Stop, Auto Reconnect トグル, Port Mapping,
        /// Cinemachine 検出, Shader 更新, Changelog Pref リセット等を統合。
        /// メニュー項目を削減してトップを見やすくする目的。
        /// </summary>
        private void DrawDiagnosticsTab()
        {
            EditorGUILayout.LabelField("接続診断 / Connection", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "AI クライアントとの接続を確認・修復します。Synaptic Code から HTTP 接続している場合は HTTP Server タブを使用してください。\n" +
                "Check and repair the connection to AI clients. If you connect from Synaptic Code via HTTP, use the HTTP Server tab instead.",
                MessageType.None);
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("AI Connection Status", GUILayout.Height(28)))
            {
                SynapticPro.NexusEditorMCPService.ShowMCPStatus();
            }
            if (GUILayout.Button("AI Reconnect", GUILayout.Height(28)))
            {
                SynapticPro.NexusEditorMCPService.QuickReconnect();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // Auto Reconnect toggle (1クリックで切替、key は NexusEditorMCPService と一致)
            bool currentAuto = EditorPrefs.GetBool("NexusMCP_AutoReconnect", true);
            bool newAuto = EditorGUILayout.ToggleLeft("Auto Reconnect (接続切断時に自動再接続 / auto-reconnect on drop)", currentAuto);
            if (newAuto != currentAuto)
            {
                if (newAuto) SynapticPro.NexusEditorMCPService.EnableAutoReconnect();
                else SynapticPro.NexusEditorMCPService.DisableAutoReconnect();
            }

            EditorGUILayout.Space(10);

            // MCP Server toggle (高度機能)
            EditorGUILayout.LabelField("MCP Server (高度機能 / Advanced)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Unity 側で MCP サーバーを起動する必要がある特殊なケース向けの高度機能です。通常のユーザーは触る必要はありません。\n" +
                "Advanced feature for special cases that require running an MCP server on the Unity side. Normal users do not need to touch this.",
                MessageType.Warning);
            EditorGUILayout.Space(4);

            bool mcpEnabled = EditorPrefs.GetBool("NexusMCP_Enabled", true);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(mcpEnabled))
            {
                if (GUILayout.Button(mcpEnabled ? "▶ MCP Running" : "▶ Start MCP", GUILayout.Height(28)))
                {
                    SynapticPro.NexusEditorMCPService.StartMCP();
                }
            }
            using (new EditorGUI.DisabledScope(!mcpEnabled))
            {
                if (GUILayout.Button(mcpEnabled ? "⏹ Stop MCP" : "⏹ MCP Stopped", GUILayout.Height(28)))
                {
                    SynapticPro.NexusEditorMCPService.StopMCP();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // Project tools
            EditorGUILayout.LabelField("プロジェクト診断 / Project Diagnostics", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            if (GUILayout.Button("Show Port Mapping", GUILayout.Height(24)))
            {
                SynapticPro.NexusProjectPortManager.ShowPortMapping();
            }
            if (GUILayout.Button("Detect Cinemachine Version", GUILayout.Height(24)))
            {
                SynapticPro.CinemachineVersionDetector.DetectAndSetSymbols();
            }
            if (GUILayout.Button("Update Shaders for Pipeline", GUILayout.Height(24)))
            {
                Synaptic.Editor.ShaderPipelineManager.UpdateShadersForCurrentPipeline();
            }

            EditorGUILayout.Space(10);

            // Misc
            EditorGUILayout.LabelField("その他 / Misc", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            if (GUILayout.Button("Reset Changelog Preference", GUILayout.Height(24)))
            {
                SynapticPro.NexusChangelogWindow.ResetPreference();
            }
        }
        
        /// <summary>
        /// Compact toolbar with live MCP connection controls. Visible only in the
        /// AI Connection tab — the HTTP Server tab uses its own port lifecycle
        /// and these controls don't apply there.
        /// </summary>
        private void DrawConnectionControlsBar()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();

            // Status indicator: a small color-coded square + plain text label.
            // Using a textured box (drawn via a colored rect) instead of an
            // emoji bullet so the visual works under any system font.
            bool connected = NexusEditorMCPService.IsConnected;
            var statusColor = connected ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.7f, 0.45f, 0.2f);
            var dotRect = GUILayoutUtility.GetRect(10, 10, GUILayout.Width(10), GUILayout.Height(10));
            // Vertical-center the dot relative to surrounding label baseline.
            dotRect.y += 5;
            EditorGUI.DrawRect(dotRect, statusColor);
            GUILayout.Space(4);
            GUILayout.Label(connected ? "MCP Connected" : "MCP Disconnected", EditorStyles.boldLabel, GUILayout.Width(160));

            GUILayout.FlexibleSpace();

            // Built-in Editor icons — no emoji, render consistently across OS.
            //   "Refresh"  : circular arrow (used by Asset refresh, Console clear)
            //   "linkicon" : chain link (Editor's link icon, varies by version)
            var reconnectIcon = EditorGUIUtility.IconContent("Refresh");
            var reconnectContent = new GUIContent(" AI Reconnect", reconnectIcon.image, "Reconnect to MCP server");
            if (GUILayout.Button(reconnectContent, GUILayout.Width(130), GUILayout.Height(22)))
            {
                NexusEditorMCPService.QuickReconnect();
            }

            EditorGUILayout.Space(4);

            // Auto Reconnect toggle — direct binding to the EditorPrefs-backed property.
            bool prevAuto = NexusEditorMCPService.AutoReconnectEnabled;
            bool nextAuto = GUILayout.Toggle(prevAuto, new GUIContent(" Auto Reconnect", "Automatically reconnect when the connection drops"), GUILayout.Width(130));
            if (nextAuto != prevAuto)
            {
                NexusEditorMCPService.AutoReconnectEnabled = nextAuto;
            }

            EditorGUILayout.Space(4);

            // No Unity built-in icon for Discord — use a small link/external
            // icon ("BuildSettings.Web.Small" is the closest globe-like icon
            // across Unity 2022 / 6.x). Fall back to plain text on failure.
            GUIContent discordContent;
            try
            {
                var icon = EditorGUIUtility.IconContent("BuildSettings.Web.Small");
                discordContent = new GUIContent(" Discord", icon != null ? icon.image : null, "Join the Synaptic Discord community");
            }
            catch
            {
                discordContent = new GUIContent("Discord", "Join the Synaptic Discord community");
            }
            if (GUILayout.Button(discordContent, GUILayout.Width(90), GUILayout.Height(22)))
            {
                NexusEditorMCPService.OpenDiscord();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(8);
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(10);
            GUILayout.Label("Synaptic Pro Setup", headerStyle, GUILayout.Height(40));
            EditorGUILayout.Space(10);
            
            // Concise status display (常にBegin/Endを呼ぶ - GUILayout不一致防止)
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            var statusText = "Loading...";
            var statusColor = Color.gray;

            if (mcpStatus != null)
            {
                if (mcpServerRunning)
                {
                    statusText = "MCP Server Running";
                    statusColor = Color.green;
                }
                else if (mcpStatus.isMCPInstalled)
                {
                    statusText = "AI Connection Ready";
                    statusColor = new Color(0.2f, 0.8f, 0.2f);
                }
                else
                {
                    statusText = "Initial Setup";
                    statusColor = new Color(0.5f, 0.5f, 0.5f);
                }
            }

            var oldColor = GUI.contentColor;
            GUI.contentColor = statusColor;
            GUILayout.Label(statusText, EditorStyles.boldLabel);
            GUI.contentColor = oldColor;

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(5);
        }
        
        private void DrawAIConnectionTab()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            // ─── Live MCP connection controls ───────────────────────────────
            // Previously the only entry points for these were Tools menu items
            // (Tools > Synaptic Pro > AI Reconnect / Auto Reconnect: Enable|Disable
            // / Join Discord). Surfacing them in the Setup window cuts the
            // discovery cost — users already open this window when troubleshooting.
            // MCP Server: Start/Stop stays in the Tools menu (advanced; the
            // typical workflow is to let Claude Desktop spawn the server).
            DrawConnectionControlsBar();

            // ─── Windows path-with-spaces workaround (ESC-0107/0110 系) ───
            // Synaptic AI Pro のインストール先パスが "Assets\Synaptic AI Pro" のように
            // 空白を含む場合、Windsurf / Codex desktop 等の MCP クライアントが
            // 接続に失敗するため、空白なし junction を 1 クリックで作成して回避する
            DrawWindowsJunctionWorkaround();

            // One-click startup
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("MCP Setup", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.LabelField("Once MCP setup is complete, Unity tools are immediately available in Claude Desktop", EditorStyles.wordWrappedLabel);
            
            EditorGUILayout.Space(10);
            
            // Display connection animation
            if (isConnecting)
            {
                DrawConnectingAnimation();
            }
            else if (!mcpConfigured)
            {
                // Setup not complete - show setup guide link
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Need help?", GUILayout.Width(70));
                if (GUILayout.Button("Setup Guide", GUILayout.Width(100)))
                {
                    Application.OpenURL("https://www.synaptic-ai.net/ja/docs/setup");
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(10);

                // AI Tool Selection (v1.1.0)
                EditorGUILayout.LabelField("Select Your AI Tool:", EditorStyles.boldLabel);
                EditorGUILayout.Space(5);

                var oldBgColor = GUI.backgroundColor;

                // Token SuperSave Mode (Recommended) - TOP
                GUI.backgroundColor = selectedAIClient == AIClientType.TokenSuperSaveMode
                    ? new Color(0.2f, 0.8f, 0.4f)
                    : new Color(0.85f, 1f, 0.85f);
                if (GUILayout.Button(
                    "★ Token SuperSave Mode [Recommended]\n" +
                    "(3 meta-tools only - 99% context reduction)",
                    GUILayout.Height(60)))
                {
                    selectedAIClient = AIClientType.TokenSuperSaveMode;
                }

                EditorGUILayout.Space(5);

                // Full Mode option
                GUI.backgroundColor = selectedAIClient == AIClientType.ClaudeDesktopOrCursor
                    ? new Color(0.3f, 0.7f, 0.9f)
                    : Color.white;
                if (GUILayout.Button(
                    "Full Mode\n" +
                    "(All 350+ tools exposed)",
                    GUILayout.Height(50)))
                {
                    selectedAIClient = AIClientType.ClaudeDesktopOrCursor;
                }

                EditorGUILayout.Space(5);

                // Essential Mode option
                GUI.backgroundColor = selectedAIClient == AIClientType.CursorOrLMStudioEssential
                    ? new Color(0.3f, 0.7f, 0.9f)
                    : Color.white;
                if (GUILayout.Button(
                    "Essential Mode\n" +
                    "(80 essential tools)",
                    GUILayout.Height(50)))
                {
                    selectedAIClient = AIClientType.CursorOrLMStudioEssential;
                }

                EditorGUILayout.Space(5);

                // Dynamic Mode option
                GUI.backgroundColor = selectedAIClient == AIClientType.GitHubCopilot
                    ? new Color(0.3f, 0.7f, 0.9f)
                    : Color.white;
                if (GUILayout.Button(
                    "Dynamic Mode\n" +
                    "(8 core tools + on-demand loading)",
                    GUILayout.Height(50)))
                {
                    selectedAIClient = AIClientType.GitHubCopilot;
                }

                GUI.backgroundColor = oldBgColor;

                EditorGUILayout.Space(5);

                // Info box based on selection
                if (selectedAIClient == AIClientType.TokenSuperSaveMode)
                {
                    EditorGUILayout.HelpBox(
                        "★ Recommended: Token SuperSave Mode\n\n" +
                        "Only 3 meta-tools for 99% context reduction:\n" +
                        "• list_categories() - Discover tool categories\n" +
                        "• list_tools(category) - See tools & parameters\n" +
                        "• execute(tool, params) - Run any of 350+ tools\n\n" +
                        "Works with all MCP clients. Best for long sessions.",
                        MessageType.Info);
                }
                else if (selectedAIClient == AIClientType.ClaudeDesktopOrCursor)
                {
                    EditorGUILayout.HelpBox(
                        "Full Mode: All 350+ Unity tools loaded at startup.\n" +
                        "• Higher context usage, but all tools immediately visible\n" +
                        "• Claude Desktop: Prompt caching helps with longer sessions",
                        MessageType.Info);
                }
                else if (selectedAIClient == AIClientType.CursorOrLMStudioEssential)
                {
                    EditorGUILayout.HelpBox(
                        "Essential Mode: 80 carefully selected tools (62% lighter).\n" +
                        "• Perfect for Cursor and LM Studio to avoid context bloat\n" +
                        "• Includes: GameObject, Camera, Scene, UI, Screenshot, Animation basics\n" +
                        "• Removed: Scripting, GOAP, Weather, Advanced VFX, Batch ops",
                        MessageType.Info);
                }
                else // GitHubCopilot
                {
                    EditorGUILayout.HelpBox(
                        "Dynamic Mode: Start with 8 tools, load more on-demand.\n" +
                        "• GitHub Copilot with MCP support\n" +
                        "• Use select_tools() to load additional tool categories\n" +
                        "• Perfect for avoiding tool limit warnings",
                        MessageType.Info);
                }

                EditorGUILayout.Space(10);

                // ESC-0162: Synaptic Code users connect through the HTTP Server
                // tab, NOT through this Complete MCP Setup button. The two
                // entry points sit next to each other so users frequently
                // press the wrong one and end up registering an MCP server
                // they don't need. Surface a one-line hint just above the
                // button so the distinction is obvious before they click.
                EditorGUILayout.HelpBox(
                    "Synaptic Code をお使いの場合はこのボタンは押さず、HTTP Server タブから接続してください。\n" +
                    "Claude Desktop / Cursor / VS Code MCP 等の MCP クライアントを使う場合のみこのボタンを使用します。",
                    MessageType.Info);

                EditorGUILayout.Space(4);

                // MCP Setup button
                var oldColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.2f, 0.6f, 0.8f);
                if (GUILayout.Button("Complete MCP Setup", setupButtonStyle, GUILayout.Height(60)))
                {
                    ConfigureMCP();
                }
                GUI.backgroundColor = oldColor;
            }
            else
            {
                // Setup complete
                EditorGUILayout.BeginHorizontal();

                var oldColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.2f, 0.8f, 0.2f);
                GUILayout.Button("✓ MCP Setup Complete", setupButtonStyle, GUILayout.Height(50));
                GUI.backgroundColor = oldColor;

                // Reconfigure button
                GUI.backgroundColor = new Color(0.8f, 0.6f, 0.2f);
                if (GUILayout.Button("🔄 Reconfigure", GUILayout.Width(100), GUILayout.Height(50)))
                {
                    ResetMCPConfiguration();
                }
                GUI.backgroundColor = oldColor;

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(10);

                // Display appropriate info based on selected mode (v1.1.0)
                string setupCompleteMessage;
                if (selectedAIClient == AIClientType.TokenSuperSaveMode)
                {
                    setupCompleteMessage =
                        "★ Setup complete! Token SuperSave Mode\n\n" +
                        "• 3 meta-tools → 350+ tools accessible\n" +
                        "• 99% context reduction for longer sessions\n\n" +
                        "Restart your AI tool, then ask:\n" +
                        "\"What Unity tools are available?\"";
                }
                else if (selectedAIClient == AIClientType.GitHubCopilot)
                {
                    setupCompleteMessage =
                        "Setup complete! Dynamic Mode (hub-server.js)\n" +
                        "• GitHub Copilot (.vscode/mcp.json)\n\n" +
                        "Restart VS Code to activate.\n" +
                        "Use select_tools() to load tool categories dynamically.";
                }
                else
                {
                    setupCompleteMessage =
                        "Setup complete! Full Mode (index.js) - All 246 tools\n" +
                        "• Claude Desktop\n" +
                        "• Cursor (~/.cursor/mcp.json)\n" +
                        "• VS Code (.vscode/mcp.json)\n\n" +
                        "Restart/Reload your AI tool to activate Unity MCP.";
                }

                EditorGUILayout.HelpBox(setupCompleteMessage, MessageType.Info);
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(20);

            // How to Use
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("How to Use Unity Tools", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.LabelField("1. Open Claude Desktop / Cursor / VS Code", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("2. Ask: \"What Unity tools are available?\"", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("3. Give instructions: \"Create a red cube\" or \"Show me the scene hierarchy\"", EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space(10);
            if (GUILayout.Button("Full Usage Guide", GUILayout.Height(35)))
            {
                ShowUsageGuide();
            }
            
            EditorGUILayout.EndVertical();
            
            // Auto-generated connection settings
            if (mcpServerRunning)
            {
                EditorGUILayout.Space(20);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label("Connection Settings (Auto-generated)", EditorStyles.boldLabel);
                EditorGUILayout.Space(5);
                
                EditorGUILayout.LabelField("MCP Server: localhost:8090");
                EditorGUILayout.LabelField("Tool Name: unity-synaptic");
                
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("💫 Usage:", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("1. Open ChatGPT or Claude Desktop");
                EditorGUILayout.LabelField("2. Start a new chat");
                EditorGUILayout.LabelField("3. Tips for using tools:");
                EditorGUILayout.LabelField("   • Include words like \"tools\" or \"unity\"");
                EditorGUILayout.LabelField("   Example: \"Use unity tools to create a red cube\"");
                EditorGUILayout.LabelField("4. AI will automatically control Unity with MCP tools");
                
                EditorGUILayout.EndVertical();
            }
            
            EditorGUILayout.EndScrollView();
        }
        
        
        private void DrawServerManagementTab()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("MCP Server Management", EditorStyles.boldLabel);
            EditorGUILayout.Space(10);
            
            // Server status
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Server Status:");
            
            var statusColor = mcpServerRunning ? Color.green : Color.red;
            var statusText = mcpServerRunning ? "● Running" : "● Stopped";
            
            var oldColor = GUI.contentColor;
            GUI.contentColor = statusColor;
            GUILayout.Label(statusText);
            GUI.contentColor = oldColor;
            
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(10);
            
            // Control buttons
            EditorGUILayout.BeginHorizontal();
            
            EditorGUI.BeginDisabledGroup(!mcpStatus?.isMCPInstalled ?? true);
            
            if (!mcpServerRunning)
            {
                if (GUILayout.Button("▶️ Start Server", GUILayout.Height(30)))
                {
                    StartMCPServer();
                }
            }
            else
            {
                if (GUILayout.Button("⏹️ Stop Server", GUILayout.Height(30)))
                {
                    StopMCPServer();
                }
            }
            
            if (GUILayout.Button("🔄 Restart", GUILayout.Height(30)))
            {
                RestartMCPServer();
            }
            
            EditorGUI.EndDisabledGroup();
            
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(20);
            
            // Server settings
            EditorGUILayout.Space(20);
            EditorGUILayout.LabelField("Server Settings:", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("MCP Port:", GUILayout.Width(100));
            mcpPort = EditorGUILayout.IntField(mcpPort);
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("WebSocket Port:", GUILayout.Width(100));
            wsPort = EditorGUILayout.IntField(wsPort);
            EditorGUILayout.EndHorizontal();
            
            EditorGUI.indentLevel--;
            
            // Connection Info
            if (mcpStatus?.isMCPInstalled ?? false)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Connection Info:", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField($"MCP: localhost:{mcpPort}");
                EditorGUILayout.LabelField($"WebSocket: ws://localhost:{wsPort}");
                EditorGUILayout.LabelField($"Accessible from Desktop AI");
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.EndVertical();
            
            // Log Viewer
            EditorGUILayout.Space(20);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Server Log", EditorStyles.boldLabel);
            
            // Display server logs here
            EditorGUILayout.TextArea("Server logs will be displayed here...", GUILayout.Height(200));
            
            EditorGUILayout.EndVertical();
        }
        
        /* Planned for future implementation
        private void DrawCLIConfigTab()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("CLI AI Configuration Manager", EditorStyles.boldLabel);
            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("Batch create configuration files for various CLI AI tools", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(10);
            
            // Claude Code configuration (2025 MCP specification)
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Claude Code (Anthropic)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Official CLI for Claude with MCP support", EditorStyles.miniLabel);
            EditorGUILayout.Space(5);
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Configure MCP", GUILayout.Height(30)))
            {
                GenerateClaudeCodeConfig();
            }
            if (GUILayout.Button("Docs", GUILayout.Width(60), GUILayout.Height(30)))
            {
                Application.OpenURL("https://docs.anthropic.com/en/docs/claude-code/mcp");
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.LabelField("Config: .claude/settings.local.json", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(10);
            
            
            // Cursor settings (2025 Popular MCP Client)
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Cursor (Anysphere)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("AI-powered code editor with MCP support", EditorStyles.miniLabel);
            EditorGUILayout.Space(5);
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Configure MCP", GUILayout.Height(30)))
            {
                if (GenerateCursorConfig())
                {
                    EditorUtility.DisplayDialog(
                        "Cursor Setup Complete",
                        "Cursor MCP configuration created successfully!\n\n" +
                        "Config file: ~/.cursor/mcp.json\n\n" +
                        "Next steps:\n" +
                        "1. Reload MCP Servers in Cursor:\n" +
                        "   Settings → MCP Servers → Reload\n" +
                        "2. Unity tools will be available in Cursor",
                        "OK"
                    );
                }
                else
                {
                    EditorUtility.DisplayDialog(
                        "Cursor Setup Failed",
                        "Failed to create Cursor configuration.\n\n" +
                        "Please check the Console for details.",
                        "OK"
                    );
                }
            }
            if (GUILayout.Button("Docs", GUILayout.Width(60), GUILayout.Height(30)))
            {
                Application.OpenURL("https://www.synaptic-ai.net/ja/docs/setup#cursor");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Config: ~/.cursor/mcp.json", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // VS Code settings (Claude Code extension)
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("VS Code (Microsoft)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Visual Studio Code with Claude Code extension MCP support", EditorStyles.miniLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Configure MCP", GUILayout.Height(30)))
            {
                if (GenerateVSCodeConfig())
                {
                    EditorUtility.DisplayDialog(
                        "VS Code Setup Complete",
                        "VS Code MCP configuration created successfully!\n\n" +
                        "Config file: .vscode/mcp.json\n\n" +
                        "Next steps:\n" +
                        "1. Reload VS Code window:\n" +
                        "   Cmd/Ctrl + Shift + P → 'Reload Window'\n" +
                        "2. Unity tools will be available in Claude Code",
                        "OK"
                    );
                }
                else
                {
                    EditorUtility.DisplayDialog(
                        "VS Code Setup Failed",
                        "Failed to create VS Code configuration.\n\n" +
                        "Please check the Console for details.",
                        "OK"
                    );
                }
            }
            if (GUILayout.Button("Docs", GUILayout.Width(60), GUILayout.Height(30)))
            {
                Application.OpenURL("https://www.synaptic-ai.net/ja/docs/setup#vscode");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Config: .vscode/mcp.json", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Windsurf settings (2025 MCP Client)
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Windsurf (Codeium)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("The IDE that writes with you - MCP enabled", EditorStyles.miniLabel);
            EditorGUILayout.Space(5);
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Configure MCP", GUILayout.Height(30)))
            {
                GenerateWindsurfConfig();
            }
            if (GUILayout.Button("Docs", GUILayout.Width(60), GUILayout.Height(30)))
            {
                Application.OpenURL("https://codeium.com/windsurf");
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.LabelField("Config: ~/.windsurf/mcp_servers.json", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(20);

            // Batch settings
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Batch Configuration", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            EditorGUILayout.LabelField("Configure all supported MCP clients at once", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(10);
            
            var oldColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.2f, 0.8f, 0.2f);
            if (GUILayout.Button("Configure All MCP Clients", GUILayout.Height(40)))
            {
                GenerateAllCLIConfigs();
            }
            GUI.backgroundColor = oldColor;
            
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(20);
            
            // File watch settings
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("File Watch Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.LabelField("Monitor project file changes and notify AI", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Create Watch Configuration", GUILayout.Height(30)))
            {
                GenerateWatchConfig();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Monitored files: .cs, .js, .ts, .json, .md", EditorStyles.miniLabel);
            
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }
        */

        private void DrawHTTPServerTab()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            // HTTP Server Section
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("HTTP Server", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.HelpBox(
                "HTTP Server allows direct API access to Unity tools.\n" +
                "Use this for custom integrations, testing, or when MCP is not available.",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("CLI Integration Guide", GUILayout.Width(150)))
            {
                Application.OpenURL("https://www.synaptic-ai.net/ja/docs/cli-integration");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // External HTTP Server UI
            DrawExternalHTTPServerUI();

            EditorGUILayout.EndVertical();

            // API Endpoints Section
            httpServerRunning = externalHttpRunning;
            if (httpServerRunning)
            {
                EditorGUILayout.Space(15);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label("API Endpoints", EditorStyles.boldLabel);
                EditorGUILayout.Space(5);

                var baseUrl = $"http://localhost:{httpPort}";

                DrawEndpointRow("Root (Prompt)", "GET", $"{baseUrl}/");
                DrawEndpointRow("Health Check", "GET", $"{baseUrl}/health");
                DrawEndpointRow("AI Prompt", "GET", $"{baseUrl}/prompt");
                DrawEndpointRow("List Tools", "GET", $"{baseUrl}/tools");
                DrawEndpointRow("Categories", "GET", $"{baseUrl}/categories");
                DrawEndpointRow("Tool Search", "GET", $"{baseUrl}/tools/search?q=");
                DrawEndpointRow("Tools Reference", "GET", $"{baseUrl}/tools/reference");
                DrawEndpointRow("Resources", "GET", $"{baseUrl}/resources");
                DrawEndpointRow("Execute Tool", "POST", $"{baseUrl}/execute");
                DrawEndpointRow("Batch Execute", "POST", $"{baseUrl}/batch");

                EditorGUILayout.Space(10);

                if (GUILayout.Button("Copy Base URL", GUILayout.Height(25)))
                {
                    GUIUtility.systemCopyBuffer = baseUrl;
                    SynLog.Info($"Copied: {baseUrl}");
                }

                EditorGUILayout.EndVertical();
            }

            // Usage Example
            EditorGUILayout.Space(15);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Usage Example", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.LabelField("cURL Example:", EditorStyles.boldLabel);
            var curlExample = $"curl http://localhost:{httpPort}/health";
            EditorGUILayout.SelectableLabel(curlExample, EditorStyles.textField, GUILayout.Height(20));

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Execute Tool (Recommended):", EditorStyles.boldLabel);
            var toolExample = $"curl -X POST http://localhost:{httpPort}/execute \\\n  -H \"Content-Type: application/json\" \\\n  -d '{{\"tool\": \"unity_create_gameobject\", \"params\": {{\"name\": \"MyCube\", \"type\": \"cube\"}}}}'";
            EditorGUILayout.SelectableLabel(toolExample, EditorStyles.textArea, GUILayout.Height(50));

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Batch Execute:", EditorStyles.boldLabel);
            var batchExample = $"curl -X POST http://localhost:{httpPort}/batch \\\n  -H \"Content-Type: application/json\" \\\n  -d '[{{\"tool\": \"unity_create_gameobject\", \"params\": {{\"name\": \"Obj1\", \"type\": \"cube\"}}}}, ...]'";
            EditorGUILayout.SelectableLabel(batchExample, EditorStyles.textArea, GUILayout.Height(50));

            EditorGUILayout.EndVertical();

            // AI Prompt Section
            EditorGUILayout.Space(15);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("AI Control Prompt", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.HelpBox(
                "Copy this prompt to your AI (Claude Code, Codex CLI, etc.) to enable HTTP control.\n" +
                $"Or fetch directly: curl http://localhost:{httpPort}/",
                MessageType.Info);

            EditorGUILayout.Space(10);

            if (GUILayout.Button("Copy AI Prompt to Clipboard", GUILayout.Height(30)))
            {
                var mcpServerPath = FindMCPServerPath();
                var aiPrompt = GetHTTPControlPrompt(mcpServerPath, httpPort);
                GUIUtility.systemCopyBuffer = aiPrompt;
                SynLog.Info("[Synaptic] AI Prompt copied to clipboard!");
                EditorUtility.DisplayDialog("Copied!", "AI Control Prompt has been copied to clipboard.\n\nPaste it to your AI assistant to enable HTTP control.", "OK");
            }

            EditorGUILayout.EndVertical();

            // ========== ログ出力設定 ==========
            EditorGUILayout.Space(15);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Log Output", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            bool currentVerbose = SynapticAIPro.SynLog.VerboseEnabled;
            bool newVerbose = EditorGUILayout.ToggleLeft(
                "Verbose Logs (Info / Warning)",
                currentVerbose
            );
            if (newVerbose != currentVerbose)
            {
                SynapticAIPro.SynLog.VerboseEnabled = newVerbose;
            }
            EditorGUILayout.LabelField(
                "オフにすると Synaptic AI Pro 関連の Info/Warning ログをコンソールに出力しません（Errorは常に表示）",
                EditorStyles.wordWrappedMiniLabel
            );

            EditorGUILayout.EndVertical();

            EditorGUILayout.EndScrollView();
        }

        private double lastHttpStatusCheckTime = 0;

        private void DrawExternalHTTPServerUI()
        {
            // Server Status（5秒ごとにチェック。毎フレームのTCP接続を防ぐ）
            if (EditorApplication.timeSinceStartup - lastHttpStatusCheckTime > 5.0)
            {
                lastHttpStatusCheckTime = EditorApplication.timeSinceStartup;
                CheckExternalHttpServerStatus();
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Status:", GUILayout.Width(60));

            var statusColor = externalHttpRunning ? Color.green : Color.gray;
            var statusText = externalHttpRunning ? "● Running (Node.js)" : "● Stopped";

            var oldColor = GUI.contentColor;
            GUI.contentColor = statusColor;
            GUILayout.Label(statusText, EditorStyles.boldLabel);
            GUI.contentColor = oldColor;

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // Port Setting
            EditorGUI.BeginDisabledGroup(externalHttpRunning);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("HTTP Port:", GUILayout.Width(80));
            httpPort = EditorGUILayout.IntField(httpPort, GUILayout.Width(80));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(5);

            // Auto-Start Toggle
            EditorGUILayout.BeginHorizontal();
            var autoStart = HttpAutoStartEnabled;
            var newAutoStart = EditorGUILayout.Toggle("Auto-Start on Load", autoStart);
            if (newAutoStart != autoStart)
            {
                HttpAutoStartEnabled = newAutoStart;
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // Control Buttons
            EditorGUILayout.BeginHorizontal();

            if (!externalHttpRunning)
            {
                var startBgColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.2f, 0.7f, 0.3f);
                if (GUILayout.Button("Start HTTP Server", GUILayout.Height(35)))
                {
                    StartExternalHttpServer();
                }
                GUI.backgroundColor = startBgColor;
            }
            else
            {
                var stopBgColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.8f, 0.3f, 0.3f);
                if (GUILayout.Button("Stop HTTP Server", GUILayout.Height(35)))
                {
                    StopExternalHttpServer();
                }
                GUI.backgroundColor = stopBgColor;
            }

            EditorGUILayout.EndHorizontal();

            // Connect status / Connect button
            EditorGUILayout.Space(5);
            var wsConnected = NexusHTTPWebSocketClient.Instance?.IsConnected ?? false;
            // GUILayout構造を常に同じにする（Layout/Repaintフェーズ不一致防止）
            EditorGUI.BeginDisabledGroup(wsConnected);
            if (GUILayout.Button(
                wsConnected ? $"✓ Unity connected (port {httpPort})" : "Connect Unity Only (Server already running)",
                GUILayout.Height(28)))
            {
                if (!wsConnected)
                {
                    _ = ConnectToHttpServerAsync(httpPort);
                }
            }
            EditorGUI.EndDisabledGroup();

            // Info about external mode
            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox(
                "External mode runs http-server.js via Node.js.\n" +
                "Benefits: Stable across domain reloads, Play mode changes.\n" +
                "HTTP and WebSocket use the same port.",
                MessageType.None);
        }

        // ===== External HTTP Server Process Management =====

        private void CheckExternalHttpServerStatus()
        {
            // Check if process is still running
            if (externalHttpProcess != null)
            {
                try
                {
                    if (externalHttpProcess.HasExited)
                    {
                        externalHttpProcess = null;
                        externalHttpRunning = false;
                    }
                    else
                    {
                        externalHttpRunning = true;
                        return;
                    }
                }
                catch
                {
                    externalHttpProcess = null;
                    externalHttpRunning = false;
                }
            }

            // No local process handle (fresh domain reload, or never-started).
            // Probe the port on a background thread and cache the result. OnGUI must
            // never block on a TCP connect — a 500ms stall per repaint freezes the editor.
            if (portCheckInFlight) return;
            double now = EditorApplication.timeSinceStartup;
            if (now - lastPortCheckTime < PORT_CHECK_INTERVAL_SEC) return;

            lastPortCheckTime = now;
            portCheckInFlight = true;
            int portToCheck = httpPort;

            _ = Task.Run(async () =>
            {
                bool listening = false;
                try
                {
                    using (var tcp = new System.Net.Sockets.TcpClient())
                    {
                        var connectTask = tcp.ConnectAsync("localhost", portToCheck);
                        var winner = await Task.WhenAny(connectTask, Task.Delay(PORT_CHECK_TIMEOUT_MS));
                        if (winner == connectTask)
                        {
                            try { await connectTask; listening = tcp.Connected; }
                            catch { listening = false; }
                        }
                    }
                }
                catch { listening = false; }

                // Marshal state + Repaint back to the main thread.
                EditorApplication.delayCall += () =>
                {
                    portCheckInFlight = false;
                    if (externalHttpRunning != listening)
                    {
                        externalHttpRunning = listening;
                        if (this) Repaint();
                    }
                };
            });
        }

        private bool IsPortListening(int port)
        {
            // Quick TCP check with short timeout (avoid blocking UI thread)
            try
            {
                using (var tcp = new System.Net.Sockets.TcpClient())
                {
                    var result = tcp.BeginConnect("localhost", port, null, null);
                    bool connected = result.AsyncWaitHandle.WaitOne(500); // 500msタイムアウト
                    if (connected && tcp.Connected)
                    {
                        tcp.EndConnect(result);
                        return true;
                    }
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        private void StartExternalHttpServer()
        {
            var mcpServerPath = FindMCPServerPath();
            var httpServerScript = Path.Combine(mcpServerPath, "http-server.js");

            if (!File.Exists(httpServerScript))
            {
                EditorUtility.DisplayDialog("Error",
                    $"http-server.js not found at:\n{httpServerScript}\n\nPlease ensure Synaptic AI Pro is properly installed.",
                    "OK");
                return;
            }

            // Find Node.js path
            var nodePath = FindNodePath();
            if (string.IsNullOrEmpty(nodePath))
            {
                EditorUtility.DisplayDialog("Error",
                    "Node.js not found.\n\nPlease install Node.js from https://nodejs.org/",
                    "OK");
                return;
            }

            try
            {
                System.Diagnostics.ProcessStartInfo startInfo;

                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    // Windows: detached from Unity's Job Object via CreateProcessW
                    // with CREATE_BREAKAWAY_FROM_JOB. Process.Start inherits the
                    // Job and gets killed on assembly reload — see ESC-0095.
                    string logDir = Path.Combine(mcpServerPath, "logs");
                    try { Directory.CreateDirectory(logDir); } catch { }
                    string logFile = Path.Combine(logDir, "http-server.log");

                    int pid = SynapticDetachedProcess.StartWindows(
                        nodePath, httpServerScript, httpPort, mcpServerPath, logFile);

                    if (pid == 0)
                    {
                        SynLog.Info("[Synaptic] Detached spawn failed, falling back to Process.Start");
                        // Fall through to legacy path below
                        startInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = nodePath,
                            Arguments = $"\"{httpServerScript}\" {httpPort}",
                            WorkingDirectory = mcpServerPath,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true,
                            StandardOutputEncoding = System.Text.Encoding.UTF8,
                            StandardErrorEncoding = System.Text.Encoding.UTF8
                        };
                        startInfo.EnvironmentVariables["HTTP_PORT"] = httpPort.ToString();
                    }
                    else
                    {
                        // Detached path: process is independent of Unity's Job.
                        // We don't keep externalHttpProcess (no handle) — recovery
                        // uses SessionState PID. Mark running and connect.
                        externalHttpProcess = null;
                        externalHttpRunning = true;
                        EditorPrefs.SetInt(PREF_HTTP_PORT, httpPort);
                        SynLog.Info($"[Synaptic] External HTTP Server started detached (PID={pid}) on port {httpPort}");
                        SynLog.Info($"[Synaptic] Node: {nodePath}");
                        SynLog.Info($"[Synaptic] Script: {httpServerScript}");
                        SynLog.Info($"[Synaptic] Log: {logFile}");
                        _ = ConnectToHttpServerAsync(httpPort);
                        return;
                    }
                }
                else
                {
                    // macOS/Linux: 直接起動
                    //
                    // Previous implementation piped stdout/stderr back to C#
                    // (RedirectStandardOutput/Error + BeginOutputReadLine).
                    // When Unity's C# domain reloads (recompile), the pipe
                    // readers on the C# side disappear; the node process's
                    // next stdout write then hits SIGPIPE and node terminates.
                    // Result: HTTP server died every time a script was edited.
                    //
                    // Fix: launch via `sh -c "nohup node ... >log 2>&1 &"` so
                    // the child is fully detached from Unity's pipes and
                    // process group, mirroring the Windows detached path.
                    string logDir = Path.Combine(mcpServerPath, "logs");
                    try { Directory.CreateDirectory(logDir); } catch { }
                    string logFile = Path.Combine(logDir, "http-server.log");

                    // sh -c is portable to both macOS (BSD sh) and Linux.
                    // Quote the script path; quote the log path; redirect both
                    // streams to the log file; trailing `&` to detach.
                    string escapedScript = httpServerScript.Replace("\"", "\\\"");
                    string escapedLog = logFile.Replace("\"", "\\\"");
                    string shellCmd =
                        $"nohup \"{nodePath}\" \"{escapedScript}\" {httpPort} " +
                        $">\"{escapedLog}\" 2>&1 </dev/null &";

                    startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "/bin/sh",
                        Arguments = $"-c \"{shellCmd.Replace("\"", "\\\"")}\"",
                        WorkingDirectory = mcpServerPath,
                        UseShellExecute = false,
                        RedirectStandardOutput = false,
                        RedirectStandardError = false,
                        CreateNoWindow = true,
                    };
                    startInfo.EnvironmentVariables["HTTP_PORT"] = httpPort.ToString();

                    var detachProc = new System.Diagnostics.Process { StartInfo = startInfo };
                    detachProc.Start();
                    detachProc.WaitForExit(); // sh exits immediately after backgrounding node
                    detachProc.Dispose();

                    // We have no handle to the actual node process — by design,
                    // so that domain reload can't kill it. Recovery on next
                    // load uses port-listen probe in RestoreDetachedHttpServerOnReload.
                    externalHttpProcess = null;
                    externalHttpRunning = true;
                    EditorPrefs.SetInt(PREF_HTTP_PORT, httpPort);
                    SynLog.Info($"[Synaptic] External HTTP Server detach-spawned on port {httpPort}");
                    SynLog.Info($"[Synaptic] Node: {nodePath}");
                    SynLog.Info($"[Synaptic] Script: {httpServerScript}");
                    SynLog.Info($"[Synaptic] Log: {logFile}");

                    _ = ConnectToHttpServerAsync(httpPort);
                    return;
                }

                externalHttpProcess = new System.Diagnostics.Process { StartInfo = startInfo };

                externalHttpProcess.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        SynLog.Info($"[HTTP Server] {e.Data}");
                };
                externalHttpProcess.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        SynLog.Info($"[HTTP Server] {e.Data}");
                };

                externalHttpProcess.Start();
                externalHttpProcess.BeginOutputReadLine();
                externalHttpProcess.BeginErrorReadLine();

                externalHttpRunning = true;
                EditorPrefs.SetInt(PREF_HTTP_PORT, httpPort);

                SynLog.Info($"[Synaptic] External HTTP Server started on port {httpPort}");
                SynLog.Info($"[Synaptic] Node: {nodePath}");
                SynLog.Info($"[Synaptic] Script: {httpServerScript}");

                // Connect Unity to HTTP Server via WebSocket (with delay to let server start)
                _ = ConnectToHttpServerAsync(httpPort);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] Failed to start external HTTP server: {e.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to start HTTP server:\n{e.Message}", "OK");
                externalHttpProcess = null;
                externalHttpRunning = false;
            }
        }

        private async Task ConnectToHttpServerAsync(int port)
        {
            // Wait for HTTP Server to start (Windows needs more time via cmd.exe)
            var waitTime = Application.platform == RuntimePlatform.WindowsEditor ? 3000 : 1500;
            await Task.Delay(waitTime);

            // Retry connection with increasing delays
            for (int i = 0; i < 3; i++)
            {
                var connected = await NexusHTTPWebSocketClient.Instance.Connect(port);
                if (connected)
                {
                    SynLog.Info($"[Synaptic] Unity connected to HTTP Server on port {port}");
                    return;
                }
                SynLog.Info($"[Synaptic] Connection attempt {i + 1}/3 failed, retrying...");
                await Task.Delay(2000);
            }

            SynLog.Warn($"[Synaptic] Failed to connect Unity to HTTP Server after 3 attempts.\nTry starting manually: cd to MCPServer folder and run 'node http-server.js {port}'");
        }

        private void StopExternalHttpServer()
        {
            // Detached path: kill by stored PID (no Process handle exists)
            if (Application.platform == RuntimePlatform.WindowsEditor &&
                externalHttpProcess == null && externalHttpRunning)
            {
                if (SynapticDetachedProcess.KillStored())
                {
                    externalHttpRunning = false;
                    SynLog.Info("[Synaptic] Detached HTTP server stopped.");
                    return;
                }
            }

            // Kill process first (WebSocket will disconnect automatically)
            if (externalHttpProcess != null)
            {
                try
                {
                    if (!externalHttpProcess.HasExited)
                    {
                        externalHttpProcess.Kill();
                        externalHttpProcess.WaitForExit(3000);
                    }
                    externalHttpProcess.Dispose();
                }
                catch (Exception e)
                {
                    SynLog.Warn($"[Synaptic] Error stopping HTTP server: {e.Message}");
                }
                finally
                {
                    externalHttpProcess = null;
                    externalHttpRunning = false;
                }
            }
            else if (externalHttpRunning)
            {
                // Process reference lost (e.g., after domain reload)
                // Kill by port number instead
                KillProcessOnPortSafe(httpPort);
                externalHttpRunning = false;
                SynLog.Info($"[Synaptic] HTTP Server on port {httpPort} - process reference lost. Killing by port.");
            }

            SynLog.Info("[Synaptic] External HTTP Server stopped");
        }

        private void KillProcessOnPortSafe(int port)
        {
            // Run in a separate thread to avoid blocking Unity
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    KillProcessOnPort(port);
                }
                catch (Exception e)
                {
                    SynLog.Warn($"[Synaptic] Could not kill process on port {port}: {e.Message}");
                }
            });
        }

        private void KillProcessOnPort(int port)
        {
            try
            {
                if (Application.platform == RuntimePlatform.OSXEditor)
                {
                    // macOS: lsof + kill
                    var lsofProcess = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "/bin/bash",
                            Arguments = $"-c \"lsof -ti :{port} | xargs kill -9 2>/dev/null\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        }
                    };
                    lsofProcess.Start();
                    lsofProcess.WaitForExit(3000);
                }
                else if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    // Windows: netstat + taskkill
                    var process = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/c \"for /f \"tokens=5\" %a in ('netstat -aon ^| findstr :{port} ^| findstr LISTENING') do taskkill /PID %a /F\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    process.Start();
                    process.WaitForExit(3000);
                }
            }
            catch (Exception e)
            {
                SynLog.Warn($"[Synaptic] Failed to kill process on port {port}: {e.Message}");
            }
        }

        private string GetHTTPControlPrompt(string mcpServerPath, int port)
        {
            return $@"# Synaptic AI Pro (Unity) HTTP Control Instructions

## Prerequisites
- Unity must be open with Synaptic AI Pro project loaded
- HTTP Server is running (started from this window or auto-start enabled)
- No external processes needed - server runs inside Unity

## Endpoints
- GET /health - Server status
- GET /prompt - Get this AI control prompt
- GET /categories - List all tool categories
- GET /tools/category/:cat - List tools in category with inputSchema
- GET /tools - Full tool registry
- POST /execute - Execute single tool (RECOMMENDED)
- POST /batch - Execute multiple tools at once (RECOMMENDED)
- POST /tool/:toolName - Execute tool (legacy)

## Verify connection
curl http://localhost:{port}/health

## Tool discovery
curl http://localhost:{port}/categories
curl http://localhost:{port}/tools/category/scene

## Single tool execution (RECOMMENDED)
curl -X POST http://localhost:{port}/execute -H ""Content-Type: application/json"" -d '{{""tool"":""unity_create_gameobject"",""params"":{{""name"":""MyCube"",""type"":""cube""}}}}'

## Batch execution (RECOMMENDED for multiple operations)
curl -X POST http://localhost:{port}/batch -H ""Content-Type: application/json"" -d '[
  {{""tool"":""unity_create_gameobject"",""params"":{{""name"":""Cube1"",""type"":""cube""}}}},
  {{""tool"":""unity_set_transform"",""params"":{{""name"":""Cube1"",""position"":""2,0,0""}}}},
  {{""tool"":""unity_create_gameobject"",""params"":{{""name"":""Sphere1"",""type"":""sphere""}}}}
]'

## If connection fails
- Connection refused → Unity not open or HTTP Server not started
- Start HTTP Server from: Tools > Synaptic Pro > Synaptic Setup > HTTP Server tab
- Or enable ""Auto-Start on Load"" for automatic startup

## Note for Codex CLI
CRITICAL: Run curl commands EXACTLY as shown. Do NOT add nice, nohup, redirects (>), or other modifications.
If you get ""nice(5) failed"" or similar sandbox errors, use escalation (request additional permission from user).
Sandbox may block localhost connections. Use escalation if curl commands fail.

## Notes
- All responses are JSON
- Use /batch for multiple operations (more efficient)
- 30 second timeout per request
";
        }

        private void DrawEndpointRow(string name, string method, string url)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(name, GUILayout.Width(100));
            EditorGUILayout.LabelField(method, EditorStyles.miniLabel, GUILayout.Width(40));
            EditorGUILayout.SelectableLabel(url, EditorStyles.textField, GUILayout.Height(18));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawHelpTab()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("AI Connection Help", EditorStyles.boldLabel);
            EditorGUILayout.Space(10);
            
            // Simple description
            DrawHelpSection("What is Synaptic Pro?",
                "A tool that connects Unity with desktop AI apps like Claude, Gemini, and more.\n" +
                "No complex setup required - get started with one click.");
            
            // How to use
            DrawHelpSection("How to Use",
                "1. Click 'Start AI Connection' in the AI Connection tab\n" +
                "2. Open Claude/Gemini desktop app\n" +
                "3. Connect to localhost:3000 in AI app settings\n" +
                "4. AI is now available in Unity!");
            
            // Supported AI apps
            DrawHelpSection("Supported AI Apps",
                "• Claude Desktop (Recommended)\n" +
                "• Cursor / Windsurf\n" +
                "• VS Code (GitHub Copilot)\n" +
                "• Gemini CLI / Codex CLI\n" +
                "• Other MCP-compatible AI apps");
            
            // Troubleshooting
            DrawHelpSection("When Things Don't Work",
                "• Error when clicking 'Start AI Connection'\n" +
                "  → Run Unity as administrator\n\n" +
                "• Cannot connect from AI app\n" +
                "  → Check firewall settings\n\n" +
                "• Connection succeeds but cannot control Unity\n" +
                "  → Confirm 'Start AI Connection' is pressed in Unity");
            
            // Links
            EditorGUILayout.Space(20);
            GUILayout.Label("Documentation", EditorStyles.boldLabel);

            if (GUILayout.Button("Setup Guide"))
            {
                Application.OpenURL("https://www.synaptic-ai.net/ja/docs/setup");
            }

            if (GUILayout.Button("CLI Integration"))
            {
                Application.OpenURL("https://www.synaptic-ai.net/ja/docs/cli-integration");
            }

            if (GUILayout.Button("Discord Community"))
            {
                Application.OpenURL("https://discord.gg/MXwHCVWmPe");
            }
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }
        
        private void DrawStatusItem(string label, bool isInstalled, string version)
        {
            EditorGUILayout.BeginHorizontal();
            
            var icon = isInstalled ? "✅" : "❌";
            var color = isInstalled ? Color.green : Color.red;
            
            var oldColor = GUI.contentColor;
            GUI.contentColor = color;
            GUILayout.Label(icon, GUILayout.Width(20));
            GUI.contentColor = oldColor;
            
            EditorGUILayout.LabelField(label, GUILayout.Width(100));
            
            if (!string.IsNullOrEmpty(version))
            {
                EditorGUILayout.LabelField(version, EditorStyles.miniLabel);
            }
            
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }
        
        private void DrawHelpSection(string title, string content)
        {
            GUILayout.Label(title, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField(content, EditorStyles.wordWrappedLabel);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(10);
        }
        
        private void DrawConnectingAnimation()
        {
            // Animation box
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Rotating spinner
            var spinnerChars = new string[] { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
            var spinnerIndex = (int)(animationTime * 10) % spinnerChars.Length;

            // Current message (fixed to avoid looping)
            var messageIndex = Mathf.Min((int)(animationTime / 3), connectingMessages.Length - 1);
            var currentMessage = connectingMessages[messageIndex];
            
            // Animation display
            var animatedText = $"{spinnerChars[spinnerIndex]} {currentMessage}...";

            var centeredStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16
            };

            GUILayout.Label(animatedText, centeredStyle, GUILayout.Height(60));

            // Progress bar (100% at the last message)
            var progress = (messageIndex + 1f) / connectingMessages.Length;
            var rect = GUILayoutUtility.GetRect(0, 4, GUILayout.ExpandWidth(true));
            EditorGUI.ProgressBar(rect, progress, "");
            
            // Cancel button
            EditorGUILayout.Space(10);
            if (GUILayout.Button("⏹️ Cancel", GUILayout.Height(30)))
            {
                isConnecting = false;
                SynLog.Info("[Synaptic] AI connection cancelled");
                Repaint();
            }
            
            EditorGUILayout.EndVertical();
        }
        
        private async void StartAIConnection()
        {
            try
            {
                SynLog.Info("[Synaptic] Starting AI Connection...");

                // Start animation
                isConnecting = true;
                animationTime = 0f;
                Repaint();

                // Auto-setup if needed
                if (mcpSetupManager != null)
                {
                    var status = await mcpSetupManager.CheckSetupStatus();
                    if (!status.isMCPInstalled)
                    {
                        await mcpSetupManager.RunCompleteSetup();
                    }
                }

                // Start MCP server
                mcpServerRunning = await mcpSetupManager.StartMCPServer();

                // Stop animation
                isConnecting = false;

                if (mcpServerRunning)
                {
                    // Auto-generate configuration files for desktop AI
                    GenerateDesktopAIConfigs();

                    SynLog.Info("[Synaptic] ✅ AI connection setup complete! Desktop AI will auto-connect");
                    EditorUtility.DisplayDialog("AI Connection Ready", 
                        "Connection completed successfully.\n\n" +
                        "Unity tools are now available in AI apps.\n" +
                        "Type \"tools\" or \"unity\" to use the tools.", 
                        "OK");
                }
                else
                {
                    Debug.LogError("[Synaptic] ❌ Failed to start AI connection");

                    // Detailed guide when MCP server is not found
                    EditorUtility.DisplayDialog(
                        "MCP Server Not Found",
                        "Please launch Claude Desktop first to start AI connection.\n\n" +
                        "Steps:\n" +
                        "1. Launch Claude Desktop app\n" +
                        "2. Wait a moment, then press 'Start AI Connection' again\n\n" +
                        "Note: Unity acts as a client to the MCP server.",
                        "OK");
                }
                
                Repaint();
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] AI connection error: {e.Message}");
                isConnecting = false; // Stop animation
                mcpServerRunning = false;
                Repaint();
            }
        }
        
        private void StopAIConnection()
        {
            mcpServerRunning = false;
            SynLog.Info("[Synaptic] AI Connection stopped");
            Repaint();
        }
        
        private void ShowUsageGuide()
        {
            EditorUtility.DisplayDialog(
                "How to Use Unity MCP Tools",
                "Tips for reliable tool usage\n\n" +
                "1. Launch your AI tool:\n" +
                "   • Claude Desktop / Cursor / VS Code\n\n" +
                "2. Start a new chat\n\n" +
                "3. First, ask the AI:\n" +
                "   • \"What tools are available?\"\n" +
                "   • \"What can unity tools do?\"\n\n" +
                "4. Then, give specific instructions:\n" +
                "   • \"Use unity tools to create a red cube\"\n" +
                "   • \"Add a Player controller with tools\"\n\n" +
                "※ Including words like \"tools\" or \"unity\"\n" +
                "  helps the AI use tools more reliably!",
                "OK"
            );
        }
        
        private void ShowAIAppsDialog()
        {
            var option = EditorUtility.DisplayDialogComplex(
                "Select Desktop AI App",
                "Which AI app would you like to use?\n\n" +
                "ChatGPT: The world's most popular AI\n" +
                "Claude: AI with advanced code understanding\n\n" +
                "※ MCP-compatible version required",
                "ChatGPT",
                "Claude Desktop",
                "Cancel"
            );
            
            switch (option)
            {
                case 0: // ChatGPT
                    Application.OpenURL("https://chatgpt.com/");
                    break;
                case 1: // Claude
                    Application.OpenURL("https://claude.ai/download");
                    break;
            }
        }
        
        // Helper method to get appropriate server path based on selected AI client.
        // Windows でパスにスペースが含まれる場合は junction (空白なしのパス) を優先する。
        // これは Windsurf / Codex desktop 等の MCP クライアントが args 配列内の
        // 空白入りパスを正しく扱えない事象 (ESC 報告複数) への根本対処。
        private string GetServerScriptPath()
        {
            var mcpServerPath = GetEffectiveMCPServerPath();

            if (selectedAIClient == AIClientType.TokenSuperSaveMode)
            {
                // Token SuperSave mode: 3 meta-tools only
                return Path.Combine(mcpServerPath, "index-supersave.js");
            }
            else if (selectedAIClient == AIClientType.GitHubCopilot)
            {
                // Dynamic mode for GitHub Copilot (VS Code)
                return Path.Combine(mcpServerPath, "hub-server.js");
            }
            else if (selectedAIClient == AIClientType.CursorOrLMStudioEssential)
            {
                // Essential mode: 80 tools
                return Path.Combine(mcpServerPath, "index-essential.js");
            }
            else
            {
                // Full mode: 246 tools
                return Path.Combine(mcpServerPath, "index.js");
            }
        }

        // ───────── Windows path-with-spaces junction workaround ─────────
        //
        // 背景: Unity プロジェクトの Assets 配下にインストールされる Synaptic AI Pro
        // のフォルダ名は "Synaptic AI Pro" (スペース2つ含む)。Claude Desktop は
        // mcp config の args 配列を spawn(argsArray, {shell:false}) で正しく
        // 渡せるためスペース入りパスでも動作するが、Windsurf / Codex desktop 等の
        // 新興 MCP クライアントは内部で args を join して shell 経由 spawn する
        // 実装になっており、空白でコマンドが分割されて起動失敗する。
        //
        // 根本対処はクライアント側の実装修正だが、こちらでは Windows 環境で
        // %LOCALAPPDATA%\Synaptic\MCPServer のような空白なしパスに junction を
        // 作成し、MCP config 生成時にそのパスを使うことで全クライアントに対応する。

        // junction の保存先 (Windows 限定)
        private string GetJunctionTargetPath()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "Synaptic", "MCPServer");
        }

        // 現環境で junction 救済が必要か (Win かつパスに空白含む)
        private bool ShouldOfferJunction()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor) return false;
            var raw = FindMCPServerPath();
            return !string.IsNullOrEmpty(raw) && raw.Contains(' ');
        }

        // junction が既にあって target に向いているか
        private bool IsJunctionInstalled()
        {
            try
            {
                var junctionPath = GetJunctionTargetPath();
                if (!Directory.Exists(junctionPath)) return false;
                // junction なら中身に index-supersave.js があるはず
                return File.Exists(Path.Combine(junctionPath, "index-supersave.js"))
                    || File.Exists(Path.Combine(junctionPath, "index.js"));
            }
            catch { return false; }
        }

        // junction を新規作成 (PowerShell New-Item -ItemType Junction、管理者権限不要)
        private bool CreateJunction(out string error)
        {
            error = null;
            try
            {
                var src = FindMCPServerPath();
                var dst = GetJunctionTargetPath();
                var parentDir = Path.GetDirectoryName(dst);
                if (!Directory.Exists(parentDir)) Directory.CreateDirectory(parentDir);

                // 既存があれば消す (古い junction や空ディレクトリの場合)
                if (Directory.Exists(dst))
                {
                    try { Directory.Delete(dst, false); }
                    catch (IOException) { /* 中身あれば skip */ }
                }

                var psCmd = $"New-Item -ItemType Junction -Path '{dst}' -Target '{src}' -Force | Out-Null";
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{psCmd}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using (var proc = System.Diagnostics.Process.Start(psi))
                {
                    proc.WaitForExit(10000);
                    if (proc.ExitCode != 0)
                    {
                        error = proc.StandardError.ReadToEnd().Trim();
                        if (string.IsNullOrEmpty(error)) error = $"PowerShell exit {proc.ExitCode}";
                        return false;
                    }
                }
                return Directory.Exists(dst);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        // Setup が config 生成時に使うべき MCPServer パス。junction が使えるなら
        // それを返し、無ければ通常パス (空白入りの可能性あり) を返す。
        private string GetEffectiveMCPServerPath()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor && IsJunctionInstalled())
            {
                return GetJunctionTargetPath();
            }
            return FindMCPServerPath();
        }

        // Windows でパスにスペースがある時に表示する 1-tap 救済 UI
        private void DrawWindowsJunctionWorkaround()
        {
            if (!ShouldOfferJunction()) return;

            var installed = IsJunctionInstalled();
            var junctionPath = GetJunctionTargetPath();

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (installed)
            {
                // 既に作成済み — 状態表示のみ
                var oldColor = GUI.color;
                GUI.color = new Color(0.6f, 1f, 0.7f);
                EditorGUILayout.LabelField("✓ Windows Path Fix: Active", EditorStyles.boldLabel);
                GUI.color = oldColor;
                EditorGUILayout.LabelField(
                    "MCP configs will use the space-free junction path. This works\n" +
                    "with Windsurf, Codex desktop, OpenCode, and any other client.\n" +
                    $"Junction: {junctionPath}",
                    EditorStyles.wordWrappedMiniLabel);

                if (GUILayout.Button("Re-create Junction", GUILayout.Height(22)))
                {
                    if (CreateJunction(out var err))
                    {
                        EditorUtility.DisplayDialog("Synaptic AI Pro", "Junction re-created successfully.", "OK");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Synaptic AI Pro", $"Failed to re-create junction:\n{err}", "OK");
                    }
                }
            }
            else
            {
                // 未作成 — 説明 + 作成ボタン
                var oldColor = GUI.color;
                GUI.color = new Color(1f, 0.85f, 0.5f);
                EditorGUILayout.LabelField("⚠ Windows: Project Path Contains Spaces", EditorStyles.boldLabel);
                GUI.color = oldColor;

                EditorGUILayout.LabelField(
                    "Your project path includes a space (\"Synaptic AI Pro\" folder).\n" +
                    "Most MCP clients (Claude Desktop) handle this fine, but some\n" +
                    "newer clients (Windsurf, Codex desktop, OpenCode) fail to spawn\n" +
                    "Node.js when the args path contains spaces.\n\n" +
                    "Click below to create a space-free shortcut (junction) at:\n" +
                    $"  {junctionPath}\n\n" +
                    "After that, all generated MCP configs will use this path and\n" +
                    "every client will connect reliably. The junction is created\n" +
                    "without admin privileges and takes no extra disk space.",
                    EditorStyles.wordWrappedMiniLabel);

                EditorGUILayout.Space(4);
                var oldBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.6f, 0.9f, 0.6f);
                if (GUILayout.Button("⚡  One-Click: Create Junction for Windows MCP Clients", GUILayout.Height(28)))
                {
                    if (CreateJunction(out var err))
                    {
                        EditorUtility.DisplayDialog(
                            "Synaptic AI Pro",
                            "Junction created.\n\nNow press \"Complete MCP Setup\" or any per-client setup button below — the new configs will use the space-free path automatically.",
                            "OK");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog(
                            "Synaptic AI Pro",
                            $"Failed to create junction:\n{err}\n\nManual fallback: open PowerShell and run:\n  New-Item -ItemType Junction -Path '{junctionPath}' -Target '{FindMCPServerPath()}'",
                            "OK");
                    }
                }
                GUI.backgroundColor = oldBg;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6);
        }

        // Update package.json "type" field based on selected server
        private void UpdatePackageJsonForSelectedServer()
        {
            try
            {
                var mcpServerPath = FindMCPServerPath();
                var packageJsonPath = Path.Combine(mcpServerPath, "package.json");

                if (!File.Exists(packageJsonPath))
                {
                    SynLog.Warn("[Synaptic] package.json not found. Skipping update.");
                    return;
                }

                // Read existing package.json
                var packageJsonContent = File.ReadAllText(packageJsonPath);
                var packageJson = JsonConvert.DeserializeObject<Dictionary<string, object>>(packageJsonContent);

                if (selectedAIClient == AIClientType.GitHubCopilot)
                {
                    // hub-server.js uses ESM (import) - requires "type": "module"
                    packageJson["type"] = "module";
                    packageJson["main"] = "hub-server.js";
                    SynLog.Info("[Synaptic] package.json updated for hub-server.js (ESM)");
                }
                else if (selectedAIClient == AIClientType.TokenSuperSaveMode)
                {
                    // index-supersave.js uses CommonJS (require) - remove "type" field
                    if (packageJson.ContainsKey("type"))
                    {
                        packageJson.Remove("type");
                    }
                    packageJson["main"] = "index-supersave.js";
                    SynLog.Info("[Synaptic] package.json updated for index-supersave.js (CommonJS)");
                }
                else
                {
                    // index.js uses CommonJS (require) - remove "type" field or set to "commonjs"
                    if (packageJson.ContainsKey("type"))
                    {
                        packageJson.Remove("type");
                    }
                    packageJson["main"] = "index.js";
                    SynLog.Info("[Synaptic] package.json updated for index.js (CommonJS)");
                }

                // Write updated package.json
                File.WriteAllText(packageJsonPath, JsonConvert.SerializeObject(packageJson, Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] Failed to update package.json: {e.Message}");
            }
        }

        private void GenerateDesktopAIConfigs()
        {
            try
            {
                var detectedAIs = DetectInstalledAIs();
                var configuredCount = 0;
                var configuredTools = new List<string>();

                foreach (var ai in detectedAIs)
                {
                    switch (ai.ToLower())
                    {
                        case "claude":
                            if (GenerateClaudeConfig())
                            {
                                configuredCount++;
                                configuredTools.Add("Claude Desktop");
                            }
                            break;
                        case "chatgpt":
                            if (GenerateChatGPTConfig())
                            {
                                configuredCount++;
                                configuredTools.Add("ChatGPT Desktop");
                            }
                            break;
                        case "gemini":
                            if (GenerateGeminiConfig())
                            {
                                configuredCount++;
                                configuredTools.Add("Gemini Desktop");
                            }
                            break;
                    }
                }

                // Always configure Cursor (user-level config)
                if (GenerateCursorConfig())
                {
                    configuredCount++;
                    configuredTools.Add("Cursor");
                }

                // Always configure VS Code (project-level config)
                if (GenerateVSCodeConfig())
                {
                    configuredCount++;
                    configuredTools.Add("VS Code");
                }

                // Always configure LM Studio (user-level config)
                if (GenerateLMStudioConfig())
                {
                    configuredCount++;
                    configuredTools.Add("LM Studio");
                }

                // Configure CLI tools (all in one setup)
                try
                {
                    if (GenerateClaudeCodeSpecificConfig())
                    {
                        configuredCount++;
                        configuredTools.Add("Claude Code");
                    }
                }
                catch { /* Ignore individual CLI errors */ }

                try
                {
                    GenerateWindsurfConfig();
                    configuredCount++;
                    configuredTools.Add("Windsurf");
                }
                catch { /* Ignore individual CLI errors */ }

                try
                {
                    if (GenerateAntigravityConfig())
                    {
                        configuredCount++;
                        configuredTools.Add("Google Antigravity");
                    }
                }
                catch { /* Ignore individual CLI errors */ }

                try
                {
                    if (GenerateKiroConfig())
                    {
                        configuredCount++;
                        configuredTools.Add("Amazon Kiro");
                    }
                }
                catch { /* Ignore individual CLI errors */ }

                // Update MCPServer/mcp-config.json with current project path
                UpdateMCPServerConfig();

                SynLog.Info($"[Synaptic] Auto-generated {configuredCount} MCP configurations: {string.Join(", ", configuredTools)}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] Configuration file generation error: {e.Message}");
            }
        }
        
        private List<string> DetectInstalledAIs()
        {
            var installedAIs = new List<string>();
            
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                // macOS application detection
                var appsDir = "/Applications";
                if (Directory.Exists($"{appsDir}/Claude.app")) installedAIs.Add("Claude");
                if (Directory.Exists($"{appsDir}/ChatGPT.app")) installedAIs.Add("ChatGPT");
                if (Directory.Exists($"{appsDir}/Gemini.app")) installedAIs.Add("Gemini");

                // Homebrew cask installation detection
                var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (Directory.Exists($"{homeDir}/Applications/Claude.app")) installedAIs.Add("Claude");
                if (Directory.Exists($"{homeDir}/Applications/ChatGPT.app")) installedAIs.Add("ChatGPT");
            }
            else if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                // Windows installation detection
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

                // Claude: Check installation paths, config directory, AND Microsoft Store
                var hasClaude = Directory.Exists($"{programFiles}/Claude") ||
                    Directory.Exists($"{localAppData}/Programs/Claude") ||
                    Directory.Exists($"{appData}/Claude");

                // Also check for Microsoft Store version
                if (!hasClaude)
                {
                    var packagesDir = Path.Combine(localAppData, "Packages");
                    if (Directory.Exists(packagesDir))
                    {
                        try
                        {
                            var claudePackages = Directory.GetDirectories(packagesDir, "Claude_*");
                            if (claudePackages.Length == 0)
                            {
                                // Fallback: enumerate and filter
                                var allPackages = Directory.GetDirectories(packagesDir);
                                hasClaude = allPackages.Any(p =>
                                    Path.GetFileName(p).StartsWith("Claude", StringComparison.OrdinalIgnoreCase));
                            }
                            else
                            {
                                hasClaude = true;
                            }
                        }
                        catch { }
                    }
                }

                if (hasClaude)
                {
                    installedAIs.Add("Claude");
                }

                // ChatGPT
                if (Directory.Exists($"{programFiles}/ChatGPT") ||
                    Directory.Exists($"{localAppData}/ChatGPT") ||
                    Directory.Exists($"{appData}/ChatGPT"))
                {
                    installedAIs.Add("ChatGPT");
                }
            }
            
            return installedAIs.Distinct().ToList();
        }
        
        private bool GenerateClaudeConfig()
        {
            try
            {
                var claudeConfigDir = DetectClaudeConfigPath();
                if (string.IsNullOrEmpty(claudeConfigDir))
                {
                    SynLog.Warn("[Synaptic] Claude Desktop config path not detected.");
                    return false;
                }

            if (!Directory.Exists(claudeConfigDir))
            {
                Directory.CreateDirectory(claudeConfigDir);
            }

            var configPath = Path.Combine(claudeConfigDir, "claude_desktop_config.json");

            // Load existing configuration
            dynamic existingConfig = null;
            if (File.Exists(configPath))
            {
                try
                {
                    var existingJson = File.ReadAllText(configPath);
                    existingConfig = JsonConvert.DeserializeObject(existingJson);
                }
                catch (Exception e)
                {
                    SynLog.Warn($"[Synaptic] Failed to load existing Claude configuration: {e.Message}");
                }
            }

            // Unity MCP server configuration (v1.1.0: uses selected server script)
            // Normalize paths for cross-platform JSON compatibility (Windows: \ -> /)
            var unityMcpServer = new
            {
                command = FindNodePath(),
                args = new[] { NormalizePathForJson(GetServerScriptPath()) },
                cwd = NormalizePathForJson(FindMCPServerPath()),  // CRITICAL: Node.js needs to run from MCPServer directory
                env = new { }
            };

            // Merge with existing configuration
            dynamic claudeConfig;
            if (existingConfig?.mcpServers != null)
            {
                // Preserve existing mcpServers and add unity server
                var mcpServers = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                    JsonConvert.SerializeObject(existingConfig.mcpServers));
                mcpServers["unity-synaptic"] = unityMcpServer;

                claudeConfig = new
                {
                    mcpServers = mcpServers
                };

                // Preserve other existing settings
                var configDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                    JsonConvert.SerializeObject(existingConfig));
                configDict["mcpServers"] = mcpServers;
                claudeConfig = configDict;
            }
            else
            {
                // Create new configuration
                claudeConfig = new
                {
                    mcpServers = new Dictionary<string, object>
                    {
                        ["unity-synaptic"] = unityMcpServer
                    }
                };
            }
            
            File.WriteAllText(configPath, JsonConvert.SerializeObject(claudeConfig, Newtonsoft.Json.Formatting.Indented));

            SynLog.Info($"[Synaptic] Claude configuration file created: {configPath}");
            return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] Claude configuration error: {e.Message}");
                return false;
            }
        }
        
        private string DetectClaudeConfigPath()
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                // macOS Claude configuration path candidates
                var candidates = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Claude"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "com.anthropic.claudefordesktop"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "claude")
                };
                
                foreach (var path in candidates)
                {
                    var parentDir = Path.GetDirectoryName(path);
                    if (Directory.Exists(parentDir))
                    {
                        return path;
                    }
                }
            }
            else if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                // Windows Claude configuration path candidates
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                // Find Microsoft Store package path dynamically
                string msStoreClaudePath = null;
                var packagesDir = Path.Combine(localAppData, "Packages");
                if (Directory.Exists(packagesDir))
                {
                    try
                    {
                        // Method 1: Direct pattern match
                        var claudePackages = Directory.GetDirectories(packagesDir, "Claude_*");

                        // Method 2: If pattern fails, enumerate and filter manually
                        if (claudePackages.Length == 0)
                        {
                            var allPackages = Directory.GetDirectories(packagesDir);
                            claudePackages = allPackages.Where(p =>
                                Path.GetFileName(p).StartsWith("Claude", StringComparison.OrdinalIgnoreCase)
                            ).ToArray();
                        }

                        if (claudePackages.Length > 0)
                        {
                            var candidatePath = Path.Combine(claudePackages[0], "LocalCache", "Roaming", "Claude");
                            // Verify the path actually exists or has config
                            if (Directory.Exists(candidatePath) ||
                                File.Exists(Path.Combine(candidatePath, "claude_desktop_config.json")))
                            {
                                msStoreClaudePath = candidatePath;
                            }
                            else
                            {
                                // Even if directory doesn't exist yet, use it if package is found
                                msStoreClaudePath = candidatePath;
                            }
                        }
                    }
                    catch { /* Ignore directory access errors */ }
                }

                var candidatesList = new List<string>
                {
                    Path.Combine(appData, "Claude"),                    // %APPDATA%\Claude (most common)
                    Path.Combine(localAppData, "Claude"),               // %LOCALAPPDATA%\Claude
                    Path.Combine(localAppData, "Programs", "Claude"),   // %LOCALAPPDATA%\Programs\Claude
                };

                // If Microsoft Store version is installed, ALWAYS use that path
                // (MS Store version only reads from its sandboxed location)
                if (!string.IsNullOrEmpty(msStoreClaudePath))
                {
                    return msStoreClaudePath;
                }

                var candidates = candidatesList.ToArray();

                // For standard installation, check if existing config file exists
                foreach (var path in candidates)
                {
                    var configFile = Path.Combine(path, "claude_desktop_config.json");
                    if (File.Exists(configFile))
                    {
                        return path;
                    }
                }

                // If no existing config, check if Claude directory exists
                foreach (var path in candidates)
                {
                    if (Directory.Exists(path))
                    {
                        return path;
                    }
                }

                // Default to standard %APPDATA%\Claude path (will create if needed)
                return Path.Combine(appData, "Claude");
            }

            return null;
        }

        private bool GenerateGeminiConfig()
        {
            try
            {
                var geminiConfigDir = DetectGeminiConfigPath();
                if (string.IsNullOrEmpty(geminiConfigDir))
                {
                    SynLog.Warn("[Synaptic] Gemini Desktop not found.");
                    return false;
                }

            if (!Directory.Exists(geminiConfigDir))
            {
                Directory.CreateDirectory(geminiConfigDir);
            }

            var geminiConfig = new
            {
                servers = new[]
                {
                    new
                    {
                        name = "unity-synaptic",
                        url = "http://localhost:3000",
                        type = "mcp"
                    }
                }
            };

            var configPath = Path.Combine(geminiConfigDir, "config.json");
            File.WriteAllText(configPath, JsonConvert.SerializeObject(geminiConfig, Newtonsoft.Json.Formatting.Indented));

            SynLog.Info($"[Synaptic] Gemini configuration file created: {configPath}");
            return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] Gemini configuration error: {e.Message}");
                return false;
            }
        }
        
        private string DetectGeminiConfigPath()
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                var candidates = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "gemini"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Gemini")
                };

                foreach (var path in candidates)
                {
                    var parentDir = Path.GetDirectoryName(path);
                    if (Directory.Exists(parentDir))
                    {
                        return path;
                    }
                }
            }
            else if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gemini");
            }

            return null;
        }

        private string DetectCursorConfigPath()
        {
            // Cursor native MCP config: ~/.cursor/mcp.json
            var cursorDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cursor"
            );

            return cursorDir;
        }

        private bool GenerateCursorConfig()
        {
            try
            {
                var cursorConfigDir = DetectCursorConfigPath();
                if (string.IsNullOrEmpty(cursorConfigDir))
                {
                    SynLog.Warn("[Synaptic] Could not determine Cursor config path.");
                    return false;
                }

                if (!Directory.Exists(cursorConfigDir))
                {
                    Directory.CreateDirectory(cursorConfigDir);
                }

                var configPath = Path.Combine(cursorConfigDir, "mcp.json");

                // Load existing configuration
                dynamic existingConfig = null;
                if (File.Exists(configPath))
                {
                    try
                    {
                        var existingJson = File.ReadAllText(configPath);
                        existingConfig = JsonConvert.DeserializeObject(existingJson);
                    }
                    catch (Exception e)
                    {
                        SynLog.Warn($"[Synaptic] Failed to load existing Cursor configuration: {e.Message}");
                    }
                }

                // Unity MCP server configuration (v1.1.3: uses selected server script)
                // Normalize paths for cross-platform JSON compatibility (Windows: \ -> /)
                // Essential mode uses index-essential.js (80 tools), Full mode uses index.js (246 tools)
                var unityMcpServer = new
                {
                    command = FindNodePath(),
                    args = new[] { NormalizePathForJson(GetServerScriptPath()) },
                    cwd = NormalizePathForJson(FindMCPServerPath())  // CRITICAL: Node.js needs to run from MCPServer directory
                };

                // Merge with existing configuration
                dynamic cursorConfig;
                if (existingConfig?.mcpServers != null)
                {
                    var mcpServers = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(existingConfig.mcpServers));
                    mcpServers["unity-synaptic"] = unityMcpServer;

                    var configDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(existingConfig));
                    configDict["mcpServers"] = mcpServers;
                    cursorConfig = configDict;
                }
                else
                {
                    cursorConfig = new
                    {
                        mcpServers = new Dictionary<string, object>
                        {
                            ["unity-synaptic"] = unityMcpServer
                        }
                    };
                }

                File.WriteAllText(configPath, JsonConvert.SerializeObject(cursorConfig, Newtonsoft.Json.Formatting.Indented));

                SynLog.Info($"[Synaptic] ✅ Cursor configuration file created: {configPath}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] Cursor configuration error: {e.Message}");
                return false;
            }
        }

        private string DetectVSCodeConfigPath()
        {
            // VS Code project-level MCP config: .vscode/mcp.json
            var projectPath = Application.dataPath.Replace("/Assets", "");
            var vscodeDir = Path.Combine(projectPath, ".vscode");

            return vscodeDir;
        }

        private bool GenerateVSCodeConfig()
        {
            try
            {
                var vscodeConfigDir = DetectVSCodeConfigPath();
                if (string.IsNullOrEmpty(vscodeConfigDir))
                {
                    SynLog.Warn("[Synaptic] Could not determine VS Code config path.");
                    return false;
                }

                if (!Directory.Exists(vscodeConfigDir))
                {
                    Directory.CreateDirectory(vscodeConfigDir);
                }

                var configPath = Path.Combine(vscodeConfigDir, "mcp.json");

                // Load existing configuration
                dynamic existingConfig = null;
                if (File.Exists(configPath))
                {
                    try
                    {
                        var existingJson = File.ReadAllText(configPath);
                        existingConfig = JsonConvert.DeserializeObject(existingJson);
                    }
                    catch (Exception e)
                    {
                        SynLog.Warn($"[Synaptic] Failed to load existing VS Code configuration: {e.Message}");
                    }
                }

                // Unity MCP server configuration (v1.1.0: VS Code format with "servers" and "type")
                // Normalize paths for cross-platform JSON compatibility (Windows: \ -> /)
                var unityMcpServer = new
                {
                    type = "stdio",  // Required by VS Code
                    command = FindNodePath(),
                    args = new[] { NormalizePathForJson(GetServerScriptPath()) },
                    cwd = NormalizePathForJson(FindMCPServerPath())  // CRITICAL: Node.js needs to run from MCPServer directory
                };

                // Merge with existing configuration (VS Code uses "servers", not "mcpServers")
                dynamic vscodeConfig;
                if (existingConfig?.servers != null)
                {
                    var servers = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(existingConfig.servers));
                    servers["unity-synaptic"] = unityMcpServer;

                    var configDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(existingConfig));
                    configDict["servers"] = servers;
                    vscodeConfig = configDict;
                }
                else
                {
                    vscodeConfig = new
                    {
                        servers = new Dictionary<string, object>
                        {
                            ["unity-synaptic"] = unityMcpServer
                        }
                    };
                }

                File.WriteAllText(configPath, JsonConvert.SerializeObject(vscodeConfig, Newtonsoft.Json.Formatting.Indented));

                SynLog.Info($"[Synaptic] ✅ VS Code configuration file created: {configPath}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] VS Code configuration error: {e.Message}");
                return false;
            }
        }

        private string DetectLMStudioConfigPath()
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                // macOS: ~/.lmstudio/
                return Path.Combine(homeDir, ".lmstudio");
            }
            else if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                // Windows: %USERPROFILE%/.lmstudio/
                return Path.Combine(homeDir, ".lmstudio");
            }

            return null;
        }

        private bool GenerateLMStudioConfig()
        {
            try
            {
                var lmstudioConfigDir = DetectLMStudioConfigPath();
                if (string.IsNullOrEmpty(lmstudioConfigDir))
                {
                    SynLog.Warn("[Synaptic] Could not determine LM Studio config path.");
                    return false;
                }

                if (!Directory.Exists(lmstudioConfigDir))
                {
                    Directory.CreateDirectory(lmstudioConfigDir);
                }

                var configPath = Path.Combine(lmstudioConfigDir, "mcp.json");

                // Load existing configuration
                dynamic existingConfig = null;
                if (File.Exists(configPath))
                {
                    try
                    {
                        var existingJson = File.ReadAllText(configPath);
                        existingConfig = JsonConvert.DeserializeObject(existingJson);
                    }
                    catch (Exception e)
                    {
                        SynLog.Warn($"[Synaptic] Failed to load existing LM Studio configuration: {e.Message}");
                    }
                }

                // Unity MCP server configuration (LM Studio uses same format as Cursor)
                // Normalize paths for cross-platform JSON compatibility (Windows: \ -> /)
                var unityMcpServer = new
                {
                    command = FindNodePath(),
                    args = new[] { NormalizePathForJson(GetServerScriptPath()) },
                    cwd = NormalizePathForJson(FindMCPServerPath()),  // CRITICAL: Node.js needs to run from MCPServer directory
                    env = new { }
                };

                // Merge with existing configuration
                dynamic lmstudioConfig;
                if (existingConfig?.mcpServers != null)
                {
                    var mcpServers = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(existingConfig.mcpServers));
                    mcpServers["unity-synaptic"] = unityMcpServer;

                    var configDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(existingConfig));
                    configDict["mcpServers"] = mcpServers;
                    lmstudioConfig = configDict;
                }
                else
                {
                    lmstudioConfig = new
                    {
                        mcpServers = new Dictionary<string, object>
                        {
                            ["unity-synaptic"] = unityMcpServer
                        }
                    };
                }

                File.WriteAllText(configPath, JsonConvert.SerializeObject(lmstudioConfig, Newtonsoft.Json.Formatting.Indented));

                SynLog.Info($"[Synaptic] ✅ LM Studio configuration file created: {configPath}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] LM Studio configuration error: {e.Message}");
                return false;
            }
        }

        private bool UpdateMCPServerConfig()
        {
            try
            {
                var mcpServerPath = FindMCPServerPath();
                if (string.IsNullOrEmpty(mcpServerPath))
                {
                    SynLog.Warn("[Synaptic] MCP Server path not found.");
                    return false;
                }

                var configPath = Path.Combine(mcpServerPath, "mcp-config.json");
                if (!File.Exists(configPath))
                {
                    SynLog.Warn($"[Synaptic] mcp-config.json not found at: {configPath}");
                    return false;
                }

                // Read the config file
                var configContent = File.ReadAllText(configPath);

                // Replace placeholder with actual path
                configContent = configContent.Replace("{{PROJECT_MCP_SERVER_PATH}}", mcpServerPath);

                // Write back
                File.WriteAllText(configPath, configContent);

                SynLog.Info($"[Synaptic] ✅ Updated mcp-config.json with project path: {mcpServerPath}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] mcp-config.json update error: {e.Message}");
                return false;
            }
        }

        private bool GenerateChatGPTConfig()
        {
            try
            {
                var chatgptConfigDir = DetectChatGPTConfigPath();
                if (string.IsNullOrEmpty(chatgptConfigDir))
                {
                    SynLog.Warn("[Synaptic] ChatGPT Desktop not found.");
                    return false;
                }

            if (!Directory.Exists(chatgptConfigDir))
            {
                Directory.CreateDirectory(chatgptConfigDir);
            }

            var configPath = Path.Combine(chatgptConfigDir, "config.json");

            // Load existing configuration
            dynamic existingConfig = null;
            if (File.Exists(configPath))
            {
                try
                {
                    var existingJson = File.ReadAllText(configPath);
                    existingConfig = JsonConvert.DeserializeObject(existingJson);
                }
                catch (Exception e)
                {
                    SynLog.Warn($"[Synaptic] Failed to load existing ChatGPT configuration: {e.Message}");
                }
            }

            // Unity MCP server configuration (v1.1.0: uses selected server script)
            // Normalize paths for cross-platform JSON compatibility (Windows: \ -> /)
            var unityMcpServer = new
            {
                command = FindNodePath(),
                args = new[] { NormalizePathForJson(GetServerScriptPath()) },
                env = new { }
            };

            // Merge with existing configuration
            dynamic chatgptConfig;
            if (existingConfig?.mcpServers != null)
            {
                // Preserve existing mcpServers and add unity server
                var mcpServers = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                    JsonConvert.SerializeObject(existingConfig.mcpServers));
                mcpServers["unity-synaptic"] = unityMcpServer;

                // Preserve other existing settings
                var configDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                    JsonConvert.SerializeObject(existingConfig));
                configDict["mcpServers"] = mcpServers;
                chatgptConfig = configDict;
            }
            else
            {
                // Create new configuration
                chatgptConfig = new
                {
                    mcpServers = new Dictionary<string, object>
                    {
                        ["unity-synaptic"] = unityMcpServer
                    }
                };
            }

            File.WriteAllText(configPath, JsonConvert.SerializeObject(chatgptConfig, Newtonsoft.Json.Formatting.Indented));

            SynLog.Info($"[Synaptic] ChatGPT configuration file created: {configPath}");
            return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] ChatGPT configuration error: {e.Message}");
                return false;
            }
        }
        
        private string DetectChatGPTConfigPath()
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                var candidates = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "com.openai.chat"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "ChatGPT"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "chatgpt")
                };
                
                foreach (var path in candidates)
                {
                    var parentDir = Path.GetDirectoryName(path);
                    if (Directory.Exists(parentDir))
                    {
                        return path;
                    }
                }
            }
            else if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChatGPT");
            }
            
            return null;
        }
        
        private async Task RefreshStatus()
        {
            mcpStatus = await mcpSetupManager.CheckSetupStatus();
            Repaint();
        }

        private void CheckMCPStatus()
        {
            // バックグラウンドスレッドで実行（Hold onを防ぐ）
            System.Threading.ThreadPool.QueueUserWorkItem(async _ =>
            {
                try
                {
                    mcpStatus = await mcpSetupManager.CheckSetupStatus();
                    EditorApplication.delayCall += () => { if (this) Repaint(); };
                }
                catch { }
            });
        }
        
        private void SaveMCPSettings()
        {
            // Save to .env file
            var envPath = Path.Combine(Application.dataPath.Replace("/Assets", ""), "MCPServer", ".env");
            var envContent = new List<string>
            {
                $"PORT={mcpPort}",
                $"WS_PORT={wsPort}"
            };

            System.IO.File.WriteAllLines(envPath, envContent);
            SynLog.Info("[Synaptic] MCP settings saved");
        }


        private async void StartMCPServer()
        {
            mcpServerRunning = await mcpSetupManager.StartMCPServer();
            Repaint();
        }

        private void StopMCPServer()
        {
            // Server stop implementation
            mcpServerRunning = false;
            Repaint();
        }
        
        private async void RestartMCPServer()
        {
            StopMCPServer();
            await Task.Delay(1000);
            await Task.Run(() => StartMCPServer());
        }
        
        private void ConfigureMCP()
        {
            try
            {
                SynLog.Info("[Synaptic] Starting MCP setup...");
                
                // Check and install dependencies
                if (!CheckAndInstallDependencies())
                {
                    EditorUtility.DisplayDialog(
                        "Setup Error",
                        "Failed to install required dependencies. Please check the console for details.",
                        "OK"
                    );
                    return;
                }
                
                // Generate MCP configuration files
                GenerateDesktopAIConfigs();

                // Update package.json based on selected server
                UpdatePackageJsonForSelectedServer();

                // Initialize MCP server
                if (mcpSetupManager == null)
                {
                    mcpSetupManager = NexusMCPSetupManager.Instance;
                }

                // Set configuration complete flag
                mcpConfigured = true;
                
                SynLog.Info("[Synaptic] MCP setup completed. Ready for AI integration.");

                // Display success message based on selected AI client (v1.1.0)
                string successMessage;
                if (selectedAIClient == AIClientType.GitHubCopilot)
                {
                    successMessage =
                        "MCP configuration completed for GitHub Copilot!\n\n" +
                        "Mode: Dynamic Tool Loading (hub-server.js)\n" +
                        "Configuration: .vscode/mcp.json\n\n" +
                        "⚠️ Important: Restart VS Code to activate Unity MCP.\n\n" +
                        "Initial tools: 8 management tools\n" +
                        "Dynamic loading: Use select_tools() to load more\n\n" +
                        "Example:\n" +
                        "\"Use select_tools to load GameObject and Material tools\"";
                }
                else
                {
                    successMessage =
                        "MCP configuration completed successfully!\n\n" +
                        "Mode: Full Mode (index.js) - All 246 tools\n" +
                        "Configurations created for:\n" +
                        "• Claude Desktop (claude_desktop_config.json)\n" +
                        "• Cursor (~/.cursor/mcp.json)\n" +
                        "• VS Code (.vscode/mcp.json)\n\n" +
                        "⚠️ Important: Restart/Reload your AI tool to activate Unity MCP.\n\n" +
                        "After restarting, ask:\n" +
                        "\"What Unity tools are available?\"";
                }

                EditorUtility.DisplayDialog(
                    "MCP Setup Complete",
                    successMessage,
                    "OK"
                );
                
                Repaint();
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] MCP configuration error: {e.Message}");
                EditorUtility.DisplayDialog(
                    "MCP Setup Error",
                    $"An error occurred during MCP setup:\n{e.Message}",
                    "OK"
                );
            }
        }
        
        private void ResetMCPConfiguration()
        {
            var confirmed = EditorUtility.DisplayDialog(
                "Reset MCP Settings",
                "Reset MCP settings and reconfigure?\n\n" +
                "Current AI connection will also be stopped.",
                "Reset",
                "Cancel"
            );
            
            if (confirmed)
            {
                // Reset configuration flag
                mcpConfigured = false;
                mcpSetupManager = null;

                SynLog.Info("[Synaptic] MCP settings have been reset. Please reconfigure.");

                EditorUtility.DisplayDialog(
                    "Settings Reset Complete",
                    "MCP settings have been reset.\n" +
                    "Please reconfigure from the 'Complete MCP Setup' button.",
                    "OK"
                );

                Repaint();
            }
        }
        
        // CLI AI configuration generation methods
        private void GenerateClaudeCodeConfig()
        {
            try
            {
                // Create Claude Code specific configuration (no detection required)
                if (GenerateClaudeCodeSpecificConfig())
                {
                    SynLog.Info("[Synaptic] Claude Code configuration complete");

                    EditorUtility.DisplayDialog(
                        "Claude Code Configuration Complete",
                        "Claude Code configuration has been completed.\n\n" +
                        "Configuration files:\n" +
                        "• Project: .claude/settings.local.json\n" +
                        "• Global: ~/.claude.json\n\n" +
                        "How to use:\n" +
                        "1. Restart Claude Code\n" +
                        "2. Unity MCP tools will be automatically enabled\n\n" +
                        "Verification commands:\n" +
                        "• claude mcp list\n" +
                        "• claude mcp get unity-synaptic",
                        "OK"
                    );
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", "Failed to create Claude Code configuration. Please check Unity Console for errors.", "OK");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] Claude Code configuration error: {e.Message}");
                EditorUtility.DisplayDialog("Error", $"Claude Code configuration failed:\n{e.Message}", "OK");
            }
        }


        // DEPRECATED: CLI MCP configurations removed - HTTP API is the recommended approach
        // private void GenerateGeminiCLIConfig() { ... }
        // private void GenerateCodexCLIConfig() { ... }
        
        
        private void GenerateWindsurfConfig()
        {
            try
            {
                // Windsurf uses user-level config
                var windsurfConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".windsurf", "mcp_servers.json"
                );
                
                var windsurfDir = Path.GetDirectoryName(windsurfConfigPath);
                if (!Directory.Exists(windsurfDir))
                {
                    Directory.CreateDirectory(windsurfDir);
                }
                
                // Unity MCP server settings
                // Normalize paths for cross-platform JSON compatibility (Windows: \ -> /)
                var unityMcpServer = new
                {
                    command = FindNodePath(),
                    args = new[] { NormalizePathForJson(GetServerScriptPath()) },
                    env = new Dictionary<string, object>(),
                    description = "Unity game development tools"
                };

                // Load existing settings
                dynamic existingConfig = null;
                if (File.Exists(windsurfConfigPath))
                {
                    try
                    {
                        var existingJson = File.ReadAllText(windsurfConfigPath);
                        existingConfig = JsonConvert.DeserializeObject(existingJson);
                    }
                    catch (Exception e)
                    {
                        SynLog.Warn($"[Synaptic] Failed to load existing Windsurf config: {e.Message}");
                    }
                }
                
                // Windsurf MCP settings (2025 specification)
                dynamic windsurfConfig;
                if (existingConfig?.servers != null)
                {
                    var servers = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(existingConfig.servers));
                    servers["unity-synaptic"] = unityMcpServer;
                    
                    var configDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(existingConfig));
                    configDict["servers"] = servers;
                    windsurfConfig = configDict;
                }
                else
                {
                    windsurfConfig = new
                    {
                        servers = new Dictionary<string, object>
                        {
                            ["unity-synaptic"] = unityMcpServer
                        }
                    };
                }
                
                File.WriteAllText(windsurfConfigPath, JsonConvert.SerializeObject(windsurfConfig, Newtonsoft.Json.Formatting.Indented));
                
                SynLog.Info($"[Synaptic] Windsurf config file created: {windsurfConfigPath}");
                
                EditorUtility.DisplayDialog(
                    "Windsurf Configuration Complete",
                    "Windsurf MCP configuration created successfully.\n\n" +
                    "Config file: ~/.windsurf/mcp_servers.json\n\n" +
                    "Next steps:\n" +
                    "1. Restart Windsurf\n" +
                    "2. Unity MCP server will be available\n" +
                    "3. Use '@unity-synaptic' to access Unity tools\n\n" +
                    "The IDE that writes with you!",
                    "OK"
                );
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] Windsurf config error: {e.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to create Windsurf configuration:\n{e.Message}", "OK");
            }
        }

        private bool GenerateAntigravityConfig()
        {
            try
            {
                // Google Antigravity config path
                string configPath;
                if (Application.platform == RuntimePlatform.OSXEditor)
                {
                    // macOS: ~/.gemini/antigravity/mcp_config.json
                    configPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".gemini", "antigravity", "mcp_config.json"
                    );
                }
                else if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    // Windows: %USERPROFILE%\.gemini\antigravity\mcp_config.json
                    configPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".gemini", "antigravity", "mcp_config.json"
                    );
                }
                else
                {
                    SynLog.Warn("[Synaptic] Antigravity config: Unsupported platform");
                    return false;
                }

                var configDir = Path.GetDirectoryName(configPath);
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }

                // Load existing configuration
                dynamic existingConfig = null;
                if (File.Exists(configPath))
                {
                    try
                    {
                        var existingJson = File.ReadAllText(configPath);
                        existingConfig = JsonConvert.DeserializeObject(existingJson);
                    }
                    catch (Exception e)
                    {
                        SynLog.Warn($"[Synaptic] Failed to load existing Antigravity config: {e.Message}");
                    }
                }

                // Unity MCP server configuration
                var unityMcpServer = new
                {
                    command = FindNodePath(),
                    args = new[] { NormalizePathForJson(GetServerScriptPath()) }
                };

                // Merge with existing configuration
                dynamic antigravityConfig;
                if (existingConfig?.mcpServers != null)
                {
                    var mcpServers = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(existingConfig.mcpServers));
                    mcpServers["unity-synaptic"] = unityMcpServer;

                    var configDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(existingConfig));
                    configDict["mcpServers"] = mcpServers;
                    antigravityConfig = configDict;
                }
                else
                {
                    antigravityConfig = new
                    {
                        mcpServers = new Dictionary<string, object>
                        {
                            ["unity-synaptic"] = unityMcpServer
                        }
                    };
                }

                File.WriteAllText(configPath, JsonConvert.SerializeObject(antigravityConfig, Newtonsoft.Json.Formatting.Indented));
                SynLog.Info($"[Synaptic] ✅ Google Antigravity config created: {configPath}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] Antigravity config error: {e.Message}");
                return false;
            }
        }

        private bool GenerateKiroConfig()
        {
            try
            {
                // Amazon Kiro config path
                string configPath;
                if (Application.platform == RuntimePlatform.OSXEditor)
                {
                    // macOS: ~/.kiro/settings/mcp.json
                    configPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".kiro", "settings", "mcp.json"
                    );
                }
                else if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    // Windows: %USERPROFILE%\.kiro\settings\mcp.json
                    configPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".kiro", "settings", "mcp.json"
                    );
                }
                else
                {
                    SynLog.Warn("[Synaptic] Kiro config: Unsupported platform");
                    return false;
                }

                var configDir = Path.GetDirectoryName(configPath);
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }

                // Load existing configuration
                dynamic existingConfig = null;
                if (File.Exists(configPath))
                {
                    try
                    {
                        var existingJson = File.ReadAllText(configPath);
                        existingConfig = JsonConvert.DeserializeObject(existingJson);
                    }
                    catch (Exception e)
                    {
                        SynLog.Warn($"[Synaptic] Failed to load existing Kiro config: {e.Message}");
                    }
                }

                // Unity MCP server configuration
                var unityMcpServer = new
                {
                    command = FindNodePath(),
                    args = new[] { NormalizePathForJson(GetServerScriptPath()) },
                    disabled = false
                };

                // Merge with existing configuration
                dynamic kiroConfig;
                if (existingConfig?.mcpServers != null)
                {
                    var mcpServers = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(existingConfig.mcpServers));
                    mcpServers["unity-synaptic"] = unityMcpServer;

                    var configDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(existingConfig));
                    configDict["mcpServers"] = mcpServers;
                    kiroConfig = configDict;
                }
                else
                {
                    kiroConfig = new
                    {
                        mcpServers = new Dictionary<string, object>
                        {
                            ["unity-synaptic"] = unityMcpServer
                        }
                    };
                }

                File.WriteAllText(configPath, JsonConvert.SerializeObject(kiroConfig, Newtonsoft.Json.Formatting.Indented));
                SynLog.Info($"[Synaptic] ✅ Amazon Kiro config created: {configPath}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] Kiro config error: {e.Message}");
                return false;
            }
        }

        private void GenerateAllCLIConfigs()
        {
            try
            {
                SynLog.Info("[Synaptic] Configuring all MCP clients...");
                
                var configuredTools = new List<string>();
                
                // Claude Code
                try
                {
                    if (GenerateClaudeCodeSpecificConfig())
                    {
                        configuredTools.Add("Claude Code");
                    }
                }
                catch { /* Ignore individual CLI errors */ }
                
                // Cursor
                try
                {
                    if (GenerateCursorConfig())
                    {
                        configuredTools.Add("Cursor");
                    }
                }
                catch (Exception e)
                {
                    SynLog.Warn($"[Synaptic] Cursor config skipped: {e.Message}");
                }

                // VS Code
                try
                {
                    if (GenerateVSCodeConfig())
                    {
                        configuredTools.Add("VS Code");
                    }
                }
                catch (Exception e)
                {
                    SynLog.Warn($"[Synaptic] VS Code config skipped: {e.Message}");
                }

                // Windsurf
                try
                {
                    GenerateWindsurfConfig();
                    configuredTools.Add("Windsurf");
                }
                catch (Exception e)
                {
                    SynLog.Warn($"[Synaptic] Windsurf config skipped: {e.Message}");
                }

                // Google Antigravity
                try
                {
                    if (GenerateAntigravityConfig())
                    {
                        configuredTools.Add("Google Antigravity");
                    }
                }
                catch (Exception e)
                {
                    SynLog.Warn($"[Synaptic] Antigravity config skipped: {e.Message}");
                }

                // Amazon Kiro
                try
                {
                    if (GenerateKiroConfig())
                    {
                        configuredTools.Add("Amazon Kiro");
                    }
                }
                catch (Exception e)
                {
                    SynLog.Warn($"[Synaptic] Kiro config skipped: {e.Message}");
                }

                // Generic settings for other MCP-compatible tools
                GenerateUniversalCLIConfig();
                configuredTools.Add("Generic MCP Clients");
                
                SynLog.Info($"[Synaptic] All CLI AI configs completed: {string.Join(", ", configuredTools)}");
                
                EditorUtility.DisplayDialog(
                    "MCP Client Configuration Complete",
                    $"Successfully configured the following MCP clients:\n\n" +
                    $"✅ {string.Join("\n✅ ", configuredTools)}\n\n" +
                    "Unity MCP tools are now available in these applications.\n\n" +
                    "Next steps:\n" +
                    "1. Restart the configured applications\n" +
                    "2. Unity MCP tools will be automatically available\n" +
                    "3. Check each app's MCP panel or use '@unity-synaptic' mention",
                    "OK"
                );
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] All CLI AI config error: {e.Message}");
                EditorUtility.DisplayDialog("Error", $"Error during CLI AI configuration:\n{e.Message}", "OK");
            }
        }
        
        private void GenerateUniversalCLIConfig()
        {
            try
            {
                var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var configDir = Path.Combine(homeDir, ".config", "synaptic-pro");
                
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }
                
                // Generic MCP config file
                // Normalize paths for cross-platform JSON compatibility (Windows: \ -> /)
                var universalConfig = new
                {
                    mcp_servers = new Dictionary<string, object>
                    {
                        ["unity-synaptic"] = new
                        {
                            command = FindNodePath(),
                            args = new[] { NormalizePathForJson(Path.Combine(FindMCPServerPath(), "index.js")) },
                            env = new { },
                            description = "Unity game development tools via MCP",
                            capabilities = new[] { "game_objects", "ui_creation", "scripting", "ai_agents", "animation" }
                        }
                    },
                    version = NexusVersion.Current,
                    created_by = "Synaptic Pro Unity Integration"
                };
                
                var configPath = Path.Combine(configDir, "mcp-config.json");
                File.WriteAllText(configPath, JsonConvert.SerializeObject(universalConfig, Newtonsoft.Json.Formatting.Indented));
                
                // Create usage example script
                var scriptPath = Path.Combine(configDir, "start-unity-mcp.sh");
                var scriptContent = $@"#!/bin/bash
# Unity MCP Server Starter Script
# Generated by Synaptic Pro Unity Integration

export MCP_CONFIG_PATH=""{NormalizePathForJson(configPath)}""
export UNITY_PROJECT_PATH=""{NormalizePathForJson(Application.dataPath.Replace($"{Path.DirectorySeparatorChar}Assets", ""))}""
export MCP_SERVER_PATH=""{NormalizePathForJson(FindMCPServerPath())}""

echo ""Starting Unity MCP Server...""
echo ""Project: $UNITY_PROJECT_PATH""
echo ""MCP Server: $MCP_SERVER_PATH""
echo ""Config: $MCP_CONFIG_PATH""

node ""$MCP_SERVER_PATH/index.js""
";
                
                File.WriteAllText(scriptPath, scriptContent);
                
                // Grant execute permission (Unix)
                if (Application.platform == RuntimePlatform.OSXEditor)
                {
                    System.Diagnostics.Process.Start("chmod", $"+x \"{scriptPath}\"");
                }
                
                SynLog.Info($"[Synaptic] Generic CLI config created: {configPath}");
            }
            catch (Exception e)
            {
                SynLog.Warn($"[Synaptic] Failed to create generic CLI config: {e.Message}");
            }
        }
        
        private void GenerateWatchConfig()
        {
            try
            {
                var projectPath = Application.dataPath.Replace("/Assets", "");
                var watchConfigPath = Path.Combine(projectPath, ".synaptic-watch");
                
                var watchConfig = new
                {
                    watch_patterns = new[]
                    {
                        "**/*.cs",
                        "**/*.js", 
                        "**/*.ts",
                        "**/*.json",
                        "**/*.md",
                        "**/*.yaml",
                        "**/*.yml"
                    },
                    ignore_patterns = new[]
                    {
                        "**/node_modules/**",
                        "**/Library/**",
                        "**/Temp/**",
                        "**/Logs/**",
                        "**/obj/**",
                        "**/bin/**"
                    },
                    commands = new
                    {
                        on_change = new[]
                        {
                            "echo \"File changed: {file}\"",
                            "# Add your custom commands here"
                        }
                    },
                    mcp_integration = new
                    {
                        enabled = true,
                        server_url = "ws://127.0.0.1:8090",
                        notify_on_change = true
                    }
                };
                
                File.WriteAllText(watchConfigPath, JsonConvert.SerializeObject(watchConfig, Newtonsoft.Json.Formatting.Indented));
                
                // Create watch script
                var watchScriptPath = Path.Combine(projectPath, "watch-synaptic.sh");
                var watchScript = $@"#!/bin/bash
# Synaptic File Watcher Script
# Monitors project files and notifies AI tools

if command -v fswatch >/dev/null 2>&1; then
    echo ""Starting Synaptic file watcher with fswatch...""
    fswatch -o . | while read f; do
        echo ""Files changed - notifying AI tools...""
        # Add notification logic here
    done
elif command -v inotifywait >/dev/null 2>&1; then
    echo ""Starting Synaptic file watcher with inotifywait...""
    inotifywait -m -r -e modify,create,delete . | while read path action file; do
        echo ""$path$file $action - notifying AI tools...""
        # Add notification logic here
    done
else
    echo ""Please install fswatch (macOS) or inotify-tools (Linux)""
    echo ""macOS: brew install fswatch""
    echo ""Linux: sudo apt-get install inotify-tools""
fi
";
                
                File.WriteAllText(watchScriptPath, watchScript);
                
                // Grant execute permission
                if (Application.platform == RuntimePlatform.OSXEditor)
                {
                    System.Diagnostics.Process.Start("chmod", $"+x \"{watchScriptPath}\"");
                }
                
                SynLog.Info($"[Synaptic] Watch config creation completed: {watchConfigPath}");

                EditorUtility.DisplayDialog(
                    "Watch Config Created",
                    $"File monitoring configuration created:\n\n" +
                    $"Config file: .synaptic-watch\n" +
                    $"Script: watch-synaptic.sh\n\n" +
                    "Monitored:\n" +
                    "• C#, JavaScript, TypeScript\n" +
                    "• JSON, Markdown, YAML\n\n" +
                    "Usage: ./watch-synaptic.sh",
                    "OK"
                );
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] Watch config error: {e.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to create Watch config:\n{e.Message}", "OK");
            }
        }
        
        // CLI detection and config path detection methods
        private string CheckCLIInstallation(string cliName)
        {
            try
            {
                // Try normal which command
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = cliName,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                
                using (var process = System.Diagnostics.Process.Start(startInfo))
                {
                    var output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();
                    
                    if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    {
                        return output;
                    }
                }
                
                // CLI-specific path detection
                return CheckCLISpecificPaths(cliName);
            }
            catch (Exception e)
            {
                SynLog.Warn($"[Synaptic] CLI check error ({cliName}): {e.Message}");
            }
            
            return null;
        }
        
        private string CheckCLISpecificPaths(string cliName)
        {
            switch (cliName)
            {
                case "claude-code":
                    return CheckClaudeCodePaths();
                    
                    
                case "gemini":
                    return CheckGeminiCLIPaths();
                    
                case "codex":
                    return CheckCodexCLIPaths();
                    
                default:
                    return null;
            }
        }
        
        private string CheckClaudeCodePaths()
        {
            var paths = new[]
            {
                // Homebrew (Apple Silicon)
                "/opt/homebrew/Caskroom/claude-code/*/claude",
                "/opt/homebrew/bin/claude-code",
                
                // Homebrew (Intel)
                "/usr/local/Caskroom/claude-code/*/claude",
                "/usr/local/bin/claude-code",
                
                // NPM global
                "/usr/local/lib/node_modules/@anthropic/claude-code/bin/claude-code",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.npm-global/bin/claude-code",
                
                // Standalone installers
                "/Applications/Claude Code.app/Contents/MacOS/Claude Code",
                "/Applications/Claude.app/Contents/MacOS/Claude",
                
                // Direct download installs
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/Applications/Claude Code.app/Contents/MacOS/Claude Code",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/Downloads/claude-code",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/bin/claude-code",
                
                // pnpm/yarn global
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.local/share/pnpm/claude-code",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.yarn/bin/claude-code"
            };
            
            return FindExecutableInPaths(paths, "claude");
        }
        
        
        private string CheckGeminiCLIPaths()
        {
            var paths = new[]
            {
                // Google Cloud SDK
                "/usr/local/bin/gcloud",
                "/opt/homebrew/bin/gcloud",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/google-cloud-sdk/bin/gcloud",
                
                // Gemini specific CLI
                "/usr/local/bin/gemini",
                "/opt/homebrew/bin/gemini",
                
                // NPM installs
                "/usr/local/lib/node_modules/@google/gemini-cli/bin/gemini",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.npm-global/bin/gemini",
                
                // pip installs
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.local/bin/gemini",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.local/bin/google-generativeai",
                
                // conda
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/anaconda3/bin/gemini",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/miniconda3/bin/gemini",
                
                // Direct installs
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/bin/gemini",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/Downloads/gemini",
                
                // Snap (if on Linux)
                "/snap/bin/gemini",
                
                // pnpm/yarn
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.local/share/pnpm/gemini",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.yarn/bin/gemini"
            };
            
            return FindExecutableInPaths(paths, "gemini");
        }
        
        private string CheckCodexCLIPaths()
        {
            var paths = new[]
            {
                // NPM global installs
                "/usr/local/bin/codex",
                "/opt/homebrew/bin/codex",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.npm-global/bin/codex",
                
                // OpenAI official CLI
                "/usr/local/lib/node_modules/@openai/codex/bin/codex",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.npm-global/lib/node_modules/@openai/codex/bin/codex",
                
                // pnpm/yarn global
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.local/share/pnpm/codex",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.yarn/bin/codex",
                
                // Direct installs
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/bin/codex",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/Downloads/codex",
                
                // Homebrew
                "/opt/homebrew/bin/openai-codex",
                "/usr/local/bin/openai-codex",
                
                // Rust cargo install
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.cargo/bin/codex",
                
                // Alternative names
                "/usr/local/bin/openai-codex-cli",
                "/opt/homebrew/bin/openai-codex-cli"
            };
            
            return FindExecutableInPaths(paths, "codex");
        }
        
        private string FindExecutableInPaths(string[] paths, string fallbackName)
        {
            foreach (var path in paths)
            {
                try
                {
                    if (path.Contains("*"))
                    {
                        // Path with wildcards
                        var baseDir = Path.GetDirectoryName(path);
                        var fileName = Path.GetFileName(path);
                        
                        if (Directory.Exists(baseDir))
                        {
                            var subdirs = Directory.GetDirectories(baseDir);
                            foreach (var subdir in subdirs)
                            {
                                var fullPath = Path.Combine(subdir, fileName);
                                if (File.Exists(fullPath))
                                {
                                    return fullPath;
                                }
                            }
                        }
                    }
                    else if (File.Exists(path))
                    {
                        return path;
                    }
                }
                catch (Exception e)
                {
                    SynLog.Warn($"[Synaptic] Path check error ({path}): {e.Message}");
                }
            }
            
            // Detect from running processes
            return FindInRunningProcesses(fallbackName);
        }
        
        private string FindInRunningProcesses(string processName)
        {
            try
            {
                var psStartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ps",
                    Arguments = "aux",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                
                using (var psProcess = System.Diagnostics.Process.Start(psStartInfo))
                {
                    var psOutput = psProcess.StandardOutput.ReadToEnd();
                    psProcess.WaitForExit();
                    
                    var lines = psOutput.Split('\n');
                    foreach (var line in lines)
                    {
                        // More strict process name matching
                        if (IsProcessLineMatch(line, processName))
                        {
                            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length > 10)
                            {
                                var executablePath = parts[10];
                                if (File.Exists(executablePath) && IsCorrectExecutable(executablePath, processName))
                                {
                                    return executablePath;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                SynLog.Warn($"[Synaptic] Process detection error ({processName}): {e.Message}");
            }
            
            return null;
        }
        
        private bool IsProcessLineMatch(string line, string processName)
        {
            switch (processName.ToLower())
            {
                case "claude":
                    // For Claude Code: claude-code or claude, but exclude gemini etc
                    return (line.Contains("claude-code") || line.Contains("/claude")) && 
                           !line.Contains("gemini") && !line.Contains("codex");
                           
                    
                case "gemini":
                    // For Gemini
                    return line.Contains("gemini") && !line.Contains("claude") && !line.Contains("codex");
                    
                case "codex":
                    // For Codex
                    return line.Contains("codex") && !line.Contains("claude") && !line.Contains("gemini");
                    
                default:
                    return line.Contains(processName);
            }
        }
        
        private bool IsCorrectExecutable(string executablePath, string processName)
        {
            var fileName = Path.GetFileNameWithoutExtension(executablePath).ToLower();
            var fullPath = executablePath.ToLower();
            
            switch (processName.ToLower())
            {
                case "claude":
                    // For Claude Code - distinguish from Claude Desktop
                    if (fullPath.Contains("claude-code") || fullPath.Contains("caskroom/claude-code"))
                    {
                        return true; // Clearly Claude Code path
                    }
                    
                    // Exclude Claude Desktop
                    if (fullPath.Contains("/applications/claude.app") || fullPath.Contains("claude.app/contents"))
                    {
                        return false; // Exclude because it's Claude Desktop
                    }
                    
                    return (fileName == "claude-code") && !fullPath.Contains("gemini") && !fullPath.Contains("codex");
                           
                           
                case "gemini":
                    return (fileName.Contains("gemini") || fileName == "gemini" || fileName == "gcloud") && 
                           (fullPath.Contains("gemini") || fullPath.Contains("google")) && 
                           !fullPath.Contains("claude") && !fullPath.Contains("codex");
                           
                case "codex":
                    return (fileName.Contains("codex") || fileName == "codex" || fileName == "openai-codex") && 
                           fullPath.Contains("codex") && 
                           !fullPath.Contains("claude") && !fullPath.Contains("chatgpt") && !fullPath.Contains("gemini");
                           
                default:
                    return fileName == processName.ToLower() || fileName.Contains(processName.ToLower());
            }
        }
        
        
        private string DetectGeminiCLIConfigPath()
        {
            try
            {
                var candidates = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "gemini", "config.json"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "config.json"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".google", "gemini", "config.json")
                };
                
                foreach (var path in candidates)
                {
                    var dir = Path.GetDirectoryName(path);
                    if (Directory.Exists(dir) || path.Contains("gemini"))
                    {
                        return path;
                    }
                }
            }
            catch (Exception e)
            {
                SynLog.Warn($"[Synaptic] Gemini CLI config path detection error: {e.Message}");
            }
            
            return null;
        }
        
        private string DetectCodexCLIConfigPath()
        {
            try
            {
                var candidates = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "openai", "codex.json"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "codex", "config.json"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.json"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".openai", "codex.json")
                };
                
                foreach (var path in candidates)
                {
                    var dir = Path.GetDirectoryName(path);
                    if (Directory.Exists(dir) || path.Contains("openai") || path.Contains("codex"))
                    {
                        return path;
                    }
                }
            }
            catch (Exception e)
            {
                SynLog.Warn($"[Synaptic] OpenAI Codex CLI config path detection error: {e.Message}");
            }
            
            return null;
        }
        
        private bool GenerateClaudeCodeSpecificConfig()
        {
            try
            {
                var projectPath = Application.dataPath.Replace("/Assets", "");
                var claudeDir = Path.Combine(projectPath, ".claude");
                var claudeConfigPath = Path.Combine(claudeDir, "settings.local.json");
                
                // Load existing settings
                dynamic existingConfig = null;
                if (File.Exists(claudeConfigPath))
                {
                    try
                    {
                        var existingJson = File.ReadAllText(claudeConfigPath);
                        existingConfig = JsonConvert.DeserializeObject(existingJson);
                    }
                    catch (Exception e)
                    {
                        SynLog.Warn($"[Synaptic] Failed to load existing Claude Code config: {e.Message}");
                    }
                }
                
                // Unity MCP server settings (2025 specification)
                // Normalize paths for cross-platform JSON compatibility (Windows: \ -> /)
                var unityMcpServer = new
                {
                    command = FindNodePath(),
                    args = new[] { NormalizePathForJson(GetServerScriptPath()) },
                    env = new Dictionary<string, object>()
                };

                // Create .claude directory
                if (!Directory.Exists(claudeDir))
                {
                    Directory.CreateDirectory(claudeDir);
                }
                
                // Project-specific config structure (2025 Claude Code format)
                dynamic claudeCodeConfig;
                if (existingConfig?.mcpServers != null)
                {
                    // Get existing mcpServers
                    var mcpServers = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(existingConfig.mcpServers));
                    mcpServers["unity-synaptic"] = unityMcpServer;
                    
                    // Update entire config
                    var configDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(existingConfig));
                    configDict["mcpServers"] = mcpServers;
                    claudeCodeConfig = configDict;
                }
                else
                {
                    // Create new - 2025 MCP specification compliant
                    claudeCodeConfig = new
                    {
                        mcpServers = new Dictionary<string, object>
                        {
                            ["unity-synaptic"] = unityMcpServer
                        },
                        permissions = new
                        {
                            allow = new[] { "mcp__unity-synaptic" },
                            deny = new object[] { }
                        }
                    };
                }
                
                File.WriteAllText(claudeConfigPath, JsonConvert.SerializeObject(claudeCodeConfig, Newtonsoft.Json.Formatting.Indented));
                
                SynLog.Info($"[Synaptic] Claude Code config file created: {claudeConfigPath}");
                
                // Also create alternate config path (user global settings)
                var userConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".claude.json"
                );
                
                CreateUserLevelConfig(userConfigPath, unityMcpServer);
                
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] Claude Code specific config error: {e.Message}");
                return false;
            }
        }
        
        private void CreateUserLevelConfig(string userConfigPath, object unityMcpServer)
        {
            try
            {
                dynamic userConfig = null;
                
                // Load existing user-level settings
                if (File.Exists(userConfigPath))
                {
                    try
                    {
                        var existingJson = File.ReadAllText(userConfigPath);
                        userConfig = JsonConvert.DeserializeObject(existingJson);
                    }
                    catch (Exception e)
                    {
                        SynLog.Warn($"[Synaptic] Failed to load existing user settings: {e.Message}");
                    }
                }
                
                // Update or create user-level settings
                if (userConfig?.mcpServers != null)
                {
                    var mcpServers = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(userConfig.mcpServers));
                    mcpServers["unity-synaptic"] = unityMcpServer;
                    
                    var configDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                        JsonConvert.SerializeObject(userConfig));
                    configDict["mcpServers"] = mcpServers;
                    userConfig = configDict;
                }
                else
                {
                    userConfig = new
                    {
                        mcpServers = new Dictionary<string, object>
                        {
                            ["unity-synaptic"] = unityMcpServer
                        }
                    };
                }
                
                File.WriteAllText(userConfigPath, JsonConvert.SerializeObject(userConfig, Newtonsoft.Json.Formatting.Indented));
                SynLog.Info($"[Synaptic] User global settings also created: {userConfigPath}");
            }
            catch (Exception e)
            {
                SynLog.Warn($"[Synaptic] User global settings creation skipped: {e.Message}");
            }
        }
        
        /// <summary>
        /// Normalize file path for JSON/Node.js consumption.
        /// Converts Windows backslashes to forward slashes.
        /// Node.js handles both, but forward slashes work cross-platform in JSON.
        /// </summary>
        private string NormalizePathForJson(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            // Convert backslashes to forward slashes for cross-platform JSON compatibility
            return path.Replace("\\", "/");
        }

        private static string _cachedMCPServerPath = null;

        private string FindMCPServerPath()
        {
            // キャッシュがあればそのまま返す（毎回の再帰検索を防ぐ）
            if (!string.IsNullOrEmpty(_cachedMCPServerPath) && Directory.Exists(_cachedMCPServerPath))
            {
                return _cachedMCPServerPath;
            }

            // 1. まず既知のパスを直接チェック（高速）
            var knownPath = Path.Combine(Application.dataPath, "Synaptic AI Pro", "MCPServer");
            if (Directory.Exists(knownPath))
            {
                SynLog.Info($"[Synaptic] Found MCPServer at known path: {knownPath}");
                _cachedMCPServerPath = knownPath;
                return knownPath;
            }

            // 2. Assets直下のみ検索（AllDirectoriesではなくTopDirectoryOnly + 1階層）
            try
            {
                foreach (var dir in Directory.GetDirectories(Application.dataPath, "*", SearchOption.TopDirectoryOnly))
                {
                    var mcpPath = Path.Combine(dir, "MCPServer");
                    if (Directory.Exists(mcpPath))
                    {
                        SynLog.Info($"[Synaptic] Found MCPServer in Assets: {mcpPath}");
                        _cachedMCPServerPath = mcpPath;
                        return mcpPath;
                    }
                }
            }
            catch { }

            // 3. Packages（パッケージマネージャー経由）
            string projectPath = Application.dataPath.Replace($"{Path.DirectorySeparatorChar}Assets", "");
            string packagesPath = Path.Combine(projectPath, "Packages");
            if (Directory.Exists(packagesPath))
            {
                try
                {
                    foreach (var dir in Directory.GetDirectories(packagesPath, "*", SearchOption.TopDirectoryOnly))
                    {
                        var mcpPath = Path.Combine(dir, "MCPServer");
                        if (Directory.Exists(mcpPath))
                        {
                            SynLog.Info($"[Synaptic] Found MCPServer in Packages: {mcpPath}");
                            _cachedMCPServerPath = mcpPath;
                            return mcpPath;
                        }
                    }
                }
                catch { }
            }

            // 4. Library/PackageCache（UPMキャッシュ）- synapticパッケージのみ検索
            string packageCachePath = Path.Combine(projectPath, "Library", "PackageCache");
            if (Directory.Exists(packageCachePath))
            {
                try
                {
                    foreach (var dir in Directory.GetDirectories(packageCachePath, "com.synaptic*", SearchOption.TopDirectoryOnly))
                    {
                        var mcpPath = Path.Combine(dir, "MCPServer");
                        if (Directory.Exists(mcpPath))
                        {
                            SynLog.Info($"[Synaptic] Found MCPServer in PackageCache: {mcpPath}");
                            _cachedMCPServerPath = mcpPath;
                            return mcpPath;
                        }
                    }
                }
                catch { }
            }

            // 5. フォールバック
            string defaultPath = Path.Combine(Application.dataPath, "Synaptic AI Pro", "MCPServer");
            SynLog.Warn($"[Synaptic] MCPServer not found, using default path: {defaultPath}");
            _cachedMCPServerPath = defaultPath;
            return defaultPath;
        }

        /// <summary>
        // ===== Auto-Update System =====

        private static string PREF_LAST_UPDATE_CHECK => "SynapticPro_LastUpdateCheck";

        private void CheckForUpdates()
        {
            if (updateCheckDone) return;

            // 1日1回チェック
            var lastCheck = EditorPrefs.GetString(PREF_LAST_UPDATE_CHECK, "");
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            if (lastCheck == today) return;

            updateCheckDone = true;
            EditorPrefs.SetString(PREF_LAST_UPDATE_CHECK, today);

            var currentVersion = NexusVersion.Current;
            var dist = ENABLE_SELF_UPDATE ? "booth" : "assetstore";

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    using (var client = new System.Net.WebClient())
                    {
                        client.Headers.Add("User-Agent", "SynapticAIPro-Unity");
                        var url = $"https://kawaii-agent-backend.vercel.app/api/synaptic/unity-version?v={currentVersion}&dist={dist}";
                        var json = client.DownloadString(url);

                        // Simple JSON parsing
                        if (json.Contains("\"updateAvailable\":true") || json.Contains("\"updateAvailable\": true"))
                        {
                            // Extract latestVersion
                            var vMatch = System.Text.RegularExpressions.Regex.Match(json, "\"latestVersion\"\\s*:\\s*\"([^\"]+)\"");
                            var urlMatch = System.Text.RegularExpressions.Regex.Match(json, "\"updateUrl\"\\s*:\\s*\"([^\"]+)\"");
                            var methodMatch = System.Text.RegularExpressions.Regex.Match(json, "\"updateMethod\"\\s*:\\s*\"([^\"]+)\"");

                            if (vMatch.Success)
                            {
                                latestVersion = vMatch.Groups[1].Value;
                                updateUrl = urlMatch.Success ? urlMatch.Groups[1].Value : "";
                                updateMethod = methodMatch.Success ? methodMatch.Groups[1].Value : "browser";
                                updateAvailable = true;

                                EditorApplication.delayCall += () =>
                                {
                                    SynLog.Info($"[Synaptic] Update available: {currentVersion} → {latestVersion}");
                                    Repaint();
                                };
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    // フェイルサイレント
                    SynLog.Info($"[Synaptic] Update check failed (non-critical): {e.Message}");
                }
            });
        }

        private void DrawUpdateBanner()
        {
            if (!updateAvailable) return;

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            var oldColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.2f, 0.6f, 1f);

            EditorGUILayout.LabelField(
                $"🔄 v{latestVersion} available (current: {NexusVersion.Current})",
                EditorStyles.boldLabel,
                GUILayout.ExpandWidth(true)
            );

            if (updateMethod == "auto" && ENABLE_SELF_UPDATE)
            {
                if (GUILayout.Button("Update Now", GUILayout.Width(100), GUILayout.Height(22)))
                {
                    StartAutoUpdate();
                }
            }
            else
            {
                if (GUILayout.Button("Open Store", GUILayout.Width(100), GUILayout.Height(22)))
                {
                    Application.OpenURL(updateUrl);
                }
            }

            GUI.backgroundColor = oldColor;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(5);
        }

        private async void StartAutoUpdate()
        {
            if (isDownloadingUpdate) return;
            isDownloadingUpdate = true;

            try
            {
                SynLog.Info($"[Synaptic] Downloading update v{latestVersion}...");

                // ダウンロード先
                var downloadPath = Path.Combine(Path.GetTempPath(), $"SynapticAIPro_v{latestVersion}.unitypackage.zip");

                await Task.Run(() =>
                {
                    using (var client = new System.Net.WebClient())
                    {
                        client.Headers.Add("User-Agent", "SynapticAIPro-Unity");
                        client.DownloadFile(updateUrl, downloadPath);
                    }
                });

                SynLog.Info($"[Synaptic] Downloaded to: {downloadPath}");

                // ZIPを展開してunitypackageを取得
                var extractDir = Path.Combine(Path.GetTempPath(), "SynapticUpdate");
                if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);

                // System.IO.Compression でZIP展開
                System.IO.Compression.ZipFile.ExtractToDirectory(downloadPath, extractDir);

                // .unitypackageファイルを探す
                var packages = Directory.GetFiles(extractDir, "*.unitypackage", SearchOption.AllDirectories);
                if (packages.Length > 0)
                {
                    var packagePath = packages[0];
                    SynLog.Info($"[Synaptic] Importing update package: {packagePath}");

                    var synapticPath = Path.Combine(Application.dataPath, "Synaptic AI Pro");

                    // ===== セーフガード =====
                    // 1. unitypackageファイルサイズ妥当性チェック
                    var pkgInfo = new FileInfo(packagePath);
                    if (pkgInfo.Length < 100_000) // < 100KB
                    {
                        EditorUtility.DisplayDialog("Update Failed",
                            "ダウンロードしたファイルが破損している可能性があります。手動で再ダウンロードしてください。",
                            "OK");
                        return;
                    }

                    // 2. パス妥当性チェック - 正確に "Synaptic AI Pro" フォルダのみ操作
                    var fullPath = Path.GetFullPath(synapticPath);
                    var assetsFullPath = Path.GetFullPath(Application.dataPath);

                    // パスが Assets/Synaptic AI Pro として整合してるか厳密確認
                    if (!fullPath.StartsWith(assetsFullPath, System.StringComparison.OrdinalIgnoreCase) ||
                        !fullPath.EndsWith("Synaptic AI Pro", System.StringComparison.OrdinalIgnoreCase) ||
                        fullPath.Equals(assetsFullPath, System.StringComparison.OrdinalIgnoreCase))
                    {
                        EditorUtility.DisplayDialog("Update Failed",
                            "アップデート対象フォルダの検証に失敗しました。\n手動で再インストールしてください。",
                            "OK");
                        return;
                    }

                    // 3. 既存フォルダが Synaptic AI Pro として整合してるか確認 (Editor配下に既知ファイルがある)
                    if (Directory.Exists(synapticPath))
                    {
                        var marker = Path.Combine(synapticPath, "Editor", "NexusSetupWindow.cs");
                        if (!File.Exists(marker))
                        {
                            EditorUtility.DisplayDialog("Update Failed",
                                "アップデート対象フォルダが Synaptic AI Pro のインストール先として認識できません。\n別のフォルダにインストールされている可能性があります。手動で再インストールしてください。",
                                "OK");
                            return;
                        }

                        Directory.Delete(synapticPath, true);
                        // .metaも削除
                        var metaPath = synapticPath + ".meta";
                        if (File.Exists(metaPath)) File.Delete(metaPath);
                    }

                    // インポート
                    AssetDatabase.ImportPackage(packagePath, false); // false = 確認ダイアログなし
                    AssetDatabase.Refresh();

                    updateAvailable = false;
                    SynLog.Info($"[Synaptic] Update to v{latestVersion} complete!");
                    EditorUtility.DisplayDialog("Update Complete",
                        $"Synaptic AI Pro has been updated to v{latestVersion}.\nPlease restart Unity for changes to take effect.",
                        "OK");
                }
                else
                {
                    Debug.LogError("[Synaptic] No .unitypackage found in downloaded ZIP");
                }

                // クリーンアップ
                try { File.Delete(downloadPath); } catch { }
                try { Directory.Delete(extractDir, true); } catch { }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] Auto-update failed: {e.Message}");
                EditorUtility.DisplayDialog("Update Failed",
                    $"Auto-update failed: {e.Message}\n\nPlease download manually from BOOTH.",
                    "OK");
            }
            finally
            {
                isDownloadingUpdate = false;
            }
        }

        /// <summary>
        /// Find Node.js executable path across different OS and installation locations
        /// Supports Windows D: drive installations, nvm, volta, and standard locations
        /// </summary>
        private string FindNodePath()
        {
            string nodeExecutable = Application.platform == RuntimePlatform.WindowsEditor ? "node.exe" : "node";

            // 1. Check PATH environment variable first
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                var separator = Application.platform == RuntimePlatform.WindowsEditor ? ';' : ':';
                foreach (var path in pathEnv.Split(separator))
                {
                    if (!string.IsNullOrEmpty(path))
                    {
                        var fullPath = Path.Combine(path.Trim(), nodeExecutable);
                        if (File.Exists(fullPath))
                        {
                            SynLog.Info($"[Synaptic] Found Node.js in PATH: {fullPath}");
                            return NormalizePathForJson(fullPath);
                        }
                    }
                }
            }

            // 2. Check common installation paths
            var searchPaths = new List<string>();

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                // Windows standard locations
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                searchPaths.AddRange(new[]
                {
                    // Standard Windows installations
                    Path.Combine(programFiles, "nodejs"),
                    Path.Combine(programFilesX86, "nodejs"),
                    Path.Combine(localAppData, "Programs", "nodejs"),

                    // nvm-windows
                    Path.Combine(appData, "nvm"),
                    Path.Combine(userProfile, ".nvm"),

                    // volta
                    Path.Combine(localAppData, "Volta", "bin"),
                    Path.Combine(userProfile, ".volta", "bin"),

                    // fnm
                    Path.Combine(localAppData, "fnm"),
                    Path.Combine(userProfile, ".fnm"),

                    // Common D: drive installations (for users who install on secondary drives)
                    "D:\\nodejs",
                    "D:\\Program Files\\nodejs",
                    "D:\\Program Files (x86)\\nodejs",
                    "D:\\nvm",
                    "D:\\nvm\\nodejs",

                    // E: drive (some users use this too)
                    "E:\\nodejs",
                    "E:\\Program Files\\nodejs",
                });
            }
            else
            {
                // macOS/Linux paths
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                searchPaths.AddRange(new[]
                {
                    "/opt/homebrew/bin",
                    "/usr/local/bin",
                    "/usr/bin",
                    Path.Combine(home, ".nvm", "versions", "node"),
                    Path.Combine(home, ".volta", "bin"),
                    Path.Combine(home, ".fnm"),
                    Path.Combine(home, ".npm-global", "bin"),
                });
            }

            foreach (var basePath in searchPaths)
            {
                if (Directory.Exists(basePath))
                {
                    // Direct check
                    var directPath = Path.Combine(basePath, nodeExecutable);
                    if (File.Exists(directPath))
                    {
                        SynLog.Info($"[Synaptic] Found Node.js: {directPath}");
                        return NormalizePathForJson(directPath);
                    }

                    // Check subdirectories (for version managers like nvm)
                    try
                    {
                        foreach (var subdir in Directory.GetDirectories(basePath))
                        {
                            var binPath = Path.Combine(subdir, nodeExecutable);
                            if (File.Exists(binPath))
                            {
                                SynLog.Info($"[Synaptic] Found Node.js in version manager: {binPath}");
                                return NormalizePathForJson(binPath);
                            }
                            // Also check bin subdirectory
                            binPath = Path.Combine(subdir, "bin", nodeExecutable);
                            if (File.Exists(binPath))
                            {
                                SynLog.Info($"[Synaptic] Found Node.js in version manager: {binPath}");
                                return NormalizePathForJson(binPath);
                            }
                        }
                    }
                    catch { }
                }
            }

            // 3. Try 'where' command on Windows, 'which' on Unix
            try
            {
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = Application.platform == RuntimePlatform.WindowsEditor ? "where" : "which",
                        Arguments = "node",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                var output = process.StandardOutput.ReadLine()?.Trim();
                process.WaitForExit();

                if (process.ExitCode == 0 && !string.IsNullOrEmpty(output) && File.Exists(output))
                {
                    SynLog.Info($"[Synaptic] Found Node.js via system command: {output}");
                    return NormalizePathForJson(output);
                }
            }
            catch { }

            // 4. Return "node" as fallback (rely on PATH)
            SynLog.Warn("[Synaptic] Node.js path not detected, using 'node' (requires PATH to be set correctly)");
            return "node";
        }

        private bool CheckAndInstallDependencies()
        {
            try
            {
                SynLog.Info("[Synaptic] Checking dependencies...");
                
                // Check if Newtonsoft.Json is installed
                var listRequest = Client.List();
                while (!listRequest.IsCompleted)
                {
                    System.Threading.Thread.Sleep(10);
                }
                
                if (listRequest.Status == StatusCode.Success)
                {
                    bool hasNewtonsoft = false;
                    foreach (var package in listRequest.Result)
                    {
                        if (package.name == "com.unity.nuget.newtonsoft-json")
                        {
                            hasNewtonsoft = true;
                            SynLog.Info($"[Synaptic] Newtonsoft.Json already installed: v{package.version}");
                            break;
                        }
                    }
                    
                    if (!hasNewtonsoft)
                    {
                        SynLog.Info("[Synaptic] Installing Newtonsoft.Json...");
                        
                        // Install Newtonsoft.Json
                        var addRequest = Client.Add("com.unity.nuget.newtonsoft-json");
                        
                        while (!addRequest.IsCompleted)
                        {
                            System.Threading.Thread.Sleep(10);
                        }
                        
                        if (addRequest.Status == StatusCode.Success)
                        {
                            SynLog.Info("[Synaptic] Newtonsoft.Json installed successfully");
                            
                            // Wait for compilation
                            EditorUtility.DisplayDialog(
                                "Dependencies Installed",
                                "Newtonsoft.Json has been installed. Unity will now recompile.\n\nPlease run setup again after compilation completes.",
                                "OK"
                            );
                            
                            return false; // Return false to stop setup and wait for recompilation
                        }
                        else if (addRequest.Status == StatusCode.Failure)
                        {
                            Debug.LogError($"[Synaptic] Failed to install Newtonsoft.Json: {addRequest.Error.message}");
                            return false;
                        }
                    }
                }
                
                // Check TextMeshPro
                bool hasTMP = false;
                if (listRequest.Status == StatusCode.Success)
                {
                    foreach (var package in listRequest.Result)
                    {
                        if (package.name == "com.unity.textmeshpro")
                        {
                            hasTMP = true;
                            SynLog.Info($"[Synaptic] TextMeshPro already installed: v{package.version}");
                            break;
                        }
                    }
                }
                
                if (!hasTMP)
                {
                    SynLog.Warn("[Synaptic] TextMeshPro not found. Some features may not work properly.");
                }
                
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Synaptic] Dependency check error: {e.Message}");
                return true; // Continue anyway
            }
        }
        
    }
}