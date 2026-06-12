using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using SynapticAIPro;
using UnityEditor;
using Newtonsoft.Json;

namespace SynapticPro
{
    /// <summary>
    /// Port management system per project
    /// Assigns and manages unique ports for each Unity project
    /// </summary>
    [InitializeOnLoad]
    public static class NexusProjectPortManager
    {
        private static string projectId;
        private static int assignedPort = -1;
        private static readonly string MAPPING_FILE_PATH;

        // Project-port mapping information
        [Serializable]
        public class ProjectPortMapping
        {
            public Dictionary<string, ProjectInfo> projects = new Dictionary<string, ProjectInfo>();
        }
        
        [Serializable]
        public class ProjectInfo
        {
            public string projectName;
            public string projectPath;
            public int port;
            public DateTime lastUpdated;
            public bool isActive;
        }
        
        static NexusProjectPortManager()
        {
            // Mapping file path (saved in user home directory)
            string homeDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            MAPPING_FILE_PATH = Path.Combine(homeDir, ".config", "nexus", "project_port_mapping.json");

            // Initialize
            Initialize();
        }

        private static void Initialize()
        {
            // Generate project ID (hash value of project path)
            string projectPath = Application.dataPath;
            projectId = GetProjectId(projectPath);

            SynLog.Info($"[Nexus Port Manager] Project ID: {projectId}");
            SynLog.Info($"[Nexus Port Manager] Project Path: {projectPath}");

            // Assign port
            AssignPort();

            // Update status periodically
            EditorApplication.update += UpdateProjectStatus;
            EditorApplication.quitting += OnEditorQuitting;
        }

        /// <summary>
        /// Generate project ID
        /// </summary>
        private static string GetProjectId(string projectPath)
        {
            // Generate unique ID from project path
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(projectPath);
                byte[] hashBytes = md5.ComputeHash(inputBytes);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant().Substring(0, 8);
            }
        }

        /// <summary>
        /// Assign port
        /// </summary>
        private static void AssignPort()
        {
            var mapping = LoadMapping();

            // Check existing port assignment
            if (mapping.projects.ContainsKey(projectId))
            {
                var info = mapping.projects[projectId];
                assignedPort = info.port;
                info.lastUpdated = DateTime.Now;
                info.isActive = true;
                info.projectName = PlayerSettings.productName;
                SynLog.Info($"[Nexus Port Manager] Using existing port: {assignedPort}");
            }
            else
            {
                // Assign new port
                assignedPort = FindAvailablePort(mapping);
                mapping.projects[projectId] = new ProjectInfo
                {
                    projectName = PlayerSettings.productName,
                    projectPath = Application.dataPath,
                    port = assignedPort,
                    lastUpdated = DateTime.Now,
                    isActive = true
                };
                SynLog.Info($"[Nexus Port Manager] Assigned new port: {assignedPort}");
            }

            SaveMapping(mapping);

            // Notify port to NexusEditorMCPService
            UpdateMCPServicePort();
        }

        /// <summary>
        /// Find available port
        /// </summary>
        private static int FindAvailablePort(ProjectPortMapping mapping)
        {
            int[] candidatePorts = { 8090, 8091, 8092, 8093, 8094, 8095, 8096, 8097, 8098, 8099 };
            
            foreach (int port in candidatePorts)
            {
                bool isUsed = false;
                foreach (var project in mapping.projects.Values)
                {
                    if (project.port == port && project.isActive)
                    {
                        isUsed = true;
                        break;
                    }
                }
                
                if (!isUsed)
                {
                    return port;
                }
            }

            // If all in use, search sequentially from 8090
            return 8090 + mapping.projects.Count;
        }

        /// <summary>
        /// Set port for MCP service
        /// </summary>
        private static void UpdateMCPServicePort()
        {
            if (assignedPort > 0)
            {
                // Check actual port of current MCP server
                string currentUrl = NexusEditorMCPService.GetServerUrl();
                if (!string.IsNullOrEmpty(currentUrl) && currentUrl.Contains("localhost:"))
                {
                    // Get actually running port
                    int startIndex = currentUrl.IndexOf("localhost:") + "localhost:".Length;
                    int endIndex = currentUrl.IndexOf("/", startIndex);
                    if (endIndex == -1) endIndex = currentUrl.Length;

                    if (int.TryParse(currentUrl.Substring(startIndex, endIndex - startIndex), out int actualPort))
                    {
                        // If actual port differs from assigned port, prioritize actual port
                        if (actualPort != assignedPort && IsPortInUse(actualPort))
                        {
                            SynLog.Info($"[Nexus Port Manager] MCP Server is actually running on port {actualPort}, not {assignedPort}. Keeping actual port.");
                            assignedPort = actualPort;

                            // Update mapping
                            var mapping = LoadMapping();
                            if (mapping.projects.ContainsKey(projectId))
                            {
                                mapping.projects[projectId].port = actualPort;
                                SaveMapping(mapping);
                            }
                            return;
                        }
                    }
                }

                // Only use saved port if actual server not found
                string serverUrl = $"ws://localhost:{assignedPort}";
                NexusEditorMCPService.SetServerUrl(serverUrl);
                SynLog.Info($"[Nexus Port Manager] Updated MCP Service URL: {serverUrl}");
            }
        }

        /// <summary>
        /// Check if specified port is in use
        /// </summary>
        private static bool IsPortInUse(int port)
        {
            try
            {
                using (var tcpClient = new System.Net.Sockets.TcpClient())
                {
                    var result = tcpClient.BeginConnect("localhost", port, null, null);
                    bool success = result.AsyncWaitHandle.WaitOne(100);
                    if (success)
                    {
                        tcpClient.EndConnect(result);
                        tcpClient.Close();
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
        
        /// <summary>
        /// Update project status
        /// </summary>
        private static void UpdateProjectStatus()
        {
            // Update status every 5 minutes
            if (EditorApplication.timeSinceStartup % 300 < 1)
            {
                var mapping = LoadMapping();
                if (mapping.projects.ContainsKey(projectId))
                {
                    mapping.projects[projectId].lastUpdated = DateTime.Now;
                    mapping.projects[projectId].isActive = true;
                    SaveMapping(mapping);
                }
            }
        }

        /// <summary>
        /// Processing on editor exit
        /// </summary>
        private static void OnEditorQuitting()
        {
            var mapping = LoadMapping();
            if (mapping.projects.ContainsKey(projectId))
            {
                mapping.projects[projectId].isActive = false;
                SaveMapping(mapping);
            }
            
            EditorApplication.update -= UpdateProjectStatus;
        }
        
        /// <summary>
        /// Load mapping information
        /// </summary>
        private static ProjectPortMapping LoadMapping()
        {
            try
            {
                if (File.Exists(MAPPING_FILE_PATH))
                {
                    string json = File.ReadAllText(MAPPING_FILE_PATH);
                    
                    // Validate and clean JSON string
                    json = json.Trim();
                    if (string.IsNullOrEmpty(json))
                    {
                        SynLog.Warn("[Nexus Port Manager] Mapping file is empty, creating new mapping.");
                        return new ProjectPortMapping();
                    }
                    
                    // Check JSON syntax
                    if (!json.StartsWith("{") || !json.EndsWith("}"))
                    {
                        Debug.LogError($"[Nexus Port Manager] Invalid JSON format in mapping file. Content: {json.Substring(0, Math.Min(100, json.Length))}...");
                        return new ProjectPortMapping();
                    }
                    
                    var result = JsonConvert.DeserializeObject<ProjectPortMapping>(json);
                    return result ?? new ProjectPortMapping();
                }
            }
            catch (JsonException jsonEx)
            {
                Debug.LogError($"[Nexus Port Manager] JSON parsing error: {jsonEx.Message}");
                Debug.LogError($"[Nexus Port Manager] Recreating mapping file due to corruption.");

                // Move-aside corrupted file, then write a fresh empty mapping so
                // the next LoadMapping doesn't hit the same corrupt content and
                // spam the console at frame rate.
                // Bugs the previous implementation had:
                //   1. `File.Move` to an existing `.backup` throws on Windows,
                //      the silent catch left the corrupt file in place → loop.
                //   2. Even on success, no fresh file was written, so any later
                //      reader before a SaveMapping run kept hitting the error
                //      (relevant when MAPPING_FILE_PATH still existed under
                //      filesystem latency on slow disks).
                try
                {
                    string backupPath = MAPPING_FILE_PATH + ".backup";
                    if (File.Exists(backupPath))
                    {
                        try { File.Delete(backupPath); } catch { }
                    }
                    if (File.Exists(MAPPING_FILE_PATH))
                    {
                        try { File.Move(MAPPING_FILE_PATH, backupPath); }
                        catch
                        {
                            // Move failed (permissions, fs lock) — delete the
                            // corrupt source outright so we don't loop.
                            try { File.Delete(MAPPING_FILE_PATH); } catch { }
                        }
                    }

                    // Immediately materialize a clean mapping file so concurrent
                    // readers (and the next Update tick) see valid JSON.
                    var fresh = new ProjectPortMapping();
                    var freshJson = JsonConvert.SerializeObject(fresh, Formatting.Indented);
                    File.WriteAllText(MAPPING_FILE_PATH, freshJson);

                    SynLog.Info($"[Nexus Port Manager] Corrupted file backed up to: {backupPath}");
                    return fresh;
                }
                catch (Exception writeErr)
                {
                    Debug.LogError($"[Nexus Port Manager] Failed to reset mapping file: {writeErr.Message}");
                    return new ProjectPortMapping();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Nexus Port Manager] Failed to load mapping: {e.Message}");
            }
            
            return new ProjectPortMapping();
        }
        
        /// <summary>
        /// Save mapping information
        /// </summary>
        private static void SaveMapping(ProjectPortMapping mapping)
        {
            try
            {
                // Clean up old entries (not updated for more than 30 days)
                var keysToRemove = new List<string>();
                foreach (var kvp in mapping.projects)
                {
                    if ((DateTime.Now - kvp.Value.lastUpdated).TotalDays > 30)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }
                
                foreach (var key in keysToRemove)
                {
                    mapping.projects.Remove(key);
                }
                
                // Create directory
                string directory = Path.GetDirectoryName(MAPPING_FILE_PATH);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                // Save
                string json = JsonConvert.SerializeObject(mapping, Formatting.Indented);
                File.WriteAllText(MAPPING_FILE_PATH, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Nexus Port Manager] Failed to save mapping: {e.Message}");
            }
        }
        
        /// <summary>
        /// Get port for current project
        /// </summary>
        public static int GetAssignedPort()
        {
            return assignedPort;
        }
        
        /// <summary>
        /// Get current project ID
        /// </summary>
        public static string GetProjectId()
        {
            return projectId;
        }
        
        /// <summary>
        /// Display mapping information (for debugging)
        /// </summary>
        // v1.2.24: Diagnostics タブに統合、メニューからは除外
        // [MenuItem("Tools/Synaptic Pro/Show Port Mapping")]
        public static void ShowPortMapping()
        {
            var mapping = LoadMapping();
            
            System.Text.StringBuilder info = new System.Text.StringBuilder();
            info.AppendLine("🔌 Nexus Project Port Mapping");
            info.AppendLine("================================");
            info.AppendLine($"Current Project ID: {projectId}");
            info.AppendLine($"Assigned Port: {assignedPort}");
            info.AppendLine("\nAll Projects:");
            
            foreach (var kvp in mapping.projects)
            {
                var project = kvp.Value;
                info.AppendLine($"\n📁 {project.projectName}");
                info.AppendLine($"   ID: {kvp.Key}");
                info.AppendLine($"   Port: {project.port}");
                info.AppendLine($"   Path: {project.projectPath}");
                info.AppendLine($"   Active: {project.isActive}");
                info.AppendLine($"   Last Updated: {project.lastUpdated}");
            }
            
            SynLog.Info(info.ToString());
            EditorUtility.DisplayDialog("Port Mapping", info.ToString(), "OK");
        }
    }
}
