using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;

namespace SynapticPro
{
    /// <summary>
    /// Dynamic Meta-Tools for NexusUnityExecutor
    /// Provides reflection-based inspection and modification of Unity components
    /// </summary>
    public partial class NexusUnityExecutor
    {
        #region Dynamic Meta-Tools

        /// <summary>
        /// Dynamically inspect any Unity object, component, scene, or project assets
        /// </summary>
        private string DynamicInspect(Dictionary<string, string> parameters)
        {
            try
            {
                var target = parameters.GetValueOrDefault("target", "gameobject").ToLower();
                var name = parameters.GetValueOrDefault("name", "");
                var componentType = parameters.GetValueOrDefault("component", "");
                var path = parameters.GetValueOrDefault("path", "");
                int.TryParse(parameters.GetValueOrDefault("depth", "2"), out int depth);

                switch (target)
                {
                    case "gameobject":
                        return InspectGameObject(name, depth);

                    case "component":
                        return InspectComponent(name, componentType, depth);

                    case "scene":
                        return InspectScene();

                    case "hierarchy":
                        return InspectHierarchy(depth);

                    case "prefabs":
                        return InspectPrefabs(path);

                    case "project":
                        return InspectProject(path);

                    default:
                        return JsonConvert.SerializeObject(new { error = $"Unknown inspect target: {target}" });
                }
            }
            catch (Exception e)
            {
                return CreateErrorResponse("DynamicInspect", e, parameters);
            }
        }

        private string InspectGameObject(string name, int depth)
        {
            if (string.IsNullOrEmpty(name))
            {
                // List all root GameObjects
                var rootObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene()
                    .GetRootGameObjects()
                    .Select(go => new {
                        name = go.name,
                        active = go.activeSelf,
                        components = go.GetComponents<Component>().Select(c => c?.GetType().Name).Where(n => n != null).ToList(),
                        childCount = go.transform.childCount
                    }).ToList();

                return JsonConvert.SerializeObject(new {
                    success = true,
                    message = $"Found {rootObjects.Count} root GameObjects",
                    gameObjects = rootObjects
                });
            }

            var gameObject = GameObject.Find(name);
            if (gameObject == null)
            {
                gameObject = FindGameObjectByPathDynamic(name);
            }

            if (gameObject == null)
            {
                return JsonConvert.SerializeObject(new { error = $"GameObject '{name}' not found" });
            }

            var components = new List<object>();
            foreach (var comp in gameObject.GetComponents<Component>())
            {
                if (comp == null) continue;
                components.Add(new {
                    type = comp.GetType().Name,
                    fullType = comp.GetType().FullName,
                    enabled = (comp is Behaviour b) ? b.enabled : true
                });
            }

            var children = new List<object>();
            if (depth > 0)
            {
                foreach (Transform child in gameObject.transform)
                {
                    children.Add(new {
                        name = child.name,
                        active = child.gameObject.activeSelf,
                        componentCount = child.GetComponents<Component>().Length,
                        childCount = child.childCount
                    });
                }
            }

            return JsonConvert.SerializeObject(new {
                success = true,
                gameObject = new {
                    name = gameObject.name,
                    active = gameObject.activeSelf,
                    layer = LayerMask.LayerToName(gameObject.layer),
                    tag = gameObject.tag,
                    isStatic = gameObject.isStatic,
                    transform = new {
                        position = new { x = gameObject.transform.position.x, y = gameObject.transform.position.y, z = gameObject.transform.position.z },
                        rotation = new { x = gameObject.transform.eulerAngles.x, y = gameObject.transform.eulerAngles.y, z = gameObject.transform.eulerAngles.z },
                        scale = new { x = gameObject.transform.localScale.x, y = gameObject.transform.localScale.y, z = gameObject.transform.localScale.z }
                    },
                    components = components,
                    children = children
                }
            });
        }

        private string InspectComponent(string gameObjectName, string componentType, int depth)
        {
            if (string.IsNullOrEmpty(gameObjectName))
            {
                return JsonConvert.SerializeObject(new { error = "GameObject name required" });
            }

            var gameObject = GameObject.Find(gameObjectName) ?? FindGameObjectByPathDynamic(gameObjectName);
            if (gameObject == null)
            {
                return JsonConvert.SerializeObject(new { error = $"GameObject '{gameObjectName}' not found" });
            }

            Component component = null;
            if (!string.IsNullOrEmpty(componentType))
            {
                component = gameObject.GetComponents<Component>()
                    .FirstOrDefault(c => c != null &&
                        (c.GetType().Name.Equals(componentType, StringComparison.OrdinalIgnoreCase) ||
                         c.GetType().Name.EndsWith(componentType, StringComparison.OrdinalIgnoreCase)));
            }

            if (component == null && !string.IsNullOrEmpty(componentType))
            {
                return JsonConvert.SerializeObject(new {
                    error = $"Component '{componentType}' not found on '{gameObjectName}'",
                    availableComponents = gameObject.GetComponents<Component>()
                        .Where(c => c != null)
                        .Select(c => c.GetType().Name)
                        .ToList()
                });
            }

            // If no specific component, list all with their properties
            if (component == null)
            {
                var allComponents = new List<object>();
                foreach (var comp in gameObject.GetComponents<Component>())
                {
                    if (comp == null) continue;
                    allComponents.Add(new {
                        type = comp.GetType().Name,
                        properties = GetSerializedPropertiesDynamic(comp, depth)
                    });
                }
                return JsonConvert.SerializeObject(new {
                    success = true,
                    gameObject = gameObjectName,
                    components = allComponents
                });
            }

            // Inspect specific component
            var properties = GetSerializedPropertiesDynamic(component, depth);
            return JsonConvert.SerializeObject(new {
                success = true,
                gameObject = gameObjectName,
                component = componentType,
                type = component.GetType().FullName,
                properties = properties
            });
        }

        private List<object> GetSerializedPropertiesDynamic(Component component, int depth)
        {
            var properties = new List<object>();
            var so = new SerializedObject(component);
            var iterator = so.GetIterator();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                if (iterator.depth > depth)
                {
                    enterChildren = false;
                    continue;
                }
                enterChildren = true;

                var propInfo = new Dictionary<string, object>
                {
                    ["path"] = iterator.propertyPath,
                    ["name"] = iterator.name,
                    ["type"] = iterator.propertyType.ToString(),
                    ["editable"] = iterator.editable
                };

                // Get value based on type
                switch (iterator.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        propInfo["value"] = iterator.intValue;
                        break;
                    case SerializedPropertyType.Float:
                        propInfo["value"] = iterator.floatValue;
                        break;
                    case SerializedPropertyType.Boolean:
                        propInfo["value"] = iterator.boolValue;
                        break;
                    case SerializedPropertyType.String:
                        propInfo["value"] = iterator.stringValue;
                        break;
                    case SerializedPropertyType.Enum:
                        propInfo["value"] = iterator.enumDisplayNames?.Length > iterator.enumValueIndex && iterator.enumValueIndex >= 0
                            ? iterator.enumDisplayNames[iterator.enumValueIndex]
                            : iterator.enumValueIndex.ToString();
                        propInfo["options"] = iterator.enumDisplayNames;
                        break;
                    case SerializedPropertyType.Vector2:
                        propInfo["value"] = new { x = iterator.vector2Value.x, y = iterator.vector2Value.y };
                        break;
                    case SerializedPropertyType.Vector3:
                        propInfo["value"] = new { x = iterator.vector3Value.x, y = iterator.vector3Value.y, z = iterator.vector3Value.z };
                        break;
                    case SerializedPropertyType.Vector4:
                        propInfo["value"] = new { x = iterator.vector4Value.x, y = iterator.vector4Value.y, z = iterator.vector4Value.z, w = iterator.vector4Value.w };
                        break;
                    case SerializedPropertyType.Color:
                        propInfo["value"] = new { r = iterator.colorValue.r, g = iterator.colorValue.g, b = iterator.colorValue.b, a = iterator.colorValue.a };
                        break;
                    case SerializedPropertyType.ObjectReference:
                        propInfo["value"] = iterator.objectReferenceValue?.name ?? "null";
                        propInfo["objectType"] = iterator.objectReferenceValue?.GetType().Name ?? "null";
                        break;
                    case SerializedPropertyType.LayerMask:
                        propInfo["value"] = iterator.intValue;
                        break;
                    case SerializedPropertyType.Rect:
                        var rect = iterator.rectValue;
                        propInfo["value"] = new { x = rect.x, y = rect.y, width = rect.width, height = rect.height };
                        break;
                    case SerializedPropertyType.ArraySize:
                        propInfo["value"] = iterator.intValue;
                        break;
                    case SerializedPropertyType.AnimationCurve:
                        propInfo["value"] = $"AnimationCurve with {iterator.animationCurveValue?.keys?.Length ?? 0} keys";
                        break;
                    default:
                        propInfo["value"] = "(complex type)";
                        break;
                }

                properties.Add(propInfo);
            }

            return properties;
        }

        private string InspectScene()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var rootObjects = scene.GetRootGameObjects();

            int totalObjects = 0;
            int totalComponents = 0;
            var componentCounts = new Dictionary<string, int>();

            void CountRecursive(GameObject go)
            {
                totalObjects++;
                foreach (var comp in go.GetComponents<Component>())
                {
                    if (comp == null) continue;
                    totalComponents++;
                    var typeName = comp.GetType().Name;
                    componentCounts[typeName] = componentCounts.GetValueOrDefault(typeName, 0) + 1;
                }
                foreach (Transform child in go.transform)
                {
                    CountRecursive(child.gameObject);
                }
            }

            foreach (var root in rootObjects)
            {
                CountRecursive(root);
            }

            return JsonConvert.SerializeObject(new {
                success = true,
                scene = new {
                    name = scene.name,
                    path = scene.path,
                    isDirty = scene.isDirty,
                    rootCount = rootObjects.Length,
                    totalGameObjects = totalObjects,
                    totalComponents = totalComponents,
                    topComponents = componentCounts
                        .OrderByDescending(x => x.Value)
                        .Take(20)
                        .Select(x => new { type = x.Key, count = x.Value })
                        .ToList()
                }
            });
        }

        private string InspectHierarchy(int depth)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var rootObjects = scene.GetRootGameObjects();

            object BuildHierarchy(GameObject go, int currentDepth)
            {
                var children = new List<object>();
                if (currentDepth < depth)
                {
                    foreach (Transform child in go.transform)
                    {
                        children.Add(BuildHierarchy(child.gameObject, currentDepth + 1));
                    }
                }

                return new {
                    name = go.name,
                    active = go.activeSelf,
                    components = go.GetComponents<Component>()
                        .Where(c => c != null)
                        .Select(c => c.GetType().Name)
                        .ToList(),
                    children = children.Count > 0 ? children : null
                };
            }

            var hierarchy = rootObjects.Select(go => BuildHierarchy(go, 0)).ToList();

            return JsonConvert.SerializeObject(new {
                success = true,
                scene = scene.name,
                depth = depth,
                hierarchy = hierarchy
            });
        }

        private string InspectPrefabs(string pathFilter)
        {
            try
            {
                var searchPath = string.IsNullOrEmpty(pathFilter) ? "Assets" : pathFilter.Replace("*", "");
                if (searchPath.EndsWith("/")) searchPath = searchPath.TrimEnd('/');
                if (!searchPath.StartsWith("Assets")) searchPath = "Assets";

                var guids = AssetDatabase.FindAssets("t:Prefab", new[] { searchPath });

                var prefabs = guids
                    .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                    .Where(p => string.IsNullOrEmpty(pathFilter) || MatchesWildcardDynamic(p, pathFilter))
                    .Take(100)
                    .Select(p => {
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                        return new {
                            path = p,
                            name = prefab?.name ?? Path.GetFileNameWithoutExtension(p),
                            components = prefab?.GetComponents<Component>()
                                .Where(c => c != null)
                                .Select(c => c.GetType().Name)
                                .ToList() ?? new List<string>()
                        };
                    })
                    .ToList();

                return JsonConvert.SerializeObject(new {
                    success = true,
                    filter = pathFilter ?? "all",
                    count = prefabs.Count,
                    prefabs = prefabs
                });
            }
            catch (Exception e)
            {
                return JsonConvert.SerializeObject(new { error = e.Message });
            }
        }

        private string InspectProject(string pathFilter)
        {
            try
            {
                var searchPath = string.IsNullOrEmpty(pathFilter) ? "Assets" : pathFilter;
                if (!searchPath.StartsWith("Assets")) searchPath = "Assets/" + searchPath;
                searchPath = searchPath.TrimEnd('/');

                // Get folder structure
                string[] folders = new string[0];
                try
                {
                    folders = AssetDatabase.GetSubFolders(searchPath);
                }
                catch { }

                // Get assets in current folder
                var guids = AssetDatabase.FindAssets("", new[] { searchPath });
                var assets = guids
                    .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                    .Where(p => Path.GetDirectoryName(p).Replace("\\", "/") == searchPath)
                    .Take(50)
                    .Select(p => new {
                        path = p,
                        name = Path.GetFileName(p),
                        type = AssetDatabase.GetMainAssetTypeAtPath(p)?.Name ?? "Unknown"
                    })
                    .ToList();

                return JsonConvert.SerializeObject(new {
                    success = true,
                    currentPath = searchPath,
                    folders = folders,
                    assets = assets
                });
            }
            catch (Exception e)
            {
                return JsonConvert.SerializeObject(new { error = e.Message });
            }
        }

        private bool MatchesWildcardDynamic(string path, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return true;
            var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*\\*", ".*")
                .Replace("\\*", "[^/]*")
                .Replace("\\?", ".") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(path, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        private GameObject FindGameObjectByPathDynamic(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            var parts = path.Split('/');
            GameObject current = null;

            foreach (var part in parts)
            {
                if (current == null)
                {
                    current = GameObject.Find(part);
                    if (current == null)
                    {
                        var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
                        current = roots.FirstOrDefault(r => r.name == part);
                    }
                }
                else
                {
                    var child = current.transform.Find(part);
                    current = child?.gameObject;
                }

                if (current == null) return null;
            }

            return current;
        }

        /// <summary>
        /// Dynamically modify any property of a Unity component
        /// </summary>
        private string DynamicModify(Dictionary<string, string> parameters)
        {
            try
            {
                var gameObjectName = parameters.GetValueOrDefault("gameObject", "");
                var componentType = parameters.GetValueOrDefault("component", "");
                var propertiesJson = parameters.GetValueOrDefault("properties", "{}");
                bool.TryParse(parameters.GetValueOrDefault("createIfMissing", "false"), out bool createIfMissing);

                if (string.IsNullOrEmpty(gameObjectName))
                {
                    return JsonConvert.SerializeObject(new { error = "GameObject name required" });
                }

                var gameObject = GameObject.Find(gameObjectName) ?? FindGameObjectByPathDynamic(gameObjectName);
                if (gameObject == null)
                {
                    return JsonConvert.SerializeObject(new { error = $"GameObject '{gameObjectName}' not found" });
                }

                // Find or create component
                Component component = null;
                if (!string.IsNullOrEmpty(componentType))
                {
                    component = gameObject.GetComponents<Component>()
                        .FirstOrDefault(c => c != null &&
                            (c.GetType().Name.Equals(componentType, StringComparison.OrdinalIgnoreCase) ||
                             c.GetType().Name.EndsWith(componentType, StringComparison.OrdinalIgnoreCase)));

                    if (component == null && createIfMissing)
                    {
                        var type = FindComponentTypeDynamic(componentType);
                        if (type != null)
                        {
                            component = gameObject.AddComponent(type);
                        }
                    }

                    if (component == null)
                    {
                        return JsonConvert.SerializeObject(new {
                            error = $"Component '{componentType}' not found on '{gameObjectName}'",
                            hint = createIfMissing ? "Could not create component - type not found" : "Use createIfMissing:true to add it"
                        });
                    }
                }
                else
                {
                    return JsonConvert.SerializeObject(new { error = "Component type required" });
                }

                // Parse properties
                var properties = JsonConvert.DeserializeObject<Dictionary<string, object>>(propertiesJson);
                if (properties == null || properties.Count == 0)
                {
                    return JsonConvert.SerializeObject(new { error = "No properties specified" });
                }

                var so = new SerializedObject(component);
                var modifiedProperties = new List<string>();
                var failedProperties = new List<object>();

                foreach (var kvp in properties)
                {
                    var prop = so.FindProperty(kvp.Key);
                    if (prop == null)
                    {
                        failedProperties.Add(new { path = kvp.Key, error = "Property not found" });
                        continue;
                    }

                    if (!prop.editable)
                    {
                        failedProperties.Add(new { path = kvp.Key, error = "Property not editable" });
                        continue;
                    }

                    try
                    {
                        SetSerializedPropertyValueDynamic(prop, kvp.Value);
                        modifiedProperties.Add(kvp.Key);
                    }
                    catch (Exception e)
                    {
                        failedProperties.Add(new { path = kvp.Key, error = e.Message });
                    }
                }

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(gameObject);

                return JsonConvert.SerializeObject(new {
                    success = true,
                    gameObject = gameObjectName,
                    component = componentType,
                    modifiedProperties = modifiedProperties,
                    failedProperties = failedProperties.Count > 0 ? failedProperties : null
                });
            }
            catch (Exception e)
            {
                return CreateErrorResponse("DynamicModify", e, parameters);
            }
        }

        private void SetSerializedPropertyValueDynamic(SerializedProperty prop, object value)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    prop.intValue = Convert.ToInt32(value);
                    break;
                case SerializedPropertyType.Float:
                    prop.floatValue = Convert.ToSingle(value);
                    break;
                case SerializedPropertyType.Boolean:
                    prop.boolValue = Convert.ToBoolean(value);
                    break;
                case SerializedPropertyType.String:
                    prop.stringValue = value?.ToString() ?? "";
                    break;
                case SerializedPropertyType.Enum:
                    if (value is int intVal)
                        prop.enumValueIndex = intVal;
                    else if (value is long longVal)
                        prop.enumValueIndex = (int)longVal;
                    else if (value is string strVal)
                    {
                        var idx = Array.IndexOf(prop.enumDisplayNames, strVal);
                        if (idx >= 0) prop.enumValueIndex = idx;
                        else if (int.TryParse(strVal, out int parsed)) prop.enumValueIndex = parsed;
                    }
                    break;
                case SerializedPropertyType.Vector2:
                    if (value is Newtonsoft.Json.Linq.JObject v2)
                        prop.vector2Value = new Vector2(v2["x"]?.ToObject<float>() ?? 0, v2["y"]?.ToObject<float>() ?? 0);
                    break;
                case SerializedPropertyType.Vector3:
                    if (value is Newtonsoft.Json.Linq.JObject v3)
                        prop.vector3Value = new Vector3(v3["x"]?.ToObject<float>() ?? 0, v3["y"]?.ToObject<float>() ?? 0, v3["z"]?.ToObject<float>() ?? 0);
                    break;
                case SerializedPropertyType.Vector4:
                    if (value is Newtonsoft.Json.Linq.JObject v4)
                        prop.vector4Value = new Vector4(v4["x"]?.ToObject<float>() ?? 0, v4["y"]?.ToObject<float>() ?? 0, v4["z"]?.ToObject<float>() ?? 0, v4["w"]?.ToObject<float>() ?? 0);
                    break;
                case SerializedPropertyType.Color:
                    if (value is Newtonsoft.Json.Linq.JObject c)
                        prop.colorValue = new Color(c["r"]?.ToObject<float>() ?? 1, c["g"]?.ToObject<float>() ?? 1, c["b"]?.ToObject<float>() ?? 1, c["a"]?.ToObject<float>() ?? 1);
                    else if (value is string hex)
                        prop.colorValue = ParseHexColorDynamic(hex);
                    break;
                case SerializedPropertyType.LayerMask:
                    prop.intValue = Convert.ToInt32(value);
                    break;
                default:
                    throw new NotSupportedException($"Property type {prop.propertyType} not supported for direct modification");
            }
        }

        private Color ParseHexColorDynamic(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Color.white;
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return new Color32(r, g, b, 255);
            }
            return Color.white;
        }

        private Type FindComponentTypeDynamic(string typeName)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                try
                {
                    var type = assembly.GetTypes()
                        .FirstOrDefault(t => typeof(Component).IsAssignableFrom(t) &&
                            (t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase) ||
                             t.Name.EndsWith(typeName, StringComparison.OrdinalIgnoreCase)));
                    if (type != null) return type;
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// Universal creation tool for GameObjects, prefabs, scenes, and components
        /// </summary>
        private string DynamicCreate(Dictionary<string, string> parameters)
        {
            try
            {
                var createType = parameters.GetValueOrDefault("type", "gameobject").ToLower();

                switch (createType)
                {
                    case "gameobject":
                        return CreateDynamicGameObject(parameters);

                    case "prefab":
                        return CreateDynamicPrefabInstance(parameters);

                    case "scene":
                        return LoadDynamicScene(parameters);

                    case "component":
                        return AddDynamicComponent(parameters);

                    default:
                        return JsonConvert.SerializeObject(new { error = $"Unknown create type: {createType}" });
                }
            }
            catch (Exception e)
            {
                return CreateErrorResponse("DynamicCreate", e, parameters);
            }
        }

        private string CreateDynamicGameObject(Dictionary<string, string> parameters)
        {
            var name = parameters.GetValueOrDefault("name", "New GameObject");
            var primitive = parameters.GetValueOrDefault("primitive", "empty").ToLower();
            var parentName = parameters.GetValueOrDefault("parent", "");

            GameObject go;
            switch (primitive)
            {
                case "cube": go = GameObject.CreatePrimitive(PrimitiveType.Cube); break;
                case "sphere": go = GameObject.CreatePrimitive(PrimitiveType.Sphere); break;
                case "cylinder": go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); break;
                case "plane": go = GameObject.CreatePrimitive(PrimitiveType.Plane); break;
                case "capsule": go = GameObject.CreatePrimitive(PrimitiveType.Capsule); break;
                case "quad": go = GameObject.CreatePrimitive(PrimitiveType.Quad); break;
                default: go = new GameObject(); break;
            }

            go.name = name;

            if (!string.IsNullOrEmpty(parentName))
            {
                var parent = GameObject.Find(parentName) ?? FindGameObjectByPathDynamic(parentName);
                if (parent != null)
                {
                    go.transform.SetParent(parent.transform, false);
                }
            }

            ApplyTransformFromParametersDynamic(go, parameters);

            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
            Selection.activeGameObject = go;

            return JsonConvert.SerializeObject(new {
                success = true,
                message = $"Created GameObject '{name}'",
                gameObject = new {
                    name = go.name,
                    primitive = primitive,
                    path = GetGameObjectPathDynamic(go)
                }
            });
        }

        private string CreateDynamicPrefabInstance(Dictionary<string, string> parameters)
        {
            var assetPath = parameters.GetValueOrDefault("asset", "");
            var instanceName = parameters.GetValueOrDefault("name", "");
            var parentName = parameters.GetValueOrDefault("parent", "");

            if (string.IsNullOrEmpty(assetPath))
            {
                return JsonConvert.SerializeObject(new { error = "Asset path required" });
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
            {
                var guids = AssetDatabase.FindAssets($"{Path.GetFileNameWithoutExtension(assetPath)} t:Prefab");
                if (guids.Length > 0)
                {
                    var foundPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(foundPath);
                }
            }

            if (prefab == null)
            {
                return JsonConvert.SerializeObject(new { error = $"Prefab not found at '{assetPath}'" });
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (!string.IsNullOrEmpty(instanceName))
            {
                instance.name = instanceName;
            }

            if (!string.IsNullOrEmpty(parentName))
            {
                var parent = GameObject.Find(parentName) ?? FindGameObjectByPathDynamic(parentName);
                if (parent != null)
                {
                    instance.transform.SetParent(parent.transform, false);
                }
            }

            ApplyTransformFromParametersDynamic(instance, parameters);

            Undo.RegisterCreatedObjectUndo(instance, $"Instantiate {prefab.name}");
            Selection.activeGameObject = instance;

            return JsonConvert.SerializeObject(new {
                success = true,
                message = $"Instantiated prefab '{prefab.name}'",
                instance = new {
                    name = instance.name,
                    prefabPath = assetPath,
                    path = GetGameObjectPathDynamic(instance)
                }
            });
        }

        private string LoadDynamicScene(Dictionary<string, string> parameters)
        {
            var sceneName = parameters.GetValueOrDefault("scene", "");
            bool.TryParse(parameters.GetValueOrDefault("additive", "false"), out bool additive);

            if (string.IsNullOrEmpty(sceneName))
            {
                return JsonConvert.SerializeObject(new { error = "Scene name required" });
            }

            string scenePath = sceneName;
            if (!sceneName.EndsWith(".unity"))
            {
                var guids = AssetDatabase.FindAssets($"{sceneName} t:Scene");
                if (guids.Length > 0)
                {
                    scenePath = AssetDatabase.GUIDToAssetPath(guids[0]);
                }
                else
                {
                    foreach (var buildScene in EditorBuildSettings.scenes)
                    {
                        if (Path.GetFileNameWithoutExtension(buildScene.path).Equals(sceneName, StringComparison.OrdinalIgnoreCase))
                        {
                            scenePath = buildScene.path;
                            break;
                        }
                    }
                }
            }

            if (!File.Exists(scenePath))
            {
                return JsonConvert.SerializeObject(new { error = $"Scene '{sceneName}' not found" });
            }

            var mode = additive ? UnityEditor.SceneManagement.OpenSceneMode.Additive : UnityEditor.SceneManagement.OpenSceneMode.Single;
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, mode);

            return JsonConvert.SerializeObject(new {
                success = true,
                message = $"Loaded scene '{scene.name}' {(additive ? "additively" : "")}",
                scene = new {
                    name = scene.name,
                    path = scene.path,
                    rootCount = scene.rootCount
                }
            });
        }

        private string AddDynamicComponent(Dictionary<string, string> parameters)
        {
            var gameObjectName = parameters.GetValueOrDefault("gameObject", "");
            var componentType = parameters.GetValueOrDefault("component", "");

            if (string.IsNullOrEmpty(gameObjectName))
            {
                return JsonConvert.SerializeObject(new { error = "GameObject name required" });
            }

            if (string.IsNullOrEmpty(componentType))
            {
                return JsonConvert.SerializeObject(new { error = "Component type required" });
            }

            var gameObject = GameObject.Find(gameObjectName) ?? FindGameObjectByPathDynamic(gameObjectName);
            if (gameObject == null)
            {
                return JsonConvert.SerializeObject(new { error = $"GameObject '{gameObjectName}' not found" });
            }

            var type = FindComponentTypeDynamic(componentType);
            if (type == null)
            {
                return JsonConvert.SerializeObject(new { error = $"Component type '{componentType}' not found" });
            }

            if (gameObject.GetComponent(type) != null)
            {
                return JsonConvert.SerializeObject(new {
                    success = true,
                    message = $"Component '{componentType}' already exists on '{gameObjectName}'",
                    alreadyExists = true
                });
            }

            var component = Undo.AddComponent(gameObject, type);

            return JsonConvert.SerializeObject(new {
                success = true,
                message = $"Added component '{type.Name}' to '{gameObjectName}'",
                component = new {
                    type = type.Name,
                    fullType = type.FullName
                }
            });
        }

        private void ApplyTransformFromParametersDynamic(GameObject go, Dictionary<string, string> parameters)
        {
            if (parameters.TryGetValue("position", out var posJson))
            {
                try
                {
                    var pos = JsonConvert.DeserializeObject<Dictionary<string, float>>(posJson);
                    if (pos != null)
                    {
                        go.transform.position = new Vector3(
                            pos.GetValueOrDefault("x", 0),
                            pos.GetValueOrDefault("y", 0),
                            pos.GetValueOrDefault("z", 0)
                        );
                    }
                }
                catch { }
            }

            if (parameters.TryGetValue("rotation", out var rotJson))
            {
                try
                {
                    var rot = JsonConvert.DeserializeObject<Dictionary<string, float>>(rotJson);
                    if (rot != null)
                    {
                        go.transform.eulerAngles = new Vector3(
                            rot.GetValueOrDefault("x", 0),
                            rot.GetValueOrDefault("y", 0),
                            rot.GetValueOrDefault("z", 0)
                        );
                    }
                }
                catch { }
            }

            if (parameters.TryGetValue("scale", out var scaleJson))
            {
                try
                {
                    var scale = JsonConvert.DeserializeObject<Dictionary<string, float>>(scaleJson);
                    if (scale != null)
                    {
                        go.transform.localScale = new Vector3(
                            scale.GetValueOrDefault("x", 1),
                            scale.GetValueOrDefault("y", 1),
                            scale.GetValueOrDefault("z", 1)
                        );
                    }
                }
                catch { }
            }
        }

        private string GetGameObjectPathDynamic(GameObject go)
        {
            string path = go.name;
            Transform current = go.transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }

        #endregion
    }
}
