#!/usr/bin/env node
/**
 * Synaptic AI Pro - HTTP Server (Standalone)
 *
 * Node.js based HTTP server for Unity control.
 * Runs outside Unity process - no domain reload issues.
 *
 * Usage:
 *   node http-server.js [port]
 *   node http-server.js 8086
 *
 * Environment variables:
 *   HTTP_PORT - Server port (default: 8086)
 *
 * HTTP and WebSocket run on the SAME port.
 * - HTTP: http://localhost:8086/
 * - WebSocket: ws://localhost:8086/
 */

const express = require('express');
const http = require('http');
const WebSocket = require('ws');
const cors = require('cors');
const fs = require('fs');
const path = require('path');

// ===== Configuration =====
const PORT = parseInt(process.argv[2]) || parseInt(process.env.HTTP_PORT) || 8086;

// ===== Parent-PID watchdog =====
// When launched detached from Unity's JobObject (Windows), we lose the
// guarantee that the OS will reap us if Unity dies. Poll the parent PID
// every 5s and self-terminate if it's gone — prevents orphaned node.exe.
(function setupParentWatchdog() {
  const arg = (process.argv.find(a => typeof a === 'string' && a.startsWith('--parent-pid=')) || '');
  const parentPid = arg ? parseInt(arg.split('=')[1], 10) : 0;
  if (!parentPid) return;
  setInterval(() => {
    try {
      // Signal 0 is a "check process exists" probe on both POSIX and Windows
      process.kill(parentPid, 0);
    } catch (_e) {
      console.error(`[watchdog] Parent PID ${parentPid} gone. Exiting.`);
      process.exit(0);
    }
  }, 5000).unref();
})();

// ===== File log (when --log=path is provided) =====
// Detached mode disables stdout/stderr piping from C# side, so route logs
// to a file instead. Best-effort: silent failure if file can't be opened.
(function setupFileLog() {
  const arg = (process.argv.find(a => typeof a === 'string' && a.startsWith('--log=')) || '');
  const logPath = arg ? arg.split('=').slice(1).join('=') : '';
  if (!logPath) return;
  try {
    const fs = require('fs');
    const stream = fs.createWriteStream(logPath, { flags: 'a' });
    const wrap = (orig) => (...args) => {
      try {
        const line = args.map(a => typeof a === 'string' ? a : JSON.stringify(a)).join(' ');
        stream.write(`[${new Date().toISOString()}] ${line}\n`);
      } catch (_) { /* ignore */ }
      try { orig.apply(console, args); } catch (_) { /* detached: stdout may be closed */ }
    };
    console.log = wrap(console.log);
    console.error = wrap(console.error);
    console.warn = wrap(console.warn);
  } catch (_e) {
    /* ignore: log is best-effort */
  }
})();

const app = express();
app.use(cors());
app.use(express.json({ limit: '10mb' }));

const server = http.createServer(app);

// ===== Tool Registry =====
let toolRegistry = {};
let toolCategories = {};

function loadToolRegistry() {
    try {
        const registryPath = path.join(__dirname, 'tool-registry.json');
        if (fs.existsSync(registryPath)) {
            toolRegistry = JSON.parse(fs.readFileSync(registryPath, 'utf8'));

            // Build category index
            toolCategories = {};
            for (const [name, tool] of Object.entries(toolRegistry)) {
                const cat = tool.category || 'Other';
                if (!toolCategories[cat]) toolCategories[cat] = [];
                toolCategories[cat].push({ name, ...tool });
            }

            console.error(`[HTTP] Loaded ${Object.keys(toolRegistry).length} tools in ${Object.keys(toolCategories).length} categories`);
        } else {
            console.error('[HTTP] Warning: tool-registry.json not found');
        }
    } catch (e) {
        console.error(`[HTTP] Failed to load tool registry: ${e.message}`);
    }
}

loadToolRegistry();

// ===== Unity WebSocket Connection =====
let unitySocket = null;
let wss = null;
const pendingRequests = new Map();
let requestCounter = 0;

function setupWebSocket() {
    // Attach WebSocket to the same HTTP server (same port)
    wss = new WebSocket.Server({ server });

    wss.on('connection', (ws, req) => {
        const isUnity = req.headers['x-client-type'] === 'unity' || req.url === '/unity';

        if (isUnity || !req.url || req.url === '/') {
            if (unitySocket) {
                try { unitySocket.close(); } catch (e) {}
            }
            unitySocket = ws;
            console.error(`[HTTP] Unity connected via WebSocket`);

            ws.on('message', (message) => {
                try {
                    const data = JSON.parse(message);

                    // Handle response from Unity
                    if (data.operationId && pendingRequests.has(data.operationId)) {
                        const { resolve } = pendingRequests.get(data.operationId);
                        pendingRequests.delete(data.operationId);
                        resolve(data);
                    }
                } catch (e) {
                    console.error(`[HTTP] WebSocket message error: ${e.message}`);
                }
            });

            ws.on('close', () => {
                console.error('[HTTP] Unity disconnected');
                unitySocket = null;
            });

            ws.on('error', (err) => {
                console.error(`[HTTP] WebSocket error: ${err.message}`);
            });

            // ESC-0108 fix (reported by xvpower., 2026-05-22):
            // Mono's `ClientWebSocket` (Unity) does NOT auto-respond to
            // WebSocket protocol-level `ping` frames with `pong` — unlike
            // .NET 5+ runtime. The previous `ws.ping() / ws.on('pong')`
            // heartbeat therefore terminated the connection after ~30s on
            // every Unity 6.3 LTS environment.
            // Switch to last-message-seen tracking: Unity already emits its
            // own application-level traffic (operation_response, debug logs)
            // so we just observe the timestamp of any received frame.
            ws.lastSeen = Date.now();
            const recordSeen = () => { ws.lastSeen = Date.now(); };
            ws.on('message', recordSeen);
            ws.on('ping', recordSeen);
            ws.on('pong', recordSeen);
        }
    });

    // Heartbeat: declare a connection dead only if no inbound frame for N ms.
    // Override via env (UNITY_STALE_TIMEOUT_MS); default 60 000 keeps a safe
    // margin over Unity's slowest legitimate quiet period.
    const STALE_TIMEOUT_MS = parseInt(process.env.UNITY_STALE_TIMEOUT_MS || '60000', 10);
    const heartbeatInterval = setInterval(() => {
        if (!wss.clients) return;
        wss.clients.forEach((ws) => {
            if (!ws.lastSeen) return;
            if (Date.now() - ws.lastSeen > STALE_TIMEOUT_MS) {
                console.error(`[HTTP] Unity stale (no frames for ${STALE_TIMEOUT_MS}ms) - closing connection`);
                try { ws.terminate(); } catch {}
            }
        });
    }, 30000);

    wss.on('close', () => {
        clearInterval(heartbeatInterval);
    });

    wss.on('error', (err) => {
        console.error(`[HTTP] WebSocket server error: ${err.message}`);
    });

    console.error(`[HTTP] WebSocket server attached to HTTP server (port ${PORT})`);
}

// Execute tool via Unity WebSocket
function executeOnUnity(toolName, params, timeout = 30000) {
    return new Promise((resolve, reject) => {
        if (!unitySocket || unitySocket.readyState !== WebSocket.OPEN) {
            reject(new Error('Unity not connected. Open Unity with Synaptic AI Pro project.'));
            return;
        }

        const operationId = `http_${++requestCounter}_${Date.now()}`;
        const operation = convertToolToOperation(toolName);

        const message = {
            type: 'operation',
            operationType: operation,
            operationId: operationId,
            parameters: params || {}
        };

        const timer = setTimeout(() => {
            pendingRequests.delete(operationId);
            reject(new Error(`Timeout waiting for Unity response (${timeout}ms)`));
        }, timeout);

        pendingRequests.set(operationId, {
            resolve: (data) => {
                clearTimeout(timer);
                resolve(data);
            },
            reject: (err) => {
                clearTimeout(timer);
                reject(err);
            }
        });

        unitySocket.send(JSON.stringify(message));
    });
}

// Convert tool name to Unity operation type
function convertToolToOperation(toolName) {
    // Remove unity_ prefix and convert to UPPER_SNAKE_CASE
    let name = toolName;
    if (name.startsWith('unity_')) {
        name = name.substring(6);
    }
    return name.toUpperCase();
}

// ===== Prompt Generation =====
function getAIControlPrompt() {
    return `# Synaptic AI Pro (Unity) HTTP Control Instructions

## Prerequisites
- Unity must be open with Synaptic AI Pro project loaded
- HTTP Server running: node http-server.js ${PORT}
- Unity connects via WebSocket automatically

## Endpoints
- GET / or /prompt - Get this AI control prompt + full tools reference
- GET /health - Server status and Unity connection
- GET /categories - List all tool categories
- GET /tools - Full tool registry
- GET /tools/category/:cat - List tools in category with inputSchema
- GET /tools/search?q=keyword - Search tools by name, description, parameters
- GET /tools/reference - Get ALL tools in Markdown format (TOKEN SAVER)
- GET /resources - List available resources (MCP-style)
- GET /resources/read?uri=synaptic://tools/reference - Read a resource
- POST /execute - Execute single tool (RECOMMENDED)
- POST /batch - Execute multiple tools at once (RECOMMENDED)

## Verify connection
curl http://localhost:${PORT}/health

## Tool discovery
curl http://localhost:${PORT}/categories
curl http://localhost:${PORT}/tools/category/scene

## Tool search
curl "http://localhost:${PORT}/tools/search?q=material"
curl "http://localhost:${PORT}/tools/search?q=color&category=Material&limit=10"

## Get full tools reference (RECOMMENDED at session start)
curl "http://localhost:${PORT}/tools/reference"
curl "http://localhost:${PORT}/tools/reference?format=compact"

## Single tool execution (RECOMMENDED)
curl -X POST http://localhost:${PORT}/execute -H "Content-Type: application/json" -d '{"tool":"unity_create_gameobject","params":{"name":"MyCube","type":"cube"}}'

## Batch execution (RECOMMENDED for multiple operations)
curl -X POST http://localhost:${PORT}/batch -H "Content-Type: application/json" -d '[
  {"tool":"unity_create_gameobject","params":{"name":"Cube1","type":"cube"}},
  {"tool":"unity_set_transform","params":{"name":"Cube1","position":"2,0,0"}},
  {"tool":"unity_create_gameobject","params":{"name":"Sphere1","type":"sphere"}}
]'

## If connection fails
- "Unity not connected" → Open Unity project with Synaptic AI Pro
- Connection refused → HTTP server not running (node http-server.js)

## Notes
- All responses are JSON
- Use /batch for multiple operations (more efficient)
- 30 second timeout per request
`;
}

// ===== Tools Reference Generation =====
function getToolsReference(format = 'markdown', category = null) {
    const tools = category
        ? (toolCategories[category] || [])
        : Object.entries(toolRegistry).map(([name, t]) => ({ name, ...t }));

    if (format === 'compact') {
        // Compact format: name | description (one line per tool)
        let output = '# Synaptic AI Pro - Tools Reference (Compact)\n\n';
        output += `Total: ${tools.length} tools\n\n`;

        const byCategory = {};
        for (const tool of tools) {
            const cat = tool.category || 'Other';
            if (!byCategory[cat]) byCategory[cat] = [];
            byCategory[cat].push(tool);
        }

        for (const [cat, catTools] of Object.entries(byCategory).sort()) {
            output += `## ${cat} (${catTools.length})\n`;
            for (const tool of catTools) {
                const desc = (tool.description || '').split('.')[0].substring(0, 80);
                output += `- ${tool.name}: ${desc}\n`;
            }
            output += '\n';
        }

        return output;
    }

    // Full markdown format with inputSchema
    let output = '# Synaptic AI Pro - Tools Reference\n\n';
    output += `Total: ${tools.length} tools\n\n`;

    const byCategory = {};
    for (const tool of tools) {
        const cat = tool.category || 'Other';
        if (!byCategory[cat]) byCategory[cat] = [];
        byCategory[cat].push(tool);
    }

    for (const [cat, catTools] of Object.entries(byCategory).sort()) {
        output += `## ${cat}\n\n`;
        for (const tool of catTools) {
            output += `### ${tool.name}\n`;
            output += `${tool.description || 'No description'}\n\n`;

            if (tool.inputSchema && tool.inputSchema.properties) {
                output += '**Parameters:**\n';
                for (const [param, schema] of Object.entries(tool.inputSchema.properties)) {
                    const required = (tool.inputSchema.required || []).includes(param) ? ' (required)' : '';
                    const desc = schema.description || '';
                    output += `- \`${param}\`${required}: ${desc}\n`;
                }
                output += '\n';
            }
        }
    }

    return output;
}

// ===== HTTP Routes =====

// Root - return prompt + full tools reference
app.get('/', (req, res) => {
    const prompt = getAIControlPrompt();
    const toolsRef = getToolsReference('markdown');
    res.type('text/plain').send(prompt + '\n\n---\n\n' + toolsRef);
});

// Prompt endpoint
app.get('/prompt', (req, res) => {
    const prompt = getAIControlPrompt();
    const toolsRef = getToolsReference('markdown');
    res.type('text/plain').send(prompt + '\n\n---\n\n' + toolsRef);
});

// Health check
app.get('/health', (req, res) => {
    res.json({
        status: 'ok',
        server: 'Synaptic AI Pro Unity HTTP Server',
        port: PORT,
        tools: Object.keys(toolRegistry).length,
        unityConnected: unitySocket !== null && unitySocket.readyState === WebSocket.OPEN,
        toolCount: Object.keys(toolRegistry).length,
        categoryCount: Object.keys(toolCategories).length
    });
});

// Categories list
app.get('/categories', (req, res) => {
    const categories = Object.entries(toolCategories)
        .map(([name, tools]) => ({ name, count: tools.length }))
        .sort((a, b) => a.name.localeCompare(b.name));
    res.json({ categories });
});

// Full tool registry
app.get('/tools', (req, res) => {
    res.json(toolRegistry);
});

// Tool list (Synaptic Code compatible)
app.get('/tools/list', (req, res) => {
    const toolNames = Object.keys(toolRegistry);
    res.json({
        tools: toolNames,
        count: toolNames.length
    });
});

// Tools by category
app.get('/tools/category/:category', (req, res) => {
    const category = req.params.category;
    const tools = toolCategories[category];

    if (!tools) {
        return res.status(404).json({
            error: `Category not found: ${category}`,
            available: Object.keys(toolCategories).sort()
        });
    }

    res.json({ category, count: tools.length, tools });
});

// Tool search
app.get('/tools/search', (req, res) => {
    const query = (req.query.q || '').toLowerCase();
    const categoryFilter = req.query.category;
    const limit = parseInt(req.query.limit) || 20;

    if (!query) {
        return res.status(400).json({ error: 'Missing query parameter: q' });
    }

    const results = [];
    const keywords = query.split(/\s+/);

    for (const [name, tool] of Object.entries(toolRegistry)) {
        if (categoryFilter && tool.category !== categoryFilter) continue;

        let score = 0;
        const searchText = `${name} ${tool.title || ''} ${tool.description || ''}`.toLowerCase();

        for (const kw of keywords) {
            if (name.toLowerCase().includes(kw)) score += 10;
            if ((tool.title || '').toLowerCase().includes(kw)) score += 5;
            if ((tool.description || '').toLowerCase().includes(kw)) score += 2;
        }

        if (score > 0) {
            results.push({ name, score, ...tool });
        }
    }

    results.sort((a, b) => b.score - a.score);
    const limited = results.slice(0, limit);

    res.json({ query, count: limited.length, total: results.length, results: limited });
});

// Tools reference (markdown)
app.get('/tools/reference', (req, res) => {
    const format = req.query.format || 'markdown';
    const category = req.query.category || null;
    const reference = getToolsReference(format, category);
    res.type('text/plain').send(reference);
});

// Resources list (MCP-style)
app.get('/resources', (req, res) => {
    res.json({
        resources: [
            {
                uri: 'synaptic://tools/reference',
                name: 'Tools Reference (Compact)',
                description: 'Complete list of all Unity tools in compact format (~30KB)',
                mimeType: 'text/markdown'
            },
            {
                uri: 'synaptic://tools/reference/full',
                name: 'Tools Reference (Full)',
                description: 'Complete list of all Unity tools with full parameter details (~100KB)',
                mimeType: 'text/markdown'
            }
        ]
    });
});

// Read resource
app.get('/resources/read', (req, res) => {
    const uri = req.query.uri;

    if (!uri) {
        return res.status(400).json({ error: 'Missing uri parameter' });
    }

    let content = null;
    let mimeType = 'text/markdown';

    if (uri === 'synaptic://tools/reference') {
        content = getToolsReference('compact');
    } else if (uri === 'synaptic://tools/reference/full') {
        content = getToolsReference('markdown');
    } else {
        return res.status(404).json({ error: `Unknown resource: ${uri}` });
    }

    res.json({
        contents: [
            { uri, mimeType, text: content }
        ]
    });
});

// Execute single tool
app.post('/execute', async (req, res) => {
    try {
        const { tool, params, timeout } = req.body;

        if (!tool) {
            return res.status(400).json({ error: 'Missing tool name' });
        }

        const result = await executeOnUnity(tool, params || {}, timeout || 30000);
        res.json(result);
    } catch (e) {
        res.status(500).json({ error: e.message });
    }
});

// Batch execute
app.post('/batch', async (req, res) => {
    try {
        const operations = req.body;

        if (!Array.isArray(operations)) {
            return res.status(400).json({ error: 'Body must be an array of operations' });
        }

        const results = [];
        for (const op of operations) {
            try {
                const result = await executeOnUnity(op.tool, op.params || {});
                results.push({ tool: op.tool, success: true, result });
            } catch (e) {
                results.push({ tool: op.tool, success: false, error: e.message });
            }
        }

        res.json({ count: results.length, results });
    } catch (e) {
        res.status(500).json({ error: e.message });
    }
});

// Legacy: POST /tool/:name
app.post('/tool/:name', async (req, res) => {
    try {
        const tool = req.params.name;
        const params = req.body || {};

        const result = await executeOnUnity(tool, params);
        res.json(result);
    } catch (e) {
        res.status(500).json({ error: e.message });
    }
});

// ===== Startup =====
server.listen(PORT, () => {
    // Setup WebSocket after server starts (attached to same port)
    setupWebSocket();

    console.error(`
╔═══════════════════════════════════════════════════════════╗
║  Synaptic AI Pro - HTTP Server                            ║
╠═══════════════════════════════════════════════════════════╣
║  Port: ${PORT.toString().padEnd(5)}                                              ║
║  HTTP:      http://localhost:${PORT.toString().padEnd(5)}                       ║
║  WebSocket: ws://localhost:${PORT.toString().padEnd(5)}                         ║
╠═══════════════════════════════════════════════════════════╣
║  Endpoints:                                               ║
║    GET  /           - AI prompt + tools reference         ║
║    GET  /health     - Connection status                   ║
║    GET  /tools      - Full tool registry                  ║
║    GET  /categories - Tool categories                     ║
║    POST /execute    - Execute tool                        ║
║    POST /batch      - Batch execute                       ║
╠═══════════════════════════════════════════════════════════╣
║  Waiting for Unity to connect...                          ║
╚═══════════════════════════════════════════════════════════╝
`);
});

// Graceful shutdown
process.on('SIGINT', () => {
    console.error('\n[HTTP] Shutting down...');
    if (wss) wss.close();
    server.close();
    process.exit(0);
});

process.on('SIGTERM', () => {
    console.error('\n[HTTP] Shutting down...');
    if (wss) wss.close();
    server.close();
    process.exit(0);
});
