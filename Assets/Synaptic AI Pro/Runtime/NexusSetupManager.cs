using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Debug = UnityEngine.Debug;

namespace SynapticPro
{
    /// <summary>
    /// One-touch MCP server setup manager
    /// Handles automatic installation of Git, Node.js, npm and various AI configurations
    /// </summary>
    public class NexusMCPSetupManager
    {
        private static NexusMCPSetupManager instance;
        private static int mcpServerProcessId = -1;
        public static NexusMCPSetupManager Instance
        {
            get
            {
                if (instance == null)
                    instance = new NexusMCPSetupManager();
                return instance;
            }
        }

        private string projectPath;
        private string mcpServerPath;
        private string toolsPath;
        
        public class SetupStatus
        {
            public bool isGitInstalled;
            public bool isNodeInstalled;
            public bool isNpmInstalled;
            public bool isMCPInstalled;
            public bool isConfigured;
            public string gitVersion;
            public string nodeVersion;
            public string npmVersion;
            public List<string> installedTools = new List<string>();
            public Dictionary<string, bool> aiConfigurations = new Dictionary<string, bool>();
        }

        private SetupStatus currentStatus = new SetupStatus();

        private NexusMCPSetupManager()
        {
            projectPath = Application.dataPath.Replace("/Assets", "");
            mcpServerPath = Path.Combine(projectPath, "MCPServer");
            toolsPath = Path.Combine(projectPath, "Tools");
        }

        /// <summary>
        /// Check setup status
        /// </summary>
        public async Task<SetupStatus> CheckSetupStatus()
        {
            currentStatus = new SetupStatus();

            // Check Git
            currentStatus.gitVersion = await CheckCommand("git", "--version");
            currentStatus.isGitInstalled = !string.IsNullOrEmpty(currentStatus.gitVersion);

            // Check Node.js
            currentStatus.nodeVersion = await CheckCommand("node", "--version");
            currentStatus.isNodeInstalled = !string.IsNullOrEmpty(currentStatus.nodeVersion);

            // Check npm
            currentStatus.npmVersion = await CheckCommand("npm", "--version");
            currentStatus.isNpmInstalled = !string.IsNullOrEmpty(currentStatus.npmVersion);

            // Check MCP server directory and version
            if (Directory.Exists(mcpServerPath) && File.Exists(Path.Combine(mcpServerPath, "package.json")))
            {
                try
                {
                    // Check package.json version
                    var packageJsonPath = Path.Combine(mcpServerPath, "package.json");
                    var packageJsonContent = File.ReadAllText(packageJsonPath);

                    // Simple version check (avoid JSON parsing dependency)
                    if (packageJsonContent.Contains("\"version\": \"1.2.0\"") ||
                        packageJsonContent.Contains("\"version\":\"1.2.0\""))
                    {
                        currentStatus.isMCPInstalled = true;
                        Debug.Log("[Synaptic] MCPServer v1.2.0 detected - up to date");
                    }
                    else
                    {
                        // Old version detected - force regeneration
                        currentStatus.isMCPInstalled = false;
                        Debug.LogWarning("[Synaptic] MCPServer outdated version detected - will regenerate");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Synaptic] Failed to check MCPServer version: {e.Message}");
                    currentStatus.isMCPInstalled = false;
                }
            }
            else
            {
                currentStatus.isMCPInstalled = false;
            }

            // Check AI configurations
            CheckAIConfigurations();

            return currentStatus;
        }

        /// <summary>
        /// Execute one-touch setup
        /// </summary>
        public async Task<bool> RunCompleteSetup(Action<string> progressCallback = null)
        {
            try
            {
                progressCallback?.Invoke("Starting setup...");

                // 1. Install necessary tools
                if (!currentStatus.isGitInstalled)
                {
                    progressCallback?.Invoke("Installing Git...");
                    await InstallGit();
                }

                if (!currentStatus.isNodeInstalled || !currentStatus.isNpmInstalled)
                {
                    progressCallback?.Invoke("Installing Node.js and npm...");
                    await InstallNodeJS();
                }

                // 2. Setup MCP server
                progressCallback?.Invoke("Building MCP Server...");
                await SetupMCPServer();

                // 3. Install dependencies
                progressCallback?.Invoke("Installing dependencies...");
                await InstallDependencies();

                // 4. Install Unity integration tools
                progressCallback?.Invoke("Setting up Unity integration tools...");
                await SetupUnityTools();

                // 5. AI configuration
                progressCallback?.Invoke("Configuring AI settings...");
                await ConfigureAIServices();

                // 6. Generate configuration files
                progressCallback?.Invoke("Generating configuration files...");
                await GenerateConfigFiles();

                progressCallback?.Invoke("Setup complete!");

                // Check final state
                await CheckSetupStatus();
                
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[MCP Setup] Setup error: {e.Message}");
                progressCallback?.Invoke($"Error: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Auto-install Git
        /// </summary>
        private Task InstallGit()
        {
            return Task.Run(async () =>
            {
                var platform = Application.platform;
                
                if (platform == RuntimePlatform.OSXEditor)
                {
                    // macOS - Install via Homebrew
                    var hasHomebrew = await CheckCommand("brew", "--version");
                if (string.IsNullOrEmpty(hasHomebrew))
                {
                    // Let user install Homebrew
#if UNITY_EDITOR
                    EditorUtility.DisplayDialog(
                        "Homebrew Required",
                        "Homebrew is not installed.\nPlease install from https://brew.sh",
                        "OK"
                    );
#endif
                    Application.OpenURL("https://brew.sh");
                    throw new Exception("Homebrew is not installed");
                }
                
                await RunCommand("brew", "install git");
            }
            else if (platform == RuntimePlatform.WindowsEditor)
            {
                // Windows - Download Git for Windows
                var gitInstallerPath = Path.Combine(toolsPath, "GitInstaller.exe");

                if (!Directory.Exists(toolsPath))
                    Directory.CreateDirectory(toolsPath);

                // Download Git for Windows
                // Note: Git download page is HTML, so use direct download URL
                using (var client = new System.Net.WebClient())
                {
                    // Use latest 2.51.0 version
                    var gitDownloadUrl = "https://github.com/git-for-windows/git/releases/download/v2.51.0.windows.1/Git-2.51.0-64-bit.exe";
                    await client.DownloadFileTaskAsync(gitDownloadUrl, gitInstallerPath);
                }

                // Silent install
                await RunCommand(gitInstallerPath, "/VERYSILENT /NORESTART");
            }

                // Update path
                RefreshEnvironmentPath();
            });
        }

        /// <summary>
        /// Auto-install Node.js
        /// </summary>
        private Task InstallNodeJS()
        {
            return Task.Run(async () =>
            {
                var platform = Application.platform;

            if (platform == RuntimePlatform.OSXEditor)
            {
                // macOS - via Homebrew
                await RunCommand("brew", "install node");
            }
            else if (platform == RuntimePlatform.WindowsEditor)
            {
                // Windows - Download Node.js installer
                var nodeInstallerPath = Path.Combine(toolsPath, "NodeInstaller.msi");

                if (!Directory.Exists(toolsPath))
                    Directory.CreateDirectory(toolsPath);

                // Download Node.js LTS v22.11.0
                using (var client = new System.Net.WebClient())
                {
                    await client.DownloadFileTaskAsync(
                        "https://nodejs.org/dist/v22.11.0/node-v22.11.0-x64.msi",
                        nodeInstallerPath
                    );
                }

                // Silent install
                await RunCommand("msiexec", $"/i \"{nodeInstallerPath}\" /qn");
            }
            
                RefreshEnvironmentPath();
            });
        }

        /// <summary>
        /// Setup MCP server
        /// </summary>
        private Task SetupMCPServer()
        {
            return Task.Run(() =>
            {
                // Create MCP server directory
            if (!Directory.Exists(mcpServerPath))
            {
                Directory.CreateDirectory(mcpServerPath);
            }

            // Generate package.json (ES module support)
            var packageJson = @"{
  ""name"": ""unity-mcp-server"",
  ""version"": ""1.2.0"",
  ""description"": ""MCP Server for Unity Integration"",
  ""main"": ""index.js"",
  ""type"": ""module"",
  ""scripts"": {
    ""start"": ""node index.js"",
    ""dev"": ""nodemon index.js""
  },
  ""dependencies"": {
    ""@modelcontextprotocol/sdk"": ""^1.18.1"",
    ""express"": ""^4.18.2"",
    ""ws"": ""^8.13.0"",
    ""cors"": ""^2.8.5"",
    ""dotenv"": ""^16.0.3"",
    ""zod"": ""^3.23.8""
  },
  ""devDependencies"": {
    ""nodemon"": ""^3.0.1""
  }
}";
            
            File.WriteAllText(Path.Combine(mcpServerPath, "package.json"), packageJson);

            // Generate MCP server main file (ES module support)
            var serverCode = @"import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import express from 'express';
import cors from 'cors';
import { WebSocketServer } from 'ws';
import dotenv from 'dotenv';

dotenv.config();

const app = express();
app.use(cors());
app.use(express.json());

// Initialize MCP server
const server = new Server(
    {
        name: 'unity-mcp-server',
        version: '1.2.0',
    },
    {
        capabilities: {
            tools: {},
        },
    }
);

// Unity operation tools
server.setRequestHandler('tools/list', async () => {
    return {
        tools: [
            {
                name: 'unity_create',
                description: 'Create Unity GameObjects and components',
                inputSchema: {
                    type: 'object',
                    properties: {
                        objectType: { type: 'string' },
                        name: { type: 'string' },
                        position: { 
                            type: 'object',
                            properties: {
                                x: { type: 'number' },
                                y: { type: 'number' },
                                z: { type: 'number' }
                            }
                        }
                    },
                },
            },
        ],
    };
});

server.setRequestHandler('tools/call', async (request) => {
    if (request.params.name === 'unity_create') {
        return await sendUnityCommand('create', request.params.arguments);
    }
    throw new Error(`Unknown tool: ${request.params.name}`);
});

// WebSocket server
const wss = new WebSocketServer({ port: 8090 });

wss.on('connection', (ws) => {
    console.log('Unity client connected');
    
    ws.on('message', (message) => {
        console.log('Received from Unity:', message.toString());
    });
});

async function sendUnityCommand(command, params) {
    // Send to Unity client
    const message = JSON.stringify({ command, params });
    wss.clients.forEach(client => {
        if (client.readyState === WebSocket.OPEN) {
            client.send(message);
        }
    });
    return { success: true };
}

// HTTP endpoints
app.post('/api/chat', async (req, res) => {
    try {
        const { message } = req.body;
        // Simple echo response
        res.json({ response: `Unity MCP Server received: ${message}` });
    } catch (error) {
        res.status(500).json({ error: error.message });
    }
});

const PORT = process.env.PORT || 3000;
app.listen(PORT, () => {
    console.log(`Unity MCP Server running on port ${PORT}`);
    console.log(`WebSocket server running on port 8090`);
});

// Start MCP server via Stdio
async function runMCPServer() {
    const transport = new StdioServerTransport();
    await server.connect(transport);
    console.log('MCP Server connected via stdio');
}

// Start both servers
runMCPServer().catch(console.error);
";


            File.WriteAllText(Path.Combine(mcpServerPath, "index.js"), serverCode);

            // Generate .env file
            var envContent = @"PORT=3000
CLAUDE_API_KEY=
GEMINI_API_KEY=
OPENAI_API_KEY=
";
            
                File.WriteAllText(Path.Combine(mcpServerPath, ".env"), envContent);
            });
        }

        /// <summary>
        /// Install dependencies
        /// </summary>
        private async Task InstallDependencies()
        {
            // Run npm install in MCP server directory
            await RunCommand("npm", "install", mcpServerPath);

            // Install MCP-related tools
            // Claude CLI doesn't officially exist as npm package, so commented out
            // await RunCommand("npm", "install -g claude-cli");
        }

        /// <summary>
        /// Setup Unity integration tools
        /// </summary>
        private async Task SetupUnityTools()
        {
            // Create Unity WebSocket client
            var clientCode = @"using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;

namespace SynapticPro.MCP
{
    public class MCPUnityClient : MonoBehaviour
    {
        private ClientWebSocket ws;
        private CancellationTokenSource cancellationToken;
        private string serverUrl = ""ws://localhost:8090"";
        private Queue<MCPCommand> commandQueue = new Queue<MCPCommand>();
        private bool isConnected = false;
        
        [Serializable]
        public class MCPCommand
        {
            public string command;
            public Dictionary<string, object> parameters;
        }
        
        void Start()
        {
            ConnectToMCPServer();
        }
        
        async void ConnectToMCPServer()
        {
            try
            {
                ws = new ClientWebSocket();
                cancellationToken = new CancellationTokenSource();
                
                await ws.ConnectAsync(new Uri(serverUrl), cancellationToken.Token);
                isConnected = true;
                Debug.Log(""[MCP Client] Connected to MCP Server"");
                
                // Start message receiving loop
                _ = ReceiveLoop();
            }
            catch (Exception e)
            {
                Debug.LogError($""[MCP Client] Connection error: {e.Message}"");
            }
        }
        
        async Task ReceiveLoop()
        {
            var buffer = new ArraySegment<byte>(new byte[4096]);
            
            while (isConnected && ws.State == WebSocketState.Open)
            {
                try
                {
                    var result = await ws.ReceiveAsync(buffer, cancellationToken.Token);
                    
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer.Array, 0, result.Count);
                        var command = JsonConvert.DeserializeObject<MCPCommand>(message);
                        commandQueue.Enqueue(command);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($""[MCP Client] Receive error: {e.Message}"");
                    isConnected = false;
                }
            }
        }
        
        void Update()
        {
            while (commandQueue.Count > 0)
            {
                var command = commandQueue.Dequeue();
                ExecuteCommand(command);
            }
        }
        
        void ExecuteCommand(MCPCommand command)
        {
            switch (command.command)
            {
                case ""create"":
                    CreateGameObject(command.parameters);
                    break;
                // Add other commands
            }
        }
        
        void CreateGameObject(Dictionary<string, object> parameters)
        {
            var name = parameters.ContainsKey(""name"") ? parameters[""name""].ToString() : ""GameObject"";
            var go = new GameObject(name);
            
            if (parameters.ContainsKey(""position""))
            {
                // Set position
            }
            
            Debug.Log($""[MCP Client] Created GameObject: {name}"");
        }
        
        async void OnDestroy()
        {
            if (ws != null && ws.State == WebSocketState.Open)
            {
                cancellationToken.Cancel();
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, ""Client closing"", CancellationToken.None);
                ws.Dispose();
            }
        }
    }
}";
            
            var clientPath = Path.Combine(Application.dataPath, "Synaptic", "Scripts", "MCP", "MCPUnityClient.cs");
            var mcpDir = Path.GetDirectoryName(clientPath);

            if (!Directory.Exists(mcpDir))
            {
                Directory.CreateDirectory(mcpDir);
            }

            File.WriteAllText(clientPath, clientCode);

            // Install required packages
            await InstallUnityPackage("com.unity.nuget.newtonsoft-json", "3.2.1");
        }

        /// <summary>
        /// Auto-configure AI services
        /// </summary>
        private async Task ConfigureAIServices()
        {
            var configPath = Path.Combine(mcpServerPath, "ai-config");
            if (!Directory.Exists(configPath))
            {
                Directory.CreateDirectory(configPath);
            }

            // Claude configuration
            var claudeConfig = @"{
  ""provider"": ""anthropic"",
  ""model"": ""claude-3-opus-20240229"",
  ""temperature"": 0.7,
  ""max_tokens"": 4096,
  ""tools"": [""unity_create"", ""unity_modify"", ""unity_delete""]
}";
            File.WriteAllText(Path.Combine(configPath, "claude.json"), claudeConfig);

            // Gemini configuration
            var geminiConfig = @"{
  ""provider"": ""google"",
  ""model"": ""gemini-pro"",
  ""temperature"": 0.8,
  ""tools"": [""unity_create"", ""unity_modify""]
}";
            File.WriteAllText(Path.Combine(configPath, "gemini.json"), geminiConfig);

            // Copilot configuration
            var copilotConfig = @"{
  ""provider"": ""github"",
  ""model"": ""gpt-4"",
  ""temperature"": 0.5,
  ""tools"": [""unity_create"", ""unity_modify"", ""code_generation""]
}";
            File.WriteAllText(Path.Combine(configPath, "copilot.json"), copilotConfig);
            
            currentStatus.aiConfigurations["Claude"] = true;
            currentStatus.aiConfigurations["Gemini"] = true;
            currentStatus.aiConfigurations["Copilot"] = true;
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Generate configuration files
        /// </summary>
        private async Task GenerateConfigFiles()
        {
            // MCP configuration file
            var mcpConfig = @"{
  ""servers"": {
    ""unity"": {
      ""command"": ""node"",
      ""args"": [""index.js""],
      ""cwd"": """ + mcpServerPath.Replace("\\", "/") + @""",
      ""env"": {
        ""NODE_ENV"": ""production""
      }
    }
  },
  ""tools"": {
    ""unity_create"": {
      ""description"": ""Create Unity GameObjects""
    },
    ""unity_modify"": {
      ""description"": ""Modify Unity GameObjects""
    },
    ""unity_delete"": {
      ""description"": ""Delete Unity GameObjects""
    }
  }
}";

            // Save MCP configuration file in project
            var mcpConfigPath = Path.Combine(mcpServerPath, "mcp-config.json");
            File.WriteAllText(mcpConfigPath, mcpConfig);

            // Generate README with documentation links for reference
            var readmeContent = @"# Unity MCP Server

## Documentation
- MCP Documentation: https://modelcontextprotocol.io/docs
- MCP Specification: https://modelcontextprotocol.io/specification/2025-06-18
- MCP SDK: https://www.npmjs.com/package/@modelcontextprotocol/sdk

## API Keys
- Claude API: https://console.anthropic.com/
- Gemini API: https://aistudio.google.com/app/apikey
- OpenAI API: https://platform.openai.com/api-keys

## Setup
1. Run `npm install` to install dependencies
2. Configure API keys in .env file
3. Run `npm start` to start the server
";
            File.WriteAllText(Path.Combine(mcpServerPath, "README.md"), readmeContent);

            // Startup script
            var startScript = @"#!/bin/bash
cd """ + mcpServerPath + @"""
npm start
";
            
            var startScriptPath = Path.Combine(mcpServerPath, "start.sh");
            File.WriteAllText(startScriptPath, startScript);
            
            if (Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.LinuxEditor)
            {
                await RunCommand("chmod", $"+x \"{startScriptPath}\"");
            }
        }

        /// <summary>
        /// Command execution helper
        /// </summary>
        private async Task<string> CheckCommand(string command, string args)
        {
            try
            {
                var resolvedCommand = ResolveCommandPath(command);
                if (string.IsNullOrEmpty(resolvedCommand)) return ""; // Not found, skip

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = resolvedCommand,
                        Arguments = args,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                
                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                process.WaitForExit(5000); // 5秒タイムアウト
                if (!process.HasExited) process.Kill();

                return output.Trim();
            }
            catch
            {
                return "";
            }
        }
        
        private string ResolveCommandPath(string command)
        {
            bool isWindows = Application.platform == RuntimePlatform.WindowsEditor;
            string executableName = isWindows ? (command.EndsWith(".exe") ? command : command + ".exe") : command;

            // First search with which/where command (most reliable)
            try
            {
                var whichResult = isWindows ? RunWhereCommand(command) : RunWhichCommand(command);
                if (!string.IsNullOrEmpty(whichResult) && File.Exists(whichResult))
                {
                    Debug.Log($"[MCP Setup] Detected {command}: {whichResult}");
                    return whichResult;
                }
            }
            catch { }

            // Search common command paths (OS-specific)
            var commonPaths = new System.Collections.Generic.List<string>();

            if (isWindows)
            {
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                commonPaths.AddRange(new[]
                {
                    // Node.js標準インストール
                    Path.Combine(programFiles, "nodejs"),
                    Path.Combine(programFilesX86, "nodejs"),
                    Path.Combine(localAppData, "Programs", "nodejs"),

                    // Git
                    Path.Combine(programFiles, "Git", "cmd"),
                    Path.Combine(programFiles, "Git", "bin"),
                    Path.Combine(localAppData, "Programs", "Git", "cmd"),

                    // nvm-windows
                    Path.Combine(appData, "nvm"),
                    Path.Combine(userProfile, ".nvm"),

                    // volta
                    Path.Combine(localAppData, "Volta", "bin"),
                    Path.Combine(userProfile, ".volta", "bin"),

                    // fnm
                    Path.Combine(localAppData, "fnm"),
                    Path.Combine(userProfile, ".fnm"),

                    // npm global
                    Path.Combine(appData, "npm"),

                    // D:ドライブ（セカンダリドライブにインストールするユーザー対策）
                    "D:\\nodejs",
                    "D:\\Program Files\\nodejs",
                    "D:\\Program Files (x86)\\nodejs",
                    "D:\\Program Files\\Git\\cmd",
                    "D:\\nvm",
                    "D:\\nvm\\nodejs",

                    // E:ドライブ
                    "E:\\nodejs",
                    "E:\\Program Files\\nodejs",

                    // その他のドライブ（F-I）
                    "F:\\nodejs", "F:\\Program Files\\nodejs",
                    "G:\\nodejs", "G:\\Program Files\\nodejs",
                    "H:\\nodejs", "H:\\Program Files\\nodejs",
                    "I:\\nodejs", "I:\\Program Files\\nodejs",
                });
            }
            else
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                commonPaths.AddRange(new[]
                {
                    "/opt/homebrew/bin",
                    "/usr/local/bin",
                    "/usr/bin",
                    "/bin",
                    "/usr/local/Cellar",
                    Path.Combine(home, ".nvm", "versions", "node"),
                    Path.Combine(home, ".volta", "bin"),
                    Path.Combine(home, ".fnm"),
                    Path.Combine(home, ".npm-global", "bin")
                });
            }
            
            foreach (var basePath in commonPaths)
            {
                if (Directory.Exists(basePath))
                {
                    // Direct check (with and without .exe)
                    var directPath = Path.Combine(basePath, executableName);
                    if (File.Exists(directPath))
                    {
                        Debug.Log($"[MCP Setup] Detected {command}: {directPath}");
                        return directPath;
                    }
                    // Try without .exe on Windows (some tools don't have .exe)
                    if (isWindows)
                    {
                        var noExtPath = Path.Combine(basePath, command);
                        if (File.Exists(noExtPath))
                        {
                            Debug.Log($"[MCP Setup] Detected {command}: {noExtPath}");
                            return noExtPath;
                        }
                    }
                    
                    // Search subdirectories (for version managers like NVM)
                    try
                    {
                        var subdirs = Directory.GetDirectories(basePath);
                        foreach (var subdir in subdirs)
                        {
                            var binPath = Path.Combine(subdir, "bin", command);
                            if (File.Exists(binPath))
                            {
                                Debug.Log($"[MCP Setup] Detected {command}: {binPath}");
                                return binPath;
                            }
                        }
                    }
                    catch { }
                }
            }
            
            // Search from PATH environment variable
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                var separator = Application.platform == RuntimePlatform.WindowsEditor ? ';' : ':';
                foreach (var path in pathEnv.Split(separator))
                {
                    if (!string.IsNullOrEmpty(path))
                    {
                        var fullPath = Path.Combine(path.Trim(), executableName);
                        if (File.Exists(fullPath))
                        {
                            Debug.Log($"[MCP Setup] Detected {command} in PATH: {fullPath}");
                            return fullPath;
                        }
                        // Try without .exe
                        if (isWindows)
                        {
                            var noExtPath = Path.Combine(path.Trim(), command);
                            if (File.Exists(noExtPath))
                            {
                                Debug.Log($"[MCP Setup] Detected {command} in PATH: {noExtPath}");
                                return noExtPath;
                            }
                        }
                    }
                }
            }
            
            Debug.LogWarning($"[MCP Setup] {command} not found in any known location");
            return null; // Not found - return null to prevent hanging Process.Start
        }
        
        private string RunWhichCommand(string command)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "/usr/bin/which",
                        Arguments = command,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(3000);
                if (!process.HasExited) process.Kill();

                return process.ExitCode == 0 ? output : "";
            }
            catch
            {
                return "";
            }
        }

        private string RunWhereCommand(string command)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "where.exe",
                        Arguments = command,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(3000);
                if (!process.HasExited) process.Kill();

                // whereは複数行返すことがある。最初の行を使う
                var firstLine = output.Split('\n')[0].Trim();
                return process.ExitCode == 0 && File.Exists(firstLine) ? firstLine : "";
            }
            catch
            {
                return "";
            }
        }
        
        private async Task<bool> RunCommand(string command, string args, string workingDir = null)
        {
            try
            {
                var resolvedCommand = ResolveCommandPath(command);
                Debug.Log($"[MCP Setup] Executing: {resolvedCommand} {args}");
                
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = resolvedCommand,
                        Arguments = args,
                        WorkingDirectory = workingDir ?? projectPath,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                
                // Set PATH environment variable appropriately
                SetupEnvironmentPath(process.StartInfo);
                
                process.Start();
                
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                
                process.WaitForExit();
                
                if (!string.IsNullOrEmpty(output))
                    Debug.Log($"[MCP Setup] {output}");
                    
                if (!string.IsNullOrEmpty(error))
                    Debug.LogWarning($"[MCP Setup] {error}");
                
                return process.ExitCode == 0;
            }
            catch (Exception e)
            {
                Debug.LogError($"[MCP Setup] Command error: {e.Message}");
                return false;
            }
        }
        
        private void RefreshEnvironmentPath()
        {
            // Reload environment variables
            var path = Environment.GetEnvironmentVariable("PATH");
            Environment.SetEnvironmentVariable("PATH", path, EnvironmentVariableTarget.Process);
        }
        
        /// <summary>
        /// Start MCP server in background
        /// </summary>
        private async Task StartMCPServerBackground(string npmPath)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = npmPath,
                        Arguments = "start",
                        WorkingDirectory = mcpServerPath,
                        UseShellExecute = false,
                        RedirectStandardOutput = false,
                        RedirectStandardError = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    }
                };
                
                // Set PATH environment variable appropriately
                SetupEnvironmentPath(process.StartInfo);
                
                Debug.Log($"[MCP Setup] Starting MCP server in background: {npmPath} start");
                process.Start();
                
                // Wait a bit for process to start
                await Task.Delay(1000);
                
                mcpServerProcessId = process.Id;
                Debug.Log($"[MCP Setup] MCP server process ID: {process.Id}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[MCP Setup] Background startup error: {e.Message}");
                
                // Fallback: start directly with node
                try
                {
                    var nodePath = ResolveCommandPath("node");
                    var nodeProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = nodePath,
                            Arguments = "index.js",
                            WorkingDirectory = mcpServerPath,
                            UseShellExecute = false,
                            RedirectStandardOutput = false,
                            RedirectStandardError = false,
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden
                        }
                    };
                    
                    SetupEnvironmentPath(nodeProcess.StartInfo);
                    Debug.Log($"[MCP Setup] Fallback: {nodePath} index.js");
                    nodeProcess.Start();
                    await Task.Delay(1000);
                }
                catch (Exception nodeEx)
                {
                    Debug.LogError($"[MCP Setup] Direct Node.js startup also failed: {nodeEx.Message}");
                }
            }
        }
        
        private void CheckAIConfigurations()
        {
            // Check existence of AI configuration files
            var configPath = Path.Combine(mcpServerPath, "ai-config");
            if (Directory.Exists(configPath))
            {
                currentStatus.aiConfigurations["Claude"] = File.Exists(Path.Combine(configPath, "claude.json"));
                currentStatus.aiConfigurations["Gemini"] = File.Exists(Path.Combine(configPath, "gemini.json"));
                currentStatus.aiConfigurations["Copilot"] = File.Exists(Path.Combine(configPath, "copilot.json"));
            }
        }
        
        private async Task InstallUnityPackage(string packageId, string version)
        {
            var manifestPath = Path.Combine(projectPath, "Packages", "manifest.json");
            if (File.Exists(manifestPath))
            {
                var manifest = File.ReadAllText(manifestPath);
                var packageLine = $"\"{packageId}\": \"{version}\"";
                
                if (!manifest.Contains(packageId))
                {
                    // Add to manifest.json
                    manifest = manifest.Replace("\"dependencies\": {", $"\"dependencies\": {{\n    {packageLine},");
                    File.WriteAllText(manifestPath, manifest);
                    
#if UNITY_EDITOR
                    AssetDatabase.Refresh();
#endif
                }
            }
            
            await Task.CompletedTask;
        }
        
        /// <summary>
        /// Start MCP server
        /// </summary>
        public async Task<bool> StartMCPServer()
        {
            try
            {
                Debug.Log("[MCP Setup] Checking for existing MCP server...");
                
                // Don't start MCP server, just connect to existing server
                bool serverExists = await CheckPortListening(8090);
                
                if (serverExists)
                {
                    Debug.Log("[MCP Setup] ✅ Found MCP server on port 8090 - Unity will connect as client");
                    mcpServerProcessId = -1; // External process so don't keep ID
                    return true;
                }
                else
                {
                    Debug.LogWarning("[MCP Setup] ❌ No MCP server found on port 8090");
                    Debug.LogWarning("[MCP Setup] Please start Claude Desktop or another AI application first");
                    
                    // Don't start MCP server on Unity side
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[MCP Setup] Failed to start server: {e.Message}");
                return false;
            }
        }
        
        private async Task<bool> CheckPortListening(int port)
        {
            try
            {
                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    var result = await CheckCommand("netstat", $"-an | findstr :{port}");
                    return !string.IsNullOrEmpty(result);
                }
                else
                {
                    var result = await CheckCommand("lsof", $"-i :{port}");
                    return !string.IsNullOrEmpty(result);
                }
            }
            catch
            {
                return false;
            }
        }
        
        private void SetupEnvironmentPath(ProcessStartInfo startInfo)
        {
            // Get current PATH
            var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            
            // Paths to add
            var additionalPaths = new[]
            {
                "/opt/homebrew/bin",
                "/usr/local/bin",
                "/usr/bin",
                "/bin"
            };
            
            var pathList = new List<string>();
            
            // Add additional paths first (to increase priority)
            foreach (var path in additionalPaths)
            {
                if (Directory.Exists(path) && !pathList.Contains(path))
                {
                    pathList.Add(path);
                }
            }
            
            // Add existing PATH
            if (!string.IsNullOrEmpty(currentPath))
            {
                var separator = Application.platform == RuntimePlatform.WindowsEditor ? ';' : ':';
                foreach (var path in currentPath.Split(separator))
                {
                    if (!string.IsNullOrEmpty(path.Trim()) && !pathList.Contains(path.Trim()))
                    {
                        pathList.Add(path.Trim());
                    }
                }
            }
            
            // Set new PATH
            var newPath = string.Join(Application.platform == RuntimePlatform.WindowsEditor ? ";" : ":", pathList);
            startInfo.EnvironmentVariables["PATH"] = newPath;
            
            Debug.Log($"[MCP Setup] Configured PATH: {newPath}");
        }
        
        /// <summary>
        /// Clean up existing MCP servers
        /// </summary>
        private async Task CleanupExistingServers()
        {
            try
            {
                // First check if port is in use
                bool needsCleanup = false;
                
                for (int port = 3000; port <= 3010; port++)
                {
                    if (await CheckPortListening(port))
                    {
                        needsCleanup = true;
                        Debug.Log($"[MCP Setup] Port {port} is in use");
                        break;
                    }
                }
                
                if (await CheckPortListening(8090))
                {
                    needsCleanup = true;
                    Debug.Log("[MCP Setup] Port 8090 is in use");
                }
                
                if (needsCleanup)
                {
                    Debug.Log("[MCP Setup] Cleaning up existing server processes");
                    await StopMCPServer();
                    
                    // Wait until port is released
                    await Task.Delay(2000);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MCP Setup] Warning during cleanup: {e.Message}");
            }
        }
        
        /// <summary>
        /// Stop MCP server
        /// </summary>
        public async Task<bool> StopMCPServer()
        {
            try
            {
                Debug.Log("[MCP Setup] Stopping MCP server...");
                
                // First try to kill with saved process ID
                if (mcpServerProcessId > 0)
                {
                    try
                    {
                        var killByIdProcess = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "/bin/kill",
                                Arguments = $"-9 {mcpServerProcessId}",
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };
                        killByIdProcess.Start();
                        killByIdProcess.WaitForExit();
                        Debug.Log($"[MCP Setup] Killed process {mcpServerProcessId}");
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[MCP Setup] Process ID kill error: {e.Message}");
                    }
                }
                
                // Kill node.js process with pkill just in case
                await Task.Run(() =>
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
                        
                        Debug.Log("[MCP Setup] pkill command execution completed");
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[MCP Setup] Warning during pkill execution: {e.Message}");
                    }
                });
                
                // Wait until process completely terminates
                await Task.Delay(1000);
                
                // Verify that port is released
                bool stillRunning = await CheckPortListening(3000);
                if (!stillRunning)
                {
                    Debug.Log("[MCP Setup] MCP server stopped successfully");
                    return true;
                }
                else
                {
                    Debug.LogWarning("[MCP Setup] MCP server may still be running");
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[MCP Setup] Server stop error: {e.Message}");
                return false;
            }
        }
    }
}