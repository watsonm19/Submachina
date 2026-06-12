/**
 * Synaptic AI Pro - Token SuperSave Mode
 *
 * Experimental: Only 6 meta-tools for 99% context reduction.
 * Compatible with all MCP clients without dynamic tool loading support.
 *
 * Tools:
 * 1. list_categories() - Show available tool categories
 * 2. list_tools(category) - Show tools in a category with their schemas
 * 3. execute(tool_name, params) - Run any tool by name
 * 4. inspect(target, ...) - Dynamically inspect objects/components
 * 5. modify(gameObject, component, properties) - Dynamically modify properties
 * 6. create(type, ...) - Create objects, prefabs, components
 */

const express = require('express');
const http = require('http');
const WebSocket = require('ws');
const cors = require('cors');
const { z } = require('zod');
const { createServer } = require('./mcp-server');
const fs = require('fs');
const path = require('path');
const os = require('os');

const app = express();
app.use(cors());
app.use(express.json());

const server = http.createServer(app);
let wss = null;

let unityWebSocket = null;
let mcpServer = null;

// ESC-0121: in share mode, this process did not bind the Unity-WebSocket port.
// All Unity-bound commands must be forwarded over HTTP to the leader process
// instead of trying to write to our local (zombie) WebSocket.
let shareModeLeaderPort = null;

// =====================================================
// Load Tool Registry from JSON file
// =====================================================
let TOOL_REGISTRY_RAW = {};
let CATEGORIES = {};
let ALL_TOOLS = {};

function loadToolRegistry() {
    try {
        const registryPath = path.join(__dirname, 'tool-registry.json');
        const data = fs.readFileSync(registryPath, 'utf8');
        TOOL_REGISTRY_RAW = JSON.parse(data);

        // Build categories from registry
        CATEGORIES = {};
        ALL_TOOLS = {};

        for (const [toolName, toolData] of Object.entries(TOOL_REGISTRY_RAW)) {
            const category = (toolData.category || 'Other').toLowerCase();

            if (!CATEGORIES[category]) {
                CATEGORIES[category] = {
                    description: `${toolData.category} tools`,
                    tools: {}
                };
            }

            // Store tool info (use clean name without unity_ prefix as primary key)
            const cleanName = toolName.replace(/^unity_/, '');
            CATEGORIES[category].tools[cleanName] = {
                fullName: toolName,
                title: toolData.title,
                description: toolData.description
            };

            // Only store clean name to avoid duplication
            ALL_TOOLS[cleanName] = {
                fullName: toolName,
                category: category,
                title: toolData.title,
                description: toolData.description
            };
        }

        console.error(`[SuperSave] Loaded ${Object.keys(TOOL_REGISTRY_RAW).length} tools from tool-registry.json`);
    } catch (err) {
        console.error('[SuperSave] Failed to load tool-registry.json:', err.message);
    }
}

// Load on startup
loadToolRegistry();

// =====================================================
// WebSocket Setup (same as index.js)
// =====================================================
function setupWebSocketHandlers() {
    if (!wss) return;

    // Surface server-level errors (ECONNRESET etc.) that would otherwise
    // disappear into the void.
    wss.on('error', (err) => {
        console.error('[SuperSave] WSS error:', err && err.message ? err.message : err);
    });

    wss.on('connection', (ws, req) => {
        const isUnity = req.headers['x-client-type'] === 'unity' || req.url === '/unity';

        // Per-socket error handler — without this, send() failures (peer gone,
        // backpressure, etc.) crash silently and the caller just hits the 60s
        // timeout with no clue why.
        ws.on('error', (err) => {
            console.error('[SuperSave] WS socket error:', err && err.message ? err.message : err);
        });

        if (isUnity || !req.url.includes('mcp')) {
            if (unityWebSocket) {
                unityWebSocket.close();
            }
            unityWebSocket = ws;

            ws.on('message', async (message) => {
                try {
                    const data = JSON.parse(message);

                    // Handle shutdown request from new process trying to take over
                    if (data.type === 'shutdown') {
                        console.error('[SuperSave] Received shutdown request from new process, shutting down...');
                        shutdownServer();
                        return;
                    }

                    const responseId = data.id || data.operationId;
                    if ((data.type === 'operation_result' || data.type === 'operation_response') && responseId) {
                        const numericId = typeof responseId === 'string' ? parseInt(responseId) : responseId;
                        if (pendingRequests.has(numericId)) {
                            const { resolve, reject, timeout } = pendingRequests.get(numericId);
                            clearTimeout(timeout);
                            pendingRequests.delete(numericId);
                            if (data.success !== false) {
                                resolve(data.content || data.result);
                            } else {
                                reject(new Error(data.content || data.error || 'Unity command failed'));
                            }
                        }
                    }
                } catch (e) {}
            });

            ws.on('close', () => {
                unityWebSocket = null;
            });
        }
    });
}

// Unity command helper
const pendingRequests = new Map();
let requestId = 0;
const sleep = (ms) => new Promise(resolve => setTimeout(resolve, ms));

async function sendUnityCommandOnce(command, params, id) {
    return new Promise((resolve, reject) => {
        const timeout = setTimeout(() => {
            pendingRequests.delete(id);
            reject(new Error('timeout'));
        }, 60000);

        pendingRequests.set(id, { resolve, reject, timeout });

        const message = JSON.stringify({
            type: 'unity_operation',
            command: command,
            parameters: {
                ...params,
                operationId: id.toString()
            }
        });

        // Confirm the socket is open before pushing — without this we have
        // raced an in-flight close() and the message disappears.
        if (!unityWebSocket || unityWebSocket.readyState !== 1 /* OPEN */) {
            clearTimeout(timeout);
            pendingRequests.delete(id);
            const state = unityWebSocket ? unityWebSocket.readyState : 'null';
            return reject(new Error(`unityWebSocket not open (readyState=${state})`));
        }

        // send() with callback so write failures surface immediately instead
        // of bleeding into the 60s timeout. ESC-0102 root-cause hunt.
        unityWebSocket.send(message, (err) => {
            if (err) {
                clearTimeout(timeout);
                pendingRequests.delete(id);
                console.error('[SuperSave] send failed:', err.message);
                reject(err);
            }
        });
    });
}

// ESC-0121: in share mode, this process did not bind the WebSocket port — the
// real Unity socket lives in the leader process. Forward the command to the
// leader via HTTP /execute instead of writing to our local zombie wss.
async function forwardToLeader(command, params) {
    const port = shareModeLeaderPort;
    if (!port) throw new Error('share mode leader port not set');
    const body = JSON.stringify({ tool: command, params });
    const res = await new Promise((resolve, reject) => {
        const req = require('http').request({
            hostname: 'localhost',
            port,
            path: '/execute',
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(body) }
        }, (resp) => {
            let data = '';
            resp.on('data', chunk => data += chunk);
            resp.on('end', () => resolve({ status: resp.statusCode, body: data }));
        });
        req.on('error', reject);
        req.setTimeout(120000, () => { req.destroy(new Error('forward timeout')); });
        req.write(body);
        req.end();
    });
    if (res.status >= 400) {
        throw new Error(`leader /execute returned ${res.status}: ${res.body.slice(0, 200)}`);
    }
    try {
        return JSON.parse(res.body);
    } catch {
        return res.body;
    }
}

async function sendUnityCommand(command, params = {}) {
    // Share mode: bypass local WebSocket entirely.
    if (shareModeLeaderPort) {
        return forwardToLeader(command, params);
    }

    const MAX_RETRIES = 30;
    const RETRY_DELAY = 10000;

    for (let attempt = 1; attempt <= MAX_RETRIES; attempt++) {
        if (!unityWebSocket || unityWebSocket.readyState !== WebSocket.OPEN) {
            if (attempt < MAX_RETRIES) {
                console.error(`[SuperSave] Unity not connected (attempt ${attempt}/${MAX_RETRIES}). Waiting...`);
                await sleep(RETRY_DELAY);
                continue;
            }
            throw new Error('Unity not connected');
        }

        const id = ++requestId;
        try {
            const result = await sendUnityCommandOnce(command, params, id);
            return result;
        } catch (error) {
            if (attempt < MAX_RETRIES) {
                console.error(`[SuperSave] Command failed (attempt ${attempt}): ${error.message}`);
                await sleep(RETRY_DELAY);
            }
        }
    }
    throw new Error('Unity not connected after retries');
}

// =====================================================
// MCP Server with 3 Meta-Tools
// =====================================================
async function setupMCPServer() {
    mcpServer = createServer();

    // ===== META-TOOL 1: list_categories =====
    mcpServer.registerTool('list_categories', {
        title: 'List Tool Categories',
        description: 'List all available tool categories. Use this first to discover what tools are available.',
        inputSchema: z.object({})
    }, async () => {
        const categories = Object.entries(CATEGORIES).map(([name, data]) => ({
            name,
            description: data.description,
            toolCount: Object.keys(data.tools).length
        }));

        const totalTools = Object.keys(TOOL_REGISTRY_RAW).length;

        return {
            content: [{
                type: 'text',
                text: `Available Categories (${categories.length} categories, ${totalTools} total tools):\n\n` +
                    categories.map(c => `• ${c.name} (${c.toolCount} tools)\n  ${c.description}`).join('\n\n') +
                    '\n\nUse list_tools(category) to see tools in a specific category.'
            }]
        };
    });

    // ===== META-TOOL 2: list_tools =====
    mcpServer.registerTool('list_tools', {
        title: 'List Tools in Category',
        description: 'List all tools in a specific category with their parameters. Use this to learn how to use specific tools.',
        inputSchema: z.object({
            category: z.string().describe('Category name (e.g., "gameobject", "material", "lighting")')
        })
    }, async (params) => {
        const category = params.category.toLowerCase();

        if (!CATEGORIES[category]) {
            const availableCategories = Object.keys(CATEGORIES).join(', ');
            return {
                content: [{
                    type: 'text',
                    text: `Unknown category: "${category}"\n\nAvailable categories: ${availableCategories}`
                }]
            };
        }

        const categoryData = CATEGORIES[category];
        const tools = Object.entries(categoryData.tools).map(([name, data]) => {
            return `• ${name} (${data.fullName})\n  ${data.title}\n  ${data.description}`;
        });

        return {
            content: [{
                type: 'text',
                text: `Category: ${category}\n${categoryData.description}\n\nTools (${tools.length}):\n\n${tools.join('\n\n')}\n\nUse execute(tool_name, params) to run a tool.`
            }]
        };
    });

    // ===== META-TOOL 2.5: search_tools =====
    mcpServer.registerTool('search_tools', {
        title: 'Search Tools',
        description: 'Search for tools by keyword in name, title, or description. Use this when you don\'t know the exact category.',
        inputSchema: z.object({
            query: z.string().describe('Search keyword (e.g., "material", "camera", "physics")'),
            category: z.string().optional().describe('Optional: filter by category'),
            limit: z.number().optional().default(20).describe('Max results (default: 20)')
        })
    }, async (params) => {
        const query = (params.query || '').toLowerCase();
        const categoryFilter = params.category?.toLowerCase();
        const limit = params.limit || 20;

        const results = [];

        for (const [toolName, toolData] of Object.entries(TOOL_REGISTRY_RAW)) {
            const title = toolData.title || toolName;
            const description = toolData.description || '';
            const category = (toolData.category || 'Other').toLowerCase();

            // Category filter
            if (categoryFilter && category !== categoryFilter) continue;

            // If no query, return all (with limit)
            if (!query) {
                results.push({ name: toolName, title, description, category: toolData.category, score: 0 });
                if (results.length >= limit) break;
                continue;
            }

            // Calculate score
            let score = 0;
            if (toolName.toLowerCase().includes(query)) score += 100;
            if (title.toLowerCase().includes(query)) score += 80;
            if (description.toLowerCase().includes(query)) score += 40;

            if (score > 0) {
                results.push({ name: toolName, title, description, category: toolData.category, score });
            }
        }

        // Sort by score and limit
        const sorted = results
            .sort((a, b) => b.score - a.score)
            .slice(0, limit);

        if (sorted.length === 0) {
            return {
                content: [{
                    type: 'text',
                    text: `No tools found for query: "${params.query}"\n\nTry different keywords or use list_categories to see available categories.`
                }]
            };
        }

        const toolList = sorted.map(t => `• ${t.name}\n  ${t.title} [${t.category}]\n  ${t.description.slice(0, 100)}${t.description.length > 100 ? '...' : ''}`);

        return {
            content: [{
                type: 'text',
                text: `Found ${sorted.length} tools for "${params.query}":\n\n${toolList.join('\n\n')}\n\nUse execute(tool_name, params) to run a tool.`
            }]
        };
    });

    // ===== META-TOOL 2.6: get_tools_reference =====
    mcpServer.registerTool('get_tools_reference', {
        title: 'Get Full Tools Reference',
        description: 'Get complete reference of ALL tools in Markdown format. Use this once at the start of a session to have full tool knowledge without repeated search_tools calls. Saves tokens by eliminating multiple tool discovery calls.',
        inputSchema: z.object({
            lang: z.enum(['en', 'jp']).optional().default('en').describe('Language for descriptions (en/jp)'),
            category: z.string().optional().describe('Optional: filter by specific category'),
            format: z.enum(['markdown', 'compact']).optional().default('markdown').describe('Output format: markdown (detailed) or compact (name + description only)')
        })
    }, async (params) => {
        const lang = params.lang || 'en';
        const categoryFilter = params.category?.toLowerCase();
        const format = params.format || 'markdown';

        // Group tools by category
        const byCategory = {};
        for (const [toolName, toolData] of Object.entries(TOOL_REGISTRY_RAW)) {
            const category = (toolData.category || 'Other');
            const categoryLower = category.toLowerCase();

            if (categoryFilter && categoryLower !== categoryFilter) continue;

            if (!byCategory[category]) {
                byCategory[category] = [];
            }
            byCategory[category].push({ name: toolName, ...toolData });
        }

        const totalTools = Object.values(byCategory).reduce((sum, tools) => sum + tools.length, 0);
        let output = '';

        if (format === 'markdown') {
            output = `# Synaptic AI Pro - Tools Reference\n`;
            output += `Total: ${totalTools} tools in ${Object.keys(byCategory).length} categories\n\n`;

            for (const [category, tools] of Object.entries(byCategory).sort((a, b) => a[0].localeCompare(b[0]))) {
                output += `## ${category} (${tools.length} tools)\n\n`;

                for (const tool of tools.sort((a, b) => a.name.localeCompare(b.name))) {
                    output += `### ${tool.name}\n`;
                    output += `${tool.description || tool.title || ''}\n`;

                    // Add inputSchema info if available
                    if (tool.inputSchema?.properties) {
                        const props = Object.entries(tool.inputSchema.properties);
                        if (props.length > 0) {
                            output += `**Parameters:**\n`;
                            for (const [propName, propData] of props) {
                                const required = tool.inputSchema.required?.includes(propName) ? ' (required)' : '';
                                const type = propData.type || 'any';
                                const desc = propData.description || '';
                                output += `- \`${propName}\`: ${type}${required} - ${desc}\n`;
                            }
                        }
                    }
                    output += '\n';
                }
            }
        } else {
            // Compact format
            output = `# Tools Reference (${totalTools} tools)\n\n`;

            for (const [category, tools] of Object.entries(byCategory).sort((a, b) => a[0].localeCompare(b[0]))) {
                output += `## ${category}\n`;
                for (const tool of tools.sort((a, b) => a.name.localeCompare(b.name))) {
                    output += `- ${tool.name}: ${(tool.description || tool.title || '').slice(0, 80)}\n`;
                }
                output += '\n';
            }
        }

        output += `\n---\nUse execute(tool_name, params) to run any tool.`;

        return {
            content: [{
                type: 'text',
                text: output
            }]
        };
    });

    // ===== MCP RESOURCES: Tools Reference =====
    // Generate tools reference markdown from tool-registry.json
    function generateToolsReference(format = 'compact') {
        const byCategory = {};
        for (const [toolName, toolData] of Object.entries(TOOL_REGISTRY_RAW)) {
            const category = (toolData.category || 'Other');
            if (!byCategory[category]) {
                byCategory[category] = [];
            }
            byCategory[category].push({ name: toolName, ...toolData });
        }

        const totalTools = Object.values(byCategory).reduce((sum, tools) => sum + tools.length, 0);
        let output = '';

        if (format === 'markdown') {
            output = `# Synaptic AI Pro - Tools Reference\n`;
            output += `Total: ${totalTools} tools in ${Object.keys(byCategory).length} categories\n\n`;

            for (const [category, tools] of Object.entries(byCategory).sort((a, b) => a[0].localeCompare(b[0]))) {
                output += `## ${category} (${tools.length} tools)\n\n`;
                for (const tool of tools.sort((a, b) => a.name.localeCompare(b.name))) {
                    output += `### ${tool.name}\n`;
                    output += `${tool.description || tool.title || ''}\n`;
                    if (tool.inputSchema?.properties) {
                        const props = Object.entries(tool.inputSchema.properties);
                        if (props.length > 0) {
                            output += `**Parameters:**\n`;
                            for (const [propName, propData] of props) {
                                const required = tool.inputSchema.required?.includes(propName) ? ' (required)' : '';
                                const type = propData.type || 'any';
                                const desc = propData.description || '';
                                output += `- \`${propName}\`: ${type}${required} - ${desc}\n`;
                            }
                        }
                    }
                    output += '\n';
                }
            }
        } else {
            // Compact format
            output = `# Tools Reference (${totalTools} tools)\n\n`;
            for (const [category, tools] of Object.entries(byCategory).sort((a, b) => a[0].localeCompare(b[0]))) {
                output += `## ${category}\n`;
                for (const tool of tools.sort((a, b) => a.name.localeCompare(b.name))) {
                    output += `- ${tool.name}: ${(tool.description || tool.title || '').slice(0, 80)}\n`;
                }
                output += '\n';
            }
        }

        output += `\n---\nUse execute(tool_name, params) to run any tool.`;
        return output;
    }

    // Register MCP Resources for tools reference (for prompt caching)
    mcpServer.registerResource('synaptic://tools/reference', {
        title: 'Tools Reference (Compact)',
        description: 'Complete list of all Unity tools in compact format. Add to context for efficient tool discovery.',
        mimeType: 'text/markdown'
    }, async () => {
        return {
            contents: [{
                uri: 'synaptic://tools/reference',
                mimeType: 'text/markdown',
                text: generateToolsReference('compact')
            }]
        };
    });

    mcpServer.registerResource('synaptic://tools/reference/full', {
        title: 'Tools Reference (Full)',
        description: 'Complete list of all Unity tools with full parameter details.',
        mimeType: 'text/markdown'
    }, async () => {
        return {
            contents: [{
                uri: 'synaptic://tools/reference/full',
                mimeType: 'text/markdown',
                text: generateToolsReference('markdown')
            }]
        };
    });

    // ===== META-TOOL 3: execute =====
    mcpServer.registerTool('execute', {
        title: 'Execute Tool',
        description: 'Execute any Unity tool by name. Use list_tools(category) first to see available tools and their parameters.',
        inputSchema: z.object({
            tool: z.string().describe('Tool name to execute (e.g., "create_gameobject", "set_transform")'),
            params: z.any().optional().describe('Parameters as JSON object {"name":"value"}')
        })
    }, async (params) => {
        const toolName = params.tool;
        let toolParams = params.params || {};

        // Handle case where params is passed as string (e.g., '{"name":"x"}')
        if (typeof toolParams === 'string') {
            try {
                toolParams = JSON.parse(toolParams);
            } catch (e) {
                // Try to parse key=value format (e.g., 'name=MyCube')
                const keyValueMatch = toolParams.match(/^(\w+)=(.+)$/);
                if (keyValueMatch) {
                    toolParams = { [keyValueMatch[1]]: keyValueMatch[2] };
                } else {
                    // Treat as single value if it's just a plain string
                    toolParams = {};
                }
            }
        }

        // Normalize tool name - strip unity_ prefix if present
        const strippedName = toolName.startsWith('unity_') ? toolName.substring(6) : toolName;
        const fullName = `unity_${strippedName}`;

        // Check if tool exists in registry
        const toolInfo = ALL_TOOLS[strippedName];
        if (!toolInfo) {
            // Find similar tool names for helpful error message
            const allToolNames = Object.keys(ALL_TOOLS);
            const similar = allToolNames.filter(t =>
                t.includes(strippedName) || strippedName.includes(t) ||
                t.split('_').some(part => strippedName.includes(part))
            ).slice(0, 5);

            let errorMsg = `Unknown tool: "${toolName}"`;
            if (similar.length > 0) {
                errorMsg += `\n\nDid you mean: ${similar.join(', ')}?`;
            }
            errorMsg += `\n\nUse list_categories() to see available categories, then list_tools(category) to see tools.`;

            return {
                content: [{
                    type: 'text',
                    text: errorMsg
                }]
            };
        }

        // Get the command name (without unity_ prefix) for Unity
        // Use lowercase - Unity's ConvertCommandToOperationType expects lowercase
        const commandName = strippedName.toLowerCase();

        try {
            const result = await sendUnityCommand(commandName, toolParams);
            return {
                content: [{
                    type: 'text',
                    text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
                }]
            };
        } catch (error) {
            // Detailed error message
            let errorDetail = `Error executing "${strippedName}":\n`;
            errorDetail += `  Message: ${error.message}\n`;

            if (error.message.includes('not connected')) {
                errorDetail += `\nTroubleshooting:\n`;
                errorDetail += `  1. Check Unity Editor is running\n`;
                errorDetail += `  2. Verify Synaptic AI Pro is connected (check Console)\n`;
                errorDetail += `  3. Try restarting the MCP server\n`;
            } else if (error.message.includes('timeout')) {
                errorDetail += `\nThe command timed out. Unity may be:\n`;
                errorDetail += `  - Compiling scripts\n`;
                errorDetail += `  - Processing a heavy operation\n`;
                errorDetail += `  - Not responding\n`;
            }

            errorDetail += `\nTool info: ${toolInfo.title} (${toolInfo.category})`;

            return {
                content: [{
                    type: 'text',
                    text: errorDetail
                }]
            };
        }
    });

    // ===== META-TOOL 3.5: run_csharp =====
    // Arbitrary C# execution escape-hatch (equivalent of Blender's run_python).
    // Promoted to a top-level meta-tool so small local LLMs don't have to go
    // through the execute({tool, params}) two-level nest.
    mcpServer.registerTool('run_csharp', {
        title: 'Run C# Code',
        description: 'Execute arbitrary C# code against the running Unity Editor (equivalent of Blender run_python). UnityEngine / UnityEditor / System.Linq / Newtonsoft.Json are pre-imported. Use this when no dedicated tool covers the operation. Does NOT trigger AssemblyReload so the connection stays alive.\n\nReturn value: end the snippet with `return X;` to capture X into the `result` field (prefix statements like `var x = ...;` execute first). A bare expression without trailing `;` is also accepted. Side-effect-only snippets return `result: null`. Debug.Log / LogWarning / LogError are captured into `output`.\n\nWORKS: GameObject / Transform / Component manipulation, AssetDatabase, Selection, EditorApplication, scene/asset queries via `FindObjectsByType<T>()` and other generic METHODS, generic method extension calls (`GetComponent<T>()`), arrays + foreach + LINQ-like loops, string interpolation, math, multi-statement bodies.\n\nKNOWN LIMITATION: Unity Mono.CSharp interactive parser cannot instantiate generic TYPES — `new List<int>()`, `new Dictionary<K,V>()`, `new HashSet<T>()` etc. silently return `result: null`. Workarounds: use plain arrays (`new int[] {1,2,3}`), `System.Collections.ArrayList`, or invoke generic helper methods that already exist (e.g. `FindObjectsByType<GameObject>(...)`). Generic method calls themselves are fine; only `new T<U>()` is blocked. LINQ chains that infer `IEnumerable<T>` may also fail — use `foreach` instead.',
        inputSchema: z.object({
            code: z.string().describe('C# code. End with `return X;` to capture X into `result`. A bare expression (no trailing `;`) also returns its value. Pre-imported: System, System.Linq, System.Collections.Generic, UnityEngine, UnityEditor, Newtonsoft.Json. AVOID `new List<int>()` / `new Dictionary<K,V>()` style generic instantiation (Mono parser limitation) — use arrays or ArrayList instead.')
        })
    }, async (params) => {
        try {
            const result = await sendUnityCommand('run_csharp', params);
            return {
                content: [{
                    type: 'text',
                    text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
                }]
            };
        } catch (error) {
            return {
                content: [{
                    type: 'text',
                    text: `run_csharp failed: ${error.message}`
                }]
            };
        }
    });

    // ===== META-TOOL 4: inspect =====
    mcpServer.registerTool('inspect', {
        title: 'Inspect Unity Objects',
        description: 'Dynamically inspect any Unity object, component, scene, or project assets. Use this to discover what properties are available before modifying them.',
        inputSchema: z.object({
            target: z.enum(['gameobject', 'component', 'scene', 'project', 'prefabs', 'hierarchy'])
                .describe('What to inspect: gameobject (properties/components), component (all serialized fields), scene (current scene info), project (project structure), prefabs (search prefabs), hierarchy (scene hierarchy)'),
            name: z.string().optional().describe('GameObject name (for gameobject/component targets)'),
            component: z.string().optional().describe('Component type to inspect (e.g., "Camera", "CinemachineVirtualCamera")'),
            path: z.string().optional().describe('Asset path filter for project/prefabs (e.g., "Assets/Prefabs/*")'),
            depth: z.number().optional().default(2).describe('Hierarchy depth for nested inspection (default: 2)')
        })
    }, async (params) => {
        try {
            const result = await sendUnityCommand('dynamic_inspect', params);
            return {
                content: [{
                    type: 'text',
                    text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
                }]
            };
        } catch (error) {
            return {
                content: [{
                    type: 'text',
                    text: `Inspect failed: ${error.message}\n\nTips:\n- For gameobject: provide "name"\n- For component: provide "name" and "component"\n- For prefabs: provide "path" with wildcards (e.g., "Assets/**/*.prefab")`
                }]
            };
        }
    });

    // ===== META-TOOL 5: modify =====
    mcpServer.registerTool('modify', {
        title: 'Modify Unity Objects',
        description: 'Dynamically modify any property of a Unity component using property paths. Use inspect() first to discover available properties.',
        inputSchema: z.object({
            gameObject: z.string().describe('GameObject name'),
            component: z.string().describe('Component type (e.g., "Transform", "Camera", "CinemachineVirtualCamera")'),
            properties: z.record(z.any()).describe('Property paths and values as key-value pairs (e.g., {"m_Lens.FieldOfView": 60, "m_Priority": 10})'),
            createIfMissing: z.boolean().optional().default(false).describe('Add component if it does not exist')
        })
    }, async (params) => {
        try {
            const result = await sendUnityCommand('dynamic_modify', params);
            return {
                content: [{
                    type: 'text',
                    text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
                }]
            };
        } catch (error) {
            return {
                content: [{
                    type: 'text',
                    text: `Modify failed: ${error.message}\n\nTips:\n- Use inspect(target:"component") first to see available property paths\n- Nested properties use dot notation: "m_Lens.FieldOfView"\n- Array elements: "m_Materials.Array.data[0]"`
                }]
            };
        }
    });

    // ===== META-TOOL 6: create =====
    mcpServer.registerTool('create', {
        title: 'Create Unity Objects',
        description: 'Create GameObjects, instantiate prefabs, load scenes, or add components. Universal creation tool.',
        inputSchema: z.object({
            type: z.enum(['gameobject', 'prefab', 'scene', 'component'])
                .describe('What to create: gameobject (empty or primitive), prefab (instantiate from asset), scene (load scene), component (add to existing object)'),
            // For gameobject
            name: z.string().optional().describe('Name for new GameObject'),
            primitive: z.enum(['empty', 'cube', 'sphere', 'cylinder', 'plane', 'capsule', 'quad']).optional().describe('Primitive type (for gameobject)'),
            // For prefab
            asset: z.string().optional().describe('Asset path for prefab (e.g., "Assets/Prefabs/Enemy.prefab")'),
            // For scene
            scene: z.string().optional().describe('Scene name or path to load'),
            additive: z.boolean().optional().default(false).describe('Load scene additively (for scene type)'),
            // For component
            gameObject: z.string().optional().describe('Target GameObject (for component type)'),
            component: z.string().optional().describe('Component type to add (for component type)'),
            // Common
            parent: z.string().optional().describe('Parent GameObject name'),
            position: z.object({ x: z.number(), y: z.number(), z: z.number() }).optional().describe('World position'),
            rotation: z.object({ x: z.number(), y: z.number(), z: z.number() }).optional().describe('Euler rotation'),
            scale: z.object({ x: z.number(), y: z.number(), z: z.number() }).optional().describe('Local scale')
        })
    }, async (params) => {
        try {
            const result = await sendUnityCommand('dynamic_create', params);
            return {
                content: [{
                    type: 'text',
                    text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
                }]
            };
        } catch (error) {
            return {
                content: [{
                    type: 'text',
                    text: `Create failed: ${error.message}\n\nExamples:\n- GameObject: {type:"gameobject", name:"Player", primitive:"capsule"}\n- Prefab: {type:"prefab", asset:"Assets/Prefabs/Enemy.prefab", position:{x:0,y:0,z:0}}\n- Scene: {type:"scene", scene:"Level2", additive:true}\n- Component: {type:"component", gameObject:"Player", component:"Rigidbody"}`
                }]
            };
        }
    });

    // Start MCP server
    await mcpServer.start();
}

// =====================================================
// Main Entry Point
// =====================================================

// Send shutdown request to prior process on same port
async function requestShutdownFromPriorProcess(port) {
    return new Promise((resolve) => {
        try {
            const ws = new WebSocket(`ws://localhost:${port}`);
            const timeout = setTimeout(() => {
                ws.close();
                resolve(false);
            }, 2000);

            ws.on('open', () => {
                ws.send(JSON.stringify({ type: 'shutdown' }));
                clearTimeout(timeout);
                setTimeout(() => {
                    ws.close();
                    resolve(true);
                }, 500);
            });

            ws.on('error', () => {
                clearTimeout(timeout);
                resolve(false);
            });
        } catch (e) {
            resolve(false);
        }
    });
}

// Try to start server with retry logic
function startServerWithRetry(port, maxRetries = 5, retryDelay = 1000) {
    return new Promise((resolve, reject) => {
        let attempt = 0;

        const tryListen = () => {
            attempt++;
            console.error(`[SuperSave] Attempting to listen on port ${port} (attempt ${attempt}/${maxRetries})`);

            const onError = async (err) => {
                server.removeListener('error', onError);

                if (err.code === 'EADDRINUSE') {
                    console.error(`[SuperSave] Port ${port} in use`);

                    // Check if existing server is healthy (another Claude session may be using it)
                    try {
                        const http = require('http');
                        const healthCheck = await new Promise((res, rej) => {
                            const req = http.get(`http://localhost:${port}/health`, { timeout: 2000 }, (resp) => {
                                let data = '';
                                resp.on('data', chunk => data += chunk);
                                resp.on('end', () => res(data));
                            });
                            req.on('error', rej);
                            req.on('timeout', () => { req.destroy(); rej(new Error('timeout')); });
                        });
                        // Existing server is healthy - don't kill it, just skip server binding
                        console.error(`[SuperSave] Existing healthy server found on port ${port} - sharing connection (forwarding to leader)`);
                        resolve({ shareMode: true, leaderPort: port });
                        return;
                    } catch (e) {
                        // Existing server is dead - safe to take over
                    }

                    if (attempt === 1) {
                        // First retry: try to shutdown prior process
                        console.error(`[SuperSave] Sending shutdown request to prior process...`);
                        await requestShutdownFromPriorProcess(port);
                    }

                    if (attempt < maxRetries) {
                        console.error(`[SuperSave] Retrying in ${retryDelay}ms...`);
                        setTimeout(tryListen, retryDelay);
                    } else {
                        reject(new Error(`Failed to bind to port ${port} after ${maxRetries} attempts`));
                    }
                } else {
                    reject(err);
                }
            };

            server.once('error', onError);

            server.listen(port, () => {
                server.removeListener('error', onError);
                console.error(`[SuperSave] Token SuperSave Mode started on port ${port}`);
                console.error(`[SuperSave] Only 6 meta-tools loaded (99% context reduction)`);
                resolve();
            });
        };

        tryListen();
    });
}

async function main() {
    const PORT = process.env.PORT || 8090;

    // ESC-0121: defer wss attachment until we know whether we'll be the leader.
    // In share mode (port already held by a healthy leader), starting a WSS on
    // a non-listening HTTP server produces a zombie that silently swallows
    // tool calls for all follower sessions.

    // Setup and start MCP (stdio side — same regardless of leader/follower)
    await setupMCPServer();

    // Expose /execute and /health on the HTTP server so leaders can serve
    // forwarded tool calls from follower sessions.
    app.get('/health', (req, res) => {
        res.json({
            status: 'ok',
            mode: 'supersave',
            shareMode: !!shareModeLeaderPort,
            unityConnected: !!(unityWebSocket && unityWebSocket.readyState === WebSocket.OPEN)
        });
    });
    app.post('/execute', async (req, res) => {
        try {
            const { tool, params } = req.body || {};
            if (!tool) return res.status(400).json({ error: 'tool is required' });
            const result = await sendUnityCommand(tool, params || {});
            res.json(result);
        } catch (e) {
            res.status(500).json({ error: e.message });
        }
    });

    // Try to bind the HTTP port; if already taken, fall into share mode.
    const bindResult = await startServerWithRetry(PORT);
    if (bindResult && bindResult.shareMode) {
        shareModeLeaderPort = bindResult.leaderPort;
        console.error(`[SuperSave] Follower mode active — forwarding Unity commands to leader on port ${shareModeLeaderPort}`);
        // Do NOT attach wss — there is no listening HTTP server to bridge it.
        return;
    }

    // Leader mode: now safe to attach WebSocket server to the listening HTTP server.
    wss = new WebSocket.Server({ server });
    setupWebSocketHandlers();
}

// Shutdown handler
function shutdownServer() {
    if (unityWebSocket && unityWebSocket.readyState === WebSocket.OPEN) {
        unityWebSocket.close();
    }
    if (wss) {
        wss.close();
    }
    if (server && server.listening) {
        server.close(() => {
            process.exit(0);
        });
    } else {
        process.exit(0);
    }
    setTimeout(() => {
        process.exit(1);
    }, 5000);
}

process.on('SIGINT', shutdownServer);
process.on('SIGTERM', shutdownServer);
process.stdin.on('close', () => {
    shutdownServer();
});

process.on('uncaughtException', (error) => {
    // Log EADDRINUSE and other critical errors
    if (error.code === 'EADDRINUSE') {
        console.error(`[SuperSave] uncaughtException: EADDRINUSE - port already in use`);
    } else {
        console.error(`[SuperSave] uncaughtException: ${error.message}`);
    }
});

process.on('unhandledRejection', (reason, promise) => {
    console.error(`[SuperSave] unhandledRejection: ${reason}`);
});

main().catch(err => {
    console.error('[SuperSave] Fatal error:', err);
    process.exit(1);
});
