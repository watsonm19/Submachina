#!/usr/bin/env node

/**
 * hub-server.js - MCP Hub Server for Unity Synaptic v1.1.2 (Dynamic Mode)
 *
 * Provides dynamic tool loading for GitHub Copilot (VS Code):
 * - Exposes only ~10 management tools initially
 * - Loads additional tools on-demand via select_tools()
 * - Uses MCP Notification System to update tool list
 * - Requires client support for notifications/tools/list_changed
 *
 * Supported Clients:
 * - ✅ GitHub Copilot (VS Code)
 * - ❌ Claude Desktop (use index.js instead)
 * - ❌ Cursor (use index.js instead)
 *
 * Usage:
 *   node hub-server.js (stdio mode for MCP clients)
 *   OPENAI_API_KEY=sk-xxx node hub-server.js (optional, for keyword search)
 */

import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import {
    CallToolRequestSchema,
    ListToolsRequestSchema,
} from '@modelcontextprotocol/sdk/types.js';
import WebSocket, { WebSocketServer } from 'ws';
import { z } from 'zod';
import express from 'express';
import http from 'http';
import cors from 'cors';
import {
    filterToolsByCategories,
    searchToolsByKeywords,
    getAvailableCategories,
    loadToolRegistry
} from './utils/tool-loader.js';

// HTTP and WebSocket server setup
const app = express();
app.use(cors());
app.use(express.json());

const httpServer = http.createServer(app);
let wss = null; // WebSocket server for Unity connections

// Unity WebSocket connection
let unityWs = null;

// Unity command tracking (same as index.js)
const pendingRequests = new Map();
let requestId = 0;

// Load tool registry (embeddings + metadata)
const toolRegistry = loadToolRegistry();

if (!toolRegistry) {
    console.error('[Hub] WARNING: tool-registry.json not found. Run: node scripts/generate-tool-registry.js');
    console.error('[Hub] Hub server will work with limited functionality');
}

// Initialize MCP Server with dynamic tool capabilities
const server = new Server(
    {
        name: 'unity-synaptic-hub',
        version: '1.1.0',
    },
    {
        capabilities: {
            tools: {
                listChanged: true  // Support for dynamic tool updates via notifications
            },
        },
    }
);

// Essential tools (always available)
// 
// RECOMMENDED WORKFLOW FOR LLMs:
// 1. Use list_available_categories() to see all 31 categories
// 2. Use search_tools(query: "keyword") to find specific tools
// 3. Use select_tools(categories: ["Cat1", "Cat2"]) to load needed tools
// 4. Call the loaded Unity tools (e.g., unity_create_gameobject)
//
// Example: User wants to add visual effects
// → search_tools(query: "bloom effect")
// → select_tools(categories: ["VFX"])
// → unity_create_bloom(...)
//
const essentialTools = [
    {
        name: 'select_tools',
        title: '🔧 Load Unity Tools by Category',
        description: 'Load additional Unity tools dynamically. Call list_available_categories first to see available categories, or use search_tools to find specific tools. Without calling this, only 8 basic tools are available. Example: select_tools({categories: ["VFX", "Camera"]}) loads visual effects and camera tools.',
        inputSchema: z.object({
            categories: z.array(z.enum([
                'GameObject', 'Transform', 'Material', 'Lighting', 'Camera', 'Physics',
                'UI', 'Animation', 'Cinemachine', 'Scene', 'GOAP', 'Audio', 'Input',
                'VFX', 'Shader', 'Weather', 'TimeOfDay', 'Editor', 'Package', 'Build',
                'Monitoring', 'AssetManagement', 'Optimization', 'Batch', 'GameSystems',
                'AI', 'Debug', 'Timeline', 'Scripting', 'Screenshot', 'Utility'
            ])).optional().describe('Tool categories to load (see list_available_categories for options)'),
            keywords: z.array(z.string()).optional().describe('Optional: filter by keywords (e.g. ["camera", "color"])'),
            maxTools: z.number().optional().default(50).describe('Max tools to load (default: 50, max: 100)')
        })
    },
    {
        name: 'list_available_categories',
        title: '📋 List Available Tool Categories',
        description: 'Show all 31 Unity tool categories with counts and descriptions. RECOMMENDED: Call this first to discover available categories, then use select_tools to load the tools you need.',
        inputSchema: z.object({})
    },
    {
        name: 'search_tools',
        title: '🔍 Search Unity Tools by Keyword',
        description: 'Find Unity tools by keyword search. Supports single or multiple keywords (e.g. "camera", "particle effect", "material color"). Returns tool names, descriptions, categories, and relevance scores. Use this when you know what you want but not which category it belongs to.',
        inputSchema: z.object({
            query: z.string().describe('Search keyword(s) - single word or phrase'),
            limit: z.number().optional().default(20).describe('Max results (default: 20)')
        })
    },
    {
        name: 'unity_get_scene_summary',
        title: 'Get Scene Summary',
        description: 'Get lightweight Unity scene overview (<200KB). Returns: scene name, GameObject count, cameras, lights, root GameObjects list.',
        inputSchema: z.object({})
    },
    {
        name: 'unity_get_gameobjects_list',
        title: 'Get GameObjects List',
        description: 'Get filtered list of GameObjects. Supports filters: layer, tag, name, activeOnly. Max 100 results.',
        inputSchema: z.object({
            layerFilter: z.string().optional(),
            tagFilter: z.string().optional(),
            nameFilter: z.string().optional(),
            activeOnly: z.boolean().optional(),
            maxCount: z.number().optional().default(100)
        })
    },
    {
        name: 'unity_get_gameobject_detail',
        title: 'Get GameObject Detail',
        description: 'Get detailed info for specific GameObject by name or instanceId.',
        inputSchema: z.object({
            nameOrId: z.string()
        })
    },
    {
        name: 'unity_undo',
        title: 'Undo Unity Operation',
        description: 'Undo last Unity operation',
        inputSchema: z.object({})
    },
    {
        name: 'unity_redo',
        title: 'Redo Unity Operation',
        description: 'Redo Unity operation',
        inputSchema: z.object({})
    }
];

// Currently active tools (starts with essential only)
let activeTools = [...essentialTools];

// Category metadata used for validation/default selection
const ALL_TOOL_CATEGORIES = [
    'GameObject', 'Transform', 'Material', 'Lighting', 'Camera', 'Physics',
    'UI', 'Animation', 'Cinemachine', 'Scene', 'GOAP', 'Audio', 'Input',
    'VFX', 'Shader', 'Weather', 'TimeOfDay', 'Editor', 'Package', 'Build',
    'Monitoring', 'AssetManagement', 'Optimization', 'Batch', 'GameSystems',
    'AI', 'Debug', 'Timeline', 'Scripting', 'Screenshot', 'Utility', 'Other'
];

// Default categories to load when no parameters are supplied (covers core workflows)
const DEFAULT_TOOL_CATEGORIES = [
    'GameObject', 'Transform', 'Material', 'Lighting', 'Camera', 'Physics',
    'UI', 'Animation', 'Scene', 'Audio', 'Utility'
];

const CATEGORY_SET = new Set(ALL_TOOL_CATEGORIES);

function normalizeInputList(value) {
    if (!value) {
        return [];
    }

    if (Array.isArray(value)) {
        return value
            .map(item => (typeof item === 'string' ? item : String(item)).trim())
            .filter(Boolean);
    }

    if (typeof value === 'string') {
        return value
            .split(/[,|]/)
            .map(item => item.trim())
            .filter(Boolean);
    }

    return [];
}

function normalizeCategories(value) {
    return normalizeInputList(value).filter(item => CATEGORY_SET.has(item));
}

function normalizeKeywords(value) {
    return normalizeInputList(value);
}

// Setup WebSocket server to accept Unity connections
function setupWebSocketServer() {
    if (!wss) return;

    wss.on('connection', (ws, req) => {
        const isUnity = req.headers['x-client-type'] === 'unity' || req.url === '/unity';

        if (isUnity) {
            // Unity client connected
            if (unityWs) {
                unityWs.close();
            }
            unityWs = ws;
            console.error('[Hub] Unity connected via WebSocket');

            // Handle Unity responses (same format as index.js)
            ws.on('message', (data) => {
                try {
                    const response = JSON.parse(data.toString());

                    // Unity sends: { id, type: 'operation_result', data: { success: true/false }, content: "result" }
                    if (response.type === 'operation_result' && response.id) {
                        // Convert id to number like index.js does
                        const numericId = typeof response.id === 'string' ? parseInt(response.id) : response.id;
                        
                        if (pendingRequests.has(numericId)) {
                            const { resolve, reject, timeout } = pendingRequests.get(numericId);
                            clearTimeout(timeout);
                            pendingRequests.delete(numericId);

                            // Match index.js format: resolve with content, not whole response
                            if (response.data && response.data.success) {
                                resolve(response.content);
                            } else {
                                reject(new Error(response.content || 'Unity command failed'));
                            }
                        }
                    }
                } catch (error) {
                    console.error('[Hub] Failed to parse Unity response:', error.message);
                }
            });

            ws.on('close', () => {
                console.error('[Hub] Unity disconnected');
                unityWs = null;
            });

            ws.on('error', (err) => {
                console.error('[Hub] Unity WebSocket error:', err.message);
            });
        } else {
            console.error('[Hub] Unknown client connected, closing connection');
            ws.close();
        }
    });
}

// Helper function for delay
function sleep(ms) {
    return new Promise(resolve => setTimeout(resolve, ms));
}

// Single command attempt to Unity
async function forwardToUnityOnce(command, params, id) {
    return new Promise((resolve, reject) => {
        const timeout = setTimeout(() => {
            pendingRequests.delete(id);
            reject(new Error('timeout'));
        }, 15000);

        pendingRequests.set(id, { resolve, reject, timeout });

        const message = JSON.stringify({
            id,
            type: 'tool_call',
            tool: command,
            command,
            parameters: params || {}
        });

        unityWs.send(message);
    });
}

// Forward command to Unity with auto-retry (enhanced for compilation waiting)
async function forwardToUnity(toolName, params) {
    const MAX_RETRIES = 30;          // 30 retries for very long compilations
    const RETRY_DELAY = 10000;       // 10 seconds between retries
    const COMPILE_WAIT_DELAY = 10000; // 10 seconds when compiling (total max: ~5 min)

    const command = toolName.startsWith('unity_') ? toolName.substring(6) : toolName;
    let lastError = null;
    let isLikelyCompiling = false;

    for (let attempt = 1; attempt <= MAX_RETRIES; attempt++) {
        if (!unityWs || unityWs.readyState !== WebSocket.OPEN) {
            lastError = new Error('Unity not connected');
            isLikelyCompiling = true;

            if (attempt < MAX_RETRIES) {
                const waitTime = isLikelyCompiling ? COMPILE_WAIT_DELAY : RETRY_DELAY;
                console.error(`[Hub] Unity not connected (attempt ${attempt}/${MAX_RETRIES}). Waiting ${waitTime/1000}s...`);
                await sleep(waitTime);
                continue;
            }
            break;
        }

        const id = ++requestId;

        try {
            const result = await forwardToUnityOnce(command, params, id);
            // Format response for MCP client
            const formattedResult = {
                content: [{
                    type: 'text',
                    text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
                }]
            };
            if (attempt > 1) {
                formattedResult.content[0].text += `\n[Note: Succeeded on retry attempt ${attempt}]`;
            }
            return formattedResult;
        } catch (error) {
            lastError = error;
            if (error.message === 'timeout') {
                isLikelyCompiling = true;
            }
            if (attempt < MAX_RETRIES) {
                const waitTime = isLikelyCompiling ? COMPILE_WAIT_DELAY : RETRY_DELAY;
                console.error(`[Hub] Command failed (attempt ${attempt}/${MAX_RETRIES}): ${error.message}. Retrying in ${waitTime/1000}s...`);
                await sleep(waitTime);
            }
        }
    }

    throw new Error(`Unity not connected (after ${MAX_RETRIES} attempts). Unity may be recompiling - wait and try again.`);
}

// Handle select_tools - load tools dynamically
async function handleSelectTools(params = {}) {
    const {
        categories = [],
        keywords = [],
        maxTools = 50
    } = params;

    let normalizedCategories = normalizeCategories(categories);
    const normalizedKeywords = normalizeKeywords(keywords);

    if (normalizedCategories.length === 0 && normalizedKeywords.length === 0) {
        normalizedCategories = [...DEFAULT_TOOL_CATEGORIES];
        console.error('[Hub] select_tools called without parameters – using default categories:', normalizedCategories.join(', '));
    }

    if (!toolRegistry) {
        return {
            content: [{
                type: 'text',
                text: JSON.stringify({
                    error: 'Tool registry not available. Run: node scripts/generate-tool-registry.js'
                }, null, 2)
            }],
            isError: true
        };
    }

    let selectedToolNames = [];

    // Filter by categories
    if (normalizedCategories.length > 0) {
        selectedToolNames = filterToolsByCategories(normalizedCategories, toolRegistry);
    }

    // Further filter by keywords using embedding search
    if (normalizedKeywords.length > 0) {
        const query = normalizedKeywords.join(' ');
        const searchResults = await searchToolsByKeywords(query, 200, toolRegistry);

        if (selectedToolNames.length > 0) {
            // Intersect with category results
            const searchNames = new Set(searchResults.map(r => r.name));
            selectedToolNames = selectedToolNames.filter(name => searchNames.has(name));
        } else {
            // Use search results only
            selectedToolNames = searchResults.map(r => r.name);
        }
    }

    // Limit results
    selectedToolNames = selectedToolNames.slice(0, Math.min(maxTools, 100));

    // Convert tool names to full tool definitions
    // Use passthrough schema to accept any parameters (forwarded to Unity)
    const selectedTools = selectedToolNames.map(name => {
        const meta = toolRegistry[name];
        return {
            name: name,
            title: meta.title || name,
            description: meta.description || '',
            inputSchema: z.object({}).passthrough() // Accept any parameters, forward to Unity
        };
    });

    // Update active tools (essential + selected)
    activeTools = [...essentialTools, ...selectedTools];

    console.error(`[Hub] Loaded ${selectedTools.length} tools. Total active: ${activeTools.length}`);

    // Send notification to client that tool list has changed
    // This triggers the client to call tools/list again
    try {
        await server.notification({
            method: 'notifications/tools/list_changed'
        });
        console.error('[Hub] Sent tools/list_changed notification to client');
    } catch (error) {
        console.error('[Hub] Warning: Failed to send notification:', error.message);
        // Continue anyway - client might not support notifications
    }

    return {
        content: [{
            type: 'text',
            text: JSON.stringify({
                success: true,
                loaded_tools: selectedTools.length,
                total_active: activeTools.length,
                categories: normalizedCategories,
                message: 'Tools loaded. Client should refresh tool list automatically.',
                tools: selectedTools.map(t => ({
                    name: t.name,
                    category: toolRegistry[t.name]?.category
                }))
            }, null, 2)
        }]
    };
}

// Handle list_available_categories
async function handleListCategories() {
    const categories = getAvailableCategories();

    return {
        content: [{
            type: 'text',
            text: JSON.stringify({ categories }, null, 2)
        }]
    };
}

// Handle search_tools
async function handleSearchTools(params) {
    const { query, limit = 20 } = params;

    if (!toolRegistry) {
        return {
            content: [{
                type: 'text',
                text: JSON.stringify({
                    error: 'Tool registry not available'
                }, null, 2)
            }],
            isError: true
        };
    }

    const results = await searchToolsByKeywords(query, limit, toolRegistry);

    return {
        content: [{
            type: 'text',
            text: JSON.stringify({
                query,
                results: results.map(r => ({
                    name: r.name,
                    description: r.description,
                    category: r.category,
                    relevance: r.score.toFixed(3)
                }))
            }, null, 2)
        }]
    };
}

// MCP handlers
server.setRequestHandler(ListToolsRequestSchema, async () => {
    console.error(`[Hub] ListTools called - returning ${activeTools.length} tools`);
    console.error(`[Hub] Essential tools: ${essentialTools.map(t => t.name).join(', ')}`);
    return {
        tools: activeTools.map(tool => {
            // Convert Zod schema to JSON Schema format
            let inputSchema;
            if (tool.inputSchema && tool.inputSchema._def) {
                // Extract properties from Zod schema
                const zodShape = tool.inputSchema._def.shape?.() || {};
                const properties = {};
                const required = [];

                for (const [key, value] of Object.entries(zodShape)) {
                    if (value._def) {
                        properties[key] = {
                            type: value._def.typeName === 'ZodString' ? 'string' :
                                  value._def.typeName === 'ZodNumber' ? 'number' :
                                  value._def.typeName === 'ZodBoolean' ? 'boolean' :
                                  value._def.typeName === 'ZodArray' ? 'array' :
                                  'string',
                            description: value._def.description || undefined
                        };

                        if (!value._def.isOptional) {
                            required.push(key);
                        }
                    }
                }

                inputSchema = {
                    type: 'object',
                    properties,
                    ...(required.length > 0 ? { required } : {})
                };
            } else {
                // Fallback for simple schemas
                inputSchema = {
                    type: 'object',
                    properties: {},
                    additionalProperties: true
                };
            }

            return {
                name: tool.name,
                description: tool.description,
                inputSchema
            };
        })
    };
});

server.setRequestHandler(CallToolRequestSchema, async (request) => {
    const { name, arguments: args } = request.params;

    console.error(`[Hub] Tool call: ${name}, args:`, JSON.stringify(args));
    console.error(`[Hub] Available tools: ${activeTools.map(t => t.name).join(', ')}`);

    try {
        // Hub management tools
        if (name === 'select_tools') {
            return await handleSelectTools(args);
        }

        if (name === 'list_available_categories') {
            return await handleListCategories();
        }

        if (name === 'search_tools') {
            return await handleSearchTools(args);
        }

        // All other tools forward to Unity
        return await forwardToUnity(name, args);

    } catch (error) {
        return {
            content: [{
                type: 'text',
                text: JSON.stringify({
                    error: error.message
                }, null, 2)
            }],
            isError: true
        };
    }
});

// Start server
async function main() {
    // Start MCP server with Stdio transport (for VS Code)
    const transport = new StdioServerTransport();
    await server.connect(transport);

    // Create WebSocket server for Unity connections
    wss = new WebSocketServer({ server: httpServer });
    setupWebSocketServer();

    // Start HTTP server
    const port = process.env.PORT || 8090;
    httpServer.listen(port, () => {
        console.error('[Hub] Unity Synaptic Hub Server v1.1.0 started');
        console.error(`[Hub] WebSocket server listening on port ${port}`);
        console.error(`[Hub] Essential tools loaded: ${essentialTools.length}`);
        console.error('[Hub] Waiting for Unity connection and select_tools() calls...');
    });
}

main().catch(error => {
    console.error('[Hub] Fatal error:', error);
    process.exit(1);
});
