/**
 * Synaptic AI Pro - Essential Mode MCP Server (v1.1.3)
 *
 * Lightweight server with 80 essential tools for Cursor/LM Studio
 * Optimized for reduced context size (62% smaller than full mode)
 *
 * Full mode: index.js (246 tools, 239KB)
 * Essential mode: index-essential.js (80 tools, 90KB)
 *
 * Generated from index.js by filter-essential-tools.py
 */

const express = require('express');
const http = require('http');
const WebSocket = require('ws');
const cors = require('cors');
const { z } = require('zod');
const { createServer } = require('./mcp-server');
const util = require('util');
const fs = require('fs');
const path = require('path');
const os = require('os');

const app = express();
app.use(cors());
app.use(express.json());

const server = http.createServer(app);
let wss = null; // 後で初期化

let unityWebSocket = null;
let mcpServer = null;
let desktopAppSocket = null; // デスクトップアプリ接続用
const BridgeHandler = require('./bridge-handler');
const bridgeHandler = new BridgeHandler();

// =====================================================
// Tool Registry (for list_categories / list_tools / search_tools / execute メタツール)
// =====================================================
let TOOL_REGISTRY_RAW = {};
let CATEGORIES = {};
let ALL_TOOLS = {};

function loadToolRegistry() {
    try {
        const registryPath = path.join(__dirname, 'tool-registry.json');
        const data = fs.readFileSync(registryPath, 'utf8');
        TOOL_REGISTRY_RAW = JSON.parse(data);

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
            const cleanName = toolName.replace(/^unity_/, '');
            CATEGORIES[category].tools[cleanName] = {
                fullName: toolName,
                title: toolData.title,
                description: toolData.description
            };
            ALL_TOOLS[cleanName] = {
                fullName: toolName,
                category: category,
                title: toolData.title,
                description: toolData.description
            };
        }

        console.error(`[Essential] Loaded ${Object.keys(TOOL_REGISTRY_RAW).length} tools from tool-registry.json`);
    } catch (err) {
        console.error('[Essential] Failed to load tool-registry.json:', err.message);
    }
}
loadToolRegistry();

// Unity WebSocket接続の管理（関数として定義）
function setupWebSocketHandlers() {
    if (!wss) return;
    
    wss.on('connection', (ws, req) => {
        // 接続タイプを判定
        const isUnity = req.headers['x-client-type'] === 'unity' || req.url === '/unity';
        const isMCP = req.headers['x-client-type'] === 'mcp' || req.url === '/mcp';
        
        if (isMCP) {
            // デスクトップアプリ接続
            if (desktopAppSocket) {
                desktopAppSocket.close();
            }
            desktopAppSocket = ws;
            
            // ブリッジハンドラーに接続を設定
            bridgeHandler.setDesktopConnection(ws);
            
            ws.on('message', async (message) => {
                try {
                    const data = JSON.parse(message);
                    
                    // デスクトップアプリからのメッセージをUnityに転送
                    if (data.type === 'chat_response' && unityWebSocket) {
                        unityWebSocket.send(JSON.stringify({
                            type: 'assistant_message',
                            content: data.content
                        }));
                    }
                    
                    // ツール実行命令をUnityに転送
                    if (data.type === 'execute_tool' && unityWebSocket) {
                        unityWebSocket.send(JSON.stringify({
                            type: 'tool_call',
                            command: data.tool,
                            parameters: data.parameters,
                            id: data.id
                        }));
                    }
                    
                    // ブリッジハンドラーも使用（追加機能）
                    if (data.type === 'assistant_response') {
                        bridgeHandler.forwardToUnity(data);
                    }
                } catch (e) {
                    // console.error('MCP message error:', e);
                }
            });
            
            ws.on('close', () => {
                desktopAppSocket = null;
                bridgeHandler.handleDisconnect('desktop');
            });
            
            return;
        }
        
        // Unity接続処理
        // 古い接続をクリーンアップ
        if (unityWebSocket) {
            unityWebSocket.close();
        }
        
        // Unity接続（ログを出力しない）
        unityWebSocket = ws;
        
        // ブリッジハンドラーにも設定
        bridgeHandler.setUnityConnection(ws);
        
        // デバッグ用：ファイルに記録 (cross-platform safe)
        try {
            const debugLogPath = path.join(os.tmpdir(), 'mcp-debug.log');
            fs.appendFileSync(debugLogPath, `[${new Date().toISOString()}] Unity connected\n`);
        } catch (e) {
            // Avoid crashing on environments without /tmp (e.g., Windows)
        }

        ws.on('message', async (message) => {
            try {
                const data = JSON.parse(message);
                
                // Unity内チャットメッセージをデスクトップアプリに転送
                if (data.type === 'chat_message') {
                    if (desktopAppSocket) {
                        desktopAppSocket.send(JSON.stringify({
                            type: 'user_message',
                            content: data.message,
                            context: {
                                source: 'unity',
                                project: data.projectName || 'Unity Project'
                            }
                        }));
                    }
                    
                    // ブリッジハンドラーも使用（会話履歴管理）
                    bridgeHandler.forwardToDesktop({
                        content: data.message,
                        projectName: data.projectName
                    });
                    return;
                }
                
                if (data.type === 'operation_result' && data.id) {
                    // idを数値に変換して検索
                    const numericId = typeof data.id === 'string' ? parseInt(data.id) : data.id;
                    
                    if (pendingRequests.has(numericId)) {
                        const { resolve, reject, timeout } = pendingRequests.get(numericId);
                        clearTimeout(timeout);
                        pendingRequests.delete(numericId);
                        
                        // Unity側は content フィールドに結果を格納し、data.success で成功/失敗を示す
                        if (data.data && data.data.success) {
                            resolve(data.content);
                        } else {
                            reject(new Error(data.content || 'Unity command failed'));
                        }
                    }
                }
            } catch (e) {
                // エラーログを出力しない
            }
        });

        ws.on('close', () => {
            // Unity切断（ログを出力しない）
            unityWebSocket = null;
            bridgeHandler.handleDisconnect('unity');
        });

        ws.on('error', (error) => {
            // WebSocketエラー（ログを出力しない）
        });
    });
}

// Unityコマンド送信用のヘルパー関数
const pendingRequests = new Map();
let requestId = 0;

// Helper function for delay
function sleep(ms) {
    return new Promise(resolve => setTimeout(resolve, ms));
}

// Single command attempt
async function sendUnityCommandOnce(command, params, id) {
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
            parameters: params
        });
        unityWebSocket.send(message);
    });
}

// Main command sender with auto-retry (enhanced for compilation waiting)
async function sendUnityCommand(command, params = {}) {
    const MAX_RETRIES = 30;          // 30 retries for very long compilations
    const RETRY_DELAY = 10000;       // 10 seconds between retries
    const COMPILE_WAIT_DELAY = 10000; // 10 seconds when compiling (total max: ~5 min)

    let lastError = null;
    let isLikelyCompiling = false;

    for (let attempt = 1; attempt <= MAX_RETRIES; attempt++) {
        if (!unityWebSocket || unityWebSocket.readyState !== WebSocket.OPEN) {
            lastError = new Error('Unity not connected');
            isLikelyCompiling = true;

            if (attempt < MAX_RETRIES) {
                const waitTime = isLikelyCompiling ? COMPILE_WAIT_DELAY : RETRY_DELAY;
                console.error(`[MCP] Unity not connected (attempt ${attempt}/${MAX_RETRIES}). Waiting ${waitTime/1000}s...`);
                await sleep(waitTime);
                continue;
            }
            break;
        }

        const id = ++requestId;

        try {
            const result = await sendUnityCommandOnce(command, params, id);
            if (attempt > 1) {
                if (typeof result === 'string') {
                    return result + `\n[Note: Succeeded on retry attempt ${attempt}]`;
                } else if (typeof result === 'object' && result !== null) {
                    result._retryInfo = `Succeeded on attempt ${attempt}`;
                }
            }
            return result;
        } catch (error) {
            lastError = error;
            if (error.message === 'timeout') {
                isLikelyCompiling = true;
            }
            if (attempt < MAX_RETRIES) {
                const waitTime = isLikelyCompiling ? COMPILE_WAIT_DELAY : RETRY_DELAY;
                console.error(`[MCP] Command failed (attempt ${attempt}/${MAX_RETRIES}): ${error.message}. Retrying in ${waitTime/1000}s...`);
                await sleep(waitTime);
            }
        }
    }

    throw new Error(`Unity not connected (after ${MAX_RETRIES} attempts). Unity may be recompiling - wait and try again.`);
}

// MCP サーバーの設定
async function setupMCPServer() {
    mcpServer = createServer();

    // ===== GameObject基本操作 =====
    mcpServer.registerTool('unity_create_gameobject', {
        title: 'Create GameObject',
        description: 'Create a new GameObject in Unity scene',
        inputSchema: z.object({
            name: z.string().describe('Name of the GameObject'),
            type: z.enum(['empty', 'cube', 'sphere', 'cylinder', 'plane', 'capsule', 'quad']).optional().default('empty').describe('Type of primitive to create'),
            parent: z.string().optional().describe('Parent GameObject name'),
            position: z.object({
                x: z.number(),
                y: z.number(),
                z: z.number()
            }).optional()
        })
    }, async (params) => {
        try {
            const result = await sendUnityCommand('create_gameobject', params);
            
            // MCPの仕様に従って、必ずcontent配列を返す
            return {
                content: [{
                    type: 'text',
                    text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
                }]
            };
        } catch (error) {
            throw error;
        }
    });

    mcpServer.registerTool('unity_update_gameobject', {
        title: 'Update GameObject',
        description: 'Update properties of an existing GameObject',
        inputSchema: z.object({
            name: z.string().describe('Name of the GameObject'),
            newName: z.string().optional(),
            active: z.boolean().optional(),
            tag: z.string().optional(),
            layer: z.number().optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('update_gameobject', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_delete_gameobject', {
        title: 'Delete GameObject',
        description: 'Delete a GameObject from the scene',
        inputSchema: z.object({
            name: z.string().describe('Name of the GameObject to delete')
        })
    }, async (params) => {
        const result = await sendUnityCommand('delete_gameobject', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_set_transform', {
        title: 'Set Transform',
        description: 'Set position, rotation, and scale of a GameObject',
        inputSchema: z.object({
            gameObject: z.string().describe('GameObject name'),
            position: z.object({
                x: z.number(),
                y: z.number(),
                z: z.number()
            }).optional(),
            rotation: z.object({
                x: z.number(),
                y: z.number(),
                z: z.number()
            }).optional().describe('Euler angles in degrees'),
            scale: z.object({
                x: z.number(),
                y: z.number(),
                z: z.number()
            }).optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('set_transform', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_add_component', {
        title: 'Add Component',
        description: 'Add a component to a GameObject',
        inputSchema: z.object({
            gameObject: z.string().describe('GameObject name'),
            component: z.string().describe('Component type (e.g., Rigidbody, BoxCollider)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('add_component', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_update_component', {
        title: 'Update Component',
        description: 'Update component properties',
        inputSchema: z.object({
            gameObject: z.string().describe('GameObject name'),
            component: z.string().describe('Component type'),
            properties: z.union([
                z.record(z.any()),
                z.string()
            ]).describe('Properties to update (JSON object or string)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('update_component', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_ui', {
        title: 'Create UI Element',
        description: 'Create UI elements in Unity',
        inputSchema: z.object({
            type: z.enum(['Canvas', 'Button', 'Text', 'Image', 'Slider', 'Toggle', 'InputField', 'Panel']),
            name: z.string(),
            parent: z.string().optional(),
            position: z.object({
                x: z.number(),
                y: z.number()
            }).optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_ui', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_setup_camera', {
        title: 'Setup Camera',
        description: 'Setup camera in the scene',
        inputSchema: z.object({
            name: z.string().optional().default('Main Camera'),
            position: z.object({
                x: z.number(),
                y: z.number(),
                z: z.number()
            }),
            lookAt: z.object({
                x: z.number(),
                y: z.number(),
                z: z.number()
            }).optional(),
            fieldOfView: z.number().optional().default(60),
            cameraType: z.enum(['Perspective', 'Orthographic']).optional().default('Perspective')
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_camera', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_virtual_camera', {
        title: 'Create Cinemachine Virtual Camera',
        description: 'Create a Cinemachine Virtual Camera with follow and look at targets',
        inputSchema: z.object({
            name: z.string().describe('Name of the virtual camera'),
            priority: z.number().optional().default(10).describe('Camera priority (higher = more important)'),
            follow: z.string().optional().describe('GameObject to follow'),
            lookAt: z.string().optional().describe('GameObject to look at'),
            position: z.object({
                x: z.number(),
                y: z.number(),
                z: z.number()
            }).optional().describe('Camera position'),
            fov: z.number().optional().default(60).describe('Field of view'),
            nearClipPlane: z.number().optional().describe('Near clip plane distance'),
            farClipPlane: z.number().optional().describe('Far clip plane distance'),
            dutch: z.number().optional().describe('Camera roll/tilt angle in degrees'),
            orthographic: z.boolean().optional().describe('Use orthographic projection'),
            orthographicSize: z.number().optional().describe('Orthographic camera size'),
            bodyType: z.enum(['Transposer', 'FramingTransposer', 'OrbitalTransposer', 'HardLockToTarget', 'DoNothing']).optional().default('Transposer').describe('Body component type for follow behavior'),
            aimType: z.enum(['Composer', 'GroupComposer', 'POV', 'SameAsFollowTarget', 'HardLookAt', 'DoNothing']).optional().default('Composer').describe('Aim component type for look at behavior'),
            damping: z.object({
                x: z.number().optional(),
                y: z.number().optional(),
                z: z.number().optional()
            }).optional().describe('Follow damping values'),
            offset: z.object({
                x: z.number().optional(),
                y: z.number().optional(),
                z: z.number().optional()
            }).optional().describe('Follow offset'),
            screenX: z.number().optional().describe('Screen X position for framing (0-1)'),
            screenY: z.number().optional().describe('Screen Y position for framing (0-1)'),
            deadZoneWidth: z.number().optional().describe('Dead zone width for Composer'),
            deadZoneHeight: z.number().optional().describe('Dead zone height for Composer'),
            horizontalAxis: z.number().optional().describe('Horizontal axis speed for POV'),
            verticalAxis: z.number().optional().describe('Vertical axis speed for POV'),
            noiseProfile: z.enum(['6D Shake', 'Handheld_normal', 'Handheld_normal_mild', 'Handheld_tele_mild', 'Handheld_wide_mild']).optional().describe('Camera shake noise profile'),
            noiseAmplitude: z.number().optional().describe('Noise amplitude/intensity (0-2)'),
            noiseFrequency: z.number().optional().describe('Noise frequency (0-2)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_virtual_camera', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_freelook_camera', {
        title: 'Create Cinemachine FreeLook Camera',
        description: 'Create a Cinemachine FreeLook Camera for third-person orbiting. REQUIRED: "follow" parameter must specify an existing GameObject name in the scene. The target GameObject must exist before creating the camera. Example: unity_create_freelook_camera({name: "PlayerCamera", follow: "Player"}). Will fail if follow target does not exist.',
        inputSchema: z.object({
            name: z.string().describe('Name of the FreeLook camera (required)'),
            follow: z.string().describe('GameObject name to follow and orbit around (REQUIRED - must exist in scene before calling this tool)'),
            lookAt: z.string().optional().describe('GameObject to look at (defaults to follow target)'),
            topRigHeight: z.number().optional().default(4.5).describe('Top rig height offset'),
            topRigRadius: z.number().optional().default(1.75).describe('Top rig radius'),
            middleRigHeight: z.number().optional().default(2.5).describe('Middle rig height offset'),
            middleRigRadius: z.number().optional().default(3).describe('Middle rig radius'),
            bottomRigHeight: z.number().optional().default(0.4).describe('Bottom rig height offset'),
            bottomRigRadius: z.number().optional().default(1.3).describe('Bottom rig radius'),
            xAxisSpeed: z.number().optional().default(300).describe('Horizontal rotation speed'),
            yAxisSpeed: z.number().optional().default(2).describe('Vertical rotation speed'),
            priority: z.number().optional().default(10).describe('Camera priority')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_freelook_camera', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_setup_cinemachine_brain', {
        title: 'Setup Cinemachine Brain',
        description: 'Setup Cinemachine Brain on a camera (required for Cinemachine to work)',
        inputSchema: z.object({
            camera: z.string().optional().default('Main Camera').describe('Camera to add Brain to'),
            blendStyle: z.enum(['Cut', 'EaseInOut', 'EaseIn', 'EaseOut', 'HardIn', 'HardOut', 'Linear']).optional().default('EaseInOut').describe('Default blend style'),
            defaultBlendTime: z.number().optional().default(2).describe('Default blend time in seconds'),
            updateMethod: z.enum(['FixedUpdate', 'LateUpdate', 'SmartUpdate']).optional().default('SmartUpdate').describe('Brain update method')
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_cinemachine_brain', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_update_virtual_camera', {
        title: 'Update Virtual Camera Settings',
        description: 'Update settings of an existing Cinemachine Virtual Camera',
        inputSchema: z.object({
            camera: z.string().describe('Virtual camera name'),
            priority: z.number().optional().describe('New priority'),
            follow: z.string().optional().describe('New follow target'),
            lookAt: z.string().optional().describe('New look at target'),
            fov: z.number().optional().describe('New field of view'),
            nearClipPlane: z.number().optional().describe('Near clip plane distance'),
            farClipPlane: z.number().optional().describe('Far clip plane distance'),
            dutch: z.number().optional().describe('Camera roll/tilt angle in degrees'),
            orthographic: z.boolean().optional().describe('Use orthographic projection'),
            orthographicSize: z.number().optional().describe('Orthographic camera size'),
            bodyType: z.enum(['Transposer', 'FramingTransposer', 'OrbitalTransposer', 'HardLockToTarget', 'DoNothing']).optional().describe('Body component type'),
            aimType: z.enum(['Composer', 'GroupComposer', 'POV', 'SameAsFollowTarget', 'HardLookAt', 'DoNothing']).optional().describe('Aim component type'),
            damping: z.object({
                x: z.number().optional(),
                y: z.number().optional(),
                z: z.number().optional()
            }).optional().describe('New damping values'),
            noiseProfile: z.enum(['6D Shake', 'Handheld_normal', 'Handheld_normal_mild', 'Handheld_tele_mild', 'Handheld_wide_mild']).optional().describe('Camera shake noise profile'),
            noiseAmplitude: z.number().optional().describe('Noise amplitude/intensity (0-2)'),
            noiseFrequency: z.number().optional().describe('Noise frequency (0-2)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('update_virtual_camera', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_dolly_track', {
        title: 'Create Cinemachine Dolly Track',
        description: 'Create a Cinemachine Dolly Track with waypoints for camera path animation',
        inputSchema: z.object({
            name: z.string().describe('Name of the dolly track'),
            waypoints: z.array(z.object({
                x: z.number(),
                y: z.number(),
                z: z.number()
            })).describe('Array of waypoint positions'),
            looped: z.boolean().optional().default(false).describe('Whether the path loops'),
            cameraName: z.string().optional().describe('Virtual camera to attach to track')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_dolly_track', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_add_collider_extension', {
        title: 'Add Cinemachine Collider Extension',
        description: 'Add Collider Extension to Virtual Camera for obstacle avoidance',
        inputSchema: z.object({
            camera: z.string().describe('Virtual camera name'),
            minimumDistanceFromTarget: z.number().optional().default(0.2).describe('Minimum distance from follow target'),
            avoidObstacles: z.boolean().optional().default(true).describe('Enable obstacle avoidance'),
            distanceLimit: z.number().optional().default(10).describe('Maximum raycast distance'),
            smoothingTime: z.number().optional().default(0).describe('Smoothing time for position changes'),
            damping: z.number().optional().default(0).describe('Damping for position changes')
        })
    }, async (params) => {
        const result = await sendUnityCommand('add_collider_extension', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_set_camera_priority', {
        title: 'Set Cinemachine Camera Priority',
        description: 'Change the priority of a Cinemachine virtual camera',
        inputSchema: z.object({
            camera: z.string().describe('Name of the Cinemachine camera'),
            priority: z.number().describe('New priority value (higher priority cameras take precedence)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('set_camera_priority', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_set_camera_enabled', {
        title: 'Enable/Disable Cinemachine Camera',
        description: 'Enable or disable a Cinemachine virtual camera',
        inputSchema: z.object({
            camera: z.string().describe('Name of the Cinemachine camera'),
            enabled: z.boolean().describe('Enable (true) or disable (false) the camera')
        })
    }, async (params) => {
        const result = await sendUnityCommand('set_camera_enabled', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_update_camera_target', {
        title: 'Update Cinemachine Camera Target',
        description: 'Update Follow and/or LookAt targets of a Cinemachine camera',
        inputSchema: z.object({
            camera: z.string().describe('Name of the Cinemachine camera'),
            follow: z.string().optional().describe('New Follow target (empty string to clear)'),
            lookAt: z.string().optional().describe('New LookAt target (empty string to clear)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('update_camera_target', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_update_brain_blend_settings', {
        title: 'Update Cinemachine Brain Blend Settings',
        description: 'Update the default blend settings on the Cinemachine Brain',
        inputSchema: z.object({
            defaultBlendTime: z.number().optional().describe('Default blend duration in seconds'),
            defaultBlendStyle: z.enum(['Cut', 'EaseInOut', 'EaseIn', 'EaseOut', 'HardIn', 'HardOut', 'Linear']).optional().describe('Default blend curve style'),
            customBlendsAsset: z.string().optional().describe('Path to custom blends asset')
        })
    }, async (params) => {
        const result = await sendUnityCommand('update_brain_blend_settings', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_get_active_camera_info', {
        title: 'Get Active Cinemachine Camera Info',
        description: 'Get information about the currently active Cinemachine virtual camera',
        inputSchema: z.object({})
    }, async (params) => {
        const result = await sendUnityCommand('get_active_camera_info', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_setup_lighting', {
        title: 'Setup Lighting',
        description: 'Setup lighting in the scene with extended options and presets',
        inputSchema: z.object({
            preset: z.enum(['studio', 'sunset', 'night', 'overcast', 'desert', 'forest', 'underwater', 'space', 'neon']).optional(),
            ambientMode: z.enum(['skybox', 'trilight', 'flat', 'custom']).optional(),
            ambientIntensity: z.number().min(0).max(2).optional(),
            ambientSkyColor: z.string().optional(),
            ambientEquatorColor: z.string().optional(),
            ambientGroundColor: z.string().optional(),
            fogEnabled: z.boolean().optional(),
            fogMode: z.enum(['linear', 'exponential', 'exponentialsquared']).optional(),
            fogColor: z.string().optional(),
            fogDensity: z.number().min(0).max(1).optional(),
            fogStartDistance: z.number().min(0).max(1000).optional(),
            fogEndDistance: z.number().min(0).max(1000).optional(),
            directionalLightIntensity: z.number().min(0).max(8).optional(),
            directionalLightColor: z.string().optional(),
            directionalLightShadows: z.enum(['none', 'hard', 'soft']).optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_lighting', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_material', {
        title: 'Create Material',
        description: 'Create a new material',
        inputSchema: z.object({
            name: z.string(),
            shader: z.string().optional().default('Standard'),
            color: z.object({
                r: z.number().min(0).max(1),
                g: z.number().min(0).max(1),
                b: z.number().min(0).max(1),
                a: z.number().min(0).max(1).optional().default(1)
            }).optional(),
            metallic: z.number().min(0).max(1).optional(),
            smoothness: z.number().min(0).max(1).optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_material', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_prefab', {
        title: 'Create Prefab',
        description: 'Create a prefab from GameObject',
        inputSchema: z.object({
            gameObject: z.string().describe('GameObject to convert to prefab'),
            path: z.string().describe('Save path for the prefab')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_prefab', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_load_scene', {
        title: 'Load Scene',
        description: '🎬 Load a scene in Editor mode. Supports Single (replace current) or Additive (load alongside current) modes. Can load by path or name.',
        inputSchema: z.object({
            scenePath: z.string().optional().describe('Scene path (e.g., Assets/Scenes/Game.unity)'),
            sceneName: z.string().optional().describe('Scene name (e.g., Game) - will search in Assets'),
            mode: z.enum(['Single', 'Additive']).optional().default('Single').describe('Load mode: Single (replace) or Additive (add)'),
            setActive: z.boolean().optional().default(true).describe('Set as active scene after loading (Additive mode only)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('load_scene', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_unload_scene', {
        title: 'Unload Scene',
        description: '🗑️ Unload a scene from the Editor. Useful for managing multi-scene setups. Cannot unload the last remaining scene.',
        inputSchema: z.object({
            sceneName: z.string().describe('Name of the scene to unload'),
            removeUnsavedChanges: z.boolean().optional().default(false).describe('Remove without saving changes')
        })
    }, async (params) => {
        const result = await sendUnityCommand('unload_scene', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_set_active_scene', {
        title: 'Set Active Scene',
        description: '⭐ Set the active scene in multi-scene editing. The active scene is where new GameObjects are created by default.',
        inputSchema: z.object({
            sceneName: z.string().describe('Name of the scene to set as active')
        })
    }, async (params) => {
        const result = await sendUnityCommand('set_active_scene', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_list_all_scenes', {
        title: 'List All Scenes',
        description: '📋 List all scene files in the project. Returns scene paths, names, and build settings info. Supports filtering and sorting.',
        inputSchema: z.object({
            includeInactive: z.boolean().optional().default(true).describe('Include scenes not in build settings'),
            sortBy: z.enum(['name', 'path', 'buildIndex']).optional().default('name').describe('Sort order'),
            searchPath: z.string().optional().default('Assets').describe('Directory to search (e.g., Assets/Scenes)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('list_all_scenes', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_add_scene_to_build', {
        title: 'Add Scene to Build Settings',
        description: '⚙️ Add or remove scenes from Build Settings. Manage build order and enable/disable scenes.',
        inputSchema: z.object({
            scenePath: z.string().describe('Scene path (e.g., Assets/Scenes/Game.unity)'),
            operation: z.enum(['add', 'remove', 'enable', 'disable']).optional().default('add').describe('Operation type'),
            buildIndex: z.number().optional().describe('Build index (for add operation, -1 = append)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('add_scene_to_build', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_setup_physics', {
        title: 'Setup Physics',
        description: 'Setup physics settings for a GameObject or global physics',
        inputSchema: z.object({
            target: z.string().describe('Target GameObject name (or "global" for global physics settings)').optional(),
            rigidbody: z.boolean().describe('Add Rigidbody component').optional(),
            mass: z.number().describe('Rigidbody mass').optional(),
            gravity: z.union([
                z.boolean(),
                z.string(), // JSON文字列形式も受け入れる
                z.object({
                    x: z.number(),
                    y: z.number(),
                    z: z.number()
                })
            ]).describe('Use gravity (bool), JSON string, or global gravity vector').optional(),
            collider: z.string().describe('Type of collider to add: box, sphere, capsule, mesh').optional(),
            defaultMaterial: z.string().optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_physics', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_particle_system', {
        title: 'Create Particle System',
        description: 'Create a particle system with extended presets and customization options',
        inputSchema: z.object({
            name: z.string(),
            preset: z.enum(['fire', 'smoke', 'sparkle', 'rain', 'explosion', 'snow', 'magic', 'lightning', 'tornado', 'galaxy']).optional(),
            lifetime: z.number().min(0.1).max(10).optional(),
            startSpeed: z.number().min(0).max(100).optional(),
            startSize: z.number().min(0.01).max(10).optional(),
            emission: z.number().min(1).max(1000).optional(),
            gravity: z.number().min(-10).max(10).optional(),
            shape: z.enum(['cone', 'sphere', 'box', 'circle', 'edge', 'mesh']).optional(),
            usePhysics: z.boolean().optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_particle_system', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_setup_material', {
        title: 'Setup Material',
        description: 'Create or modify materials with PBR properties and shaders',
        inputSchema: z.object({
            targetObject: z.string(),
            materialName: z.string().optional(),
            shader: z.enum(['Standard', 'Unlit', 'Toon', 'Water', 'Glass', 'Hologram']).optional(),
            color: z.string().optional(),
            metallic: z.number().min(0).max(1).optional(),
            smoothness: z.number().min(0).max(1).optional(),
            emission: z.boolean().optional(),
            emissionColor: z.string().optional(),
            transparency: z.number().min(0).max(1).optional(),
            normalMap: z.boolean().optional(),
            texture: z.string().optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_material', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_setup_navmesh', {
        title: 'Setup NavMesh',
        description: 'Setup navigation mesh',
        inputSchema: z.object({
            agentRadius: z.number().optional().default(0.5),
            agentHeight: z.number().optional().default(2.0),
            maxSlope: z.number().optional().default(45),
            stepHeight: z.number().optional().default(0.4)
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_navmesh', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_audio_source', {
        title: 'Create Audio Source',
        description: 'Create and configure an AudioSource component on a GameObject',
        inputSchema: z.object({
            gameObject: z.string(),
            audioClip: z.string().optional(),
            playOnAwake: z.boolean().optional().default(true),
            loop: z.boolean().optional().default(false),
            volume: z.number().min(0).max(1).optional().default(1),
            pitch: z.number().min(-3).max(3).optional().default(1),
            spatialMode: z.enum(['2D', '3D']).optional().default('3D'),
            minDistance: z.number().optional().default(1),
            maxDistance: z.number().optional().default(500)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_audio_source', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_setup_3d_audio', {
        title: 'Setup 3D Audio',
        description: 'Configure 3D spatial audio settings for an AudioSource',
        inputSchema: z.object({
            audioSource: z.string(),
            dopplerLevel: z.number().min(0).max(5).optional().default(1),
            spread: z.number().min(0).max(360).optional().default(0),
            volumeRolloff: z.enum(['Logarithmic', 'Linear', 'Custom']).optional().default('Logarithmic'),
            minDistance: z.number().optional().default(1),
            maxDistance: z.number().optional().default(500),
            customCurve: z.boolean().optional().default(false)
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_3d_audio', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_particle_preset', {
        title: 'Create Particle Preset',
        description: 'Create advanced particle effects with presets',
        inputSchema: z.object({
            effectName: z.string(),
            preset: z.enum(['Fire', 'Smoke', 'Explosion', 'Rain', 'Snow', 'Sparkles', 'Magic', 'Lightning', 'Tornado', 'Galaxy', 'Custom']).default('Fire'),
            customSettings: z.object({
                lifetime: z.number().optional().default(5),
                startSpeed: z.number().optional().default(5),
                startSize: z.number().optional().default(1),
                emission: z.number().optional().default(10),
                gravity: z.number().optional().default(0),
                shape: z.enum(['Sphere', 'Cone', 'Box', 'Circle', 'Edge', 'Mesh']).optional().default('Cone')
            }).optional(),
            colors: z.array(z.string()).optional(),
            usePhysics: z.boolean().optional().default(false)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_particle_preset', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_setup_lighting_preset', {
        title: 'Setup Lighting Preset',
        description: 'Apply professional lighting presets to the scene',
        inputSchema: z.object({
            preset: z.enum(['Studio', 'Sunset', 'Night', 'Overcast', 'Desert', 'Forest', 'Underwater', 'Space', 'Neon', 'Custom']).default('Studio'),
            intensity: z.number().optional().default(1),
            ambientMode: z.enum(['Skybox', 'Gradient', 'Color']).optional().default('Skybox'),
            fog: z.boolean().optional().default(false),
            fogSettings: z.object({
                color: z.string().optional().default('#CCCCCC'),
                density: z.number().optional().default(0.01),
                start: z.number().optional().default(0),
                end: z.number().optional().default(300)
            }).optional(),
            shadows: z.enum(['None', 'Hard', 'Soft']).optional().default('Soft')
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_lighting_preset', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_skybox_from_image', {
        title: 'Create Skybox from Image',
        description: 'Create a Skybox from image(s). Supports: (1) Panoramic/HDRI - 360° image, (2) 6-Sided - six faces, (3) Sphere/Dome - regular landscape photo on inverted sphere',
        inputSchema: z.object({
            type: z.enum(['panoramic', 'hdri', '6sided', 'cubemap', 'sphere', 'dome', 'landscape']).optional().default('panoramic'),
            imagePath: z.string().optional().describe('Path to image'),
            front: z.string().optional(),
            back: z.string().optional(),
            left: z.string().optional(),
            right: z.string().optional(),
            up: z.string().optional(),
            down: z.string().optional(),
            materialName: z.string().optional().default('CustomSkybox'),
            exposure: z.number().optional().default(1.0),
            rotation: z.number().optional().default(0),
            applyToScene: z.boolean().optional().default(true),
            radius: z.number().optional().default(500).describe('Sphere radius (for sphere type)'),
            followCamera: z.boolean().optional().default(true).describe('Follow camera (for sphere type)'),
            objectName: z.string().optional().default('SkySphere'),
            applyToCamera: z.boolean().optional().default(true).describe('Apply to MainCamera skybox'),
            applyToScene: z.boolean().optional().default(false).describe('Apply to scene RenderSettings')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_skybox_from_image', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_setup_reflection_probe', {
        title: 'Setup Reflection Probe',
        description: 'Setup reflection probes for realistic reflections',
        inputSchema: z.object({
            probeName: z.string().optional().default('ReflectionProbe'),
            position: z.object({ x: z.number(), y: z.number(), z: z.number() }).optional(),
            size: z.object({ x: z.number(), y: z.number(), z: z.number() }).optional().default({ x: 10, y: 10, z: 10 }),
            resolution: z.enum(['16', '32', '64', '128', '256', '512', '1024', '2048']).optional().default('128'),
            updateMode: z.enum(['OnAwake', 'EveryFrame', 'ViaScripting']).optional().default('OnAwake'),
            importance: z.number().optional().default(1)
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_reflection_probe', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_light_probe_group', {
        title: 'Create Light Probe Group',
        description: 'Create light probe groups for dynamic GI',
        inputSchema: z.object({
            groupName: z.string().optional().default('LightProbeGroup'),
            gridSize: z.object({ x: z.number(), y: z.number(), z: z.number() }).optional().default({ x: 5, y: 3, z: 5 }),
            spacing: z.number().optional().default(2),
            center: z.object({ x: z.number(), y: z.number(), z: z.number() }).optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_light_probe_group', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_screen_shake', {
        title: 'Create Screen Shake',
        description: 'Apply screen shake effect for dramatic impact',
        inputSchema: z.object({
            duration: z.number().optional().default(0.5),
            intensity: z.number().optional().default(1),
            frequency: z.number().optional().default(10),
            damping: z.number().optional().default(1),
            camera: z.string().optional().default('Main Camera')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_screen_shake', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_screen_fade', {
        title: 'Create Screen Fade',
        description: 'Create fade in/out transitions',
        inputSchema: z.object({
            fadeType: z.enum(['FadeIn', 'FadeOut']).default('FadeIn'),
            duration: z.number().optional().default(1),
            color: z.string().optional().default('#000000'),
            delay: z.number().optional().default(0),
            curve: z.enum(['Linear', 'EaseIn', 'EaseOut', 'EaseInOut']).optional().default('Linear')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_screen_fade', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_search', {
        title: 'Search Objects',
        description: 'Search for objects in the scene',
        inputSchema: z.object({
            searchType: z.enum(['name', 'tag', 'layer', 'component']),
            query: z.string(),
            includeInactive: z.boolean().optional().default(false)
        })
    }, async (params) => {
        const result = await sendUnityCommand('UNITY_SEARCH', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_get_scene_info', {
        title: 'Get Scene Information',
        description: 'Get comprehensive information about the current Unity scene including hierarchy, statistics, lighting, and cameras. Now includes detailed Transform data (world/local position, rotation, scale) and RectTransform data (anchors, pivot, sizeDelta, rect) for all GameObjects. WARNING: Can return large response (>1MB) for complex scenes. For large scenes, use unity_get_scene_summary instead.',
        inputSchema: z.object({})
    }, async (params) => {
        const result = await sendUnityCommand('GET_SCENE_INFO', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_get_scene_summary', {
        title: 'Get Scene Summary (Lightweight)',
        description: 'Get lightweight scene overview (<200KB). Returns: scene name, GameObject count, cameras, lights, root GameObjects list (max 50). Recommended for large scenes instead of unity_get_scene_info.',
        inputSchema: z.object({})
    }, async (params) => {
        const result = await sendUnityCommand('GET_SCENE_SUMMARY', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_get_gameobjects_list', {
        title: 'Get Filtered GameObjects List',
        description: 'Get filtered list of GameObjects (<50KB). Returns GameObject names, IDs, paths, layers, tags. Supports filters: layer, tag, name (contains), activeOnly. Max 100 results.',
        inputSchema: z.object({
            layerFilter: z.string().optional().describe('Filter by layer name (e.g. "UI", "Default")'),
            tagFilter: z.string().optional().describe('Filter by tag (e.g. "Player", "Enemy")'),
            nameFilter: z.string().optional().describe('Filter by name (contains, case-sensitive)'),
            activeOnly: z.boolean().optional().describe('Only return active GameObjects'),
            maxCount: z.number().optional().default(100).describe('Maximum number of results (default: 100)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('GET_GAMEOBJECTS_LIST', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_get_gameobject_detail', {
        title: 'Get GameObject Detailed Information',
        description: 'Get detailed information for a specific GameObject (<10KB). Returns: transform, components with details, children, parent. Find GameObject by name or instanceId (use unity_get_gameobjects_list first to get IDs).',
        inputSchema: z.object({
            nameOrId: z.string().describe('GameObject name or instanceId (from unity_get_gameobjects_list)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('GET_GAMEOBJECT_DETAIL', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_get_scene_changes_since', {
        title: 'Get Incremental Scene Changes',
        description: 'Get scene changes since a timestamp. Returns: added, removed, modified GameObjects. Use for efficient scene monitoring. On first call or if timestamp too old, returns full scene summary.',
        inputSchema: z.object({
            timestamp: z.string().describe('ISO 8601 timestamp (e.g. "2025-01-15T12:00:00Z"). Use current time on first call, then use returned timestamp for next call.')
        })
    }, async (params) => {
        const result = await sendUnityCommand('GET_SCENE_CHANGES_SINCE', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_capture_game_view', {
        title: 'Capture Game View Screenshot',
        description: 'Capture a screenshot of the Unity Game View including all Canvas/UI elements. WORKFLOW: 1) Call this tool → returns "status: pending" if Play mode transition needed. 2) DO NOT CALL ANY OTHER TOOLS. WAIT EXACTLY 3 SECONDS. This is REQUIRED for Play mode transition and rendering stabilization. 3) After 3 seconds, call unity_get_screenshot_result to retrieve the actual screenshot path and resolution. CRITICAL: The screenshot is NOT complete until you retrieve the result with unity_get_screenshot_result. If you call it too early, you will get "status: capturing". NOTE: If Unity is already in Play mode, captures and returns result immediately without the pending status.',
        inputSchema: z.object({
            filename: z.string().optional().describe('Output filename (default: GameView_timestamp.png)'),
            path: z.string().optional().default('Assets/Screenshots').describe('Output directory path')
        })
    }, async (params) => {
        const result = await sendUnityCommand('CAPTURE_GAME_VIEW', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_capture_scene_view', {
        title: 'Capture Scene View Screenshot',
        description: 'Capture a screenshot of the Unity Scene View. The image is saved to Assets/Screenshots and can be analyzed by Claude Vision. Returns the file path for immediate analysis.',
        inputSchema: z.object({
            filename: z.string().optional().describe('Output filename (default: SceneView_timestamp.png)'),
            path: z.string().optional().default('Assets/Screenshots').describe('Output directory path')
        })
    }, async (params) => {
        const result = await sendUnityCommand('CAPTURE_SCENE_VIEW', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_capture_region', {
        title: 'Capture Specific Region Screenshot',
        description: 'Capture a screenshot of a specific region within Unity Game View or Scene View. Useful for capturing specific UI elements or areas. WORKFLOW for view="game": 1) Call this tool → returns "status: pending" if Play mode transition needed. 2) DO NOT CALL ANY OTHER TOOLS. WAIT EXACTLY 3 SECONDS for Play mode transition and rendering stabilization. 3) After 3 seconds, call unity_get_screenshot_result to retrieve result. CRITICAL: The capture is NOT complete until you retrieve the result with unity_get_screenshot_result. If you call it too early, you will get "status: capturing". For view="scene": captures immediately without pending status.',
        inputSchema: z.object({
            x: z.number().int().describe('X coordinate of the region (top-left corner)'),
            y: z.number().int().describe('Y coordinate of the region (top-left corner)'),
            width: z.number().int().describe('Width of the region in pixels'),
            height: z.number().int().describe('Height of the region in pixels'),
            view: z.enum(['game', 'scene']).optional().default('game').describe('Which view to capture from: "game" (includes Canvas/UI, auto Play mode if needed) or "scene" (3D view only, works in Edit mode)'),
            filename: z.string().optional().describe('Output filename (default: Region_timestamp.png)'),
            path: z.string().optional().default('Assets/Screenshots').describe('Output directory path')
        })
    }, async (params) => {
        const result = await sendUnityCommand('CAPTURE_REGION', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_get_screenshot_result', {
        title: 'Get Screenshot Result',
        description: 'Retrieve the result of a screenshot capture operation that required Play mode transition. WORKFLOW: When unity_capture_game_view or unity_capture_region returns {"status": "pending"}, Unity is entering Play mode to capture Canvas/UI. Wait EXACTLY 3 SECONDS, then call THIS tool to retrieve the actual screenshot result including file path, width, and height. Returns {"status": "capturing"} if still in progress (you called too early - wait more), {"status": "no_result"} if no capture pending.',
        inputSchema: z.object({})
    }, async (params) => {
        const result = await sendUnityCommand('GET_SCREENSHOT_RESULT', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_force_refresh_assets', {
        title: 'Force Refresh Assets',
        description: 'Force Unity to refresh the asset database and recompile scripts. IMPORTANT: Before using this tool after editing .cs files, always run unity_analyze_console_logs with logType:"error" to check for syntax/compile errors. If errors exist, fix them first. Only refresh when error-free. Essential after modifying .cs files to ensure changes are recognized before invoking methods.',
        inputSchema: z.object({
            importMode: z.enum(['default', 'force', 'forcesynchronous']).optional().default('default').describe('Import mode: "default" (normal refresh), "force" (force reimport all), "forcesynchronous" (force reimport and wait for completion)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('FORCE_REFRESH_ASSETS', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_invoke_context_menu', {
        title: 'Invoke ContextMenu Method',
        description: 'Invoke a method decorated with [ContextMenu] attribute on a component. Perfect for triggering custom editor actions like BuildLayout(), GenerateUI(), etc. WORKFLOW: 1) Edit code 2) unity_analyze_console_logs(error) 3) Fix errors if any 4) unity_force_refresh_assets 5) unity_analyze_console_logs(error) 6) Use this tool. This ensures error-free execution.',
        inputSchema: z.object({
            componentName: z.string().describe('Name of the component class (e.g., "HomeScreenFlexLayout")'),
            methodName: z.string().describe('Name of the method to invoke (e.g., "BuildLayout")'),
            gameObjectName: z.string().optional().describe('Name of the GameObject containing the component (optional, will search scene if not provided)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('INVOKE_CONTEXT_MENU', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_get_inspector_info', {
        title: 'Get Inspector Information',
        description: 'Get detailed inspector information for a specific GameObject, including all components, properties, and values',
        inputSchema: z.object({
            gameObjectName: z.string().describe('Name of the GameObject to inspect'),
            includePrivateFields: z.boolean().optional().default(false).describe('Include private/protected fields in inspection'),
            includeEvents: z.boolean().optional().default(false).describe('Include UnityEvent information'),
            componentFilter: z.string().optional().describe('Filter by specific component type (e.g., "Transform", "Rigidbody")')
        })
    }, async (params) => {
        const result = await sendUnityCommand('GET_INSPECTOR_INFO', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_get_selected_object_info', {
        title: 'Get Selected Object Inspector Info',
        description: 'Get detailed inspector information for the currently selected GameObject in the Unity Editor',
        inputSchema: z.object({
            includePrivateFields: z.boolean().optional().default(false).describe('Include private/protected fields in inspection'),
            includeEvents: z.boolean().optional().default(false).describe('Include UnityEvent information'),
            componentFilter: z.string().optional().describe('Filter by specific component type')
        })
    }, async (params) => {
        const result = await sendUnityCommand('GET_SELECTED_OBJECT_INFO', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_get_component_details', {
        title: 'Get Component Details',
        description: 'Get detailed information about a specific component on a GameObject, including all serialized properties',
        inputSchema: z.object({
            gameObjectName: z.string().describe('Name of the GameObject'),
            componentType: z.string().describe('Type of the component (e.g., "Transform", "Rigidbody", "BoxCollider")'),
            includeSerializedProperties: z.boolean().optional().default(true).describe('Include all serialized properties')
        })
    }, async (params) => {
        const result = await sendUnityCommand('GET_COMPONENT_DETAILS', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_console', {
        title: 'Console Operations',
        description: 'Unity console log count and basic info retrieval',
        inputSchema: z.object({
            operation: z.enum(['read', 'clear']).default('read'),
            logType: z.enum(['all', 'info', 'warning', 'error']).optional().default('all'),
            limit: z.number().optional().default(50)
        })
    }, async (params) => {
        const result = await sendUnityCommand('console_operation', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_analyze_console_logs', {
        title: 'Analyze Console Logs',
        description: 'Detailed analysis of Unity console logs with file paths, line numbers and stack traces. CRITICAL: Always use this BEFORE unity_force_refresh_assets when you edit .cs files - check for syntax/compile errors with logType:"error". This prevents compilation failures and ensures clean workflows. Returns detailed error info including file paths and line numbers for easy fixing.',
        inputSchema: z.object({
            logType: z.enum(['all', 'error', 'warning', 'log']).optional().default('error'),
            limit: z.number().optional().default(10),
            includeStackTrace: z.boolean().optional().default(true),
            operation: z.enum(['analyze']).optional().default('analyze')
        })
    }, async (params) => {
        const result = await sendUnityCommand('analyze_console_logs', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_list_packages', {
        title: 'List Packages',
        description: 'List all installed Unity packages',
        inputSchema: z.object({
            filter: z.enum(['all', 'offline']).optional().default('all')
        })
    }, async (params) => {
        const result = await sendUnityCommand('list_packages', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_list_assets', {
        title: 'List Assets',
        description: 'List assets in the project',
        inputSchema: z.object({
            path: z.string().optional().default('Assets'),
            type: z.string().optional(),
            recursive: z.boolean().optional().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('list_assets', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_search_prefabs_by_component', {
        title: 'Search Prefabs by Component',
        description: '🔍 Search for prefabs containing specific component(s). Useful for finding all prefabs with Rigidbody, Collider, custom scripts, etc.',
        inputSchema: z.object({
            componentName: z.string().describe('Component name to search for (e.g., "Rigidbody", "AudioSource")'),
            searchPath: z.string().optional().default('Assets').describe('Directory to search in'),
            recursive: z.boolean().optional().default(true).describe('Search recursively'),
            includeDisabled: z.boolean().optional().default(true).describe('Include disabled components')
        })
    }, async (params) => {
        const result = await sendUnityCommand('search_prefabs_by_component', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_find_material_usage', {
        title: 'Find Material Usage',
        description: '🎨 Find all GameObjects, prefabs, and renderers using a specific material. Shows where materials are referenced in the project.',
        inputSchema: z.object({
            materialPath: z.string().describe('Material path (e.g., Assets/Materials/Wood.mat)'),
            materialName: z.string().optional().describe('Material name (alternative to path)'),
            searchInScenes: z.boolean().optional().default(true).describe('Search in currently loaded scenes'),
            searchInPrefabs: z.boolean().optional().default(true).describe('Search in all prefabs')
        })
    }, async (params) => {
        const result = await sendUnityCommand('find_material_usage', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_get_asset_dependencies', {
        title: 'Get Asset Dependencies',
        description: '🔗 Get all dependencies of an asset (textures, materials, scripts, etc.). Shows complete dependency tree.',
        inputSchema: z.object({
            assetPath: z.string().describe('Asset path (e.g., Assets/Prefabs/Player.prefab)'),
            recursive: z.boolean().optional().default(true).describe('Get recursive dependencies'),
            includeBuiltIn: z.boolean().optional().default(false).describe('Include built-in Unity assets'),
            groupByType: z.boolean().optional().default(true).describe('Group results by asset type')
        })
    }, async (params) => {
        const result = await sendUnityCommand('get_asset_dependencies', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_find_missing_references', {
        title: 'Find Missing References',
        description: '⚠️ Find missing scripts, materials, textures, and other references in scenes and prefabs. Critical for project cleanup.',
        inputSchema: z.object({
            searchPath: z.string().optional().default('Assets').describe('Directory to search in'),
            types: z.array(z.enum(['script', 'material', 'texture', 'prefab', 'mesh', 'audio', 'all'])).optional().default(['all']).describe('Types to check'),
            searchInScenes: z.boolean().optional().default(true).describe('Search in currently loaded scenes'),
            searchInPrefabs: z.boolean().optional().default(true).describe('Search in all prefabs'),
            includeChildren: z.boolean().optional().default(true).describe('Check child objects')
        })
    }, async (params) => {
        const result = await sendUnityCommand('find_missing_references', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_check_folder', {
        title: 'Check Folder',
        description: 'Check if folder exists',
        inputSchema: z.object({
            path: z.string()
        })
    }, async (params) => {
        const result = await sendUnityCommand('check_folder', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_folder', {
        title: 'Create Folder',
        description: 'Create a new folder',
        inputSchema: z.object({
            path: z.string()
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_folder', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_duplicate_gameobject', {
        title: 'Duplicate GameObject',
        description: 'Duplicate an existing GameObject',
        inputSchema: z.object({
            gameObject: z.string(),
            newName: z.string().optional(),
            position: z.object({
                x: z.number(),
                y: z.number(),
                z: z.number()
            }).optional()
        })
    }, async (params) => {
        const result = await sendUnityCommand('duplicate_gameobject', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_find_gameobjects_by_component', {
        title: 'Find GameObjects by Component',
        description: 'Find all GameObjects with specific component',
        inputSchema: z.object({
            componentType: z.string(),
            includeInactive: z.boolean().optional().default(false)
        })
    }, async (params) => {
        const result = await sendUnityCommand('find_by_component', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_group_gameobjects', {
        title: 'Group GameObjects',
        description: 'Group multiple GameObjects under a parent',
        inputSchema: z.object({
            gameObjects: z.array(z.string()).describe('Names of GameObjects to group'),
            parentName: z.string().describe('Name for the parent group'),
            position: z.object({
                x: z.number(),
                y: z.number(),
                z: z.number()
            }).optional().describe('Position of the parent group')
        })
    }, async (params) => {
        const result = await sendUnityCommand('group_gameobjects', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_pause_scene', {
        title: 'Pause Scene',
        description: 'Pause or unpause the scene view',
        inputSchema: z.object({
            pause: z.boolean().describe('True to pause, false to unpause')
        })
    }, async (params) => {
        const result = await sendUnityCommand('pause_scene', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // (duplicate unity_find_missing_references removed — the richer definition above at L1603 wins)

    mcpServer.registerTool('unity_create_animator_controller', {
        title: 'Create Animator Controller',
        description: 'Create a new Animator Controller with default states and parameters',
        inputSchema: z.object({
            name: z.string().default('NewAnimatorController'),
            path: z.string().default('Assets/Animations/Controllers/'),
            targetObject: z.string().optional().describe('GameObject to apply the controller to'),
            applyToObject: z.boolean().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_animator_controller', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_add_animation_state', {
        title: 'Add Animation State',
        description: 'Add a new state to an Animator Controller',
        inputSchema: z.object({
            controllerPath: z.string().describe('Path to the Animator Controller asset'),
            stateName: z.string().default('NewState'),
            animationClipPath: z.string().optional().describe('Path to animation clip'),
            layerIndex: z.number().default(0),
            isDefault: z.boolean().default(false)
        })
    }, async (params) => {
        const result = await sendUnityCommand('add_animation_state', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_animation_clip', {
        title: 'Create Animation Clip',
        description: 'Create a new animation clip with sample curves',
        inputSchema: z.object({
            name: z.string().default('NewAnimation'),
            path: z.string().default('Assets/Animations/Clips/'),
            duration: z.number().default(1),
            frameRate: z.number().default(30),
            targetObject: z.string().optional(),
            animationType: z.enum(['position', 'rotation', 'scale', 'color']).default('position')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_animation_clip', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_add_animation_transition', {
        title: 'Add Animation Transition',
        description: 'Create a transition between animation states',
        inputSchema: z.object({
            controllerPath: z.string().describe('Path to the Animator Controller'),
            fromState: z.string().describe('Source state name (use "Any" for Any State)'),
            toState: z.string().describe('Destination state name'),
            condition: z.string().optional().describe('Parameter name for condition'),
            conditionValue: z.string().optional().describe('Value for condition'),
            hasExitTime: z.boolean().default(true),
            transitionDuration: z.number().default(0.25),
            layerIndex: z.number().default(0)
        })
    }, async (params) => {
        const result = await sendUnityCommand('add_animation_transition', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_bake_animation', {
        title: 'Bake Animation',
        description: 'Bake runtime animation into an animation clip',
        inputSchema: z.object({
            sourceObject: z.string().describe('GameObject with animation to bake'),
            animationName: z.string().default('BakedAnimation'),
            startFrame: z.number().default(0),
            endFrame: z.number().default(60),
            frameRate: z.number().default(30),
            path: z.string().default('Assets/Animations/Baked/')
        })
    }, async (params) => {
        const result = await sendUnityCommand('bake_animation', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_setup_ui_anchors', {
        title: 'Setup UI Anchors',
        description: 'Automatically setup anchors and pivots for UI elements',
        inputSchema: z.object({
            targetObject: z.string().describe('Target GameObject name'),
            anchorPreset: z.enum([
                'top-left', 'top-center', 'top-right',
                'middle-left', 'center', 'middle-right', 
                'bottom-left', 'bottom-center', 'bottom-right',
                'stretch-horizontal', 'stretch-vertical', 'stretch-all'
            ]).default('center'),
            pivotPreset: z.enum([
                'top-left', 'top-center', 'top-right',
                'middle-left', 'center', 'middle-right',
                'bottom-left', 'bottom-center', 'bottom-right'
            ]).default('center'),
            margin: z.number().default(10),
            recursive: z.boolean().default(false)
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_ui_anchors', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_ui_grid', {
        title: 'Create UI Grid',
        description: 'Create UI grid layout with customizable elements',
        inputSchema: z.object({
            gridName: z.string().default('UIGrid'),
            columns: z.number().default(3),
            rows: z.number().default(3),
            cellSize: z.string().default('100,100').describe('Cell size as "width,height"'),
            spacing: z.string().default('10,10').describe('Spacing as "x,y"'),
            padding: z.string().default('10,10,10,10').describe('Padding as "left,right,top,bottom"'),
            fillType: z.enum(['button', 'image', 'text', 'toggle']).default('button')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_ui_grid', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_create_ui_dialog', {
        title: 'Create UI Dialog',
        description: 'Create modal dialogs (confirmation, alert, input)',
        inputSchema: z.object({
            dialogName: z.string().default('Dialog'),
            dialogType: z.enum(['confirmation', 'alert', 'input']).default('confirmation'),
            title: z.string().default('Dialog Title'),
            message: z.string().default('Dialog message content'),
            hasOverlay: z.boolean().default(true),
            isModal: z.boolean().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_ui_dialog', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_setup_safe_area', {
        title: 'Setup Safe Area',
        description: 'Setup Safe Area for mobile devices with notch support',
        inputSchema: z.object({
            safeAreaName: z.string().default('SafeAreaContainer'),
            targetObject: z.string().optional().describe('Target object (leave empty to create new)'),
            applyToCanvas: z.boolean().default(false),
            includeNotch: z.boolean().default(true)
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_safe_area', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_setup_ui_canvas', {
        title: 'Setup UI Canvas',
        description: 'Configure UI Canvas with different render modes (overlay, camera, world) and settings',
        inputSchema: z.object({
            canvasName: z.string().optional().default('Canvas').describe('Name of the canvas'),
            renderMode: z.enum(['overlay', 'screenspace-overlay', 'camera', 'screenspace-camera', 'world', 'worldspace']).optional().default('overlay').describe('Canvas render mode'),
            sortingOrder: z.number().optional().default(0).describe('Sorting order for overlay mode'),
            pixelPerfect: z.boolean().optional().default(false).describe('Enable pixel perfect rendering'),
            camera: z.string().optional().describe('Camera name for camera mode (use "new" to create UI camera)'),
            planeDistance: z.number().optional().default(100).describe('Plane distance for camera mode'),
            position: z.string().optional().describe('Position for world space canvas (e.g., "0,0,5")'),
            rotation: z.string().optional().describe('Rotation for world space canvas (e.g., "0,180,0")'),
            scale: z.string().optional().describe('Scale for world space canvas (e.g., "0.01,0.01,0.01")'),
            sizeDelta: z.string().optional().describe('Canvas size for world space (e.g., "1920,1080")'),
            scaleMode: z.enum(['constant-pixel', 'scale-with-screen', 'constant-physical']).optional().describe('Canvas scaler mode'),
            referenceResolution: z.string().optional().default('1920,1080').describe('Reference resolution for scale-with-screen mode'),
            match: z.number().optional().default(0.5).describe('Width/Height match for scale-with-screen mode'),
            scaleFactor: z.number().optional().default(1).describe('Scale factor for constant-pixel mode'),
            pixelsPerUnit: z.number().optional().default(100).describe('Pixels per unit for constant-physical mode')
        })
    }, async (params) => {
        const result = await sendUnityCommand('setup_ui_canvas', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    mcpServer.registerTool('unity_set_ui_anchor', {
        title: 'Set UI Anchor',
        description: 'Set UI element anchor presets or custom anchor values for RectTransform',
        inputSchema: z.object({
            target: z.string().describe('Name of the UI element'),
            preset: z.enum([
                'center', 'middle-center',
                'top-left', 'top-center', 'top-right',
                'middle-left', 'middle-right',
                'bottom-left', 'bottom-center', 'bottom-right',
                'stretch-horizontal', 'stretch-vertical', 'stretch-all', 'fill',
                'top-stretch', 'bottom-stretch', 'left-stretch', 'right-stretch',
                'custom'
            ]).optional().describe('Anchor preset to apply'),
            anchorMin: z.string().optional().describe('Custom anchor min (e.g., "0,0") - only for custom preset'),
            anchorMax: z.string().optional().describe('Custom anchor max (e.g., "1,1") - only for custom preset'),
            pivot: z.string().optional().describe('Pivot point (e.g., "0.5,0.5") - only for custom preset'),
            anchoredPosition: z.string().optional().describe('Anchored position (e.g., "0,0")'),
            sizeDelta: z.string().optional().describe('Size delta (e.g., "100,100")'),
            keepPosition: z.boolean().optional().default(false).describe('Keep current position when applying preset'),
            keepSize: z.boolean().optional().default(false).describe('Keep current size when applying preset'),
            offset: z.number().optional().describe('Offset for stretch-all/fill presets')
        })
    }, async (params) => {
        const result = await sendUnityCommand('set_ui_anchor', params);
        return {
            content: [{
                type: 'text',
                text: typeof result === 'string' ? result : JSON.stringify(result, null, 2)
            }]
        };
    });

    // ===== 追加 essential ツール (頻出だが既存にない) =====
    mcpServer.registerTool('unity_execute_menu_item', {
        title: 'Execute Menu Item',
        description: 'Execute any Unity Editor menu item by its path (e.g. "Edit/Play", "Assets/Refresh", "Window/General/Console").',
        inputSchema: z.object({
            menuPath: z.string().describe('Full menu path (e.g. "Assets/Refresh")')
        })
    }, async (params) => {
        const result = await sendUnityCommand('execute_menu_item', params);
        return {
            content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }]
        };
    });

    mcpServer.registerTool('unity_get_gameobject_details', {
        title: 'Get GameObject Details',
        description: 'Get detailed info about a specific GameObject including components and serialized fields.',
        inputSchema: z.object({
            objectName: z.string().describe('GameObject name in the active scene')
        })
    }, async (params) => {
        const result = await sendUnityCommand('get_gameobject_details', params);
        return {
            content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }]
        };
    });

    mcpServer.registerTool('unity_create_script', {
        title: 'Create C# Script',
        description: 'Create a new C# script asset in the project. Triggers compile after save.',
        inputSchema: z.object({
            name: z.string().describe('Script class/file name (no .cs)'),
            path: z.string().optional().describe('Folder path under Assets (default: Assets/Scripts)'),
            template: z.string().optional().describe('Template name (e.g. "MonoBehaviour")'),
            content: z.string().optional().describe('Full file content; overrides template if given')
        })
    }, async (params) => {
        const result = await sendUnityCommand('create_script', params);
        return {
            content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }]
        };
    });

    mcpServer.registerTool('unity_read_script', {
        title: 'Read Script Content',
        description: 'Read the full source of a Unity C# script. Provide scriptPath or fileName.',
        inputSchema: z.object({
            scriptPath: z.string().optional().describe('Asset path to .cs file (e.g. "Assets/Scripts/Player.cs")'),
            fileName: z.string().optional().describe('Script file name (Player.cs) to search for')
        })
    }, async (params) => {
        const result = await sendUnityCommand('read_script', params);
        return {
            content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }]
        };
    });

    // ===== Essential 追加分: Scene/Material/Asset の基本操作で頻出するもの =====
    mcpServer.registerTool('unity_manage_scene', {
        title: 'Manage Scene',
        description: 'Scene management: save current scene, load from path, or create a new empty scene.',
        inputSchema: z.object({
            operation: z.enum(['save', 'load', 'new']).describe('save / load / new'),
            path: z.string().optional().describe('Scene file path (e.g. "Assets/Scenes/Main.unity"). Required for save/load.')
        })
    }, async (params) => {
        const result = await sendUnityCommand('manage_scene', params);
        return {
            content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }]
        };
    });

    mcpServer.registerTool('unity_set_material_property', {
        title: 'Set Material Property',
        description: 'Modify a single property on an existing material (color/float/vector/texture). Faster than recreating the material.',
        inputSchema: z.object({
            materialPath: z.string().describe('Path to the material asset (e.g. "Assets/Materials/Water.mat")'),
            propertyName: z.string().describe('Shader property name (e.g. "_BaseColor", "_Smoothness")'),
            propertyType: z.enum(['float', 'color', 'vector', 'texture']).describe('Property type'),
            value: z.string().describe('Float: "1.5" / Color: "#FF0000" or "1,0.5,0,1" / Vector: "1,2,3,4" / Texture: asset path')
        })
    }, async (params) => {
        const result = await sendUnityCommand('set_material_property', params);
        return {
            content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }]
        };
    });

    mcpServer.registerTool('unity_delete_asset', {
        title: 'Delete Asset',
        description: 'Delete an asset from the project (defaults to moving to trash).',
        inputSchema: z.object({
            assetPath: z.string().describe('Asset path to delete (e.g. "Assets/Prefabs/Old.prefab")'),
            moveToTrash: z.boolean().optional().default(true).describe('Move to trash instead of permanent delete')
        })
    }, async (params) => {
        const result = await sendUnityCommand('delete_asset', params);
        return {
            content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }]
        };
    });

    mcpServer.registerTool('unity_rename_asset', {
        title: 'Rename Asset',
        description: 'Rename an asset file in place.',
        inputSchema: z.object({
            oldPath: z.string().describe('Current asset path'),
            newName: z.string().describe('New name (without folder; same folder is kept)')
        })
    }, async (params) => {
        const result = await sendUnityCommand('rename_asset', params);
        return {
            content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }]
        };
    });

    // ===== Meta-tools (SuperSave 互換): list_categories / list_tools / search_tools / execute / run_csharp =====
    mcpServer.registerTool('list_categories', {
        title: 'List Tool Categories',
        description: 'List all available Unity tool categories from the full 355+ tool registry. Use this to discover tools beyond the Essential set.',
        inputSchema: z.object({})
    }, async () => {
        const categories = Object.entries(CATEGORIES).map(([name, data]) => ({
            name, description: data.description, toolCount: Object.keys(data.tools).length
        }));
        const totalTools = Object.keys(TOOL_REGISTRY_RAW).length;
        return {
            content: [{
                type: 'text',
                text: `Available Categories (${categories.length} categories, ${totalTools} total tools):\n\n` +
                    categories.map(c => `• ${c.name} (${c.toolCount} tools)\n  ${c.description}`).join('\n\n') +
                    '\n\nUse list_tools(category) to see tools, or execute(tool, params) to run.'
            }]
        };
    });

    mcpServer.registerTool('list_tools', {
        title: 'List Tools in Category',
        description: 'List all tools in a specific category with titles and descriptions.',
        inputSchema: z.object({
            category: z.string().describe('Category name (e.g., "gameobject", "material", "lighting")')
        })
    }, async (params) => {
        const category = params.category.toLowerCase();
        if (!CATEGORIES[category]) {
            return {
                content: [{
                    type: 'text',
                    text: `Unknown category: "${category}"\n\nAvailable categories: ${Object.keys(CATEGORIES).join(', ')}`
                }]
            };
        }
        const categoryData = CATEGORIES[category];
        const tools = Object.entries(categoryData.tools).map(([name, data]) =>
            `• ${name} (${data.fullName})\n  ${data.title}\n  ${data.description}`);
        return {
            content: [{
                type: 'text',
                text: `Category: ${category}\n${categoryData.description}\n\nTools (${tools.length}):\n\n${tools.join('\n\n')}\n\nUse execute(tool_name, params) to run a tool.`
            }]
        };
    });

    mcpServer.registerTool('search_tools', {
        title: 'Search Tools',
        description: 'Search across all 355+ tools by keyword in name/title/description.',
        inputSchema: z.object({
            query: z.string().describe('Search keyword (e.g., "shader", "input", "weather")'),
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
            if (categoryFilter && category !== categoryFilter) continue;
            if (!query) {
                results.push({ name: toolName, title, description, category: toolData.category, score: 0 });
                if (results.length >= limit) break;
                continue;
            }
            let score = 0;
            if (toolName.toLowerCase().includes(query)) score += 100;
            if (title.toLowerCase().includes(query)) score += 80;
            if (description.toLowerCase().includes(query)) score += 40;
            if (score > 0) results.push({ name: toolName, title, description, category: toolData.category, score });
        }
        const sorted = results.sort((a, b) => b.score - a.score).slice(0, limit);
        if (sorted.length === 0) {
            return {
                content: [{
                    type: 'text',
                    text: `No tools found for query: "${params.query}"\n\nTry different keywords or use list_categories.`
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

    mcpServer.registerTool('execute', {
        title: 'Execute Any Tool',
        description: 'Execute any of the 355+ Unity tools by name. Essential tools are directly registered (unity_*); use this fallback for anything else. Tool name accepts both "unity_create_terrain" and "create_terrain".',
        inputSchema: z.object({
            tool: z.string().describe('Tool name (e.g., "create_terrain", "unity_setup_volumetric_fog")'),
            params: z.any().optional().describe('Parameters as JSON object')
        })
    }, async (params) => {
        const toolName = params.tool;
        let toolParams = params.params || {};
        if (typeof toolParams === 'string') {
            try { toolParams = JSON.parse(toolParams); }
            catch (e) {
                const m = toolParams.match(/^(\w+)=(.+)$/);
                toolParams = m ? { [m[1]]: m[2] } : {};
            }
        }
        const strippedName = toolName.startsWith('unity_') ? toolName.substring(6) : toolName;
        const toolInfo = ALL_TOOLS[strippedName];
        if (!toolInfo) {
            const similar = Object.keys(ALL_TOOLS).filter(t =>
                t.includes(strippedName) || strippedName.includes(t) ||
                t.split('_').some(part => strippedName.includes(part))
            ).slice(0, 5);
            let errorMsg = `Unknown tool: "${toolName}"`;
            if (similar.length > 0) errorMsg += `\n\nDid you mean: ${similar.join(', ')}?`;
            errorMsg += `\n\nUse list_categories() then list_tools(category), or search_tools(query).`;
            return { content: [{ type: 'text', text: errorMsg }] };
        }
        const commandName = strippedName.toLowerCase();
        try {
            const result = await sendUnityCommand(commandName, toolParams);
            return {
                content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }]
            };
        } catch (error) {
            let errorDetail = `Error executing "${strippedName}":\n  Message: ${error.message}\n`;
            errorDetail += `\nTool info: ${toolInfo.title} (${toolInfo.category})`;
            return { content: [{ type: 'text', text: errorDetail }] };
        }
    });

    mcpServer.registerTool('run_csharp', {
        title: 'Run C# Code',
        description: 'Execute arbitrary C# code against the running Unity Editor (equivalent of Blender run_python). UnityEngine / UnityEditor / System.Linq / Newtonsoft.Json are pre-imported. Use when no dedicated tool covers the operation. Does NOT trigger AssemblyReload.\n\nEnd snippet with `return X;` to capture X into `result`. Bare expression (no trailing `;`) also returns its value. Debug.Log/LogWarning/LogError captured into `output`.\n\nKNOWN LIMITATION: Mono.CSharp parser cannot instantiate generic TYPES — `new List<int>()` etc. silently return null. Use arrays or ArrayList instead. Generic METHOD calls (`FindObjectsByType<T>()`) work fine.',
        inputSchema: z.object({
            code: z.string().describe('C# code. End with `return X;` to capture X. Pre-imported: System, System.Linq, System.Collections.Generic, UnityEngine, UnityEditor, Newtonsoft.Json.')
        })
    }, async (params) => {
        try {
            const result = await sendUnityCommand('run_csharp', params);
            return {
                content: [{ type: 'text', text: typeof result === 'string' ? result : JSON.stringify(result, null, 2) }]
            };
        } catch (error) {
            return {
                content: [{ type: 'text', text: `run_csharp failed: ${error.message}` }]
            };
        }
    });

    // Dialog-less Unity reconnect — bypasses Tools > AI Reconnect dialog.
    mcpServer.registerTool('unity_reconnect', {
        title: 'Reconnect Unity Bridge',
        description: 'Force Unity to re-establish its WebSocket connection to the MCP bridge. Use when tool calls time out or Unity-side state is stuck. No dialog.',
        inputSchema: z.object({})
    }, async () => {
        if (!unityWebSocket || unityWebSocket.readyState !== WebSocket.OPEN) {
            return {
                content: [{ type: 'text', text: 'Unity not connected. Auto-reconnect should attach within a few seconds if Unity is open.' }],
                isError: true
            };
        }
        const id = ++requestId;
        const message = JSON.stringify({ id, type: 'system_command', command: 'reconnect' });
        unityWebSocket.send(message);
        return {
            content: [{ type: 'text', text: 'Reconnect signal sent to Unity. Bridge will re-attach in ~1 second.' }]
        };
    });

    // リソース定義
    mcpServer.registerResource('unity://project-stats', {
        title: 'Unity Project Statistics',
        description: 'Get project statistics and implementation status',
        mimeType: 'application/json'
    }, async () => {
        // registeredToolsプロパティを直接チェック
        const toolCount = mcpServer._private ? Object.keys(mcpServer._private.registeredTools || {}).length : 0;
        
        return {
            content: [
                {
                    type: 'text',
                    text: JSON.stringify({
                        implementedTools: toolCount,
                        status: 'active',
                        unityConnection: unityWebSocket && unityWebSocket.readyState === WebSocket.OPEN
                    }, null, 2)
                }
            ]
        };
    });

    // 登録されたツールの数を確認（ログは出力しない）

    // ===== Phase 3: Prompt Caching - Tool Catalog Resource =====
    // Tool catalog リソースを登録（Claude Desktop向けPrompt Caching）
    mcpServer.registerResource('unity://tools/catalog', {
        title: 'Unity Tools Catalog',
        description: 'Complete catalog of all Unity MCP tools with descriptions and schemas. This resource is designed for prompt caching to reduce token consumption in long sessions. Read this ONCE at session start, then use the specific tools you need.',
        mimeType: 'application/json'
    }, async () => {
        // Get all registered tools from the server's internal registry
        const toolsList = [];

        // We need to access the internal registeredTools
        // Since we can't directly access it, we'll collect tool names and regenerate their definitions
        // This is a simplified version - in production, you'd extract from the actual registry

        // For now, return a placeholder that indicates caching is enabled
        const totalTools = Object.keys(TOOL_REGISTRY_RAW).length;
        const catalog = {
            version: '1.1.0',
            total_tools: totalTools,
            note: `This catalog references ${totalTools} Unity MCP tool definitions for caching purposes.`,
            cache_instructions: `This resource should be read once at the beginning of your session and cached. It references all ${totalTools} available Unity tools with their descriptions and input schemas.`,
            categories: [
                'GameObject', 'Transform', 'Material', 'Lighting', 'Camera', 'Physics',
                'UI', 'Animation', 'Cinemachine', 'Scene', 'GOAP', 'Audio', 'Screenshot', 'Utility'
            ],
            usage: 'After reading this catalog once, use the specific unity_* tools directly without re-reading the catalog.'
        };

        return {
            contents: [{
                uri: 'unity://tools/catalog',
                mimeType: 'application/json',
                text: JSON.stringify(catalog, null, 2)
            }]
        };
    });

    // 全てのサーバーを起動
    await mcpServer.start();
}

// HTTPエンドポイント
app.get('/health', (req, res) => {
    res.json({ 
        status: 'ok',
        unityConnected: unityWebSocket !== null && unityWebSocket.readyState === WebSocket.OPEN
    });
});

// サーバー起動
async function startServer() {
    // 最初にMCPサーバーをセットアップ
    await setupMCPServer();
    
    // WebSocketサーバーを作成
    wss = new WebSocket.Server({ server });
    setupWebSocketHandlers();
    
    const port = process.env.PORT || 8090;
    server.listen(port, () => {
        // サーバー起動（ログを出力しない）
    });
}

// プロセス終了時のクリーンアップ
// 終了処理用の共通関数
function shutdownServer() {
    // Unity WebSocket接続を閉じる
    if (unityWebSocket && unityWebSocket.readyState === WebSocket.OPEN) {
        unityWebSocket.close();
    }
    
    // WebSocketサーバーを閉じる
    if (wss) {
        wss.close();
    }
    
    // HTTPサーバーを閉じる
    if (server && server.listening) {
        server.close(() => {
            process.exit(0);
        });
    } else {
        process.exit(0);
    }
    
    // 5秒後に強制終了
    setTimeout(() => {
        process.exit(1);
    }, 5000);
}

process.on('SIGINT', shutdownServer);
process.on('SIGTERM', shutdownServer);

// stdioが閉じられた時も終了
process.stdin.on('close', () => {
    shutdownServer();
});

// エラーハンドリング
process.on('uncaughtException', (error) => {
    // console.error('[MCP Server] Uncaught Exception:', error);
});

process.on('unhandledRejection', (reason, promise) => {
    // console.error('[MCP Server] Unhandled Rejection at:', promise, 'reason:', reason);
});

startServer().catch(err => {
    // console.error(err);
});
