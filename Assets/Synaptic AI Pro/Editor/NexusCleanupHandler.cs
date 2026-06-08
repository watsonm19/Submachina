using System;
using System.Diagnostics;
using UnityEngine;
using SynapticAIPro;
using UnityEditor;

namespace SynapticPro
{
    /// <summary>
    /// Cleanup MCP server on Unity exit or play mode change
    /// </summary>
    [InitializeOnLoad]
    public static class NexusCleanupHandler
    {
        static NexusCleanupHandler()
        {
            // [ProjectLazarus patch — REVERTED 2026-04-23]
            // We tried adding `afterAssemblyReload += Connect()` here to skip the up-to-10s
            // wait for NexusEditorMCPService.Update's poll. But it raced with
            // NexusSetupWindow.OnEnable's existing auto-start Connect call, and the resulting
            // double-Connect created an infinite WS connect→die→reconnect loop. Reverted.
            // Original symptom (WS reconnect lag after heavy ops) is acceptable; Tools/synaptic-server.cmd
            // is the manual escape hatch when needed.

            // Event on Unity editor exit
            EditorApplication.wantsToQuit += OnEditorQuitting;

            // Event on play mode change
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            // On domain reload (after script compilation, etc.)
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;

            // Cleanup existing MCP server processes on startup
            CleanupExistingProcesses();
        }
        
        private static bool OnEditorQuitting()
        {
            SynLog.Info("[Synaptic] Unity exit detected - Cleaning up MCP server");

            // Ensure MCP server is stopped
            var stopTask = NexusMCPSetupManager.Instance?.StopMCPServer();
            if (stopTask != null)
            {
                // Wait synchronously for async operation (max 5 seconds)
                if (stopTask.Wait(5000))
                {
                    SynLog.Info("[Synaptic] MCP server stopped successfully");
                }
                else
                {
                    SynLog.Warn("[Synaptic] MCP server stop timed out");
                }
            }

            CleanupMCPServer();
            return true; // Continue Unity exit
        }
        
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode)
            {
                SynLog.Info($"[Synaptic] Play mode change detected ({state}) - Checking MCP server");
                // Cleanup as needed on play mode change
                // Usually maintain server, but stop if there are issues
            }
        }

        private static void OnBeforeAssemblyReload()
        {
            SynLog.Info("[Synaptic] Before assembly reload - Saving MCP server state");
            // Handle state saving before assembly reload if needed
        }

        /// <summary>
        /// Cleanup existing MCP server processes on startup
        /// </summary>
        private static void CleanupExistingProcesses()
        {
            try
            {
                // In new architecture, cleanup on Unity startup is not needed
                // Claude Desktop manages MCP server
                SynLog.Info("[Synaptic] Unity is now a MCP client - skipping server cleanup");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"[Synaptic] Error in cleanup: {e.Message}");
            }
        }

        /// <summary>
        /// Check if port is in use
        /// </summary>
        private static bool CheckPortInUse(int port)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "/usr/sbin/lsof",
                        Arguments = $"-i :{port}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                
                return !string.IsNullOrEmpty(output);
            }
            catch
            {
                // If lsof fails, try another method
                return false;
            }
        }

        /// <summary>
        /// Cleanup MCP server
        /// </summary>
        private static void CleanupMCPServer()
        {
            try
            {
                KillNodeProcesses();
                SynLog.Info("[Synaptic] MCP server cleaned up");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"[Synaptic] MCP server cleanup error: {e.Message}");
            }
        }

        /// <summary>
        /// Terminate Node.js processes
        /// </summary>
        private static void KillNodeProcesses()
        {
            try
            {
                var killProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "/usr/bin/pkill",
                        Arguments = "-f \"node.*index.js\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                
                killProcess.Start();
                killProcess.WaitForExit();

                // Wait a bit for process to fully terminate
                System.Threading.Thread.Sleep(500);
            }
            catch (Exception e)
            {
                SynLog.Warn($"[Synaptic] Warning during process termination: {e.Message}");
            }
        }

        /// <summary>
        /// Search for available port
        /// </summary>
        private static int FindAvailablePort(int startPort, int endPort)
        {
            for (int port = startPort; port <= endPort; port++)
            {
                if (!CheckPortInUse(port))
                {
                    return port;
                }
            }
            return -1; // Not found
        }

        /// <summary>
        /// Update MCP client ports (HTTP + WebSocket)
        /// </summary>
        private static void UpdateMCPClientPorts(int httpPort, int wsPort)
        {
            try
            {
#if UNITY_EDITOR
                // Update WebSocket client port (Editor only)
                try
                {
                    var webSocketClientType = System.Type.GetType("NexusAIConnect.NexusWebSocketClient");
                    if (webSocketClientType != null)
                    {
                        var instanceProperty = webSocketClientType.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        var instance = instanceProperty?.GetValue(null);
                        if (instance != null)
                        {
                            var setServerUrlMethod = webSocketClientType.GetMethod("SetServerUrl");
                            setServerUrlMethod?.Invoke(instance, new object[] { $"ws://localhost:{wsPort}" });
                            SynLog.Info($"[Synaptic] WebSocket client port updated to {wsPort}");
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    SynLog.Warn($"[Synaptic] Failed to update WebSocket client: {ex.Message}");
                }
#endif

                // Update Editor MCP Service port
                try
                {
                    var editorMCPServiceType = System.Type.GetType("NexusAIConnect.NexusEditorMCPService");
                    if (editorMCPServiceType != null)
                    {
                        var setServerUrlMethod = editorMCPServiceType.GetMethod("SetServerUrl", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        setServerUrlMethod?.Invoke(null, new object[] { $"ws://localhost:{wsPort}" });
                        SynLog.Info($"[Synaptic] Editor MCP Service port updated to {wsPort}");
                    }
                }
                catch (System.Exception ex)
                {
                    SynLog.Warn($"[Synaptic] Failed to update Editor MCP Service: {ex.Message}");
                }

                // Update MCP client port (for Runtime)
                var mcpClient = NexusMCPClient.Instance;
                if (mcpClient != null)
                {
                    mcpClient.SetServerUrl($"ws://localhost:{wsPort}");
                    SynLog.Info($"[Synaptic] MCP client port updated to {wsPort}");
                }

                // Update Claude Desktop configuration
                if (wsPort != 8090 || httpPort != 3000)
                {
                    UpdateClaudeDesktopConfig(httpPort, wsPort);
                }

                // HTTP port settings (for future expansion)
                if (httpPort != 3000)
                {
                    SynLog.Info($"[Synaptic] HTTP port changed to {httpPort} (will apply on MCP server startup)");
                    // TODO: Update MCP server config file or environment variables
                    System.Environment.SetEnvironmentVariable("NEXUS_HTTP_PORT", httpPort.ToString());
                }
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($"[Synaptic] Error during port update: {e.Message}");
            }
        }

        /// <summary>
        /// Dynamically update Claude Desktop config file ports (HTTP + WebSocket)
        /// </summary>
        private static void UpdateClaudeDesktopConfig(int newHttpPort, int newWsPort)
        {
            try
            {
                // Detect platform-specific Claude Desktop config path
                string configPath;

                if (UnityEngine.Application.platform == UnityEngine.RuntimePlatform.OSXEditor)
                {
                    configPath = System.IO.Path.Combine(
                        System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
                        "Library", "Application Support", "Claude", "claude_desktop_config.json"
                    );
                }
                else if (UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsEditor)
                {
                    configPath = System.IO.Path.Combine(
                        System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                        "Claude", "claude_desktop_config.json"
                    );
                }
                else
                {
                    SynLog.Warn("[Synaptic] Unsupported platform for Claude Desktop config update");
                    return;
                }

                if (!System.IO.File.Exists(configPath))
                {
                    SynLog.Warn($"[Synaptic] Claude Desktop config file not found: {configPath}");
                    return;
                }

                // Load config file
                string configContent = System.IO.File.ReadAllText(configPath);

                bool updated = false;

                // Update WebSocket port (supports multiple patterns)
                string newWsPattern = $"ws://localhost:{newWsPort}";
                string[] oldWsPatterns = {
                    "ws://localhost:8090",
                    "ws://localhost:8081",
                    "ws://localhost:8082", 
                    "ws://localhost:8083",
                    "ws://localhost:8084"
                };
                
                foreach (string oldWsPattern in oldWsPatterns)
                {
                    if (configContent.Contains(oldWsPattern) && oldWsPattern != newWsPattern)
                    {
                        configContent = configContent.Replace(oldWsPattern, newWsPattern);
                        SynLog.Info($"[Synaptic] WebSocket port updated: {oldWsPattern} → {newWsPattern}");
                        updated = true;
                    }
                }

                // Update HTTP port
                string oldHttpPattern = "http://localhost:3000";
                string newHttpPattern = $"http://localhost:{newHttpPort}";

                if (configContent.Contains(oldHttpPattern))
                {
                    configContent = configContent.Replace(oldHttpPattern, newHttpPattern);
                    SynLog.Info($"[Synaptic] HTTP port updated: {oldHttpPattern} → {newHttpPattern}");
                    updated = true;
                }

                // Update port number only ("port": 3000 → "port": 3001)
                string oldPortPattern = "\"port\": 3000";
                string newPortPattern = $"\"port\": {newHttpPort}";

                if (configContent.Contains(oldPortPattern))
                {
                    configContent = configContent.Replace(oldPortPattern, newPortPattern);
                    SynLog.Info($"[Synaptic] Port setting updated: {oldPortPattern} → {newPortPattern}");
                    updated = true;
                }

                if (updated)
                {
                    // Create backup
                    string backupPath = configPath + $".backup_{System.DateTime.Now:yyyyMMdd_HHmmss}";
                    System.IO.File.Copy(configPath, backupPath);

                    // Write new configuration
                    System.IO.File.WriteAllText(configPath, configContent);

                    SynLog.Info($"[Synaptic] Claude Desktop configuration updated");
                    SynLog.Info($"[Synaptic] Backup created: {backupPath}");
                    SynLog.Info($"[Synaptic] 🔄 Please restart Claude Desktop");
                }
                else
                {
                    SynLog.Warn($"[Synaptic] No update targets found in config file");
                }
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($"[Synaptic] Claude Desktop config update error: {e.Message}");
            }
        }
    }
}
