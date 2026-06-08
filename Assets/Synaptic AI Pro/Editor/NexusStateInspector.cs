using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.IO;
using Newtonsoft.Json;

namespace SynapticPro
{
    /// <summary>
    /// Unity project state inspection and reporting class
    /// Enables AI to understand current implementation status
    /// </summary>
    public static class NexusStateInspector
    {
        /// <summary>
        /// Get scene information
        /// </summary>
        public static string GetSceneInformation(bool includeHierarchy = true, int maxDepth = 3)
        {
            var scene = SceneManager.GetActiveScene();
            var info = new System.Text.StringBuilder();

            info.AppendLine($"🎬 Scene Information");
            info.AppendLine($"Name: {scene.name}");
            info.AppendLine($"Path: {scene.path}");
            info.AppendLine($"Build Index: {scene.buildIndex}");

            var rootObjects = scene.GetRootGameObjects();
            info.AppendLine($"Root GameObject Count: {rootObjects.Length}");

            if (includeHierarchy)
            {
                info.AppendLine("\n📊 Hierarchy Structure:");
                foreach (var root in rootObjects)
                {
                    info.Append(GetGameObjectHierarchy(root, 0, maxDepth));
                }
            }
            
            return info.ToString();
        }
        
        /// <summary>
        /// Get GameObject details
        /// </summary>
        public static string GetGameObjectDetails(string name)
        {
            var obj = GameObject.Find(name);
            if (obj == null) return $"❌ GameObject '{name}' not found";

            var info = new System.Text.StringBuilder();
            info.AppendLine($"🎯 GameObject Details: {obj.name}");
            info.AppendLine($"Path: {GetFullPath(obj)}");
            info.AppendLine($"Tag: {obj.tag}");
            info.AppendLine($"Layer: {LayerMask.LayerToName(obj.layer)} ({obj.layer})");
            info.AppendLine($"Active: {obj.activeSelf}");
            info.AppendLine($"Static: {obj.isStatic}");

            var transform = obj.transform;
            info.AppendLine($"\n📐 Transform:");
            info.AppendLine($"  Position: {transform.position}");
            info.AppendLine($"  Rotation: {transform.rotation.eulerAngles}");
            info.AppendLine($"  Scale: {transform.localScale}");
            info.AppendLine($"  Child Count: {transform.childCount}");

            info.AppendLine($"\n🔧 Components:");
            var components = obj.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp == null) continue;
                
                var compType = comp.GetType();
                info.AppendLine($"  • {compType.Name}");

                // Details of major components
                if (comp is Rigidbody rb)
                {
                    info.AppendLine($"    - Mass: {rb.mass}");
                    info.AppendLine($"    - Use Gravity: {rb.useGravity}");
                    info.AppendLine($"    - Kinematic: {rb.isKinematic}");
                }
                else if (comp is Collider col)
                {
                    info.AppendLine($"    - Trigger: {col.isTrigger}");
                    info.AppendLine($"    - Enabled: {col.enabled}");
                }
                else if (comp is Renderer rend)
                {
                    info.AppendLine($"    - Material Count: {rend.sharedMaterials.Length}");
                    info.AppendLine($"    - Enabled: {rend.enabled}");
                }
            }
            
            return info.ToString();
        }
        
        /// <summary>
        /// Get project asset information
        /// </summary>
        public static string GetProjectAssets(string assetType = "all", string folder = "Assets")
        {
            var info = new System.Text.StringBuilder();
            info.AppendLine($"📁 Asset Information ({folder})");
            
            string searchFilter = assetType switch
            {
                "scripts" => "t:MonoScript",
                "prefabs" => "t:Prefab",
                "materials" => "t:Material",
                "textures" => "t:Texture2D",
                "audio" => "t:AudioClip",
                _ => ""
            };
            
            string[] guids = AssetDatabase.FindAssets(searchFilter, new[] { folder });
            var assetsByType = new Dictionary<string, List<string>>();
            
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var type = AssetDatabase.GetMainAssetTypeAtPath(path);
                string typeName = type?.Name ?? "Unknown";
                
                if (!assetsByType.ContainsKey(typeName))
                    assetsByType[typeName] = new List<string>();
                
                assetsByType[typeName].Add(Path.GetFileName(path));
            }
            
            foreach (var kvp in assetsByType.OrderBy(k => k.Key))
            {
                info.AppendLine($"\n{kvp.Key} ({kvp.Value.Count} items):");
                foreach (var asset in kvp.Value.Take(10))
                {
                    info.AppendLine($"  - {asset}");
                }
                if (kvp.Value.Count > 10)
                {
                    info.AppendLine($"  ... {kvp.Value.Count - 10} more");
                }
            }
            
            return info.ToString();
        }
        
        /// <summary>
        /// Get overall project statistics
        /// </summary>
        public static string GetProjectStatistics()
        {
            try
            {
                // Scene statistics
                var scene = SceneManager.GetActiveScene();
                var allGameObjects = GameObject.FindObjectsOfType<GameObject>();

                // Component statistics
                var componentCounts = new Dictionary<string, int>();
                foreach (var go in allGameObjects)
                {
                    foreach (var comp in go.GetComponents<Component>())
                    {
                        if (comp == null) continue;
                        string typeName = comp.GetType().Name;
                        componentCounts[typeName] = componentCounts.GetValueOrDefault(typeName, 0) + 1;
                    }
                }
                
                // Asset statistics
                string[] allAssets = AssetDatabase.FindAssets("");
                var scripts = allAssets.Count(g => AssetDatabase.GUIDToAssetPath(g).EndsWith(".cs"));
                var prefabs = allAssets.Count(g => AssetDatabase.GUIDToAssetPath(g).EndsWith(".prefab"));
                var materials = allAssets.Count(g => AssetDatabase.GUIDToAssetPath(g).EndsWith(".mat"));
                var textures = allAssets.Count(g => AssetDatabase.GUIDToAssetPath(g).EndsWith(".png") ||
                                                        AssetDatabase.GUIDToAssetPath(g).EndsWith(".jpg"));

                // Nexus-created objects
                var nexusCreated = allGameObjects.Where(go => go.name.Contains("Nexus_") || 
                                                              go.name.Contains("HelloWorld") ||
                                                              go.name.Contains("UI")).ToList();
                
                var statistics = new Dictionary<string, object>
                {
                    ["scene_info"] = new Dictionary<string, object>
                    {
                        ["name"] = scene.name,
                        ["path"] = scene.path,
                        ["is_loaded"] = scene.isLoaded,
                        ["is_dirty"] = scene.isDirty
                    },
                    ["gameobject_statistics"] = new Dictionary<string, object>
                    {
                        ["total_count"] = allGameObjects.Length,
                        ["active_count"] = allGameObjects.Count(go => go.activeInHierarchy),
                        ["inactive_count"] = allGameObjects.Count(go => !go.activeInHierarchy)
                    },
                    ["component_statistics"] = componentCounts.OrderByDescending(k => k.Value).Take(10).ToDictionary(k => k.Key, k => k.Value),
                    ["asset_statistics"] = new Dictionary<string, object>
                    {
                        ["total_assets"] = allAssets.Length,
                        ["scripts"] = scripts,
                        ["prefabs"] = prefabs,
                        ["materials"] = materials,
                        ["textures"] = textures
                    },
                    ["nexus_created_objects"] = nexusCreated.Take(10).Select(obj => new Dictionary<string, object>
                    {
                        ["name"] = obj.name,
                        ["type"] = obj.GetType().Name,
                        ["active"] = obj.activeInHierarchy,
                        ["tag"] = obj.tag,
                        ["layer"] = obj.layer
                    }).ToList()
                };
                
                return JsonConvert.SerializeObject(statistics, Formatting.Indented);
            }
            catch (System.Exception e)
            {
                return $"Error getting project statistics: {e.Message}";
            }
        }
        
        /// <summary>
        /// Get camera information
        /// </summary>
        public static string GetCameraInformation()
        {
            try
            {
                var cameras = GameObject.FindObjectsOfType<Camera>();
                var mainCam = Camera.main;
                
                var cameraInfo = new Dictionary<string, object>
                {
                    ["camera_count"] = cameras.Length,
                    ["main_camera"] = mainCam != null ? mainCam.name : null,
                    ["cameras"] = cameras.Select(cam => new Dictionary<string, object>
                    {
                        ["name"] = cam.name,
                        ["enabled"] = cam.enabled,
                        ["transform"] = new Dictionary<string, object>
                        {
                            ["position"] = new Dictionary<string, float>
                            {
                                ["x"] = cam.transform.position.x,
                                ["y"] = cam.transform.position.y,
                                ["z"] = cam.transform.position.z
                            },
                            ["rotation"] = new Dictionary<string, float>
                            {
                                ["x"] = cam.transform.rotation.eulerAngles.x,
                                ["y"] = cam.transform.rotation.eulerAngles.y,
                                ["z"] = cam.transform.rotation.eulerAngles.z
                            }
                        },
                        ["camera_settings"] = new Dictionary<string, object>
                        {
                            ["field_of_view"] = cam.fieldOfView,
                            ["orthographic"] = cam.orthographic,
                            ["orthographic_size"] = cam.orthographic ? cam.orthographicSize : null,
                            ["depth"] = cam.depth,
                            ["culling_mask"] = cam.cullingMask,
                            ["background_color"] = new Dictionary<string, float>
                            {
                                ["r"] = cam.backgroundColor.r,
                                ["g"] = cam.backgroundColor.g,
                                ["b"] = cam.backgroundColor.b,
                                ["a"] = cam.backgroundColor.a
                            },
                            ["clear_flags"] = cam.clearFlags.ToString(),
                            ["near_clip_plane"] = cam.nearClipPlane,
                            ["far_clip_plane"] = cam.farClipPlane
                        },
                        ["render_settings"] = new Dictionary<string, object>
                        {
                            ["render_texture"] = cam.targetTexture != null ? cam.targetTexture.name : null,
                            ["allow_hdr"] = cam.allowHDR,
                            ["allow_msaa"] = cam.allowMSAA,
                            ["use_physical_properties"] = cam.usePhysicalProperties
                        },
                        ["has_post_process"] = cam.GetComponent("PostProcessVolume") != null
                    }).ToList()
                };
                
                return JsonConvert.SerializeObject(cameraInfo, Formatting.Indented);
            }
            catch (System.Exception e)
            {
                return $"Error getting camera information: {e.Message}";
            }
        }
        
        /// <summary>
        /// Get terrain information
        /// </summary>
        public static string GetTerrainInformation()
        {
            try
            {
                var terrains = GameObject.FindObjectsOfType<Terrain>();
                
                var terrainInfo = new Dictionary<string, object>
                {
                    ["terrain_count"] = terrains.Length,
                    ["terrains"] = terrains.Select(terrain => {
                        var data = terrain.terrainData;
                        return new Dictionary<string, object>
                        {
                            ["name"] = terrain.name,
                            ["position"] = new Dictionary<string, float>
                            {
                                ["x"] = terrain.transform.position.x,
                                ["y"] = terrain.transform.position.y,
                                ["z"] = terrain.transform.position.z
                            },
                            ["terrain_data"] = new Dictionary<string, object>
                            {
                                ["size"] = new Dictionary<string, float>
                                {
                                    ["x"] = data.size.x,
                                    ["y"] = data.size.y,
                                    ["z"] = data.size.z
                                },
                                ["heightmap_resolution"] = data.heightmapResolution,
                                ["detail_resolution"] = data.detailResolution,
                                ["alphamap_resolution"] = data.alphamapResolution,
                                ["base_map_resolution"] = data.baseMapResolution
                            },
                            ["terrain_layers"] = new Dictionary<string, object>
                            {
                                ["count"] = data.terrainLayers?.Length ?? 0,
                                ["layers"] = data.terrainLayers != null ? 
                                    data.terrainLayers.Take(5).Where(layer => layer != null).Select(layer => new Dictionary<string, object>
                                    {
                                        ["name"] = layer.name,
                                        ["diffuse_texture"] = layer.diffuseTexture?.name,
                                        ["normal_map"] = layer.normalMapTexture?.name,
                                        ["tile_size"] = new Dictionary<string, float>
                                        {
                                            ["x"] = layer.tileSize.x,
                                            ["y"] = layer.tileSize.y
                                        }
                                    }).ToList() : new List<Dictionary<string, object>>()
                            },
                            ["vegetation"] = new Dictionary<string, object>
                            {
                                ["tree_prototype_count"] = data.treePrototypes?.Length ?? 0,
                                ["detail_prototype_count"] = data.detailPrototypes?.Length ?? 0,
                                ["tree_instances"] = data.treeInstanceCount
                            },
                            ["settings"] = new Dictionary<string, object>
                            {
                                ["pixel_error"] = terrain.heightmapPixelError,
                                ["base_map_distance"] = terrain.basemapDistance,
                                ["detail_object_distance"] = terrain.detailObjectDistance,
                                ["tree_distance"] = terrain.treeDistance,
                                ["tree_billboard_distance"] = terrain.treeBillboardDistance
                            }
                        };
                    }).ToList()
                };
                
                return JsonConvert.SerializeObject(terrainInfo, Formatting.Indented);
            }
            catch (System.Exception e)
            {
                return $"Error getting terrain information: {e.Message}";
            }
        }
        
        /// <summary>
        /// Get lighting information
        /// </summary>
        public static string GetLightingInformation()
        {
            try
            {
                var lights = GameObject.FindObjectsOfType<Light>();
                var probes = GameObject.FindObjectsOfType<ReflectionProbe>();
                
                var lightingInfo = new Dictionary<string, object>
                {
                    ["ambient_settings"] = new Dictionary<string, object>
                    {
                        ["mode"] = RenderSettings.ambientMode.ToString(),
                        ["intensity"] = RenderSettings.ambientIntensity,
                        ["color"] = new Dictionary<string, float>
                        {
                            ["r"] = RenderSettings.ambientLight.r,
                            ["g"] = RenderSettings.ambientLight.g,
                            ["b"] = RenderSettings.ambientLight.b,
                            ["a"] = RenderSettings.ambientLight.a
                        },
                        ["skybox"] = RenderSettings.skybox != null ? RenderSettings.skybox.name : null
                    },
                    ["fog_settings"] = new Dictionary<string, object>
                    {
                        ["enabled"] = RenderSettings.fog,
                        ["mode"] = RenderSettings.fogMode.ToString(),
                        ["color"] = new Dictionary<string, float>
                        {
                            ["r"] = RenderSettings.fogColor.r,
                            ["g"] = RenderSettings.fogColor.g,
                            ["b"] = RenderSettings.fogColor.b,
                            ["a"] = RenderSettings.fogColor.a
                        },
                        ["density"] = RenderSettings.fogDensity,
                        ["start_distance"] = RenderSettings.fogStartDistance,
                        ["end_distance"] = RenderSettings.fogEndDistance
                    },
                    ["lights"] = new Dictionary<string, object>
                    {
                        ["count"] = lights.Length,
                        ["light_list"] = lights.Select(light => new Dictionary<string, object>
                        {
                            ["name"] = light.name,
                            ["enabled"] = light.enabled,
                            ["type"] = light.type.ToString(),
                            ["color"] = new Dictionary<string, float>
                            {
                                ["r"] = light.color.r,
                                ["g"] = light.color.g,
                                ["b"] = light.color.b,
                                ["a"] = light.color.a
                            },
                            ["intensity"] = light.intensity,
                            ["range"] = light.range,
                            ["spot_angle"] = light.spotAngle,
                            ["shadows"] = light.shadows.ToString(),
                            ["transform"] = light.type == LightType.Directional ? 
                                new Dictionary<string, object>
                                {
                                    ["rotation"] = new Dictionary<string, float>
                                    {
                                        ["x"] = light.transform.rotation.eulerAngles.x,
                                        ["y"] = light.transform.rotation.eulerAngles.y,
                                        ["z"] = light.transform.rotation.eulerAngles.z
                                    }
                                } :
                                new Dictionary<string, object>
                                {
                                    ["position"] = new Dictionary<string, float>
                                    {
                                        ["x"] = light.transform.position.x,
                                        ["y"] = light.transform.position.y,
                                        ["z"] = light.transform.position.z
                                    }
                                }
                        }).ToList()
                    },
                    ["reflection_probes"] = new Dictionary<string, object>
                    {
                        ["count"] = probes.Length,
                        ["probes"] = probes.Select(probe => new Dictionary<string, object>
                        {
                            ["name"] = probe.name,
                            ["enabled"] = probe.enabled,
                            ["resolution"] = probe.resolution,
                            ["hdr"] = probe.hdr,
                            ["clear_flags"] = probe.clearFlags.ToString(),
                            ["culling_mask"] = probe.cullingMask
                        }).ToList()
                    }
                };
                
                var settings = new JsonSerializerSettings
                {
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                    NullValueHandling = NullValueHandling.Ignore
                };
                return JsonConvert.SerializeObject(lightingInfo, Formatting.Indented, settings);
            }
            catch (System.Exception e)
            {
                return $"Error getting lighting information: {e.Message}";
            }
        }

        /// <summary>
        /// Get material information
        /// </summary>
        public static string GetMaterialInformation()
        {
            try
            {
                // Materials used in scene
                var renderers = GameObject.FindObjectsOfType<Renderer>();
                var usedMaterials = new HashSet<Material>();

                foreach (var renderer in renderers)
                {
                    foreach (var mat in renderer.sharedMaterials)
                    {
                        if (mat != null)
                        {
                            usedMaterials.Add(mat);
                        }
                    }
                }

                // Classify by shader
                var materialsByShader = usedMaterials.GroupBy(m => m.shader.name);

                // Material assets in project
                var materialAssets = AssetDatabase.FindAssets("t:Material");
                
                var materialInfo = new Dictionary<string, object>
                {
                    ["scene_materials"] = new Dictionary<string, object>
                    {
                        ["total_count"] = usedMaterials.Count,
                        ["by_shader"] = materialsByShader.Select(group => new Dictionary<string, object>
                        {
                            ["shader_name"] = group.Key,
                            ["count"] = group.Count(),
                            ["materials"] = group.Take(5).Select(mat => 
                            {
                                var matData = new Dictionary<string, object>
                                {
                                    ["name"] = mat.name,
                                    ["shader"] = mat.shader.name,
                                    ["render_queue"] = mat.renderQueue,
                                    ["keywords"] = mat.shaderKeywords.ToList()
                                };

                                // Color properties
                                if (mat.HasProperty("_Color") || mat.HasProperty("_BaseColor"))
                                {
                                    var color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : mat.GetColor("_BaseColor");
                                    matData["color"] = new Dictionary<string, float>
                                    {
                                        ["r"] = color.r,
                                        ["g"] = color.g,
                                        ["b"] = color.b,
                                        ["a"] = color.a
                                    };
                                }
                                
                                // Texture properties
                                if (mat.HasProperty("_MainTex") || mat.HasProperty("_BaseMap"))
                                {
                                    var tex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : mat.GetTexture("_BaseMap");
                                    if (tex != null)
                                    {
                                        matData["main_texture"] = new Dictionary<string, object>
                                        {
                                            ["name"] = tex.name,
                                            ["width"] = tex.width,
                                            ["height"] = tex.height
                                        };
                                    }
                                }
                                
                                // PBR properties
                                var pbrData = new Dictionary<string, object>();
                                if (mat.HasProperty("_Metallic"))
                                {
                                    pbrData["metallic"] = mat.GetFloat("_Metallic");
                                }
                                if (mat.HasProperty("_Glossiness"))
                                {
                                    pbrData["smoothness"] = mat.GetFloat("_Glossiness");
                                }
                                else if (mat.HasProperty("_Smoothness"))
                                {
                                    pbrData["smoothness"] = mat.GetFloat("_Smoothness");
                                }
                                if (mat.HasProperty("_BumpMap") || mat.HasProperty("_NormalMap"))
                                {
                                    var normalMap = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : mat.GetTexture("_NormalMap");
                                    pbrData["has_normal_map"] = normalMap != null;
                                }
                                if (pbrData.Count > 0)
                                {
                                    matData["pbr_properties"] = pbrData;
                                }
                                
                                return matData;
                            }).ToList()
                        }).ToList()
                    },
                    ["project_materials"] = new Dictionary<string, object>
                    {
                        ["total_count"] = materialAssets.Length,
                        ["paths"] = materialAssets.Take(20).Select(guid => AssetDatabase.GUIDToAssetPath(guid)).ToList()
                    }
                };
                
                return JsonConvert.SerializeObject(materialInfo, Formatting.Indented);
            }
            catch (System.Exception e)
            {
                return $"Error getting material information: {e.Message}";
            }
        }
        
        /// <summary>
        /// Get UI information
        /// </summary>
        public static string GetUIInformation()
        {
#if UNITY_EDITOR
            try
            {
                var canvases = GameObject.FindObjectsOfType<Canvas>();
                var eventSystem = GameObject.FindObjectOfType<UnityEngine.EventSystems.EventSystem>();

                var uiInfo = new Dictionary<string, object>
                {
                    ["canvas_count"] = canvases.Length,
                    ["canvases"] = canvases.Select(canvas =>
                    {
                        // Aggregate UI elements
                        var buttons = canvas.GetComponentsInChildren<UnityEngine.UI.Button>(true);
                        var texts = canvas.GetComponentsInChildren<UnityEngine.UI.Text>(true);
                        var images = canvas.GetComponentsInChildren<UnityEngine.UI.Image>(true);
                        var inputFields = canvas.GetComponentsInChildren<UnityEngine.UI.InputField>(true);
                        var sliders = canvas.GetComponentsInChildren<UnityEngine.UI.Slider>(true);
                        var toggles = canvas.GetComponentsInChildren<UnityEngine.UI.Toggle>(true);
                        var scrollViews = canvas.GetComponentsInChildren<UnityEngine.UI.ScrollRect>(true);
                        
                        return new Dictionary<string, object>
                        {
                            ["name"] = canvas.name,
                            ["enabled"] = canvas.enabled,
                            ["render_mode"] = canvas.renderMode.ToString(),
                            ["sorting_order"] = canvas.sortingOrder,
                            ["sorting_layer"] = canvas.sortingLayerName,
                            ["pixel_perfect"] = canvas.pixelPerfect,
                            ["ui_elements"] = new Dictionary<string, object>
                            {
                                ["buttons"] = new Dictionary<string, object>
                                {
                                    ["count"] = buttons.Length,
                                    ["items"] = buttons.Take(5).Select(btn => {
                                        var btnText = btn.GetComponentInChildren<UnityEngine.UI.Text>();
                                        return new Dictionary<string, object>
                                        {
                                            ["name"] = btn.name,
                                            ["text"] = btnText != null ? btnText.text : null,
                                            ["interactable"] = btn.interactable,
                                            ["active"] = btn.gameObject.activeInHierarchy
                                        };
                                    }).ToList()
                                },
                                ["texts"] = new Dictionary<string, object>
                                {
                                    ["count"] = texts.Length,
                                    ["items"] = texts.Take(5).Select(txt => new Dictionary<string, object>
                                    {
                                        ["name"] = txt.name,
                                        ["text"] = txt.text,
                                        ["font"] = txt.font != null ? txt.font.name : null,
                                        ["font_size"] = txt.fontSize,
                                        ["color"] = new Dictionary<string, float>
                                        {
                                            ["r"] = txt.color.r,
                                            ["g"] = txt.color.g,
                                            ["b"] = txt.color.b,
                                            ["a"] = txt.color.a
                                        }
                                    }).ToList()
                                },
                                ["images"] = new Dictionary<string, object>
                                {
                                    ["count"] = images.Length,
                                    ["items"] = images.Take(5).Select(img => new Dictionary<string, object>
                                    {
                                        ["name"] = img.name,
                                        ["sprite_name"] = img.sprite != null ? img.sprite.name : "(none)",
                                        ["color"] = new Dictionary<string, float>
                                        {
                                            ["r"] = img.color.r,
                                            ["g"] = img.color.g,
                                            ["b"] = img.color.b,
                                            ["a"] = img.color.a
                                        },
                                        ["raycast_target"] = img.raycastTarget,
                                        ["active"] = img.gameObject.activeInHierarchy
                                    }).ToList()
                                },
                                ["input_fields"] = inputFields.Length,
                                ["sliders"] = sliders.Length,
                                ["toggles"] = toggles.Length,
                                ["scroll_views"] = scrollViews.Length
                            }
                        };
                    }).ToList(),
                    ["event_system"] = eventSystem != null ? new Dictionary<string, object>
                    {
                        ["name"] = eventSystem.name,
                        ["current_selected"] = eventSystem.currentSelectedGameObject != null ? eventSystem.currentSelectedGameObject.name : null,
                        ["send_navigation_events"] = eventSystem.sendNavigationEvents,
                        ["pixel_drag_threshold"] = eventSystem.pixelDragThreshold
                    } : null
                };

                var settings = new JsonSerializerSettings
                {
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                    NullValueHandling = NullValueHandling.Ignore
                };
                return JsonConvert.SerializeObject(uiInfo, Formatting.Indented, settings);
            }
            catch (System.Exception e)
            {
                return $"Error getting UI information: {e.Message}";
            }
#else
            return "UI information is only available in Unity Editor";
#endif
        }
        
        /// <summary>
        /// Get physics information
        /// </summary>
        public static string GetPhysicsInformation()
        {
            try
            {
                var rigidbodies = GameObject.FindObjectsOfType<Rigidbody>();
                var colliders = GameObject.FindObjectsOfType<Collider>();
                var joints = GameObject.FindObjectsOfType<Joint>();
                
                var physicsInfo = new Dictionary<string, object>
                {
                    ["global_settings"] = new Dictionary<string, object>
                    {
                        ["gravity"] = new Dictionary<string, float>
                        {
                            ["x"] = Physics.gravity.x,
                            ["y"] = Physics.gravity.y,
                            ["z"] = Physics.gravity.z
                        },
                        ["default_solver_iterations"] = Physics.defaultSolverIterations,
                        ["default_solver_velocity_iterations"] = Physics.defaultSolverVelocityIterations,
                        ["bounce_threshold"] = Physics.bounceThreshold,
                        ["sleep_threshold"] = Physics.sleepThreshold,
                        ["default_contact_offset"] = Physics.defaultContactOffset,
                        ["queries_hit_triggers"] = Physics.queriesHitTriggers
                    },
                    ["rigidbodies"] = new Dictionary<string, object>
                    {
                        ["total_count"] = rigidbodies.Length,
                        ["items"] = rigidbodies.Take(10).Select(rb => new Dictionary<string, object>
                        {
                            ["name"] = rb.name,
                            ["mass"] = rb.mass,
                            ["drag"] = rb.linearDamping,
                            ["angular_drag"] = rb.angularDamping,
                            ["use_gravity"] = rb.useGravity,
                            ["is_kinematic"] = rb.isKinematic,
                            ["constraints"] = rb.constraints.ToString(),
                            ["velocity"] = new Dictionary<string, object>
                            {
                                ["magnitude"] = rb.linearVelocity.magnitude,
                                ["vector"] = new Dictionary<string, float>
                                {
                                    ["x"] = rb.linearVelocity.x,
                                    ["y"] = rb.linearVelocity.y,
                                    ["z"] = rb.linearVelocity.z
                                }
                            },
                            ["angular_velocity"] = new Dictionary<string, float>
                            {
                                ["x"] = rb.angularVelocity.x,
                                ["y"] = rb.angularVelocity.y,
                                ["z"] = rb.angularVelocity.z
                            }
                        }).ToList()
                    },
                    ["colliders"] = new Dictionary<string, object>
                    {
                        ["total_count"] = colliders.Length,
                        ["by_type"] = colliders.GroupBy(c => c.GetType().Name).Select(group => new Dictionary<string, object>
                        {
                            ["type"] = group.Key,
                            ["count"] = group.Count()
                        }).ToList(),
                        ["triggers"] = colliders.Count(c => c.isTrigger),
                        ["non_triggers"] = colliders.Count(c => !c.isTrigger)
                    },
                    ["joints"] = new Dictionary<string, object>
                    {
                        ["total_count"] = joints.Length,
                        ["by_type"] = joints.GroupBy(j => j.GetType().Name).Select(group => new Dictionary<string, object>
                        {
                            ["type"] = group.Key,
                            ["count"] = group.Count()
                        }).ToList()
                    }
                };
                
                return JsonConvert.SerializeObject(physicsInfo, Formatting.Indented);
            }
            catch (System.Exception e)
            {
                return $"Error getting physics information: {e.Message}";
            }
        }
        
        /// <summary>
        /// Check implementation progress
        /// </summary>
        public static string GetImplementationProgress()
        {
            var info = new System.Text.StringBuilder();
            info.AppendLine("📈 Implementation Progress Checklist");

            // Check UI elements
            var canvas = GameObject.FindObjectOfType<Canvas>();
            info.AppendLine($"\n🖼️ UI Implementation:");
            info.AppendLine($"  ✓ Canvas: {(canvas != null ? "Present" : "None")}");
            if (canvas != null)
            {
                var buttons = canvas.GetComponentsInChildren<UnityEngine.UI.Button>();
                var texts = canvas.GetComponentsInChildren<UnityEngine.UI.Text>();
                var images = canvas.GetComponentsInChildren<UnityEngine.UI.Image>();

                info.AppendLine($"  ✓ Buttons: {buttons.Length}");
                info.AppendLine($"  ✓ Texts: {texts.Length}");
                info.AppendLine($"  ✓ Images: {images.Length}");
            }

            // Check GameObjects
            info.AppendLine($"\n🎮 GameObjects:");
            var primitives = new[] { "Cube", "Sphere", "Cylinder", "Plane", "Capsule" };
            foreach (var prim in primitives)
            {
                var count = GameObject.FindObjectsOfType<GameObject>()
                    .Count(go => go.name.Contains(prim));
                if (count > 0)
                {
                    info.AppendLine($"  ✓ {prim}: {count}");
                }
            }

            // Check script implementation
            var customScripts = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" })
                .Select(g => AssetDatabase.GUIDToAssetPath(g))
                .Where(p => !p.Contains("/Editor/") && !p.Contains("Nexus"))
                .Select(p => Path.GetFileName(p))
                .ToList();

            info.AppendLine($"\n📝 Custom Scripts ({customScripts.Count}):");
            foreach (var script in customScripts.Take(5))
            {
                info.AppendLine($"  - {script}");
            }
            
            return info.ToString();
        }
        
        // Helper methods
        private static string GetGameObjectHierarchy(GameObject obj, int depth, int maxDepth)
        {
            if (depth >= maxDepth) return "";

            var indent = new string(' ', depth * 2);
            var info = new System.Text.StringBuilder();

            // Object name and components
            info.Append($"{indent}├─ {obj.name}");

            var components = obj.GetComponents<Component>()
                .Where(c => c != null && !(c is Transform))
                .Select(c => c.GetType().Name);

            if (components.Any())
            {
                info.Append($" [{string.Join(", ", components)}]");
            }

            if (!obj.activeInHierarchy)
            {
                info.Append(" (Inactive)");
            }

            info.AppendLine();

            // Child objects
            foreach (Transform child in obj.transform)
            {
                info.Append(GetGameObjectHierarchy(child.gameObject, depth + 1, maxDepth));
            }
            
            return info.ToString();
        }
        
        private static string GetFullPath(GameObject obj)
        {
            var path = obj.name;
            var parent = obj.transform.parent;
            
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            
            return path;
        }
    }
}