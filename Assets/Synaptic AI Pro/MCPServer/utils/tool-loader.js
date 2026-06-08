// utils/tool-loader.js - Tool categorization and filtering

import { getEmbedding, cosineSimilarity } from './embedding.js';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Tool categories mapping (tool name pattern -> category)
const categoryPatterns = {
    GameObject: [
        'create_gameobject', 'update_gameobject', 'delete_gameobject', 'destroy_gameobject',
        'get_gameobject', 'find_gameobject', 'set_active', 'set_parent', 'set_tag',
        'set_layer', 'duplicate_gameobject', 'get_children', 'get_parent', 'add_component',
        'update_component', 'remove_component'
    ],
    Transform: [
        'set_position', 'set_rotation', 'set_scale', 'get_position', 'get_rotation',
        'get_scale', 'move_', 'rotate_', 'look_at', 'set_transform'
    ],
    Material: [
        'create_material', 'setup_material', 'set_material', 'get_material', 'set_color',
        'set_shader', 'set_texture', 'material_property', 'assign_material', 'advanced_material'
    ],
    Lighting: [
        'lighting', 'setup_lighting', 'create_light', 'set_light', 'get_light',
        'light_intensity', 'light_color', 'light_shadow', 'reflection_probe',
        'lightmap', 'ambient_', 'skybox', 'volumetric', 'fog'
    ],
    Camera: [
        'camera', 'setup_camera', 'create_camera', 'set_camera', 'get_camera',
        'camera_fov', 'camera_depth', 'camera_target', 'camera_viewport',
        'camera_priority', 'camera_enabled', 'active_camera'
    ],
    Physics: [
        'physics', 'setup_physics', 'add_rigidbody', 'add_collider', 'set_rigidbody',
        'set_collider', 'raycast', 'overlap_', 'apply_force', 'set_velocity',
        'navmesh', 'navigation'
    ],
    UI: [
        'create_ui', 'ui_', 'canvas', 'create_button', 'create_text', 'create_image',
        'set_anchor', 'set_pivot', 'rect_transform', 'layout', 'panel'
    ],
    Animation: [
        'animation', 'create_animation', 'create_animator', 'add_animation',
        'play_animation', 'animator_', 'animation_clip', 'animation_curve'
    ],
    Cinemachine: [
        'cinemachine', 'vcam', 'virtual_camera', 'freelook', 'dolly', 'track',
        'brain', 'blend', 'state_driven', 'clear_shot', 'impulse', 'confiner',
        'collider_extension', 'target_group', 'mixing_camera', 'blend_list'
    ],
    Scene: [
        'scene', 'get_scene', 'scene_info', 'scene_summary', 'gameobjects_list',
        'gameobject_detail', 'load_scene', 'unload_scene', 'save_scene', 'new_scene',
        'set_active_scene', 'list_all_scenes', 'add_scene_to_build', 'manage_scene'
    ],
    GOAP: [
        'goap_', 'create_agent', 'add_action', 'add_goal', 'planner', 'sensor',
        'ai_agent', 'behavior'
    ],
    Audio: [
        'audio', 'create_audio', 'setup_audio', 'play_sound', 'stop_sound',
        'audio_mixer', 'audio_source', 'audio_clip', 'audio_effects',
        'reverb', '3d_audio', 'music_', 'adaptive_music', 'sound_pool'
    ],
    Input: [
        'input', 'gesture', 'touch', 'haptic', 'multitouch', 'swipe', 'pinch',
        'drag_drop', 'input_validation', 'input_recording', 'accessibility_input'
    ],
    VFX: [
        'vfx', 'visual_effect', 'post_processing', 'bloom', 'vignette', 'chromatic',
        'lens_flare', 'screen_shake', 'screen_fade', 'film_grain', 'motion_blur',
        'depth_of_field', 'lens_distortion', 'color_grading', 'urp_settings',
        'hdrp_settings', 'vfx_graph', 'decal'
    ],
    Shader: [
        'shader', 'shader_property', 'shader_graph', 'shader_texture', 'shader_gradient',
        'shader_animator', 'water_material', 'toon_material', 'hair_material', 'skin_material',
        'eye_material', 'fabric_material'
    ],
    Weather: [
        'weather', 'rain', 'snow', 'wind', 'storm', 'fog', 'clouds', 'weather_preset'
    ],
    TimeOfDay: [
        'time_of_day', 'day_night', 'time_event', 'sunrise', 'sunset'
    ],
    Editor: [
        'inspector', 'selected_object', 'component_details', 'console', 'analyze_console',
        'context_menu', 'force_refresh', 'editor_'
    ],
    Package: [
        'package', 'install_package', 'list_packages', 'check_package'
    ],
    Build: [
        'build_settings', 'player_settings', 'quality_settings', 'project_summary'
    ],
    Monitoring: [
        'monitor', 'subscribe_events', 'get_events', 'monitoring_status', 'play_state',
        'file_changes', 'compile', 'operation_history', 'checkpoint'
    ],
    AssetManagement: [
        'folder', 'rename_asset', 'delete_asset', 'organize_folder', 'cleanup_empty',
        'project_snapshot', 'dependencies', 'export_project', 'naming_conventions'
    ],
    Optimization: [
        'optimize', 'analyze_draw', 'performance_report', 'estimate_build_size',
        'auto_atlas', 'generate_lod', 'best_practices'
    ],
    Batch: [
        'batch_rename', 'batch_prefab', 'batch_material', 'extract_all'
    ],
    GameSystems: [
        'game_controller', 'state_machine', 'inventory', 'game_template', 'prototype'
    ],
    AI: [
        'ml_agent', 'neural_network', 'pathfinding', 'ai_'
    ],
    Debug: [
        'debug', 'profile', 'run_tests', 'breakpoint', 'control_game_speed'
    ],
    Timeline: [
        'timeline', 'avatar'
    ],
    Scripting: [
        'modify_script', 'edit_script', 'add_script', 'update_script', 'read_script',
        'grep_scripts', 'list_script', 'analyze_script'
    ],
    Screenshot: [
        'capture', 'screenshot', 'get_screenshot'
    ],
    Utility: [
        'undo', 'redo', 'search', 'find_', 'get_asset', 'import_', 'create_prefab',
        'create_script', 'particle', 'terrain', 'modify_terrain', 'create_terrain',
        'place_objects', 'list_assets', 'group_gameobjects', 'add_target_to_group',
        'responsive_ui', 'scroll_view', 'safe_area', 'auto_attach'
    ]
};

/**
 * Detect category for a tool name
 * @param {string} toolName - Tool name
 * @returns {string} - Category name
 */
export function detectCategory(toolName) {
    const lowerName = toolName.toLowerCase();

    for (const [category, patterns] of Object.entries(categoryPatterns)) {
        for (const pattern of patterns) {
            if (lowerName.includes(pattern.toLowerCase())) {
                return category;
            }
        }
    }

    return 'Other';
}

/**
 * Load tool registry from file
 * @returns {Object} - Tool registry
 */
export function loadToolRegistry() {
    try {
        const registryPath = path.join(__dirname, '..', 'tool-registry.json');

        if (!fs.existsSync(registryPath)) {
            console.warn('[ToolLoader] tool-registry.json not found, will need to generate');
            return null;
        }

        const data = fs.readFileSync(registryPath, 'utf-8');
        return JSON.parse(data);
    } catch (error) {
        console.error('[ToolLoader] Error loading tool registry:', error.message);
        return null;
    }
}

/**
 * Filter tools by categories
 * @param {string[]} categories - Categories to include
 * @param {Object} toolRegistry - Tool registry
 * @returns {string[]} - Filtered tool names
 */
export function filterToolsByCategories(categories, toolRegistry) {
    if (!toolRegistry) {
        return [];
    }

    const toolNames = [];

    for (const [toolName, meta] of Object.entries(toolRegistry)) {
        if (categories.includes(meta.category)) {
            toolNames.push(toolName);
        }
    }

    return toolNames;
}

/**
 * Search tools by keyword using embeddings or text matching
 * @param {string} query - Search query
 * @param {number} limit - Max results
 * @param {Object} toolRegistry - Tool registry
 * @returns {Promise<Array>} - Results with scores
 */
export async function searchToolsByKeywords(query, limit, toolRegistry) {
    if (!toolRegistry) {
        return [];
    }

    const queryLower = query.toLowerCase();
    const results = [];

    // Check if embeddings are available
    const hasEmbeddings = Object.values(toolRegistry).some(meta => meta.embedding);

    if (hasEmbeddings) {
        // Use semantic search with embeddings
        try {
            const queryEmbedding = await getEmbedding(query);

            for (const [toolName, meta] of Object.entries(toolRegistry)) {
                if (!meta.embedding) {
                    continue;
                }

                const similarity = cosineSimilarity(queryEmbedding, meta.embedding);

                results.push({
                    name: toolName,
                    description: meta.description,
                    category: meta.category,
                    score: similarity
                });
            }
        } catch (error) {
            console.error('[ToolLoader] Embedding search error:', error.message);
            // Fall through to text matching
        }
    }

    // Fallback to simple text matching if no embeddings or error
    if (results.length === 0) {
        // Split query into keywords for OR search
        const keywords = queryLower.split(/\s+/).filter(k => k.length > 0);

        for (const [toolName, meta] of Object.entries(toolRegistry)) {
            const toolNameLower = toolName.toLowerCase();
            const descLower = (meta.description || '').toLowerCase();
            const categoryLower = (meta.category || '').toLowerCase();

            let score = 0;
            let matchCount = 0;

            // Check each keyword
            for (const keyword of keywords) {
                // Exact match in tool name (highest priority)
                if (toolNameLower === keyword) {
                    score = Math.max(score, 1.0);
                    matchCount++;
                }
                // Tool name contains keyword
                else if (toolNameLower.includes(keyword)) {
                    score = Math.max(score, 0.8);
                    matchCount++;
                }
                // Description contains keyword
                else if (descLower.includes(keyword)) {
                    score = Math.max(score, 0.6);
                    matchCount++;
                }
                // Category contains keyword
                else if (categoryLower.includes(keyword)) {
                    score = Math.max(score, 0.4);
                    matchCount++;
                }
            }

            // Boost score if multiple keywords match
            if (matchCount > 1) {
                score = Math.min(1.0, score + (matchCount - 1) * 0.1);
            }

            if (score > 0) {
                results.push({
                    name: toolName,
                    description: meta.description,
                    category: meta.category,
                    score: score
                });
            }
        }
    }

    // Sort by similarity and limit
    results.sort((a, b) => b.score - a.score);

    return results.slice(0, limit);
}

/**
 * Get all available categories
 * @returns {Object} - Category descriptions
 */
export function getAvailableCategories() {
    const registry = loadToolRegistry();
    
    if (!registry) {
        // Fallback to categoryPatterns if registry not available
        const categories = {};
        for (const [name, patterns] of Object.entries(categoryPatterns)) {
            categories[name] = {
                count: patterns.length,
                description: getCategoryDescription(name)
            };
        }
        return categories;
    }
    
    // Count tools by category from registry
    const categoryCounts = {};
    for (const [toolName, meta] of Object.entries(registry)) {
        const category = meta.category;
        if (!categoryCounts[category]) {
            categoryCounts[category] = 0;
        }
        categoryCounts[category]++;
    }
    
    // Build category info
    const categories = {};
    for (const [category, count] of Object.entries(categoryCounts)) {
        categories[category] = {
            count: count,
            description: getCategoryDescription(category)
        };
    }
    
    return categories;
}

function getCategoryDescription(category) {
    const descriptions = {
        GameObject: 'Create, destroy, find, parent, tag, layer operations',
        Transform: 'Position, rotation, scale manipulation',
        Material: 'Create materials, set colors, shaders, textures, properties',
        Lighting: 'Lights, reflection probes, lightmaps, ambient lighting',
        Camera: 'Camera creation and configuration',
        Physics: 'Rigidbody, colliders, physics simulation, raycasts',
        UI: 'Canvas, UI elements, anchors, text, buttons, images',
        Animation: 'Animator, animation clips, state machines, curves',
        Cinemachine: 'Virtual cameras, follow, dolly, brain, blending',
        Scene: 'Scene management, loading, saving, building, info',
        GOAP: 'AI agents, goals, actions, planners, sensors',
        Audio: 'Audio sources, clips, mixers, spatial audio, music',
        Input: 'Touch, gesture, haptic, multitouch input handling',
        VFX: 'Visual effects, post-processing, bloom, particles',
        Shader: 'Shader creation, properties, textures, materials',
        Weather: 'Weather systems, rain, snow, fog, clouds',
        TimeOfDay: 'Day/night cycle, time events, sunrise/sunset',
        Editor: 'Unity Editor operations, inspector, console',
        Package: 'Package management, installation, updates',
        Build: 'Build settings, player settings, quality settings',
        Monitoring: 'Performance monitoring, events, file changes',
        AssetManagement: 'Asset organization, folders, naming, cleanup',
        Optimization: 'Performance optimization, draw calls, profiling',
        Batch: 'Batch operations on multiple objects',
        GameSystems: 'Game controllers, state machines, inventory',
        AI: 'AI pathfinding, neural networks, ML agents',
        Debug: 'Debugging tools, profiling, testing',
        Timeline: 'Timeline and avatar setup',
        Scripting: 'Script editing, analysis, modification',
        Screenshot: 'Screenshot capture tools',
        Utility: 'Undo, redo, search, find, asset management',
        Other: 'Miscellaneous tools'
    };
    return descriptions[category] || 'Various Unity tools';
}
