# Synaptic AI Pro - Tool Reference (350 Tools)

Complete reference for all available MCP tools in Synaptic AI Pro for Unity.

## Table of Contents
- [GameObject (11 tools)](#gameobject)
- [Transform (4 tools)](#transform)
- [Camera (13 tools)](#camera)
- [Cinemachine (9 tools)](#cinemachine)
- [Lighting (10 tools)](#lighting)
- [Material (9 tools)](#material)
- [Shader (13 tools)](#shader)
- [VFX (41 tools)](#vfx)
- [Animation (14 tools)](#animation)
- [Audio (14 tools)](#audio)
- [Physics (6 tools)](#physics)
- [UI (15 tools)](#ui)
- [Scene (10 tools)](#scene)
- [Input (13 tools)](#input)
- [Weather (9 tools)](#weather)
- [TimeOfDay (4 tools)](#timeofdday)
- [GOAP AI (10 tools)](#goap-ai)
- [AI (3 tools)](#ai)
- [GameSystems (5 tools)](#gamesystems)
- [Scripting (9 tools)](#scripting)
- [Editor (7 tools)](#editor)
- [Screenshot (4 tools)](#screenshot)
- [Monitoring (10 tools)](#monitoring)
- [AssetManagement (12 tools)](#assetmanagement)
- [Optimization (7 tools)](#optimization)
- [Batch (5 tools)](#batch)
- [Build (4 tools)](#build)
- [Package (3 tools)](#package)
- [Debug (6 tools)](#debug)
- [Timeline (2 tools)](#timeline)
- [Utility (26 tools)](#utility)
- [Other (42 tools)](#other)

---

## GameObject

| Tool | Description |
|------|-------------|
| `unity_create_gameobject` | Create a new GameObject in Unity scene |
| `unity_update_gameobject` | Update properties of an existing GameObject |
| `unity_delete_gameobject` | Delete a GameObject from the scene |
| `unity_add_component` | Add a component to a GameObject |
| `unity_update_component` | Update component properties |
| `unity_set_active_scene` | Set the active scene in multi-scene editing |
| `unity_get_gameobjects_list` | Get filtered list of GameObjects |
| `unity_get_gameobject_detail` | Get detailed information for a specific GameObject |
| `unity_duplicate_gameobject` | Duplicate an existing GameObject |
| `unity_find_gameobjects_by_component` | Find all GameObjects with specific component |
| `unity_get_gameobject_details` | Get detailed information about a specific GameObject |

---

## Transform

| Tool | Description |
|------|-------------|
| `unity_set_transform` | Set position, rotation, and scale of a GameObject |
| `unity_vfx_remove_block` | Remove a block from a VFX context |
| `unity_remove_package` | Remove an installed Unity package |
| `unity_move_asset` | Move an asset to a different folder |

---

## Camera

| Tool | Description |
|------|-------------|
| `unity_setup_camera` | Setup camera in the scene |
| `unity_create_virtual_camera` | Create a Cinemachine Virtual Camera with follow and look at targets |
| `unity_create_freelook_camera` | Create a Cinemachine FreeLook Camera for third-person orbiting |
| `unity_update_virtual_camera` | Update settings of an existing Cinemachine Virtual Camera |
| `unity_create_state_driven_camera` | Create a State-Driven Camera that switches based on Animator state |
| `unity_create_clear_shot_camera` | Create a Clear Shot Camera for dynamic shot selection |
| `unity_create_blend_list_camera` | Create a Blend List Camera that manages prioritized child cameras |
| `unity_set_camera_priority` | Change the priority of a Cinemachine virtual camera |
| `unity_set_camera_enabled` | Enable or disable a Cinemachine virtual camera |
| `unity_create_mixing_camera` | Create a Mixing Camera that blends multiple child cameras |
| `unity_update_camera_target` | Update Follow and/or LookAt targets of a Cinemachine camera |
| `unity_get_active_camera_info` | Get information about the currently active Cinemachine virtual camera |
| `unity_get_camera_info` | Get information about cameras in the scene |

---

## Cinemachine

| Tool | Description |
|------|-------------|
| `unity_setup_cinemachine_brain` | Setup Cinemachine Brain on a camera |
| `unity_create_dolly_track` | Create a Cinemachine Dolly Track with waypoints |
| `unity_add_confiner_extension` | Add Confiner Extension to restrict camera movement |
| `unity_create_impulse_source` | Create an Impulse Source for camera shake effects |
| `unity_add_impulse_listener` | Add an Impulse Listener extension to receive shake effects |
| `unity_create_target_group` | Create a Target Group for managing multiple camera targets |
| `unity_update_brain_blend_settings` | Update the default blend settings on the Cinemachine Brain |
| `unity_setup_blend_tree` | Create a blend tree for smooth animation transitions |
| `unity_setup_advanced_blend_tree` | Create complex blend trees for animation |

---

## Lighting

| Tool | Description |
|------|-------------|
| `unity_setup_lighting` | Setup lighting in the scene with extended options and presets |
| `unity_setup_lighting_preset` | Apply professional lighting presets to the scene |
| `unity_setup_reflection_probe` | Setup reflection probes for realistic reflections |
| `unity_create_light_probe_group` | Create light probe groups for dynamic GI |
| `unity_setup_volumetric_fog` | Create atmospheric volumetric fog effects |
| `unity_create_lightning_effect` | Create lightning strikes with flash effects |
| `unity_create_skybox_blend` | Blend between day and night skyboxes |
| `unity_create_skybox_from_image` | Create a Skybox from panoramic/HDRI, 6-sided, or sphere mode (for regular landscape photos) |
| `unity_setup_lighting_scenarios` | Create lighting scenarios for different moods |
| `unity_get_lighting_info` | Get information about lights in the scene |

---

## Material

| Tool | Description |
|------|-------------|
| `unity_create_material` | Create a new material |
| `unity_setup_material` | Create or modify materials with PBR properties |
| `unity_create_advanced_material` | Create materials with advanced settings and textures |
| `unity_create_material_property_block` | Modify material properties without creating instances |
| `unity_vfx_set_color_gradient` | Set color gradient for ColorOverLife blocks |
| `unity_set_material_property` | Modify properties of an existing material |
| `unity_get_material_properties` | Get all properties and values from a material |
| `unity_get_material_details` | Get detailed information about a material |
| `unity_get_material_info` | Get information about materials used in scene |

---

## Shader

| Tool | Description |
|------|-------------|
| `unity_create_shader_property_animator` | Animate shader properties like color, float, or vector |
| `unity_animate_shader_texture` | Create scrolling, flipbook, or rotating texture animations |
| `unity_create_shader_gradient` | Apply gradient effects to materials |
| `unity_create_shader_graph` | Create Shader Graph with natural language |
| `unity_create_water_material` | Create Genshin Impact-quality water with physics-based reflection |
| `unity_create_toon_material` | Create stylized toon/cel-shaded material |
| `unity_create_hair_material` | Create realistic/stylized hair material |
| `unity_create_eye_material` | Create realistic/stylized eye material |
| `unity_fix_urp_particle_shaders` | Fix particle materials for URP compatibility |
| `unity_read_shader` | Read the content of a .shader file |
| `unity_modify_shader` | Modify an existing .shader file |
| `unity_analyze_shader` | Analyze a shader's structure |
| `unity_read_shader_graph` | Read and parse a Unity ShaderGraph file |

---

## VFX

| Tool | Description |
|------|-------------|
| `unity_create_visual_effect` | Create complex visual effects combining particles, lights |
| `unity_create_decal` | Create decal projections for details |
| `unity_setup_color_grading` | Apply color grading for cinematic look |
| `unity_create_lens_flare` | Create realistic lens flare effects |
| `unity_create_screen_shake` | Apply screen shake effect for dramatic impact |
| `unity_create_screen_fade` | Create fade in/out transitions |
| `unity_create_vignette_effect` | Add cinematic vignette framing |
| `unity_create_chromatic_aberration` | Add lens distortion and color separation |
| `unity_vfx_set_spawn_rate` | Set the spawn rate of a VFX Graph |
| `unity_vfx_list_blocks` | List all contexts and blocks in a VFX Graph |
| `unity_vfx_get_block_info` | Get detailed information about a VFX block |
| `unity_create_bloom` | Create bloom effect with auto-generated shader |
| `unity_create_film_grain` | Create film grain effect |
| `unity_create_motion_blur` | Create motion blur effect |
| `unity_create_depth_of_field` | Create depth of field effect |
| `unity_create_lens_distortion` | Create lens distortion effect |
| `unity_setup_urp_settings` | Configure Universal Render Pipeline settings |
| `unity_setup_hdrp_settings` | Configure High Definition Render Pipeline settings |
| `unity_setup_post_processing` | Configure post-processing effects stack |
| `unity_create_vfx_graph` | Create VFX particle effects with advanced features |
| `unity_set_vfx_property` | Modify exposed properties of a VFX Graph |
| `unity_get_vfx_properties` | Get all exposed properties of a VFX Graph |
| `unity_trigger_vfx_event` | Send event to VFX Graph (play, stop, custom) |
| `unity_vfx_create` | Create a new VFX Graph asset |
| `unity_vfx_add_context` | Add a context (Spawn, Initialize, Update, Output) |
| `unity_vfx_add_block` | Add a block to a context |
| `unity_vfx_add_operator` | Add an operator node to VFX Graph |
| `unity_vfx_link_contexts` | Link two contexts together |
| `unity_vfx_get_structure` | Get the structure of a VFX Graph |
| `unity_vfx_compile` | Compile and save the VFX Graph |
| `unity_vfx_get_available_types` | List all available context, block, and operator types |
| `unity_vfx_add_parameter` | Add an exposed parameter to VFX Graph |
| `unity_vfx_connect_slots` | Connect output slot to input slot |
| `unity_vfx_set_attribute` | Set attribute value on a SetAttribute block |
| `unity_vfx_create_preset` | Create a complete VFX Graph from preset |
| `unity_vfx_configure_output` | Configure VFX output context settings |
| `unity_read_vfx_graph` | Read and analyze an existing VFX Graph |
| `unity_modify_vfx_graph` | Modify an existing VFX Graph asset |
| `unity_analyze_vfx_graph` | Analyze a VFX Graph asset structure |
| `unity_vfx_set_output` | Set output context settings on a VFX Graph |
| `unity_vfx_set_block_value` | Set a value on a VFX block |

---

## Animation

| Tool | Description |
|------|-------------|
| `unity_create_animation` | Create animation for GameObject |
| `unity_create_animator_controller` | Create a new Animator Controller |
| `unity_add_animation_state` | Add a new state to an Animator Controller |
| `unity_create_animation_clip` | Create a new animation clip with sample curves |
| `unity_add_animation_transition` | Create a transition between animation states |
| `unity_setup_animation_layer` | Add and configure an animation layer |
| `unity_create_animation_event` | Add an event to an animation clip |
| `unity_bake_animation` | Bake runtime animation into an animation clip |
| `unity_import_mixamo_animation` | Import and setup animation from Mixamo |
| `unity_organize_animation_assets` | Organize animation files into proper folder structure |
| `unity_create_animation_layer_mask` | Create an avatar mask for animation layers |
| `unity_retarget_animation` | Retarget animation from one avatar to another |
| `unity_analyze_animation_performance` | Analyze and optimize animation performance |
| `unity_get_animation_details` | Get detailed information about an animation clip |

---

## Audio

| Tool | Description |
|------|-------------|
| `unity_create_audio_mixer` | Create an audio mixer |
| `unity_create_audio_source` | Create and configure an AudioSource component |
| `unity_setup_3d_audio` | Configure 3D spatial audio settings |
| `unity_create_audio_clip` | Import audio file or create procedural audio clip |
| `unity_setup_audio_effects` | Add and configure audio effects |
| `unity_create_reverb_zones` | Create audio reverb zones |
| `unity_setup_audio_occlusion` | Configure audio occlusion |
| `unity_create_adaptive_music` | Create intro+loop music system |
| `unity_setup_audio_triggers` | Configure event-based audio triggers |
| `unity_create_sound_pools` | Create sound variation pools |
| `unity_create_audio_mixing` | Setup real-time audio mixing and ducking |
| `unity_setup_spatial_audio` | Configure advanced spatial audio for VR/AR |
| `unity_create_audio_visualization` | Create visual effects that react to audio |
| `unity_get_audio_details` | Get detailed information about an audio clip |

---

## Physics

| Tool | Description |
|------|-------------|
| `unity_setup_physics` | Setup physics settings for a GameObject or global |
| `unity_setup_navmesh` | Setup navigation mesh |
| `unity_add_collider_extension` | Add Collider Extension for obstacle avoidance |
| `unity_get_physics_settings` | Get Unity physics settings |
| `unity_get_physics_info` | Get information about physics objects in scene |
| `unity_setup_ui_navigation` | Create UI navigation system |

---

## UI

| Tool | Description |
|------|-------------|
| `unity_create_ui` | Create UI elements in Unity |
| `unity_capture_ui_element` | Capture a specific UI element by name |
| `unity_setup_ui_anchors` | Automatically setup anchors and pivots |
| `unity_setup_ui_animation` | Setup UI animations (fade, scale, slide) |
| `unity_create_ui_grid` | Create UI grid layout |
| `unity_create_ui_notification` | Create notification system |
| `unity_create_ui_dialog` | Create modal dialogs |
| `unity_optimize_ui_canvas` | Optimize Canvas for performance |
| `unity_apply_ui_theme` | Apply a complete theme to UI elements |
| `unity_set_ui_colors` | Set colors for UI elements |
| `unity_style_ui_elements` | Apply styling to UI elements |
| `unity_add_ui_effects` | Add visual effects to UI elements |
| `unity_get_ui_info` | Get information about UI elements in scene |
| `unity_setup_ui_canvas` | Configure UI Canvas with different render modes |
| `unity_set_ui_anchor` | Set UI element anchor presets |

---

## Scene

| Tool | Description |
|------|-------------|
| `unity_manage_scene` | Scene management operations |
| `unity_load_scene` | Load a scene in Editor mode |
| `unity_unload_scene` | Unload a scene from the Editor |
| `unity_list_all_scenes` | List all scene files in the project |
| `unity_add_scene_to_build` | Add or remove scenes from Build Settings |
| `unity_get_scene_info` | Get comprehensive scene information |
| `unity_get_scene_summary` | Get lightweight scene overview |
| `unity_get_scene_changes_since` | Get scene changes since a timestamp |
| `unity_capture_scene_view` | Capture a screenshot of the Scene View |
| `unity_pause_scene` | Pause or unpause the scene view |

---

## Input

| Tool | Description |
|------|-------------|
| `unity_setup_custom_input` | Configure custom input actions and bindings |
| `unity_create_gesture_recognition` | Setup gesture recognition for touch or motion |
| `unity_setup_haptic_feedback` | Configure haptic/vibration feedback |
| `unity_create_input_validation` | Create input validation system |
| `unity_setup_accessibility_input` | Configure accessibility features for input |
| `unity_create_input_recording` | Setup input recording and playback |
| `unity_setup_multitouch` | Configure multitouch input handling |
| `unity_create_pinch_zoom` | Setup pinch-to-zoom functionality |
| `unity_setup_swipe_detection` | Configure swipe gesture detection |
| `unity_create_drag_drop` | Setup drag and drop functionality |
| `unity_setup_touch_effects` | Configure visual effects for touch |
| `unity_get_input_settings` | Get Unity input settings |
| `unity_setup_input_system` | Setup Unity Input System with templates |

---

## Weather

| Tool | Description |
|------|-------------|
| `unity_create_terrain` | Create a terrain in Unity |
| `unity_modify_terrain` | Modify terrain height or textures |
| `unity_create_weather_system` | Create complete weather system |
| `unity_set_weather_preset` | Transition to a different weather preset |
| `unity_create_rain_effect` | Create realistic rain effect |
| `unity_create_snow_effect` | Create snow effect with falling snowflakes |
| `unity_create_wind_effect` | Create wind effect affecting objects |
| `unity_create_thunderstorm` | Create complete thunderstorm |
| `unity_get_terrain_info` | Get information about terrain |

---

## TimeOfDay

| Tool | Description |
|------|-------------|
| `unity_create_time_of_day` | Create dynamic day-night cycle |
| `unity_set_time_of_day` | Set specific time in the day-night cycle |
| `unity_create_day_night_preset` | Apply preset lighting conditions |
| `unity_create_time_event` | Create events triggered at specific times |

---

## GOAP AI

| Tool | Description |
|------|-------------|
| `unity_setup_behavior_tree` | Setup a behavior tree AI system |
| `unity_define_goap_goal` | Define a goal for GOAP agents |
| `unity_create_goap_action` | Create an action for GOAP agents |
| `unity_generate_goap_action_set` | Auto-generate action set based on agent type |
| `unity_setup_goap_world_state` | Configure the world state for GOAP planning |
| `unity_debug_goap_decisions` | Visualize and debug GOAP decisions |
| `unity_optimize_goap_performance` | Optimize GOAP agent performance |
| `unity_create_goap_agent` | Create a GOAP AI agent |
| `unity_define_behavior_language` | Define AI behavior using natural language |
| `unity_create_goap_template` | Create GOAP AI from professional templates |

---

## AI

| Tool | Description |
|------|-------------|
| `unity_setup_ml_agent` | Setup a Machine Learning Agent |
| `unity_create_neural_network` | Create a neural network system |
| `unity_create_ai_pathfinding` | Create AI pathfinding with A* algorithm |

---

## GameSystems

| Tool | Description |
|------|-------------|
| `unity_create_game_controller` | Create player controller for different game types |
| `unity_create_state_machine` | Create a state machine for character or game states |
| `unity_setup_inventory_system` | Create an inventory system with UI |
| `unity_create_game_template` | Create complete game templates |
| `unity_quick_prototype` | Create a quick playable prototype |

---

## Scripting

| Tool | Description |
|------|-------------|
| `unity_grep_scripts` | Search for patterns across script files |
| `unity_read_script_range` | Read a specific range of lines from a script |
| `unity_list_script_files` | List script files with glob pattern support |
| `unity_analyze_script` | Comprehensive script analysis for quality issues |
| `unity_modify_script` | Edit existing Unity scripts |
| `unity_edit_script_line` | Edit a specific line in a Unity script |
| `unity_add_script_method` | Add a new method to a Unity script |
| `unity_update_script_variable` | Update variable declaration or value |
| `unity_read_script` | Read the content of a Unity script file |

---

## Editor

| Tool | Description |
|------|-------------|
| `unity_force_refresh_assets` | Force Unity to refresh asset database |
| `unity_invoke_context_menu` | Invoke a [ContextMenu] method on a component |
| `unity_execute_menu_item` | Execute a Unity Editor menu item |
| `unity_get_inspector_info` | Get detailed inspector information |
| `unity_get_selected_object_info` | Get info for currently selected GameObject |
| `unity_get_component_details` | Get details about a specific component |
| `unity_console` | Unity console log operations |
| `unity_analyze_console_logs` | Detailed analysis of Unity console logs |

---

## Screenshot

| Tool | Description |
|------|-------------|
| `unity_capture_game_view` | Capture Game View including Canvas/UI |
| `unity_capture_region` | Capture a specific region |
| `unity_get_screenshot_result` | Retrieve screenshot capture result |
| `unity_capture_grid` | Split and capture Game View as grid |

---

## Monitoring

| Tool | Description |
|------|-------------|
| `unity_get_operation_history` | Get history of Unity operations |
| `unity_create_checkpoint` | Create a checkpoint to restore later |
| `unity_restore_checkpoint` | Restore a previously created checkpoint |
| `unity_monitor_play_state` | Monitor Unity play mode state changes |
| `unity_monitor_file_changes` | Monitor file changes in the project |
| `unity_monitor_compile` | Monitor script compilation events |
| `unity_subscribe_events` | Subscribe to Unity events |
| `unity_get_events` | Get recent Unity events |
| `unity_get_monitoring_status` | Get current monitoring status |
| `unity_monitor_runtime_errors` | Real-time runtime error monitoring |

---

## AssetManagement

| Tool | Description |
|------|-------------|
| `unity_get_asset_dependencies` | Get all dependencies of an asset |
| `unity_check_folder` | Check if folder exists |
| `unity_create_folder` | Create a new folder |
| `unity_list_folders` | List folders in a path |
| `unity_cleanup_empty_objects` | Remove empty GameObjects from scene |
| `unity_rename_asset` | Rename an asset file |
| `unity_delete_asset` | Delete an asset from the project |
| `unity_create_project_snapshot` | Create a snapshot of project state |
| `unity_analyze_dependencies` | Analyze and visualize asset dependencies |
| `unity_export_project_structure` | Export project folder structure |
| `unity_validate_naming_conventions` | Check if assets follow naming conventions |
| `unity_auto_organize_folders` | Automatically organize assets into folders |

---

## Optimization

| Tool | Description |
|------|-------------|
| `unity_optimize_textures_batch` | Batch optimize texture import settings |
| `unity_analyze_draw_calls` | Analyze draw call optimization opportunities |
| `unity_estimate_build_size` | Estimate build size for different platforms |
| `unity_performance_report` | Generate comprehensive performance report |
| `unity_generate_lod` | Automatically generate LOD groups |
| `unity_auto_atlas_textures` | Automatically create texture atlases |
| `unity_check_best_practices` | Analyze scripts for best practices compliance |

---

## Batch

| Tool | Description |
|------|-------------|
| `unity_extract_all_text` | Extract all text content for localization |
| `unity_batch_rename` | Batch rename multiple assets |
| `unity_batch_prefab_update` | Update multiple prefabs with changes |
| `unity_batch_material_apply` | Apply material to multiple objects |
| `unity_batch_prefab_create` | Create prefabs from multiple objects |

---

## Build

| Tool | Description |
|------|-------------|
| `unity_get_build_settings` | Get Unity build settings |
| `unity_get_player_settings` | Get Unity player settings |
| `unity_get_quality_settings` | Get Unity quality settings |
| `unity_get_project_summary` | Get overall project summary |

---

## Package

| Tool | Description |
|------|-------------|
| `unity_list_packages` | List all installed Unity packages |
| `unity_install_package` | Install a Unity package |
| `unity_check_package` | Check if a package is installed |

---

## Debug

| Tool | Description |
|------|-------------|
| `unity_control_game_speed` | Control Unity game speed (time scale) |
| `unity_profile_performance` | Get Unity performance profiling data |
| `unity_debug_draw` | Draw debug shapes in Unity scene |
| `unity_run_tests` | Run Unity Test Runner tests |
| `unity_manage_breakpoints` | Manage debugging breakpoints |
| `unity_debug_bt` | Get debug information about a behavior tree |

---

## Timeline

| Tool | Description |
|------|-------------|
| `unity_setup_avatar` | Configure avatar for a 3D model |
| `unity_create_timeline` | Create a Unity Timeline for cinematics |

---

## Utility

| Tool | Description |
|------|-------------|
| `unity_add_target_to_group` | Add target to Cinemachine Target Group |
| `unity_create_script` | Create a new C# script |
| `unity_create_particle_system` | Create a particle system |
| `unity_create_particle_preset` | Create advanced particle effects |
| `unity_place_objects` | Place multiple objects with pattern |
| `unity_undo_operation` | Undo last Unity operation |
| `unity_redo_operation` | Redo previously undone operation |
| `unity_list_assets` | List assets in the project |
| `unity_search_prefabs_by_component` | Search for prefabs with specific components |
| `unity_find_material_usage` | Find all objects using a material |
| `unity_find_texture_usage` | Find all materials using a texture |
| `unity_find_missing_references` | Find missing references in project |
| `unity_group_gameobjects` | Group multiple GameObjects under parent |
| `unity_batch_import_settings` | Apply import settings to multiple assets |
| `unity_find_unused_assets` | Find assets not referenced in project |
| `unity_create_responsive_ui` | Create responsive UI container |
| `unity_setup_scroll_view` | Create complete scroll view |
| `unity_setup_safe_area` | Setup Safe Area for mobile |
| `unity_get_asset_file_info` | Get file system info about an asset |
| `unity_get_asset_import_settings` | Get import settings for an asset |
| `unity_search_code` | Search for code patterns in project |
| `unity_auto_attach_ui` | Auto attach UI elements to component fields |
| `unity_create_prefab` | Create a prefab from a GameObject |
| `unity_read_particle_system` | Read all Particle System properties |
| `unity_modify_particle_system` | Modify Particle System properties |

---

## Other

42 additional tools for specialized functionality including:
- Behavior Trees (`unity_create_bt_agent`, `unity_add_bt_node`, etc.)
- Water systems (`unity_create_ocean_system`, `unity_add_buoyancy`, etc.)
- HDRP Water (`unity_create_hdrp_water`, `unity_set_hdrp_water_property`)
- Special effects (`unity_create_caustics`, `unity_trigger_dissolve`, etc.)
- Runtime status (`unity_get_runtime_status`, `unity_get_performance_metrics`, etc.)
- Communication (`unity_send_chat_response`, `unity_check_messages`)

---

## Version

- **Tool Count**: 350
- **Version**: 1.2.0
- **Last Updated**: 2026-01-19

---

*Generated from tool-registry.json*
