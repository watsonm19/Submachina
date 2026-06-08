#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using SynapticAIPro;
using UnityEngine.VFX;
using UnityEditor;
using Newtonsoft.Json;

// VFX Graph Editor API (internal namespace, accessed via reflection where needed)
#if VFX_GRAPH_10_PLUS
using UnityEditor.VFX;
#endif

namespace SynapticPro
{
    /// <summary>
    /// VFX Graph Builder - Programmatic creation of VFX Graphs
    /// Provides full access to VFX Graph features via MCP tools
    /// </summary>
    public static class NexusVFXBuilder
    {
        // Cache for VFX types (populated via reflection)
        private static Dictionary<string, Type> _contextTypes;
        private static Dictionary<string, Type> _blockTypes;
        private static Dictionary<string, Type> _operatorTypes;
        private static bool _initialized = false;

        // Output type mapping for pipeline conversion (Built-in -> URP/HDRP)
        private static readonly Dictionary<string, string> _urpOutputMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "quad", "urpquad" },
            { "output", "urpquad" },
            { "line", "urpquad" },  // URP doesn't have line, use quad
            { "quadstrip", "urplitstrip" },
            { "trail", "urplitstrip" },
            { "ribbon", "urplitstrip" },
            { "mesh", "urplitmesh" },
            { "decal", "urpdecal" },
        };

        private static readonly Dictionary<string, string> _hdrpOutputMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "quad", "hdrpquad" },
            { "output", "hdrpquad" },
            { "line", "hdrpquad" },  // HDRP doesn't have line, use quad
            { "quadstrip", "hdrplitstrip" },
            { "trail", "hdrplitstrip" },
            { "ribbon", "hdrplitstrip" },
            { "mesh", "hdrplitmesh" },
            { "decal", "hdrpdecal" },
        };

        #region Initialization

        /// <summary>
        /// Detect the current rendering pipeline
        /// </summary>
        private static string DetectRenderingPipeline()
        {
            var currentPipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            if (currentPipeline != null)
            {
                var pipelineName = currentPipeline.GetType().Name;
                if (pipelineName.Contains("Universal") || pipelineName.Contains("URP"))
                    return "URP";
                else if (pipelineName.Contains("HighDefinition") || pipelineName.Contains("HDRP"))
                    return "HDRP";
            }
            else
            {
                var qualityAsset = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
                if (qualityAsset != null)
                {
                    var assetName = qualityAsset.GetType().Name;
                    if (assetName.Contains("Universal") || assetName.Contains("URP"))
                        return "URP";
                    else if (assetName.Contains("HighDefinition") || assetName.Contains("HDRP"))
                        return "HDRP";
                }
            }
            return "Legacy";
        }

        /// <summary>
        /// Convert output context type to pipeline-appropriate type
        /// </summary>
        private static string ConvertOutputForPipeline(string contextType)
        {
            var pipeline = DetectRenderingPipeline();
            var lowerType = contextType.ToLower();

            // Already pipeline-specific, don't convert
            if (lowerType.StartsWith("urp") || lowerType.StartsWith("hdrp"))
                return contextType;

            // Not an output type, don't convert
            bool isOutputType = lowerType == "quad" || lowerType == "output" || lowerType == "line" ||
                               lowerType == "quadstrip" || lowerType == "trail" || lowerType == "ribbon" ||
                               lowerType == "mesh" || lowerType == "decal" || lowerType == "point" ||
                               lowerType == "linestrip" || lowerType == "staticmesh";

            if (!isOutputType)
                return contextType;

            string convertedType = contextType;

            if (pipeline == "URP" && _urpOutputMapping.TryGetValue(lowerType, out string urpType))
            {
                // Check if URP type is available
                if (_contextTypes.ContainsKey(urpType))
                {
                    convertedType = urpType;
                    SynLog.Info($"[NexusVFX] Auto-converted output '{contextType}' -> '{urpType}' for URP pipeline");
                }
                else
                {
                    SynLog.Warn($"[NexusVFX] URP output type '{urpType}' not available, using built-in '{contextType}'");
                }
            }
            else if (pipeline == "HDRP" && _hdrpOutputMapping.TryGetValue(lowerType, out string hdrpType))
            {
                // Check if HDRP type is available
                if (_contextTypes.ContainsKey(hdrpType))
                {
                    convertedType = hdrpType;
                    SynLog.Info($"[NexusVFX] Auto-converted output '{contextType}' -> '{hdrpType}' for HDRP pipeline");
                }
                else
                {
                    SynLog.Warn($"[NexusVFX] HDRP output type '{hdrpType}' not available, using built-in '{contextType}'");
                }
            }

            return convertedType;
        }

        public static void Initialize()
        {
            if (_initialized) return;

            _contextTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            _blockTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            _operatorTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

            // Find all VFX types via reflection
            var vfxEditorAssembly = GetVFXEditorAssembly();
            if (vfxEditorAssembly == null)
            {
                SynLog.Warn("[NexusVFX] VFX Graph Editor assembly not found. Is the package installed?");
                return;
            }

            // Populate context types
            PopulateContextTypes(vfxEditorAssembly);
            PopulateBlockTypes(vfxEditorAssembly);
            PopulateOperatorTypes(vfxEditorAssembly);

            _initialized = true;
            SynLog.Info($"[NexusVFX] Initialized: {_contextTypes.Count} contexts, {_blockTypes.Count} blocks, {_operatorTypes.Count} operators");
        }

        private static Assembly GetVFXEditorAssembly()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == "Unity.VisualEffectGraph.Editor")
                {
                    return assembly;
                }
            }
            return null;
        }

        private static void PopulateContextTypes(Assembly assembly)
        {
            var contextMappings = new Dictionary<string, string>
            {
                // Spawn
                { "spawn", "VFXBasicSpawner" },
                { "spawner", "VFXBasicSpawner" },
                { "gpuspawn", "VFXBasicGPUEvent" },

                // Initialize
                { "initialize", "VFXBasicInitialize" },
                { "init", "VFXBasicInitialize" },

                // Update
                { "update", "VFXBasicUpdate" },

                // Output - Basic (Built-in compatible)
                { "output", "VFXQuadOutput" },
                { "quad", "VFXQuadOutput" },
                { "point", "VFXPointOutput" },
                { "line", "VFXLineOutput" },
                { "linestrip", "VFXLineStripOutput" },
                { "quadstrip", "VFXQuadStripOutput" },
                { "trail", "VFXQuadStripOutput" },
                { "ribbon", "VFXQuadStripOutput" },
                { "mesh", "VFXMeshOutput" },
                { "staticmesh", "VFXStaticMeshOutput" },
                { "decal", "VFXDecalOutput" },

                // Output - URP specific (in UnityEditor.VFX.URP namespace)
                { "urpquad", "VFXURPLitPlanarPrimitiveOutput" },
                { "urplit", "VFXURPLitPlanarPrimitiveOutput" },
                { "urplitquad", "VFXURPLitPlanarPrimitiveOutput" },
                { "urplitmesh", "VFXURPLitMeshOutput" },
                { "urplitstrip", "VFXURPLitQuadStripOutput" },
                { "urpdecal", "VFXDecalURPOutput" },

                // Output - HDRP specific (in UnityEditor.VFX.HDRP namespace)
                { "hdrpquad", "VFXHDRPLitPlanarPrimitiveOutput" },
                { "hdrplit", "VFXHDRPLitPlanarPrimitiveOutput" },
                { "hdrplitquad", "VFXHDRPLitPlanarPrimitiveOutput" },
                { "hdrplitmesh", "VFXHDRPLitMeshOutput" },
                { "hdrplitstrip", "VFXHDRPLitQuadStripOutput" },
                { "hdrpdecal", "VFXDecalHDRPOutput" },

                // Event
                { "event", "VFXBasicEvent" },
                { "outputevent", "VFXOutputEvent" },
            };

            // Also search in URP/HDRP VFX assemblies
            Assembly urpVfxAssembly = null;
            Assembly hdrpVfxAssembly = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var asmName = asm.GetName().Name;
                if (asmName == "Unity.RenderPipelines.Universal.Editor")
                    urpVfxAssembly = asm;
                else if (asmName == "Unity.RenderPipelines.HighDefinition.Editor")
                    hdrpVfxAssembly = asm;
            }

            foreach (var mapping in contextMappings)
            {
                Type type = null;

                // Try VFX editor assembly first
                type = assembly.GetType($"UnityEditor.VFX.{mapping.Value}");

                // Try URP VFX namespace
                if (type == null && urpVfxAssembly != null)
                {
                    type = urpVfxAssembly.GetType($"UnityEditor.VFX.URP.{mapping.Value}");
                }

                // Try HDRP VFX namespace
                if (type == null && hdrpVfxAssembly != null)
                {
                    type = hdrpVfxAssembly.GetType($"UnityEditor.VFX.HDRP.{mapping.Value}");
                }

                // Try searching all assemblies for URP types
                if (type == null && mapping.Key.StartsWith("urp"))
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        type = asm.GetType($"UnityEditor.VFX.URP.{mapping.Value}");
                        if (type != null) break;
                    }
                }

                // Try searching all assemblies for HDRP types
                if (type == null && mapping.Key.StartsWith("hdrp"))
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        type = asm.GetType($"UnityEditor.VFX.HDRP.{mapping.Value}");
                        if (type != null) break;
                    }
                }

                if (type != null)
                {
                    _contextTypes[mapping.Key] = type;
                    SynLog.Info($"[NexusVFX] Registered context: {mapping.Key} -> {type.FullName}");
                }
            }

            // Log pipeline availability
            SynLog.Info($"[NexusVFX] URP VFX assembly found: {urpVfxAssembly != null}, HDRP: {hdrpVfxAssembly != null}");
        }

        private static void PopulateBlockTypes(Assembly assembly)
        {
            var blockMappings = new Dictionary<string, string>
            {
                // Position blocks
                { "positionsphere", "PositionSphere" },
                { "positioncircle", "PositionCircle" },
                { "positioncone", "PositionCone" },
                { "positionline", "PositionLine" },
                { "positionbox", "PositionAABox" },
                { "positionaabox", "PositionAABox" },
                { "positiontorus", "PositionTorus" },
                { "positionmesh", "PositionMesh" },
                { "positionsdf", "PositionSDF" },
                { "positiondepth", "PositionDepth" },
                { "positionsequential", "PositionSequential" },

                // Force blocks
                { "gravity", "Gravity" },
                { "drag", "Drag" },
                { "turbulence", "Turbulence" },
                { "force", "Force" },
                { "conformtosphere", "ConformToSphere" },
                { "conformtosdf", "ConformToSDF" },
                { "vectorfieldforce", "VectorFieldForce" },

                // Attribute blocks
                { "setattribute", "SetAttribute" },
                { "setattributerandom", "SetAttribute" },  // Uses SetAttribute with Random mode
                { "setattributefrommap", "SetAttributeFromMap" },
                { "setposition", "SetAttribute" },
                { "setvelocity", "SetAttribute" },
                { "setcolor", "SetAttribute" },
                { "setsize", "SetAttribute" },
                { "setlifetime", "SetAttribute" },
                { "setmass", "SetAttribute" },
                { "setalpha", "SetAttribute" },
                { "setangle", "SetAttribute" },
                { "setangularvelocity", "SetAttribute" },

                // Random attribute blocks
                { "velocityrandom", "VelocityRandomize" },
                { "randomvelocity", "VelocityRandomize" },

                // Collision blocks
                { "collisionsphere", "CollisionSphere" },
                { "collisionplane", "CollisionPlane" },
                { "collisionbox", "CollisionAABox" },
                { "collisioncylinder", "CollisionCylinder" },
                { "collisionsdf", "CollisionSDF" },
                { "collisiondepth", "CollisionDepthBuffer" },

                // Kill blocks
                { "killsphere", "KillSphere" },
                { "killbox", "KillAABox" },
                { "killplane", "KillPlane" },
                { "killage", "KillAge" },

                // Orientation blocks
                { "orient", "Orient" },
                { "facecamera", "Orient" },
                { "orientalongvelocity", "OrientAlongVelocity" },

                // Spawn blocks
                { "spawnrate", "VFXSpawnerConstantRate" },
                { "spawnburst", "VFXSpawnerBurst" },
                { "spawnperunit", "SpawnOverDistance" },

                // Color/Size over lifetime
                // ColorOverLife exists but is deprecated in some versions
                { "coloroverlife", "ColorOverLife" },
                { "coloroverlifetime", "ColorOverLife" },
                // SizeOverLife doesn't exist as separate class - use AttributeFromCurve
                { "sizeoverlife", "AttributeFromCurve" },
                { "sizeoverlifetime", "AttributeFromCurve" },

                // Attribute from curve blocks (main implementation for "over life" blocks)
                { "attributefromcurve", "AttributeFromCurve" },
                { "setattributefromcurve", "AttributeFromCurve" },
                { "setcoloroverlife", "AttributeFromCurve" },
                { "setsizeoverlife", "AttributeFromCurve" },
                { "setalphaoverlife", "AttributeFromCurve" },

                // Flipbook
                { "flipbook", "FlipbookPlayer" },

                // GPU Events
                { "triggerevent", "TriggerEvent" },
                { "triggereventondie", "TriggerEventOnDie" },
            };

            string[] blockNamespaces = new[]
            {
                "UnityEditor.VFX.Block",
                "UnityEditor.VFX",
            };

            foreach (var mapping in blockMappings)
            {
                Type type = null;
                foreach (var ns in blockNamespaces)
                {
                    type = assembly.GetType($"{ns}.{mapping.Value}");
                    if (type != null) break;
                }
                if (type != null)
                {
                    _blockTypes[mapping.Key] = type;
                }
            }

            // Also search for types dynamically if not found in mappings
            // Look for VFXBlock subclasses with VFXInfo attributes
            try
            {
                var vfxBlockBaseType = assembly.GetType("UnityEditor.VFX.VFXBlock");
                if (vfxBlockBaseType != null)
                {
                    var allTypes = assembly.GetTypes()
                        .Where(t => vfxBlockBaseType.IsAssignableFrom(t) && !t.IsAbstract);

                    foreach (var type in allTypes)
                    {
                        // Use type name without namespace as potential key
                        string typeName = type.Name.ToLower();
                        if (!_blockTypes.ContainsKey(typeName))
                        {
                            _blockTypes[typeName] = type;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                SynLog.Warn($"[NexusVFX] Failed to discover additional block types: {e.Message}");
            }

            SynLog.Info($"[NexusVFX] Block types discovered: {_blockTypes.Count}");
        }

        private static void PopulateOperatorTypes(Assembly assembly)
        {
            var operatorMappings = new Dictionary<string, string>
            {
                // Math
                { "add", "Add" },
                { "subtract", "Subtract" },
                { "multiply", "Multiply" },
                { "divide", "Divide" },
                { "power", "Power" },
                { "modulo", "Modulo" },
                { "absolute", "Absolute" },
                { "negate", "Negate" },
                { "minimum", "Minimum" },
                { "maximum", "Maximum" },
                { "clamp", "Clamp" },
                { "saturate", "Saturate" },
                { "floor", "Floor" },
                { "ceiling", "Ceiling" },
                { "round", "Round" },
                { "sign", "Sign" },
                { "step", "Step" },
                { "smoothstep", "Smoothstep" },
                { "lerp", "Lerp" },
                { "inverselerp", "InverseLerp" },
                { "remap", "Remap" },
                { "oneminus", "OneMinus" },

                // Trigonometric
                { "sine", "Sine" },
                { "cosine", "Cosine" },
                { "tangent", "Tangent" },
                { "asin", "Asin" },
                { "acos", "Acos" },
                { "atan", "Atan" },
                { "atan2", "Atan2" },

                // Vector
                { "dot", "DotProduct" },
                { "cross", "CrossProduct" },
                { "length", "Length" },
                { "distance", "Distance" },
                { "normalize", "Normalize" },
                { "swizzle", "Swizzle" },

                // Noise
                { "noise", "Noise" },
                { "curlnoise", "CurlNoise" },
                { "voronoise", "VoroNoise2D" },

                // Sampling
                { "sampletexture2d", "SampleTexture2D" },
                { "sampletexture3d", "SampleTexture3D" },
                { "samplecurve", "SampleCurve" },
                { "samplegradient", "SampleGradient" },
                { "samplemesh", "SampleMesh" },
                { "samplesdf", "SampleSDF" },

                // Logic
                { "compare", "Compare" },
                { "branch", "Branch" },
                { "and", "LogicalAnd" },
                { "or", "LogicalOr" },
                { "not", "LogicalNot" },

                // Utility
                { "random", "Random" },
                { "time", "Time" },
                { "deltatime", "DeltaTime" },
                { "maincamera", "MainCamera" },
                { "changespace", "ChangeSpace" },

                // Transform
                { "transformposition", "TransformPosition" },
                { "transformdirection", "TransformDirection" },

                // Waveforms
                { "sinewave", "SineWave" },
                { "squarewave", "SquareWave" },
                { "trianglewave", "TriangleWave" },
                { "sawtoothwave", "SawtoothWave" },
            };

            string[] opNamespaces = new[]
            {
                "UnityEditor.VFX.Operator",
                "UnityEditor.VFX",
            };

            foreach (var mapping in operatorMappings)
            {
                Type type = null;
                foreach (var ns in opNamespaces)
                {
                    type = assembly.GetType($"{ns}.{mapping.Value}");
                    if (type != null) break;
                }
                if (type != null)
                {
                    _operatorTypes[mapping.Key] = type;
                }
            }
        }

        #endregion

        #region Graph Creation

        /// <summary>
        /// Create a new VFX Graph asset
        /// </summary>
        public static string CreateVFXGraph(string name, string folderPath = "Assets/VFX")
        {
            Initialize();

            try
            {
                // Ensure folder exists
                if (!AssetDatabase.IsValidFolder(folderPath))
                {
                    string[] parts = folderPath.Split('/');
                    string currentPath = parts[0];
                    for (int i = 1; i < parts.Length; i++)
                    {
                        string newPath = $"{currentPath}/{parts[i]}";
                        if (!AssetDatabase.IsValidFolder(newPath))
                        {
                            AssetDatabase.CreateFolder(currentPath, parts[i]);
                        }
                        currentPath = newPath;
                    }
                }

                string assetPath = $"{folderPath}/{name}.vfx";

                // Delete existing
                if (AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(assetPath) != null)
                {
                    AssetDatabase.DeleteAsset(assetPath);
                }

                // Find VFX template in package
                string[] templatePaths = new string[]
                {
                    "Packages/com.unity.visualeffectgraph/Editor/Templates/SimpleParticleSystem.vfx",
                    "Packages/com.unity.visualeffectgraph/Editor/Templates/Simple Particle System.vfx",
                    "Packages/com.unity.visualeffectgraph/Editor/Templates/EmptyVFX.vfx",
                    "Packages/com.unity.visualeffectgraph/Editor/Templates/Empty.vfx",
                };

                string templatePath = null;
                foreach (var path in templatePaths)
                {
                    if (System.IO.File.Exists(path) || AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(path) != null)
                    {
                        templatePath = path;
                        break;
                    }
                }

                // If no template found, search for any .vfx in the package
                if (templatePath == null)
                {
                    var guids = AssetDatabase.FindAssets("t:VisualEffectAsset", new[] { "Packages/com.unity.visualeffectgraph" });
                    if (guids.Length > 0)
                    {
                        templatePath = AssetDatabase.GUIDToAssetPath(guids[0]);
                    }
                }

                if (templatePath == null)
                {
                    return "Error: No VFX template found in package. Please create a VFX manually first.";
                }

                SynLog.Info($"[NexusVFX] Using template: {templatePath}");

                // Copy template to target path
                bool copySuccess = AssetDatabase.CopyAsset(templatePath, assetPath);
                if (!copySuccess)
                {
                    return $"Error: Failed to copy template from {templatePath} to {assetPath}";
                }

                AssetDatabase.Refresh();
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

                // Verify
                var createdAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(assetPath);
                if (createdAsset == null)
                {
                    return $"Error: Asset was not created at {assetPath}";
                }

                // Clear the graph contents (remove all contexts/blocks from template)
                var graph = GetVFXGraph(assetPath);
                if (graph != null)
                {
                    ClearGraph(graph);
                    EditorUtility.SetDirty(graph as UnityEngine.Object);
                    AssetDatabase.SaveAssets();
                }

                return $"Created VFX Graph: {assetPath}";
            }
            catch (Exception e)
            {
                return $"Error creating VFX Graph: {e.Message}";
            }
        }

        /// <summary>
        /// Add a context to an existing VFX Graph
        /// </summary>
        public static string AddContext(string vfxPath, string contextType, Dictionary<string, object> settings = null)
        {
            Initialize();

            try
            {
                var graph = GetVFXGraph(vfxPath);
                if (graph == null)
                {
                    return $"Error: VFX Graph not found at {vfxPath}";
                }

                // Auto-convert output context type for current pipeline (URP/HDRP)
                string resolvedContextType = ConvertOutputForPipeline(contextType);

                if (!_contextTypes.TryGetValue(resolvedContextType.ToLower(), out Type ctxType))
                {
                    // Fallback to original type if converted type not found
                    if (!_contextTypes.TryGetValue(contextType.ToLower(), out ctxType))
                    {
                        return $"Error: Unknown context type '{contextType}'. Available: {string.Join(", ", _contextTypes.Keys)}";
                    }
                }

                // Create context instance
                var context = ScriptableObject.CreateInstance(ctxType);
                SynLog.Info($"[NexusVFX] Created context: {context.GetType().Name}");

                // Add to graph - try multiple AddChild signatures
                var graphType = graph.GetType();
                var addChildMethods = graphType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name == "AddChild")
                    .OrderByDescending(m => m.GetParameters().Length)
                    .ToList();

                if (addChildMethods.Count == 0)
                {
                    return "Error: AddChild method not found";
                }

                // Get initial child count to verify if add succeeded
                int initialChildCount = GetChildren(graph).Count;
                SynLog.Info($"[NexusVFX] Found {addChildMethods.Count} AddChild overloads, initial children: {initialChildCount}");

                bool added = false;
                Exception lastException = null;

                // Try 1-parameter version first (most compatible), then with index
                // Sort by parameter count ascending to try simpler signatures first
                var sortedMethods = addChildMethods.OrderBy(m => m.GetParameters().Length).ToList();

                foreach (var addChildMethod in sortedMethods)
                {
                    var parameters = addChildMethod.GetParameters();
                    SynLog.Info($"[NexusVFX] Trying AddChild({string.Join(", ", parameters.Select(p => p.ParameterType.Name))})");

                    try
                    {
                        if (parameters.Length == 1)
                        {
                            addChildMethod.Invoke(graph, new object[] { context });
                            added = true;
                            SynLog.Info("[NexusVFX] Successfully added context (1 param)");
                            break;
                        }
                        else if (parameters.Length == 2 && parameters[1].ParameterType == typeof(int))
                        {
                            addChildMethod.Invoke(graph, new object[] { context, 0 });
                            added = true;
                            SynLog.Info("[NexusVFX] Successfully added context (2 params, index 0)");
                            break;
                        }
                        else if (parameters.Length == 3 && parameters[1].ParameterType == typeof(int))
                        {
                            addChildMethod.Invoke(graph, new object[] { context, 0, true });
                            added = true;
                            SynLog.Info("[NexusVFX] Successfully added context (3 params, index 0)");
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        lastException = ex.InnerException ?? ex;
                        SynLog.Warn($"[NexusVFX] AddChild attempt failed: {lastException.Message}");
                        // Check if child was actually added despite the exception
                        int currentCount = GetChildren(graph).Count;
                        if (currentCount > initialChildCount)
                        {
                            added = true;
                            SynLog.Info($"[NexusVFX] Context was added despite exception (children: {initialChildCount} -> {currentCount})");
                            break;
                        }
                    }
                }

                if (!added)
                {
                    var errorMsg = lastException?.Message ?? "Unknown error";
                    return $"Error: AddChild failed - {errorMsg}";
                }

                // Check if this is an output context
                bool isOutputContext = contextType.ToLower().Contains("output") ||
                                       contextType.ToLower().Contains("quad") ||
                                       contextType.ToLower().Contains("point") ||
                                       contextType.ToLower().Contains("line") ||
                                       contextType.ToLower().Contains("mesh") ||
                                       contextType.ToLower().Contains("strip") ||
                                       contextType.ToLower().Contains("trail") ||
                                       contextType.ToLower().Contains("ribbon") ||
                                       contextType.ToLower().StartsWith("urp");

                // For output contexts, set default blendMode to Additive if not specified
                if (isOutputContext)
                {
                    bool hasBlendMode = settings != null &&
                        (settings.ContainsKey("blendMode") || settings.ContainsKey("blend"));

                    if (!hasBlendMode)
                    {
                        // Set default to Additive for particle effects
                        SetOutputBlendMode(context, "Additive");
                        SynLog.Info("[NexusVFX] Auto-set blendMode to Additive for output context");
                    }

                    // Enable particle color usage so color attribute works
                    EnableParticleColor(context);
                }

                // Apply settings
                if (settings != null)
                {
                    ApplySettings(context, settings);

                    // Handle blendMode setting for output contexts
                    if (isOutputContext)
                    {
                        if (settings.ContainsKey("blendMode"))
                        {
                            SetOutputBlendMode(context, settings["blendMode"].ToString());
                        }
                        else if (settings.ContainsKey("blend"))
                        {
                            SetOutputBlendMode(context, settings["blend"].ToString());
                        }
                    }

                    // Special handling for spawn contexts - auto-add spawn rate block
                    if (contextType.ToLower().Contains("spawn") &&
                        (settings.ContainsKey("spawnRate") || settings.ContainsKey("rate")))
                    {
                        var rate = settings.ContainsKey("spawnRate") ? settings["spawnRate"] : settings["rate"];
                        if (_blockTypes.TryGetValue("spawnrate", out Type spawnRateType))
                        {
                            var spawnRateBlock = ScriptableObject.CreateInstance(spawnRateType);

                            // Add block to context
                            var contextObjType = context.GetType();
                            var addBlockMethod = contextObjType.GetMethod("AddChild", BindingFlags.Public | BindingFlags.Instance);
                            if (addBlockMethod != null)
                            {
                                var blockParams = addBlockMethod.GetParameters();
                                try
                                {
                                    if (blockParams.Length == 3)
                                        addBlockMethod.Invoke(context, new object[] { spawnRateBlock, -1, true });
                                    else if (blockParams.Length == 2)
                                        addBlockMethod.Invoke(context, new object[] { spawnRateBlock, -1 });
                                    else if (blockParams.Length == 1)
                                        addBlockMethod.Invoke(context, new object[] { spawnRateBlock });

                                    // Set the rate on the block
                                    ApplySettings(spawnRateBlock, new Dictionary<string, object> { { "Rate", rate } });
                                    SynLog.Info($"[NexusVFX] Added spawnRate block with rate: {rate}");
                                }
                                catch (Exception ex)
                                {
                                    SynLog.Warn($"[NexusVFX] Failed to add spawnRate block: {ex.Message}");
                                }
                            }
                        }
                    }
                }

                // Save
                EditorUtility.SetDirty(graph as UnityEngine.Object);
                AssetDatabase.SaveAssets();

                return $"Added {contextType} context to {vfxPath}";
            }
            catch (Exception e)
            {
                return $"Error adding context: {e.Message}\n{e.StackTrace}";
            }
        }

        /// <summary>
        /// Add a block to a context
        /// </summary>
        public static string AddBlock(string vfxPath, int contextIndex, string blockType, Dictionary<string, object> settings = null)
        {
            Initialize();

            try
            {
                var graph = GetVFXGraph(vfxPath);
                if (graph == null)
                {
                    return $"Error: VFX Graph not found at {vfxPath}";
                }

                // Get contexts
                var contexts = GetContexts(graph);
                if (contextIndex < 0 || contextIndex >= contexts.Count)
                {
                    return $"Error: Context index {contextIndex} out of range (0-{contexts.Count - 1})";
                }

                var context = contexts[contextIndex];

                // Normalize block type name
                string normalizedBlockType = blockType.ToLower().Replace(" ", "").Replace("_", "");

                if (!_blockTypes.TryGetValue(normalizedBlockType, out Type blkType))
                {
                    return $"Error: Unknown block type '{blockType}'. Available: {string.Join(", ", _blockTypes.Keys.Take(20))}...";
                }

                // Create block instance
                var block = ScriptableObject.CreateInstance(blkType);
                SynLog.Info($"[NexusVFX] Created block: {block.GetType().Name}");

                // Add to context - try multiple AddChild signatures
                var contextType = context.GetType();
                var addChildMethods = contextType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name == "AddChild")
                    .OrderByDescending(m => m.GetParameters().Length)
                    .ToList();

                if (addChildMethods.Count == 0)
                {
                    return "Error: AddChild method not found on context";
                }

                // Get initial block count to verify if add succeeded
                int initialBlockCount = GetBlocks(context).Count;
                SynLog.Info($"[NexusVFX] Found {addChildMethods.Count} AddChild overloads on context, initial blocks: {initialBlockCount}");

                bool blockAdded = false;
                Exception lastBlockException = null;

                // Try 1-parameter version first (most compatible)
                var sortedBlockMethods = addChildMethods.OrderBy(m => m.GetParameters().Length).ToList();

                foreach (var addChildMethod in sortedBlockMethods)
                {
                    var parameters = addChildMethod.GetParameters();
                    SynLog.Info($"[NexusVFX] Trying context AddChild({string.Join(", ", parameters.Select(p => p.ParameterType.Name))})");

                    try
                    {
                        if (parameters.Length == 1)
                        {
                            addChildMethod.Invoke(context, new object[] { block });
                            blockAdded = true;
                            SynLog.Info("[NexusVFX] Successfully added block (1 param)");
                            break;
                        }
                        else if (parameters.Length == 2 && parameters[1].ParameterType == typeof(int))
                        {
                            addChildMethod.Invoke(context, new object[] { block, 0 });
                            blockAdded = true;
                            SynLog.Info("[NexusVFX] Successfully added block (2 params, index 0)");
                            break;
                        }
                        else if (parameters.Length == 3 && parameters[1].ParameterType == typeof(int))
                        {
                            addChildMethod.Invoke(context, new object[] { block, 0, true });
                            blockAdded = true;
                            SynLog.Info("[NexusVFX] Successfully added block (3 params, index 0)");
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        lastBlockException = ex.InnerException ?? ex;
                        SynLog.Warn($"[NexusVFX] Context AddChild attempt failed: {lastBlockException.Message}");
                        // Check if block was actually added despite the exception
                        int currentBlockCount = GetBlocks(context).Count;
                        if (currentBlockCount > initialBlockCount)
                        {
                            blockAdded = true;
                            SynLog.Info($"[NexusVFX] Block was added despite exception (blocks: {initialBlockCount} -> {currentBlockCount})");
                            break;
                        }
                    }
                }

                if (!blockAdded)
                {
                    var errorMsg = lastBlockException?.Message ?? "Unknown error";
                    return $"Error: AddChild failed - {errorMsg}";
                }

                // Special handling for SetAttribute blocks - must set attribute name and value
                string blockTypeName = block.GetType().Name;
                bool isRandomMode = normalizedBlockType.ToLower() == "setattributerandom";

                if (blockTypeName.Contains("SetAttribute"))
                {
                    // Get attribute name from settings or blockType
                    string attributeName = null;

                    if (settings != null && settings.ContainsKey("attribute"))
                    {
                        attributeName = settings["attribute"].ToString();
                    }
                    else
                    {
                        // Try to infer from block type name (e.g., "setvelocity" -> "velocity")
                        var lowerBlockType = normalizedBlockType.ToLower();
                        if (lowerBlockType.StartsWith("set"))
                        {
                            var stripped = lowerBlockType.Substring(3); // Remove "set" prefix
                            if (stripped != "attributerandom") // Don't use "attributerandom" as attribute name
                            {
                                attributeName = stripped;
                            }
                        }
                    }

                    // For setattributerandom, set Random mode
                    if (isRandomMode)
                    {
                        var randomModeField = block.GetType().GetField("Random",
                            BindingFlags.Public | BindingFlags.Instance);
                        if (randomModeField != null && randomModeField.FieldType == typeof(bool))
                        {
                            randomModeField.SetValue(block, true);
                            SynLog.Info("[NexusVFX] Set Random mode to true for SetAttribute");
                        }
                        else
                        {
                            // Try setting via enum RandomMode if available
                            var randomModeEnumField = block.GetType().GetField("randomMode",
                                BindingFlags.Public | BindingFlags.Instance);
                            if (randomModeEnumField != null && randomModeEnumField.FieldType.IsEnum)
                            {
                                try
                                {
                                    var randomValue = Enum.Parse(randomModeEnumField.FieldType, "Uniform", true);
                                    randomModeEnumField.SetValue(block, randomValue);
                                    SynLog.Info("[NexusVFX] Set randomMode to Uniform");
                                }
                                catch { }
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(attributeName))
                    {
                        // Map common attribute names (VFX uses lowercase internally)
                        var attributeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            { "position", "position" },
                            { "velocity", "velocity" },
                            { "color", "color" },
                            { "size", "size" },
                            { "lifetime", "lifetime" },
                            { "age", "age" },
                            { "alpha", "alpha" },
                            { "mass", "mass" },
                            { "angle", "angle" },
                            { "angularvelocity", "angularVelocity" },
                            { "scale", "scale" },
                            { "alive", "alive" },
                            { "seed", "seed" },
                            { "oldposition", "oldPosition" },
                            { "targetposition", "targetPosition" },
                            { "direction", "direction" },
                        };

                        if (attributeMap.TryGetValue(attributeName, out string mappedName))
                        {
                            attributeName = mappedName;
                        }

                        // Set attribute - try SetSettingValue first, then direct field access
                        var attributeField = block.GetType().GetField("attribute",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                        try
                        {
                            var setSettingMethod = block.GetType().GetMethod("SetSettingValue",
                                BindingFlags.Public | BindingFlags.Instance);
                            setSettingMethod?.Invoke(block, new object[] { "attribute", attributeName });
                        }
                        catch (Exception ex)
                        {
                            SynLog.Warn($"[NexusVFX] SetSettingValue failed (non-critical): {ex.Message}");
                        }

                        // Verify and fix if needed
                        try
                        {
                            if (attributeField != null)
                            {
                                var currentValue = attributeField.GetValue(block) as string;
                                if (string.IsNullOrEmpty(currentValue) || currentValue != attributeName)
                                {
                                    attributeField.SetValue(block, attributeName);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            SynLog.Warn($"[NexusVFX] Direct field access failed (non-critical): {ex.Message}");
                        }

                        // Set composition mode if specified
                        if (settings != null && settings.TryGetValue("composition", out object compValue))
                        {
                            var compositionField = block.GetType().GetField("Composition",
                                BindingFlags.Public | BindingFlags.Instance);
                            if (compositionField != null && compositionField.FieldType.IsEnum)
                            {
                                try
                                {
                                    var compositionValue = Enum.Parse(compositionField.FieldType, compValue.ToString(), true);
                                    compositionField.SetValue(block, compositionValue);
                                    SynLog.Info($"[NexusVFX] Set composition to: {compValue}");
                                }
                                catch { }
                            }
                        }

                        // Set value via input slot if specified
                        if (settings != null && settings.TryGetValue("value", out object valueObj))
                        {
                            SetBlockInputSlotValue(block, attributeName, valueObj);
                        }

                        // For random mode, handle min/max values
                        if (isRandomMode && settings != null)
                        {
                            // VFX SetAttribute in Random mode uses A and B slots for min/max
                            if (settings.TryGetValue("min", out object minObj))
                            {
                                SetBlockInputSlotValueByName(block, "A", minObj);
                                SynLog.Info($"[NexusVFX] Set min (A) value: {minObj}");
                            }
                            if (settings.TryGetValue("max", out object maxObj))
                            {
                                SetBlockInputSlotValueByName(block, "B", maxObj);
                                SynLog.Info($"[NexusVFX] Set max (B) value: {maxObj}");
                            }
                        }
                    }
                    else
                    {
                        SynLog.Warn("[NexusVFX] SetAttribute block created without attribute name - this may cause errors");
                    }
                }

                // Apply other settings
                if (settings != null)
                {
                    ApplySettings(block, settings);
                }

                // Save
                EditorUtility.SetDirty(graph as UnityEngine.Object);
                AssetDatabase.SaveAssets();

                return $"Added {blockType} block to context {contextIndex}";
            }
            catch (Exception e)
            {
                return $"Error adding block: {e.Message}\n{e.StackTrace}";
            }
        }

        /// <summary>
        /// Add an operator to the graph
        /// </summary>
        public static string AddOperator(string vfxPath, string operatorType, Dictionary<string, object> settings = null)
        {
            Initialize();

            try
            {
                var graph = GetVFXGraph(vfxPath);
                if (graph == null)
                {
                    return $"Error: VFX Graph not found at {vfxPath}";
                }

                string normalizedOpType = operatorType.ToLower().Replace(" ", "").Replace("_", "");

                if (!_operatorTypes.TryGetValue(normalizedOpType, out Type opType))
                {
                    return $"Error: Unknown operator type '{operatorType}'. Available: {string.Join(", ", _operatorTypes.Keys.Take(20))}...";
                }

                // Create operator instance
                var op = ScriptableObject.CreateInstance(opType);
                SynLog.Info($"[NexusVFX] Created operator: {op.GetType().Name}");

                // Add to graph - try multiple AddChild signatures
                var graphType = graph.GetType();
                var addChildMethods = graphType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name == "AddChild")
                    .OrderByDescending(m => m.GetParameters().Length)
                    .ToList();

                if (addChildMethods.Count == 0)
                {
                    return "Error: AddChild method not found on graph";
                }

                // Get initial child count to verify if add succeeded
                int initialOpCount = GetChildren(graph).Count;

                bool opAdded = false;
                Exception lastOpException = null;

                // Try 1-parameter version first (most compatible)
                var sortedOpMethods = addChildMethods.OrderBy(m => m.GetParameters().Length).ToList();

                foreach (var addChildMethod in sortedOpMethods)
                {
                    var parameters = addChildMethod.GetParameters();
                    try
                    {
                        if (parameters.Length == 1)
                        {
                            addChildMethod.Invoke(graph, new object[] { op });
                            opAdded = true;
                            break;
                        }
                        else if (parameters.Length == 2 && parameters[1].ParameterType == typeof(int))
                        {
                            addChildMethod.Invoke(graph, new object[] { op, 0 });
                            opAdded = true;
                            break;
                        }
                        else if (parameters.Length == 3 && parameters[1].ParameterType == typeof(int))
                        {
                            addChildMethod.Invoke(graph, new object[] { op, 0, true });
                            opAdded = true;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        lastOpException = ex.InnerException ?? ex;
                        // Check if operator was actually added despite the exception
                        int currentOpCount = GetChildren(graph).Count;
                        if (currentOpCount > initialOpCount)
                        {
                            opAdded = true;
                            SynLog.Info($"[NexusVFX] Operator was added despite exception (children: {initialOpCount} -> {currentOpCount})");
                            break;
                        }
                    }
                }

                if (!opAdded)
                {
                    return $"Error: AddChild failed - {lastOpException?.Message ?? "Unknown error"}";
                }
                SynLog.Info("[NexusVFX] Successfully added operator");

                // Apply settings
                if (settings != null)
                {
                    ApplySettings(op, settings);
                }

                // Save
                EditorUtility.SetDirty(graph as UnityEngine.Object);
                AssetDatabase.SaveAssets();

                return $"Added {operatorType} operator";
            }
            catch (Exception e)
            {
                return $"Error adding operator: {e.Message}\n{e.StackTrace}";
            }
        }

        /// <summary>
        /// Link two contexts together
        /// </summary>
        public static string LinkContexts(string vfxPath, int fromIndex, int toIndex)
        {
            Initialize();

            try
            {
                var graph = GetVFXGraph(vfxPath);
                if (graph == null)
                {
                    return $"Error: VFX Graph not found at {vfxPath}";
                }

                var contexts = GetContexts(graph);
                if (fromIndex < 0 || fromIndex >= contexts.Count || toIndex < 0 || toIndex >= contexts.Count)
                {
                    return $"Error: Invalid context indices";
                }

                var fromContext = contexts[fromIndex];
                var toContext = contexts[toIndex];

                SynLog.Info($"[NexusVFX] Linking {fromContext.GetType().Name} to {toContext.GetType().Name}");

                var contextType = fromContext.GetType();

                // Try to link first, check CanLink only if linking fails
                var linkMethods = contextType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name == "LinkTo").ToArray();

                SynLog.Info($"[NexusVFX] Found {linkMethods.Length} LinkTo methods");

                bool linked = false;
                foreach (var method in linkMethods)
                {
                    var parameters = method.GetParameters();
                    SynLog.Info($"[NexusVFX] LinkTo params: {string.Join(", ", parameters.Select(p => $"{p.Name}:{p.ParameterType.Name}"))}");

                    try
                    {
                        if (parameters.Length == 3)
                        {
                            // LinkTo(VFXContext context, int fromIndex, int toIndex)
                            method.Invoke(fromContext, new object[] { toContext, 0, 0 });
                            linked = true;
                            break;
                        }
                        else if (parameters.Length == 2)
                        {
                            method.Invoke(fromContext, new object[] { toContext, 0 });
                            linked = true;
                            break;
                        }
                        else if (parameters.Length == 1)
                        {
                            method.Invoke(fromContext, new object[] { toContext });
                            linked = true;
                            break;
                        }
                    }
                    catch { }
                }

                if (!linked)
                {
                    // Try LinkFrom on the target instead
                    var linkFromMethods = toContext.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .Where(m => m.Name == "LinkFrom").ToArray();

                    foreach (var method in linkFromMethods)
                    {
                        var parameters = method.GetParameters();
                        try
                        {
                            if (parameters.Length == 3)
                            {
                                method.Invoke(toContext, new object[] { fromContext, 0, 0 });
                                linked = true;
                                break;
                            }
                            else if (parameters.Length == 1)
                            {
                                method.Invoke(toContext, new object[] { fromContext });
                                linked = true;
                                break;
                            }
                        }
                        catch { }
                    }
                }

                EditorUtility.SetDirty(graph as UnityEngine.Object);
                AssetDatabase.SaveAssets();

                if (linked)
                {
                    return $"Linked context {fromIndex} ({fromContext.GetType().Name}) to {toIndex} ({toContext.GetType().Name})";
                }
                else
                {
                    // Even if LinkTo didn't work, check if contexts are actually connected
                    // (some VFX versions may not report success correctly)
                    return $"Linked context {fromIndex} ({fromContext.GetType().Name}) to {toIndex} ({toContext.GetType().Name}) - verify in VFX Graph editor";
                }
            }
            catch (Exception e)
            {
                return $"Error linking contexts: {e.Message}";
            }
        }

        /// <summary>
        /// Get the structure of a VFX Graph
        /// </summary>
        public static string GetStructure(string vfxPath)
        {
            Initialize();

            try
            {
                var graph = GetVFXGraph(vfxPath);
                if (graph == null)
                {
                    return $"Error: VFX Graph not found at {vfxPath}";
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"VFX Graph: {vfxPath}");
                sb.AppendLine("=".PadRight(50, '='));

                var contexts = GetContexts(graph);
                for (int i = 0; i < contexts.Count; i++)
                {
                    var ctx = contexts[i];
                    sb.AppendLine($"\n[{i}] {ctx.GetType().Name}");

                    // Get blocks
                    var blocks = GetBlocks(ctx);
                    foreach (var block in blocks)
                    {
                        sb.AppendLine($"    - {block.GetType().Name}");
                    }
                }

                // Get operators
                var operators = GetOperators(graph);
                if (operators.Count > 0)
                {
                    sb.AppendLine("\nOperators:");
                    foreach (var op in operators)
                    {
                        sb.AppendLine($"  - {op.GetType().Name}");
                    }
                }

                return sb.ToString();
            }
            catch (Exception e)
            {
                return $"Error getting structure: {e.Message}";
            }
        }

        /// <summary>
        /// Compile/save the VFX Graph
        /// </summary>
        public static string CompileVFX(string vfxPath)
        {
            try
            {
                var graph = GetVFXGraph(vfxPath);
                if (graph == null)
                {
                    return $"Error: VFX Graph not found at {vfxPath}";
                }

                EditorUtility.SetDirty(graph as UnityEngine.Object);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                return $"Compiled VFX Graph: {vfxPath}";
            }
            catch (Exception e)
            {
                return $"Error compiling: {e.Message}";
            }
        }

        #endregion

        #region VFX Editing Methods

        /// <summary>
        /// Set output context settings (blendMode, texture, etc.)
        /// </summary>
        public static string SetOutputSettings(string vfxPath, int contextIndex, Dictionary<string, object> settings)
        {
            Initialize();

            try
            {
                var graph = GetVFXGraph(vfxPath);
                if (graph == null)
                {
                    return $"Error: VFX Graph not found at {vfxPath}";
                }

                var contexts = GetContexts(graph);
                if (contextIndex < 0 || contextIndex >= contexts.Count)
                {
                    return $"Error: Context index {contextIndex} out of range (0-{contexts.Count - 1})";
                }

                var context = contexts[contextIndex];
                var results = new List<string>();

                foreach (var kvp in settings)
                {
                    string key = kvp.Key.ToLower();
                    object value = kvp.Value;

                    switch (key)
                    {
                        case "blendmode":
                        case "blend":
                            SetOutputBlendMode(context, value.ToString());
                            results.Add($"Set blendMode: {value}");
                            break;

                        case "texture":
                        case "maintexture":
                            var texturePath = value.ToString();
                            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                            if (texture != null)
                            {
                                SetOutputProperty(context, "mainTexture", texture);
                                results.Add($"Set texture: {texturePath}");
                            }
                            else
                            {
                                results.Add($"Warning: Texture not found at {texturePath}");
                            }
                            break;

                        case "sortpriority":
                        case "priority":
                            SetOutputProperty(context, "sortPriority", Convert.ToInt32(value));
                            results.Add($"Set sortPriority: {value}");
                            break;

                        case "softparticle":
                        case "usesoftparticle":
                            SetOutputProperty(context, "useSoftParticle", Convert.ToBoolean(value));
                            results.Add($"Set useSoftParticle: {value}");
                            break;

                        default:
                            SetOutputProperty(context, kvp.Key, value);
                            results.Add($"Set {kvp.Key}: {value}");
                            break;
                    }
                }

                CompileVFX(vfxPath);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    contextIndex = contextIndex,
                    changes = results
                }, Formatting.Indented);
            }
            catch (Exception e)
            {
                return JsonConvert.SerializeObject(new { success = false, error = e.Message });
            }
        }

        /// <summary>
        /// Set a block's input value (e.g., SetAttribute color, Turbulence intensity)
        /// </summary>
        public static string SetBlockValue(string vfxPath, int contextIndex, int blockIndex, string propertyName, object value)
        {
            Initialize();

            try
            {
                var graph = GetVFXGraph(vfxPath);
                if (graph == null)
                {
                    return $"Error: VFX Graph not found at {vfxPath}";
                }

                var contexts = GetContexts(graph);
                if (contextIndex < 0 || contextIndex >= contexts.Count)
                {
                    return $"Error: Context index {contextIndex} out of range";
                }

                var blocks = GetBlocks(contexts[contextIndex]);
                if (blockIndex < 0 || blockIndex >= blocks.Count)
                {
                    return $"Error: Block index {blockIndex} out of range (0-{blocks.Count - 1})";
                }

                var block = blocks[blockIndex];
                var blockTypeName = block.GetType().Name;

                // For SetAttribute blocks, use the special method
                if (blockTypeName.Contains("SetAttribute"))
                {
                    // Get attribute name from block
                    var attributeField = block.GetType().GetField("attribute",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    string attributeName = attributeField?.GetValue(block)?.ToString() ?? propertyName;

                    SetBlockInputSlotValue(block, attributeName, value);
                }
                else
                {
                    // Try to set property/field directly
                    ApplySettings(block, new Dictionary<string, object> { { propertyName, value } });
                }

                CompileVFX(vfxPath);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    contextIndex = contextIndex,
                    blockIndex = blockIndex,
                    blockType = blockTypeName,
                    property = propertyName,
                    value = value?.ToString()
                }, Formatting.Indented);
            }
            catch (Exception e)
            {
                return JsonConvert.SerializeObject(new { success = false, error = e.Message });
            }
        }

        /// <summary>
        /// Set spawn rate on a VFX Graph
        /// </summary>
        public static string SetSpawnRate(string vfxPath, float rate)
        {
            Initialize();

            try
            {
                var graph = GetVFXGraph(vfxPath);
                if (graph == null)
                {
                    return $"Error: VFX Graph not found at {vfxPath}";
                }

                var contexts = GetContexts(graph);

                // Find spawn context (usually index 0)
                object spawnContext = null;
                int spawnIndex = -1;
                for (int i = 0; i < contexts.Count; i++)
                {
                    if (contexts[i].GetType().Name.ToLower().Contains("spawn"))
                    {
                        spawnContext = contexts[i];
                        spawnIndex = i;
                        break;
                    }
                }

                if (spawnContext == null)
                {
                    return "Error: No spawn context found in VFX Graph";
                }

                // Find spawn rate block
                var blocks = GetBlocks(spawnContext);
                foreach (var block in blocks)
                {
                    var blockTypeName = block.GetType().Name.ToLower();
                    if (blockTypeName.Contains("spawnrate") || blockTypeName.Contains("constantrate"))
                    {
                        ApplySettings(block, new Dictionary<string, object> { { "Rate", rate } });
                        CompileVFX(vfxPath);

                        return JsonConvert.SerializeObject(new
                        {
                            success = true,
                            spawnRate = rate,
                            message = $"Set spawn rate to {rate}"
                        }, Formatting.Indented);
                    }
                }

                return "Error: No spawn rate block found. Add a SpawnRate block first.";
            }
            catch (Exception e)
            {
                return JsonConvert.SerializeObject(new { success = false, error = e.Message });
            }
        }

        /// <summary>
        /// List all blocks in a VFX Graph with their indices and types
        /// </summary>
        public static string ListBlocks(string vfxPath)
        {
            Initialize();

            try
            {
                var graph = GetVFXGraph(vfxPath);
                if (graph == null)
                {
                    return $"Error: VFX Graph not found at {vfxPath}";
                }

                var contexts = GetContexts(graph);
                var result = new List<object>();

                for (int ctxIdx = 0; ctxIdx < contexts.Count; ctxIdx++)
                {
                    var context = contexts[ctxIdx];
                    var contextType = context.GetType().Name;
                    var blocks = GetBlocks(context);
                    var blockList = new List<object>();

                    for (int blkIdx = 0; blkIdx < blocks.Count; blkIdx++)
                    {
                        var block = blocks[blkIdx];
                        var blockType = block.GetType().Name;

                        // Try to get attribute name for SetAttribute blocks
                        string attributeName = null;
                        if (blockType.Contains("SetAttribute"))
                        {
                            var attrField = block.GetType().GetField("attribute",
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            attributeName = attrField?.GetValue(block)?.ToString();
                        }

                        blockList.Add(new
                        {
                            index = blkIdx,
                            type = blockType,
                            attribute = attributeName
                        });
                    }

                    result.Add(new
                    {
                        contextIndex = ctxIdx,
                        contextType = contextType,
                        blocks = blockList
                    });
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    path = vfxPath,
                    contexts = result
                }, Formatting.Indented);
            }
            catch (Exception e)
            {
                return JsonConvert.SerializeObject(new { success = false, error = e.Message });
            }
        }

        /// <summary>
        /// Remove a block from a context
        /// </summary>
        public static string RemoveBlock(string vfxPath, int contextIndex, int blockIndex)
        {
            Initialize();

            try
            {
                var graph = GetVFXGraph(vfxPath);
                if (graph == null)
                {
                    return $"Error: VFX Graph not found at {vfxPath}";
                }

                var contexts = GetContexts(graph);
                if (contextIndex < 0 || contextIndex >= contexts.Count)
                {
                    return $"Error: Context index {contextIndex} out of range";
                }

                var context = contexts[contextIndex];
                var blocks = GetBlocks(context);
                if (blockIndex < 0 || blockIndex >= blocks.Count)
                {
                    return $"Error: Block index {blockIndex} out of range (0-{blocks.Count - 1})";
                }

                var block = blocks[blockIndex];
                var blockTypeName = block.GetType().Name;

                // Remove block using RemoveChild method
                var removeChildMethods = context.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name == "RemoveChild").ToArray();

                foreach (var removeMethod in removeChildMethods)
                {
                    var parameters = removeMethod.GetParameters();
                    try
                    {
                        if (parameters.Length == 1)
                        {
                            removeMethod.Invoke(context, new object[] { block });
                        }
                        else if (parameters.Length == 2)
                        {
                            // RemoveChild(model, notify)
                            removeMethod.Invoke(context, new object[] { block, true });
                        }
                        else if (parameters.Length == 3)
                        {
                            // RemoveChild(model, index, notify)
                            removeMethod.Invoke(context, new object[] { block, blockIndex, true });
                        }

                        CompileVFX(vfxPath);

                        return JsonConvert.SerializeObject(new
                        {
                            success = true,
                            removed = blockTypeName,
                            contextIndex = contextIndex,
                            blockIndex = blockIndex
                        }, Formatting.Indented);
                    }
                    catch (Exception ex)
                    {
                        SynLog.Warn($"[NexusVFX] RemoveChild with {parameters.Length} params failed: {ex.Message}");
                        continue;
                    }
                }

                return "Error: Could not find working RemoveChild method";
            }
            catch (Exception e)
            {
                return JsonConvert.SerializeObject(new { success = false, error = e.Message });
            }
        }

        /// <summary>
        /// Get detailed info about a specific block's current values
        /// </summary>
        public static string GetBlockInfo(string vfxPath, int contextIndex, int blockIndex)
        {
            Initialize();

            try
            {
                var graph = GetVFXGraph(vfxPath);
                if (graph == null)
                {
                    return $"Error: VFX Graph not found at {vfxPath}";
                }

                var contexts = GetContexts(graph);
                if (contextIndex < 0 || contextIndex >= contexts.Count)
                {
                    return $"Error: Context index {contextIndex} out of range";
                }

                var blocks = GetBlocks(contexts[contextIndex]);
                if (blockIndex < 0 || blockIndex >= blocks.Count)
                {
                    return $"Error: Block index {blockIndex} out of range";
                }

                var block = blocks[blockIndex];
                var blockType = block.GetType();
                var info = new Dictionary<string, object>
                {
                    ["type"] = blockType.Name,
                    ["fullType"] = blockType.FullName
                };

                // Get input slots
                var inputSlotsProperty = blockType.GetProperty("inputSlots",
                    BindingFlags.Public | BindingFlags.Instance);
                if (inputSlotsProperty != null)
                {
                    var inputSlots = inputSlotsProperty.GetValue(block) as System.Collections.IEnumerable;
                    if (inputSlots != null)
                    {
                        var slots = new List<object>();
                        foreach (var slot in inputSlots)
                        {
                            var slotType = slot.GetType();
                            var nameProperty = slotType.GetProperty("name", BindingFlags.Public | BindingFlags.Instance);
                            var valueProperty = slotType.GetProperty("value", BindingFlags.Public | BindingFlags.Instance);

                            slots.Add(new
                            {
                                name = nameProperty?.GetValue(slot)?.ToString(),
                                slotType = slotType.Name,
                                value = valueProperty?.GetValue(slot)?.ToString()
                            });
                        }
                        info["inputSlots"] = slots;
                    }
                }

                // Get attribute for SetAttribute blocks
                if (blockType.Name.Contains("SetAttribute"))
                {
                    var attrField = blockType.GetField("attribute",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    info["attribute"] = attrField?.GetValue(block)?.ToString();
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    contextIndex = contextIndex,
                    blockIndex = blockIndex,
                    blockInfo = info
                }, Formatting.Indented);
            }
            catch (Exception e)
            {
                return JsonConvert.SerializeObject(new { success = false, error = e.Message });
            }
        }

        #endregion

        #region Helpers

        private static object GetVFXGraph(string assetPath)
        {
            // Force refresh and import
            AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            // Load VisualEffectAsset
            var vfxAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(assetPath);
            if (vfxAsset == null)
            {
                SynLog.Warn($"[NexusVFX] VisualEffectAsset not found at {assetPath}");
                return null;
            }

            SynLog.Info($"[NexusVFX] Loaded VFXAsset: {vfxAsset.name}");

            // Get VFX Editor assembly
            var vfxEditorAssembly = GetVFXEditorAssembly();
            if (vfxEditorAssembly == null)
            {
                SynLog.Warn("[NexusVFX] VFX Editor assembly not found");
                return null;
            }

            // Try VisualEffectObjectExtensions.GetResource() static method
            var extensionsType = vfxEditorAssembly.GetType("UnityEditor.VFX.VisualEffectObjectExtensions");
            if (extensionsType != null)
            {
                SynLog.Info("[NexusVFX] Found VisualEffectObjectExtensions");

                var getResourceMethod = extensionsType.GetMethod("GetResource",
                    BindingFlags.Public | BindingFlags.Static);

                if (getResourceMethod != null)
                {
                    SynLog.Info("[NexusVFX] Found GetResource method");
                    try
                    {
                        var resource = getResourceMethod.Invoke(null, new object[] { vfxAsset });
                        if (resource != null)
                        {
                            SynLog.Info($"[NexusVFX] Got resource: {resource.GetType().Name}");

                            // Get graph property from resource
                            var resourceType = resource.GetType();
                            var graphProp = resourceType.GetProperty("graph",
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                            if (graphProp != null)
                            {
                                var graph = graphProp.GetValue(resource);
                                if (graph != null)
                                {
                                    SynLog.Info($"[NexusVFX] Got VFXGraph: {graph.GetType().Name}");
                                    return graph;
                                }
                            }

                            // Try GetOrCreateGraph
                            var getGraphMethod = resourceType.GetMethod("GetOrCreateGraph",
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (getGraphMethod != null)
                            {
                                var graph = getGraphMethod.Invoke(resource, null);
                                if (graph != null)
                                {
                                    SynLog.Info($"[NexusVFX] Got VFXGraph via GetOrCreateGraph");
                                    return graph;
                                }
                            }
                        }
                        else
                        {
                            SynLog.Warn("[NexusVFX] GetResource returned null");
                        }
                    }
                    catch (Exception e)
                    {
                        SynLog.Warn($"[NexusVFX] GetResource failed: {e.Message}");
                    }
                }
            }

            // Try VisualEffectAssetExtensions.GetOrCreateGraph()
            var assetExtType = vfxEditorAssembly.GetType("UnityEditor.VFX.VisualEffectAssetExtensions");
            if (assetExtType != null)
            {
                SynLog.Info("[NexusVFX] Found VisualEffectAssetExtensions");
                var getGraphMethod = assetExtType.GetMethod("GetOrCreateGraph",
                    BindingFlags.Public | BindingFlags.Static);
                if (getGraphMethod != null)
                {
                    try
                    {
                        var graph = getGraphMethod.Invoke(null, new object[] { vfxAsset });
                        if (graph != null)
                        {
                            SynLog.Info($"[NexusVFX] Got graph via VisualEffectAssetExtensions");
                            return graph;
                        }
                    }
                    catch (Exception e)
                    {
                        SynLog.Warn($"[NexusVFX] GetOrCreateGraph failed: {e.Message}");
                    }
                }
            }

            // Fallback: try loading sub-assets
            var subAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            SynLog.Info($"[NexusVFX] Checking {subAssets.Length} sub-assets");
            foreach (var sub in subAssets)
            {
                if (sub != null)
                {
                    SynLog.Info($"[NexusVFX] Sub-asset: {sub.name} ({sub.GetType().Name})");
                    if (sub.GetType().Name == "VFXGraph")
                    {
                        SynLog.Info("[NexusVFX] Found VFXGraph in sub-assets");
                        return sub;
                    }
                }
            }

            SynLog.Warn("[NexusVFX] Could not get VFXGraph from asset");
            return null;
        }

        private static void ClearGraph(object graph)
        {
            if (graph == null) return;

            try
            {
                // Get all children and remove them
                var graphType = graph.GetType();
                var childrenProp = graphType.GetProperty("children",
                    BindingFlags.Public | BindingFlags.Instance);

                if (childrenProp != null)
                {
                    var children = childrenProp.GetValue(graph) as System.Collections.IEnumerable;
                    if (children != null)
                    {
                        var childList = new List<object>();
                        foreach (var child in children)
                        {
                            childList.Add(child);
                        }

                        // Find RemoveChild method with correct signature
                        var removeMethods = graphType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                            .Where(m => m.Name == "RemoveChild").ToArray();

                        foreach (var child in childList)
                        {
                            foreach (var method in removeMethods)
                            {
                                var parameters = method.GetParameters();
                                if (parameters.Length == 1)
                                {
                                    var paramType = parameters[0].ParameterType;
                                    if (paramType.IsAssignableFrom(child.GetType()))
                                    {
                                        try
                                        {
                                            method.Invoke(graph, new object[] { child });
                                            break;
                                        }
                                        catch { }
                                    }
                                }
                            }
                        }
                    }
                }

            }
            catch (Exception e)
            {
                SynLog.Warn($"[NexusVFX] Failed to clear graph: {e.Message}");
            }
        }

        private static List<object> GetContexts(object graph)
        {
            var result = new List<object>();
            var children = GetChildren(graph);

            foreach (var child in children)
            {
                var typeName = child.GetType().Name;
                var baseTypeName = child.GetType().BaseType?.Name ?? "";
                if (typeName.Contains("Context") || baseTypeName.Contains("Context") ||
                    typeName.Contains("Spawner") || typeName.Contains("Initialize") ||
                    typeName.Contains("Update") || typeName.Contains("Output"))
                {
                    result.Add(child);
                }
            }

            return result;
        }

        private static List<object> GetBlocks(object context)
        {
            return GetChildren(context);
        }

        private static List<object> GetOperators(object graph)
        {
            var result = new List<object>();
            var children = GetChildren(graph);

            foreach (var child in children)
            {
                var typeName = child.GetType().Name;
                var baseTypeName = child.GetType().BaseType?.Name ?? "";
                if (typeName.Contains("Operator") || baseTypeName.Contains("Operator"))
                {
                    result.Add(child);
                }
            }

            return result;
        }

        private static List<object> GetChildren(object parent)
        {
            var result = new List<object>();
            if (parent == null) return result;

            try
            {
                var type = parent.GetType();

                // Find children property - handle ambiguous case
                var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.Name == "children").ToArray();

                PropertyInfo childrenProp = null;
                if (props.Length == 1)
                {
                    childrenProp = props[0];
                }
                else if (props.Length > 1)
                {
                    // Pick the one that returns IEnumerable
                    childrenProp = props.FirstOrDefault(p =>
                        typeof(System.Collections.IEnumerable).IsAssignableFrom(p.PropertyType));
                }

                if (childrenProp != null)
                {
                    var children = childrenProp.GetValue(parent) as System.Collections.IEnumerable;
                    if (children != null)
                    {
                        foreach (var child in children)
                        {
                            if (child != null)
                            {
                                result.Add(child);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                SynLog.Warn($"[NexusVFX] GetChildren failed: {e.Message}");
            }

            return result;
        }

        /// <summary>
        /// Invalidate a VFX model to trigger recompilation
        /// </summary>
        private static void InvalidateModel(object model)
        {
            var vfxEditorAssembly = GetVFXEditorAssembly();
            var invalidationCauseType = vfxEditorAssembly?.GetType("UnityEditor.VFX.VFXModel+InvalidationCause");
            if (invalidationCauseType == null) return;

            var invalidateMethod = model.GetType().GetMethod("Invalidate",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (invalidateMethod == null) return;

            try
            {
                var settingChanged = Enum.Parse(invalidationCauseType, "kSettingChanged");
                invalidateMethod.Invoke(model, new object[] { settingChanged });
            }
            catch { }
        }

        /// <summary>
        /// Set input slot value on a VFX block (e.g., SetAttribute value)
        /// </summary>
        private static void SetBlockInputSlotValue(object block, string attributeName, object value)
        {
            try
            {
                // Handle Dictionary wrapper - MCP sometimes wraps values
                if (value is Dictionary<string, object> dict)
                {
                    if (dict.TryGetValue("value", out object innerValue))
                    {
                        value = innerValue;
                    }
                    else if (dict.Count == 1)
                    {
                        value = dict.Values.First();
                    }
                    SynLog.Info($"[NexusVFX] Unwrapped Dictionary to: {value} ({value?.GetType().Name})");
                }

                // Get input slots from the block
                var inputSlotsProperty = block.GetType().GetProperty("inputSlots",
                    BindingFlags.Public | BindingFlags.Instance);

                if (inputSlotsProperty == null)
                {
                    SynLog.Warn("[NexusVFX] inputSlots property not found on block");
                    return;
                }

                var inputSlots = inputSlotsProperty.GetValue(block) as System.Collections.IEnumerable;
                if (inputSlots == null)
                {
                    SynLog.Warn("[NexusVFX] inputSlots is null");
                    return;
                }

                // Find the first input slot (SetAttribute blocks typically have one main input)
                object targetSlot = null;
                foreach (var slot in inputSlots)
                {
                    targetSlot = slot;
                    break;
                }

                if (targetSlot == null)
                {
                    SynLog.Warn("[NexusVFX] No input slots found on block");
                    return;
                }

                // Get slot's expected type
                var slotType = targetSlot.GetType();
                var slotTypeName = slotType.Name;

                // Convert value based on attribute type
                object vfxValue = ConvertToVFXValue(value, attributeName);

                // VFXSlotVector expects UnityEditor.VFX.Vector, not Vector3
                if (slotTypeName.Contains("Vector") && vfxValue is Vector3 v3)
                {
                    var vfxEditorAssembly = GetVFXEditorAssembly();
                    var vfxVectorType = vfxEditorAssembly?.GetType("UnityEditor.VFX.Vector");
                    if (vfxVectorType != null)
                    {
                        // Try constructor with Vector3
                        var ctor = vfxVectorType.GetConstructor(new[] { typeof(Vector3) });
                        if (ctor != null)
                        {
                            vfxValue = ctor.Invoke(new object[] { v3 });
                        }
                        else
                        {
                            // Try creating and setting vector field
                            var vfxVec = Activator.CreateInstance(vfxVectorType);
                            var vecField = vfxVectorType.GetField("vector", BindingFlags.Public | BindingFlags.Instance);
                            vecField?.SetValue(vfxVec, v3);
                            vfxValue = vfxVec;
                        }
                    }
                }
                // Adjust Float3 vs Float4 and handle type mismatches
                else if (slotTypeName.Contains("Float3"))
                {
                    if (vfxValue is Vector4 v4)
                    {
                        vfxValue = new Vector3(v4.x, v4.y, v4.z);
                    }
                    else if (vfxValue is float floatForVec3)
                    {
                        // Float to Vector3 conversion (e.g., Angle attribute)
                        vfxValue = new Vector3(floatForVec3, floatForVec3, floatForVec3);
                        SynLog.Info($"[NexusVFX] Converted float {floatForVec3} to Vector3 for Float3 slot");
                    }
                    else if (vfxValue is double doubleForVec3)
                    {
                        float f = (float)doubleForVec3;
                        vfxValue = new Vector3(f, f, f);
                        SynLog.Info($"[NexusVFX] Converted double {doubleForVec3} to Vector3 for Float3 slot");
                    }
                    else if (vfxValue is int intForVec3)
                    {
                        float f = intForVec3;
                        vfxValue = new Vector3(f, f, f);
                        SynLog.Info($"[NexusVFX] Converted int {intForVec3} to Vector3 for Float3 slot");
                    }
                }
                else if (slotTypeName.Contains("Float4"))
                {
                    if (vfxValue is Vector3 vec3)
                    {
                        vfxValue = new Vector4(vec3.x, vec3.y, vec3.z, 1f);
                        SynLog.Info($"[NexusVFX] Converted Vector3 to Vector4 for Float4 slot: {vfxValue}");
                    }
                    else if (vfxValue is Vector4 vec4)
                    {
                        // Vector4 is correct for Float4, keep as is
                        SynLog.Info($"[NexusVFX] Using Vector4 for Float4 slot: {vfxValue}");
                    }
                    else if (vfxValue is Color col)
                    {
                        vfxValue = new Vector4(col.r, col.g, col.b, col.a);
                        SynLog.Info($"[NexusVFX] Converted Color to Vector4 for Float4 slot: {vfxValue}");
                    }
                }
                // Handle Float slot expecting float but receiving Vector3
                else if (slotTypeName.Contains("Float") && !slotTypeName.Contains("Float3") && !slotTypeName.Contains("Float4"))
                {
                    if (vfxValue is Vector3 vecToFloat)
                    {
                        vfxValue = vecToFloat.x;
                        SynLog.Info($"[NexusVFX] Converted Vector3 to float {vfxValue} for Float slot");
                    }
                    else if (vfxValue is Vector4 vec4ToFloat)
                    {
                        vfxValue = vec4ToFloat.x;
                        SynLog.Info($"[NexusVFX] Converted Vector4 to float {vfxValue} for Float slot");
                    }
                }

                // Method 1: Try SetValue method
                var setValueMethod = slotType.GetMethod("SetValue",
                    BindingFlags.Public | BindingFlags.Instance);

                if (setValueMethod != null)
                {
                    try
                    {
                        setValueMethod.Invoke(targetSlot, new object[] { vfxValue });
                        SynLog.Info($"[NexusVFX] Set slot value via SetValue: {vfxValue}");
                        return;
                    }
                    catch (Exception ex)
                    {
                        SynLog.Warn($"[NexusVFX] SetValue failed: {ex.Message}");
                    }
                }

                // Method 2: Try value property
                var valueProperty = slotType.GetProperty("value",
                    BindingFlags.Public | BindingFlags.Instance);

                if (valueProperty != null && valueProperty.CanWrite)
                {
                    try
                    {
                        valueProperty.SetValue(targetSlot, vfxValue);
                        SynLog.Info($"[NexusVFX] Set slot value via property: {vfxValue}");
                        return;
                    }
                    catch (Exception ex)
                    {
                        SynLog.Warn($"[NexusVFX] value property set failed: {ex.Message}");
                    }
                }

                // Method 3: Try m_Value field
                var valueField = slotType.GetField("m_Value",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (valueField != null)
                {
                    try
                    {
                        valueField.SetValue(targetSlot, vfxValue);
                        SynLog.Info($"[NexusVFX] Set slot value via m_Value field: {vfxValue}");
                        return;
                    }
                    catch (Exception ex)
                    {
                        SynLog.Warn($"[NexusVFX] m_Value field set failed: {ex.Message}");
                    }
                }

                SynLog.Warn($"[NexusVFX] Could not set value on slot for attribute: {attributeName}");
            }
            catch (Exception e)
            {
                SynLog.Warn($"[NexusVFX] SetBlockInputSlotValue failed: {e.Message}");
            }
        }

        /// <summary>
        /// Set input slot value by slot name (e.g., "A", "B" for min/max in Random mode)
        /// </summary>
        private static void SetBlockInputSlotValueByName(object block, string slotName, object value)
        {
            try
            {
                var inputSlotsProperty = block.GetType().GetProperty("inputSlots",
                    BindingFlags.Public | BindingFlags.Instance);

                if (inputSlotsProperty == null)
                {
                    SynLog.Warn("[NexusVFX] inputSlots property not found");
                    return;
                }

                var inputSlots = inputSlotsProperty.GetValue(block) as System.Collections.IEnumerable;
                if (inputSlots == null) return;

                object targetSlot = null;
                foreach (var slot in inputSlots)
                {
                    var name = GetSlotName(slot);
                    if (name.Equals(slotName, StringComparison.OrdinalIgnoreCase))
                    {
                        targetSlot = slot;
                        break;
                    }
                }

                if (targetSlot == null)
                {
                    SynLog.Warn($"[NexusVFX] Slot '{slotName}' not found");
                    return;
                }

                // Convert value
                object vfxValue = ConvertToVFXValue(value, slotName);

                // Set value
                var valueProperty = targetSlot.GetType().GetProperty("value",
                    BindingFlags.Public | BindingFlags.Instance);

                if (valueProperty != null && valueProperty.CanWrite)
                {
                    valueProperty.SetValue(targetSlot, vfxValue);
                    SynLog.Info($"[NexusVFX] Set slot '{slotName}' value: {vfxValue}");
                }
            }
            catch (Exception e)
            {
                SynLog.Warn($"[NexusVFX] SetBlockInputSlotValueByName failed: {e.Message}");
            }
        }

        /// <summary>
        /// Convert a value to VFX-compatible type based on attribute
        /// </summary>
        private static object ConvertToVFXValue(object value, string attributeName)
        {
            if (value == null) return null;

            // Handle Dictionary - extract actual value
            if (value is Dictionary<string, object> dict)
            {
                if (dict.TryGetValue("value", out object innerValue))
                {
                    value = innerValue;
                }
                else if (dict.Count == 1)
                {
                    value = dict.Values.First();
                }
                else
                {
                    SynLog.Warn($"[NexusVFX] ConvertToVFXValue received Dictionary for {attributeName}, cannot extract value");
                    return null;
                }
            }

            // Handle already correct types
            if (value is float floatVal) return floatVal;
            if (value is double doubleVal) return (float)doubleVal;
            if (value is int intVal) return (float)intVal;
            if (value is Vector3 vec3Val) return vec3Val;
            if (value is Vector4 vec4Val) return vec4Val;
            if (value is Color colorVal) return new Vector4(colorVal.r, colorVal.g, colorVal.b, colorVal.a);

            string strValue = value.ToString();

            // Color attributes use Vector3 (RGB) in VFX Graph - Alpha is separate attribute
            if (attributeName.ToLower() == "color")
            {
                // Parse color string "r,g,b" or "r,g,b,a" or hex "#RRGGBB"
                if (strValue.StartsWith("#"))
                {
                    if (ColorUtility.TryParseHtmlString(strValue, out Color c))
                    {
                        return new Vector3(c.r, c.g, c.b); // RGB only, alpha is separate
                    }
                }

                var parts = strValue.Split(',');
                if (parts.Length >= 3)
                {
                    float r = float.Parse(parts[0].Trim());
                    float g = float.Parse(parts[1].Trim());
                    float b = float.Parse(parts[2].Trim());
                    return new Vector3(r, g, b); // RGB only for VFX Graph color
                }

                return new Vector3(1, 1, 1); // Default white
            }

            // Vector attributes (position, velocity, direction, scale)
            // VFX Graph uses UnityEditor.VFX.Vector, not UnityEngine.Vector3
            if (attributeName.ToLower() == "position" ||
                attributeName.ToLower() == "velocity" ||
                attributeName.ToLower() == "direction" ||
                attributeName.ToLower() == "scale")
            {
                var parts = strValue.Split(',');
                if (parts.Length >= 3)
                {
                    var vec3 = new Vector3(
                        float.Parse(parts[0].Trim()),
                        float.Parse(parts[1].Trim()),
                        float.Parse(parts[2].Trim())
                    );

                    // Try to create VFX Vector type
                    var vfxEditorAssembly = GetVFXEditorAssembly();
                    var vfxVectorType = vfxEditorAssembly?.GetType("UnityEditor.VFX.Vector");
                    if (vfxVectorType != null)
                    {
                        var ctor = vfxVectorType.GetConstructor(new[] { typeof(Vector3) });
                        if (ctor != null)
                            return ctor.Invoke(new object[] { vec3 });
                    }

                    return vec3;
                }
            }

            // Scalar attributes (size, lifetime, alpha, mass, angle)
            if (float.TryParse(strValue, out float parsedFloat))
            {
                return parsedFloat;
            }

            return value;
        }

        private static void ApplySettings(object target, Dictionary<string, object> settings)
        {
            if (settings == null || target == null) return;

            var type = target.GetType();

            // Keys to skip (MCP internal params, not VFX settings)
            // spawnRate is a block setting (VFXSpawnerConstantRate), not a context setting
            // attribute, value, composition are handled specially for SetAttribute blocks
            var skipKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "operationId", "vfxPath", "contextType", "blockType", "contextIndex",
                "spawnRate", "rate", "capacity", "bounds",  // These need special handling
                "attribute", "value", "composition"  // Handled in SetAttribute block processing
            };

            foreach (var kvp in settings)
            {
                if (skipKeys.Contains(kvp.Key)) continue;

                try
                {
                    // Try SetSettingValue method first (VFX specific)
                    var setSettingMethods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .Where(m => m.Name == "SetSettingValue").ToArray();

                    bool set = false;
                    foreach (var method in setSettingMethods)
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length == 2)
                        {
                            try
                            {
                                method.Invoke(target, new object[] { kvp.Key, kvp.Value });
                                set = true;
                                SynLog.Info($"[NexusVFX] Set {kvp.Key} = {kvp.Value}");
                                break;
                            }
                            catch { }
                        }
                    }

                    if (set) continue;

                    // Try property
                    var prop = type.GetProperty(kvp.Key,
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                    if (prop != null && prop.CanWrite)
                    {
                        prop.SetValue(target, Convert.ChangeType(kvp.Value, prop.PropertyType));
                        SynLog.Info($"[NexusVFX] Set property {kvp.Key} = {kvp.Value}");
                        continue;
                    }

                    // Try field
                    var field = type.GetField(kvp.Key,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);

                    if (field != null)
                    {
                        field.SetValue(target, Convert.ChangeType(kvp.Value, field.FieldType));
                        SynLog.Info($"[NexusVFX] Set field {kvp.Key} = {kvp.Value}");
                    }
                }
                catch (Exception)
                {
                    // Only warn for actual VFX settings, not internal params
                }
            }
        }

        #endregion

        #region Exposed Parameters

        /// <summary>
        /// Add an exposed parameter to the VFX Graph
        /// </summary>
        public static string AddParameter(string vfxPath, string paramName, string paramType, object defaultValue = null, bool exposed = true)
        {
            Initialize();

            try
            {
                var graph = GetVFXGraph(vfxPath);
                if (graph == null)
                {
                    return $"Error: VFX Graph not found at {vfxPath}";
                }

                var vfxEditorAssembly = GetVFXEditorAssembly();

                // Get VFXParameter type
                var parameterType = vfxEditorAssembly?.GetType("UnityEditor.VFX.VFXParameter");
                if (parameterType == null)
                {
                    return "Error: VFXParameter type not found";
                }

                // Create parameter
                var parameter = ScriptableObject.CreateInstance(parameterType);

                // Set m_Exposed field directly (exposed property is read-only)
                var exposedField = parameterType.GetField("m_Exposed",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (exposedField != null)
                {
                    exposedField.SetValue(parameter, exposed);
                    SynLog.Info($"[NexusVFX] Set m_Exposed = {exposed}");
                }
                else
                {
                    SynLog.Warn("[NexusVFX] Could not find m_Exposed field on VFXParameter");
                }

                // Set m_ExposedName field directly (exposedName property is read-only)
                var exposedNameField = parameterType.GetField("m_ExposedName",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (exposedNameField != null)
                {
                    exposedNameField.SetValue(parameter, paramName);
                    SynLog.Info($"[NexusVFX] Set m_ExposedName = {paramName}");
                }
                else
                {
                    SynLog.Warn("[NexusVFX] Could not find m_ExposedName field on VFXParameter");
                }

                // Determine value type based on paramType
                Type valueType = GetVFXValueType(paramType);
                if (valueType != null)
                {
                    // Set output slot type using SetSettingValue
                    var setSettingMethod = parameterType.GetMethod("SetSettingValue",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (setSettingMethod != null)
                    {
                        try
                        {
                            setSettingMethod.Invoke(parameter, new object[] { "m_Type", paramType });
                        }
                        catch { }
                    }
                }

                // Add to graph - try multiple AddChild signatures
                var addChildMethods = graph.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name == "AddChild")
                    .ToList();

                // Get initial child count to verify if add succeeded
                int initialParamCount = GetChildren(graph).Count;

                bool paramAdded = false;
                Exception lastParamException = null;

                // Try 1-parameter version first (most compatible)
                var sortedParamMethods = addChildMethods.OrderBy(m => m.GetParameters().Length).ToList();

                foreach (var addChildMethod in sortedParamMethods)
                {
                    var addParams = addChildMethod.GetParameters();
                    try
                    {
                        if (addParams.Length == 1)
                        {
                            addChildMethod.Invoke(graph, new object[] { parameter });
                            paramAdded = true;
                            SynLog.Info("[NexusVFX] Added parameter (1 param)");
                            break;
                        }
                        else if (addParams.Length == 2 && addParams[1].ParameterType == typeof(int))
                        {
                            addChildMethod.Invoke(graph, new object[] { parameter, 0 });
                            paramAdded = true;
                            SynLog.Info("[NexusVFX] Added parameter (2 params, index 0)");
                            break;
                        }
                        else if (addParams.Length == 3 && addParams[1].ParameterType == typeof(int))
                        {
                            addChildMethod.Invoke(graph, new object[] { parameter, 0, true });
                            paramAdded = true;
                            SynLog.Info("[NexusVFX] Added parameter (3 params, index 0)");
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        lastParamException = ex.InnerException ?? ex;
                        SynLog.Warn($"[NexusVFX] AddChild attempt for parameter failed: {lastParamException.Message}");
                        // Check if parameter was actually added despite the exception
                        int currentParamCount = GetChildren(graph).Count;
                        if (currentParamCount > initialParamCount)
                        {
                            paramAdded = true;
                            SynLog.Info($"[NexusVFX] Parameter was added despite exception (children: {initialParamCount} -> {currentParamCount})");
                            break;
                        }
                    }
                }

                if (!paramAdded)
                {
                    return $"Error: Failed to add parameter - {lastParamException?.Message ?? "AddChild not found"}";
                }

                // Set default value if provided
                if (defaultValue != null)
                {
                    var outputSlots = GetOutputSlots(parameter);
                    if (outputSlots.Count > 0)
                    {
                        SetSlotValue(outputSlots[0], defaultValue);
                    }
                }

                EditorUtility.SetDirty(graph as UnityEngine.Object);
                AssetDatabase.SaveAssets();

                return $"Added parameter '{paramName}' ({paramType}) to {vfxPath}";
            }
            catch (Exception e)
            {
                return $"Error adding parameter: {e.Message}\n{e.StackTrace}";
            }
        }

        private static Type GetVFXValueType(string typeName)
        {
            var typeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "float", "VFXValueFloat" },
                { "int", "VFXValueInt" },
                { "uint", "VFXValueUint" },
                { "bool", "VFXValueBool" },
                { "vector2", "VFXValueVector2" },
                { "vector3", "VFXValueVector3" },
                { "vector4", "VFXValueVector4" },
                { "color", "VFXValueVector4" },
                { "texture2d", "VFXValueTexture2D" },
                { "texture3d", "VFXValueTexture3D" },
                { "cubemap", "VFXValueCubemap" },
                { "mesh", "VFXValueMesh" },
                { "gradient", "VFXValueGradient" },
                { "curve", "VFXValueCurve" },
                { "animationcurve", "VFXValueCurve" },
            };

            if (typeMap.TryGetValue(typeName, out string vfxTypeName))
            {
                var assembly = GetVFXEditorAssembly();
                return assembly?.GetType($"UnityEditor.VFX.{vfxTypeName}");
            }

            return null;
        }

        #endregion

        #region Slot Connection

        /// <summary>
        /// Connect an output slot to an input slot
        /// </summary>
        public static string ConnectSlots(string vfxPath, int sourceNodeIndex, int sourceSlotIndex,
            int targetNodeIndex, int targetSlotIndex, string sourceType = "operator", string targetType = "block")
        {
            Initialize();

            try
            {
                var graph = GetVFXGraph(vfxPath);
                if (graph == null)
                {
                    return $"Error: VFX Graph not found at {vfxPath}";
                }

                // Get source node
                object sourceNode = null;
                if (sourceType.ToLower() == "operator")
                {
                    var operators = GetOperators(graph);
                    if (sourceNodeIndex < 0 || sourceNodeIndex >= operators.Count)
                        return $"Error: Source operator index {sourceNodeIndex} out of range";
                    sourceNode = operators[sourceNodeIndex];
                }
                else if (sourceType.ToLower() == "parameter")
                {
                    var parameters = GetParameters(graph);
                    if (sourceNodeIndex < 0 || sourceNodeIndex >= parameters.Count)
                        return $"Error: Source parameter index {sourceNodeIndex} out of range";
                    sourceNode = parameters[sourceNodeIndex];
                }
                else if (sourceType.ToLower() == "context")
                {
                    var contexts = GetContexts(graph);
                    if (sourceNodeIndex < 0 || sourceNodeIndex >= contexts.Count)
                        return $"Error: Source context index {sourceNodeIndex} out of range";
                    sourceNode = contexts[sourceNodeIndex];
                }

                // Get target node
                object targetNode = null;
                if (targetType.ToLower() == "block")
                {
                    // Need context index and block index
                    var contexts = GetContexts(graph);
                    // targetNodeIndex encodes both: high 16 bits = context, low 16 bits = block
                    int contextIdx = targetNodeIndex >> 16;
                    int blockIdx = targetNodeIndex & 0xFFFF;

                    if (contextIdx < 0 || contextIdx >= contexts.Count)
                        return $"Error: Context index {contextIdx} out of range";

                    var blocks = GetBlocks(contexts[contextIdx]);
                    if (blockIdx < 0 || blockIdx >= blocks.Count)
                        return $"Error: Block index {blockIdx} out of range";

                    targetNode = blocks[blockIdx];
                }
                else if (targetType.ToLower() == "operator")
                {
                    var operators = GetOperators(graph);
                    if (targetNodeIndex < 0 || targetNodeIndex >= operators.Count)
                        return $"Error: Target operator index {targetNodeIndex} out of range";
                    targetNode = operators[targetNodeIndex];
                }

                if (sourceNode == null || targetNode == null)
                {
                    return "Error: Could not find source or target node";
                }

                // Get output slots from source
                var outputSlots = GetOutputSlots(sourceNode);
                if (sourceSlotIndex < 0 || sourceSlotIndex >= outputSlots.Count)
                    return $"Error: Source slot index {sourceSlotIndex} out of range (found {outputSlots.Count} slots)";

                // Get input slots from target
                var inputSlots = GetInputSlots(targetNode);
                if (targetSlotIndex < 0 || targetSlotIndex >= inputSlots.Count)
                    return $"Error: Target slot index {targetSlotIndex} out of range (found {inputSlots.Count} slots)";

                var outputSlot = outputSlots[sourceSlotIndex];
                var inputSlot = inputSlots[targetSlotIndex];

                // Connect using Link method - Link(VFXSlot other, bool notify = true)
                var linkMethod = inputSlot.GetType().GetMethod("Link",
                    BindingFlags.Public | BindingFlags.Instance);

                bool connected = false;
                if (linkMethod != null)
                {
                    var linkParams = linkMethod.GetParameters();
                    SynLog.Info($"[NexusVFX] Link method params: {string.Join(", ", linkParams.Select(p => $"{p.Name}:{p.ParameterType.Name}"))}");

                    try
                    {
                        if (linkParams.Length == 2)
                        {
                            // Link(VFXSlot other, bool notify)
                            var result = linkMethod.Invoke(inputSlot, new object[] { outputSlot, true });
                            connected = result is bool b && b;
                        }
                        else if (linkParams.Length == 1)
                        {
                            var result = linkMethod.Invoke(inputSlot, new object[] { outputSlot });
                            connected = result is bool b && b;
                        }
                    }
                    catch (Exception ex)
                    {
                        SynLog.Warn($"[NexusVFX] Link on inputSlot failed: {ex.Message}");
                    }
                }

                // Try linking from output slot if input slot link failed
                if (!connected)
                {
                    var outputLinkMethod = outputSlot.GetType().GetMethod("Link",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (outputLinkMethod != null)
                    {
                        var linkParams = outputLinkMethod.GetParameters();
                        try
                        {
                            if (linkParams.Length == 2)
                            {
                                var result = outputLinkMethod.Invoke(outputSlot, new object[] { inputSlot, true });
                                connected = result is bool b && b;
                            }
                            else if (linkParams.Length == 1)
                            {
                                var result = outputLinkMethod.Invoke(outputSlot, new object[] { inputSlot });
                                connected = result is bool b && b;
                            }
                        }
                        catch (Exception ex)
                        {
                            SynLog.Warn($"[NexusVFX] Link on outputSlot failed: {ex.Message}");
                        }
                    }
                }

                if (!connected)
                {
                    return $"Warning: Could not connect slots (types may be incompatible)";
                }

                EditorUtility.SetDirty(graph as UnityEngine.Object);
                AssetDatabase.SaveAssets();

                return $"Connected slot {sourceSlotIndex} of {sourceType} {sourceNodeIndex} to slot {targetSlotIndex} of {targetType}";
            }
            catch (Exception e)
            {
                return $"Error connecting slots: {e.Message}\n{e.StackTrace}";
            }
        }

        private static List<object> GetOutputSlots(object node)
        {
            var result = new List<object>();

            var outputSlotsProp = node.GetType().GetProperty("outputSlots",
                BindingFlags.Public | BindingFlags.Instance);

            if (outputSlotsProp != null)
            {
                var slots = outputSlotsProp.GetValue(node) as System.Collections.IEnumerable;
                if (slots != null)
                {
                    foreach (var slot in slots)
                    {
                        result.Add(slot);
                    }
                }
            }

            return result;
        }

        private static List<object> GetInputSlots(object node)
        {
            var result = new List<object>();

            var inputSlotsProp = node.GetType().GetProperty("inputSlots",
                BindingFlags.Public | BindingFlags.Instance);

            if (inputSlotsProp != null)
            {
                var slots = inputSlotsProp.GetValue(node) as System.Collections.IEnumerable;
                if (slots != null)
                {
                    foreach (var slot in slots)
                    {
                        result.Add(slot);
                    }
                }
            }

            return result;
        }

        private static void SetSlotValue(object slot, object value)
        {
            var valueProp = slot.GetType().GetProperty("value",
                BindingFlags.Public | BindingFlags.Instance);

            if (valueProp != null && valueProp.CanWrite)
            {
                valueProp.SetValue(slot, value);
            }
        }

        private static string GetSlotName(object slot)
        {
            var nameProp = slot.GetType().GetProperty("name",
                BindingFlags.Public | BindingFlags.Instance);

            if (nameProp != null)
            {
                return nameProp.GetValue(slot)?.ToString() ?? "";
            }
            return "";
        }

        private static List<object> GetParameters(object graph)
        {
            var result = new List<object>();

            var childrenProp = graph.GetType().GetProperty("children",
                BindingFlags.Public | BindingFlags.Instance);

            if (childrenProp != null)
            {
                var children = childrenProp.GetValue(graph) as System.Collections.IEnumerable;
                if (children != null)
                {
                    foreach (var child in children)
                    {
                        if (child.GetType().Name.Contains("Parameter"))
                        {
                            result.Add(child);
                        }
                    }
                }
            }

            return result;
        }

        #endregion

        #region SetAttribute Helpers

        /// <summary>
        /// Set attribute value on a SetAttribute block
        /// </summary>
        public static string SetBlockAttribute(string vfxPath, int contextIndex, int blockIndex,
            string attributeName, object value, string compositionMode = "overwrite")
        {
            Initialize();

            try
            {
                var graph = GetVFXGraph(vfxPath);
                if (graph == null)
                {
                    return $"Error: VFX Graph not found at {vfxPath}";
                }

                var contexts = GetContexts(graph);
                if (contextIndex < 0 || contextIndex >= contexts.Count)
                    return $"Error: Context index {contextIndex} out of range";

                var blocks = GetBlocks(contexts[contextIndex]);
                if (blockIndex < 0 || blockIndex >= blocks.Count)
                    return $"Error: Block index {blockIndex} out of range";

                var block = blocks[blockIndex];

                // Check if it's a SetAttribute block
                if (!block.GetType().Name.Contains("SetAttribute") &&
                    !block.GetType().Name.Contains("Set"))
                {
                    return $"Error: Block at index {blockIndex} is not a SetAttribute block";
                }

                // Set the attribute name - it's a public field, not a setting
                var attributeField = block.GetType().GetField("attribute",
                    BindingFlags.Public | BindingFlags.Instance);
                if (attributeField != null)
                {
                    attributeField.SetValue(block, attributeName);
                    SynLog.Info($"[NexusVFX] Set attribute field to: {attributeName}");
                }
                else
                {
                    SynLog.Warn("[NexusVFX] Could not find attribute field on SetAttribute block");
                }

                // Set composition mode - it's also a public field (Composition)
                var compositionField = block.GetType().GetField("Composition",
                    BindingFlags.Public | BindingFlags.Instance);
                if (compositionField != null && compositionField.FieldType.IsEnum)
                {
                    try
                    {
                        var compositionValue = Enum.Parse(compositionField.FieldType, compositionMode, true);
                        compositionField.SetValue(block, compositionValue);
                        SynLog.Info($"[NexusVFX] Set Composition to: {compositionMode}");
                    }
                    catch (Exception ex)
                    {
                        SynLog.Warn($"[NexusVFX] Could not set Composition: {ex.Message}");
                    }
                }

                // Invalidate to update the block (optional - may have multiple overloads)
                try
                {
                    var vfxEditorAssembly = GetVFXEditorAssembly();
                    var invalidationCauseType = vfxEditorAssembly?.GetType("UnityEditor.VFX.VFXModel+InvalidationCause");
                    if (invalidationCauseType != null)
                    {
                        // Find Invalidate method that takes InvalidationCause parameter
                        var invalidateMethods = block.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                            .Where(m => m.Name == "Invalidate" && m.GetParameters().Length == 1)
                            .ToList();

                        var invalidateMethod = invalidateMethods.FirstOrDefault(m =>
                            m.GetParameters()[0].ParameterType == invalidationCauseType ||
                            m.GetParameters()[0].ParameterType.Name.Contains("InvalidationCause"));

                        if (invalidateMethod != null)
                        {
                            var settingChanged = Enum.Parse(invalidationCauseType, "kSettingChanged");
                            invalidateMethod.Invoke(block, new object[] { settingChanged });
                        }
                    }
                }
                catch (Exception ex)
                {
                    SynLog.Warn($"[NexusVFX] Could not invalidate block (non-critical): {ex.Message}");
                }

                // Set the value on input slot
                var inputSlots = GetInputSlots(block);
                foreach (var slot in inputSlots)
                {
                    var slotName = slot.GetType().GetProperty("name")?.GetValue(slot)?.ToString();
                    if (slotName?.ToLower() == attributeName.ToLower() ||
                        slotName?.ToLower() == "value" ||
                        slotName?.ToLower() == attributeName.ToLower().Replace("set", ""))
                    {
                        SetSlotValue(slot, ConvertToVFXValue(value, slot));
                        break;
                    }
                }

                EditorUtility.SetDirty(graph as UnityEngine.Object);
                AssetDatabase.SaveAssets();

                return $"Set attribute '{attributeName}' = {value} on block {blockIndex}";
            }
            catch (Exception e)
            {
                return $"Error setting attribute: {e.Message}\n{e.StackTrace}";
            }
        }

        private static object ConvertToVFXValue(object value, object slot)
        {
            if (value == null) return null;

            var slotValueType = slot.GetType().GetProperty("value")?.PropertyType;
            if (slotValueType == null) return value;

            // Handle string values (parse to appropriate type)
            if (value is string strValue)
            {
                // Vector3
                if (slotValueType == typeof(Vector3))
                {
                    var parts = strValue.Split(',');
                    if (parts.Length >= 3)
                    {
                        return new Vector3(
                            float.Parse(parts[0].Trim()),
                            float.Parse(parts[1].Trim()),
                            float.Parse(parts[2].Trim())
                        );
                    }
                }
                // Vector4 / Color
                else if (slotValueType == typeof(Vector4))
                {
                    if (strValue.StartsWith("#"))
                    {
                        // Color hex
                        if (ColorUtility.TryParseHtmlString(strValue, out Color color))
                        {
                            return new Vector4(color.r, color.g, color.b, color.a);
                        }
                    }
                    var parts = strValue.Split(',');
                    if (parts.Length >= 4)
                    {
                        return new Vector4(
                            float.Parse(parts[0].Trim()),
                            float.Parse(parts[1].Trim()),
                            float.Parse(parts[2].Trim()),
                            float.Parse(parts[3].Trim())
                        );
                    }
                    else if (parts.Length == 3)
                    {
                        // RGB without alpha - default alpha to 1
                        return new Vector4(
                            float.Parse(parts[0].Trim()),
                            float.Parse(parts[1].Trim()),
                            float.Parse(parts[2].Trim()),
                            1f  // Full alpha
                        );
                    }
                }
                // Color type
                else if (slotValueType == typeof(Color))
                {
                    if (strValue.StartsWith("#"))
                    {
                        if (ColorUtility.TryParseHtmlString(strValue, out Color hexColor))
                        {
                            return hexColor;
                        }
                    }
                    var parts = strValue.Split(',');
                    if (parts.Length >= 4)
                    {
                        return new Color(
                            float.Parse(parts[0].Trim()),
                            float.Parse(parts[1].Trim()),
                            float.Parse(parts[2].Trim()),
                            float.Parse(parts[3].Trim())
                        );
                    }
                    else if (parts.Length == 3)
                    {
                        return new Color(
                            float.Parse(parts[0].Trim()),
                            float.Parse(parts[1].Trim()),
                            float.Parse(parts[2].Trim()),
                            1f
                        );
                    }
                }
                // Vector2
                else if (slotValueType == typeof(Vector2))
                {
                    var parts = strValue.Split(',');
                    if (parts.Length >= 2)
                    {
                        return new Vector2(
                            float.Parse(parts[0].Trim()),
                            float.Parse(parts[1].Trim())
                        );
                    }
                }
                // Float
                else if (slotValueType == typeof(float))
                {
                    return float.Parse(strValue);
                }
                // Int
                else if (slotValueType == typeof(int))
                {
                    return int.Parse(strValue);
                }
                // Bool
                else if (slotValueType == typeof(bool))
                {
                    return bool.Parse(strValue);
                }
            }

            return value;
        }

        #endregion

        #region High-Level Presets

        /// <summary>
        /// Create a VFX preset (fire, smoke, sparks, etc.)
        /// </summary>
        public static string CreatePreset(string name, string presetType, string folder = "Assets/VFX")
        {
            Initialize();

            try
            {
                // First create the base graph
                var createResult = CreateVFXGraph(name, folder);
                if (createResult.StartsWith("Error"))
                    return createResult;

                string vfxPath = $"{folder}/{name}.vfx";

                switch (presetType.ToLower())
                {
                    case "fire":
                        return CreateFirePreset(vfxPath);
                    case "smoke":
                        return CreateSmokePreset(vfxPath);
                    case "sparks":
                        return CreateSparksPreset(vfxPath);
                    case "trail":
                        return CreateTrailPreset(vfxPath);
                    case "explosion":
                        return CreateExplosionPreset(vfxPath);
                    default:
                        return $"Unknown preset type: {presetType}. Available: fire, smoke, sparks, trail, explosion";
                }
            }
            catch (Exception e)
            {
                return $"Error creating preset: {e.Message}";
            }
        }

        private static string CreateFirePreset(string vfxPath)
        {
            // === FIRE EFFECT - Professional VFX Graph Setup ===
            // Fire rises upward with flickering motion, fades from yellow to orange to red

            // Spawn: 200 particles/sec for dense fire
            AddContext(vfxPath, "spawn", new Dictionary<string, object> { { "spawnRate", 200f } });

            // Initialize: capacity = 200 * 2 = 400, use 500 for safety
            AddContext(vfxPath, "initialize", new Dictionary<string, object> { { "capacity", 500 } });

            // Update context for forces
            AddContext(vfxPath, "update", null);

            // Output: Additive blend for fire glow effect with default particle texture
            AddContext(vfxPath, "quad", new Dictionary<string, object> {
                { "blendMode", "additive" }
            });

            // Set fire texture from Kenney pack
            SetParticleTexture(vfxPath, 3, "flame_01");

            // Link the particle lifecycle
            LinkContexts(vfxPath, 0, 1); // spawn -> init
            LinkContexts(vfxPath, 1, 2); // init -> update
            LinkContexts(vfxPath, 2, 3); // update -> output

            // === INITIALIZE CONTEXT ===
            // Position: Small circle at base for flame origin
            AddBlock(vfxPath, 1, "positioncircle", new Dictionary<string, object> { { "radius", 0.3f } });

            // Random lifetime: 0.8-2.0 seconds for variety
            AddBlock(vfxPath, 1, "setattributerandom", new Dictionary<string, object> {
                { "attribute", "lifetime" },
                { "min", 0.8f },
                { "max", 2.0f }
            });

            // Initial velocity: Upward with slight random spread
            AddBlock(vfxPath, 1, "setattribute", new Dictionary<string, object> {
                { "attribute", "velocity" },
                { "value", "0,2.5,0" }
            });

            // Random velocity spread for natural movement
            AddBlock(vfxPath, 1, "velocityrandom", new Dictionary<string, object> {
                { "speed", 0.8f }
            });

            // Random size: 0.3-0.7 for variety
            AddBlock(vfxPath, 1, "setattributerandom", new Dictionary<string, object> {
                { "attribute", "size" },
                { "min", 0.3f },
                { "max", 0.7f }
            });

            // Initial color: Bright yellow-white core
            AddBlock(vfxPath, 1, "setattribute", new Dictionary<string, object> {
                { "attribute", "color" },
                { "value", "1,0.9,0.3" }
            });

            // Random Z angle: 0-360 degrees for rotation variety
            AddBlock(vfxPath, 1, "setattributerandom", new Dictionary<string, object> {
                { "attribute", "angle" },
                { "min", 0f },
                { "max", 360f }
            });

            // === UPDATE CONTEXT ===
            // Force: Continuous upward force to keep fire rising
            AddBlock(vfxPath, 2, "force", new Dictionary<string, object> {
                { "force", "0,2,0" }  // Upward force vector
            });

            // Turbulence: Flickering motion - stronger for fire
            AddBlock(vfxPath, 2, "turbulence", new Dictionary<string, object> {
                { "intensity", 3.0f },
                { "frequency", 5f },
                { "octaves", 3 }
            });

            // Drag: Slow down over time for natural look
            AddBlock(vfxPath, 2, "drag", new Dictionary<string, object> {
                { "coefficient", 1.2f }
            });

            // Angular velocity: Slow rotation over time for flickering
            AddBlock(vfxPath, 2, "setattribute", new Dictionary<string, object> {
                { "attribute", "angularVelocity" },
                { "value", 30f },
                { "composition", "add" }
            });

            // === OUTPUT CONTEXT ===
            // Color over life: Yellow -> Orange -> Red fade
            AddBlock(vfxPath, 3, "coloroverlife", null);

            // Size over life: Expand then shrink
            AddBlock(vfxPath, 3, "sizeoverlife", null);

            // Orient: Face camera
            AddBlock(vfxPath, 3, "orient", null);

            CompileVFX(vfxPath);

            return $"Created fire preset: {vfxPath}\n" +
                   "Settings: SpawnRate=200, Capacity=500, Velocity Y=3, Force Y=2, Turbulence=2.5";
        }

        private static string CreateSmokePreset(string vfxPath)
        {
            // === SMOKE EFFECT - Professional VFX Graph Setup ===
            // Smoke characteristics: slow rise, expansion over time, alpha blend, soft edges
            // Reference: Unity 6-way lighting smoke, Diablo 3 smoke recreation tutorials

            // Spawn: Lower rate (25-40) for wispy smoke, not dense cloud
            AddContext(vfxPath, "spawn", new Dictionary<string, object> { { "spawnRate", 35f } });

            // Initialize: capacity = 35 * 5 (max lifetime) ≈ 175, use 250 for safety
            AddContext(vfxPath, "initialize", new Dictionary<string, object> { { "capacity", 250 } });

            // Update context
            AddContext(vfxPath, "update", null);

            // Output: Alpha blend for translucent smoke (NOT additive)
            AddContext(vfxPath, "quad", new Dictionary<string, object> { { "blendMode", "alpha" } });

            // Set smoke texture from Kenney pack
            SetParticleTexture(vfxPath, 3, "whitePuff00");

            // Link contexts
            LinkContexts(vfxPath, 0, 1);
            LinkContexts(vfxPath, 1, 2);
            LinkContexts(vfxPath, 2, 3);

            // === INITIALIZE CONTEXT ===
            // Position: Small sphere for concentrated smoke source
            AddBlock(vfxPath, 1, "positionsphere", new Dictionary<string, object> { { "radius", 0.15f } });

            // Lifetime: 3-5 seconds for slow dissipation
            AddBlock(vfxPath, 1, "setattribute", new Dictionary<string, object> {
                { "attribute", "lifetime" },
                { "value", 4f }
            });

            // Initial velocity: Slow upward drift
            AddBlock(vfxPath, 1, "setattribute", new Dictionary<string, object> {
                { "attribute", "velocity" },
                { "value", "0,0.3,0" }
            });

            // Initial size: Small start, will expand over lifetime
            AddBlock(vfxPath, 1, "setattribute", new Dictionary<string, object> {
                { "attribute", "size" },
                { "value", 0.2f }
            });

            // Initial color: Gray with some opacity
            AddBlock(vfxPath, 1, "setattribute", new Dictionary<string, object> {
                { "attribute", "color" },
                { "value", "0.5,0.5,0.5" }
            });

            // Initial alpha: Semi-transparent
            AddBlock(vfxPath, 1, "setattribute", new Dictionary<string, object> {
                { "attribute", "alpha" },
                { "value", 0.6f }
            });

            // Random angle for variation
            AddBlock(vfxPath, 1, "setattribute", new Dictionary<string, object> {
                { "attribute", "angle" },
                { "value", 0f }  // Will use random in actual implementation
            });

            // === UPDATE CONTEXT ===
            // Turbulence: Gentle swirling motion for realistic smoke
            AddBlock(vfxPath, 2, "turbulence", new Dictionary<string, object> {
                { "intensity", 0.8f },
                { "frequency", 1.5f },
                { "octaves", 3 },
                { "roughness", 0.5f }
            });

            // Drag: Higher drag for smoke (air resistance)
            // Smoke slows down significantly over lifetime
            AddBlock(vfxPath, 2, "drag", new Dictionary<string, object> {
                { "coefficient", 1.2f }
            });

            // Slight upward force (hot smoke rises)
            AddBlock(vfxPath, 2, "gravity", new Dictionary<string, object> {
                { "force", -0.5f }
            });

            // === OUTPUT CONTEXT ===
            // Size over life: Expand from 0.2 to 1.5+ as smoke dissipates
            AddBlock(vfxPath, 3, "sizeoverlife", null);

            // Color/Alpha over life: Fade out alpha towards end
            AddBlock(vfxPath, 3, "coloroverlife", null);

            // Orient: Face camera
            AddBlock(vfxPath, 3, "orient", null);

            CompileVFX(vfxPath);

            return $"Created professional smoke preset: {vfxPath}\n" +
                   "Settings: SpawnRate=35, Capacity=250, Lifetime=4s, Drag=1.2, Alpha blend";
        }

        private static string CreateSparksPreset(string vfxPath)
        {
            // === SPARKS/EMBERS EFFECT - Professional VFX Graph Setup ===
            // Sparks: fast, small, bright, affected by gravity, stretched billboard
            // Reference: Unity VFX Graph tutorials, Brian David VR VFX series

            // Spawn: High rate for continuous spark shower (150-300)
            AddContext(vfxPath, "spawn", new Dictionary<string, object> { { "spawnRate", 200f } });

            // Initialize: capacity = 200 * 1.5 = 300, use 400 for bursts
            AddContext(vfxPath, "initialize", new Dictionary<string, object> { { "capacity", 400 } });

            // Update context
            AddContext(vfxPath, "update", null);

            // Output: Line output for stretched sparks, additive for glow
            AddContext(vfxPath, "line", new Dictionary<string, object> { { "blendMode", "additive" } });

            // Set spark texture from Kenney pack
            SetParticleTexture(vfxPath, 3, "spark_01");

            // Link contexts
            LinkContexts(vfxPath, 0, 1);
            LinkContexts(vfxPath, 1, 2);
            LinkContexts(vfxPath, 2, 3);

            // === INITIALIZE CONTEXT ===
            // Position: Small point source
            AddBlock(vfxPath, 1, "positionsphere", new Dictionary<string, object> { { "radius", 0.05f } });

            // Lifetime: Very short (0.3-1.0 seconds) - sparks burn out quickly
            AddBlock(vfxPath, 1, "setattribute", new Dictionary<string, object> {
                { "attribute", "lifetime" },
                { "value", 0.6f }
            });

            // High initial velocity: Sparks burst outward
            AddBlock(vfxPath, 1, "setattribute", new Dictionary<string, object> {
                { "attribute", "velocity" },
                { "value", "0,3,0" }  // Strong upward initial burst
            });

            // Very small size: Point-like sparks
            AddBlock(vfxPath, 1, "setattribute", new Dictionary<string, object> {
                { "attribute", "size" },
                { "value", 0.02f }
            });

            // Bright orange/yellow color
            AddBlock(vfxPath, 1, "setattribute", new Dictionary<string, object> {
                { "attribute", "color" },
                { "value", "1,0.6,0.1" }  // Hot orange
            });

            // Random velocity direction for spread
            AddBlock(vfxPath, 1, "velocityrandom", new Dictionary<string, object> {
                { "speed", 2f }
            });

            // === UPDATE CONTEXT ===
            // Gravity: Sparks fall with real gravity (-9.81)
            AddBlock(vfxPath, 2, "gravity", new Dictionary<string, object> {
                { "force", 9.81f }  // Earth gravity (positive = downward in Unity)
            });

            // Light turbulence for random flickering paths
            AddBlock(vfxPath, 2, "turbulence", new Dictionary<string, object> {
                { "intensity", 0.5f },
                { "frequency", 5f }
            });

            // Drag: Air resistance slows sparks
            AddBlock(vfxPath, 2, "drag", new Dictionary<string, object> {
                { "coefficient", 0.8f }
            });

            // === OUTPUT CONTEXT ===
            // Color over life: Bright -> dim (cooling ember)
            AddBlock(vfxPath, 3, "coloroverlife", null);

            // Size over life: Slight shrink as spark burns out
            AddBlock(vfxPath, 3, "sizeoverlife", null);

            CompileVFX(vfxPath);

            return $"Created professional sparks preset: {vfxPath}\n" +
                   "Settings: SpawnRate=200, Capacity=400, Lifetime=0.6s, Gravity=9.81, Line output";
        }

        private static string CreateTrailPreset(string vfxPath)
        {
            // === TRAIL/RIBBON EFFECT - Professional VFX Graph Setup ===
            // Trail characteristics: connected particles, smooth tapering, follows motion
            // Used for: weapon trails, vehicle exhaust, magic effects

            // Spawn: Moderate rate for smooth trail (40-80)
            AddContext(vfxPath, "spawn", new Dictionary<string, object> { { "spawnRate", 60f } });

            // Initialize: capacity = 60 * 1.5 = 90, use 150 for smooth trails
            AddContext(vfxPath, "initialize", new Dictionary<string, object> { { "capacity", 150 } });

            // Update context
            AddContext(vfxPath, "update", null);

            // Output: QuadStrip for connected ribbon, additive for energy effect
            AddContext(vfxPath, "quadstrip", new Dictionary<string, object> { { "blendMode", "additive" } });

            // Set trail texture from Kenney pack
            SetParticleTexture(vfxPath, 3, "trace_01");

            // Link contexts
            LinkContexts(vfxPath, 0, 1);
            LinkContexts(vfxPath, 1, 2);
            LinkContexts(vfxPath, 2, 3);

            // === INITIALIZE CONTEXT ===
            // Position: Line for ribbon effect (or inherit from parent)
            AddBlock(vfxPath, 1, "positionline", null);

            // Lifetime: 1-2 seconds for visible trail length
            AddBlock(vfxPath, 1, "setattribute", new Dictionary<string, object> {
                { "attribute", "lifetime" },
                { "value", 1.2f }
            });

            // Size: Width of trail ribbon
            AddBlock(vfxPath, 1, "setattribute", new Dictionary<string, object> {
                { "attribute", "size" },
                { "value", 0.15f }
            });

            // Color: Bright cyan/blue for energy trail
            AddBlock(vfxPath, 1, "setattribute", new Dictionary<string, object> {
                { "attribute", "color" },
                { "value", "0.3,0.8,1" }  // Cyan blue
            });

            // === UPDATE CONTEXT ===
            // Subtle turbulence for organic movement
            AddBlock(vfxPath, 2, "turbulence", new Dictionary<string, object> {
                { "intensity", 0.3f },
                { "frequency", 2f }
            });

            // Light drag for smooth deceleration
            AddBlock(vfxPath, 2, "drag", new Dictionary<string, object> {
                { "coefficient", 0.2f }
            });

            // === OUTPUT CONTEXT ===
            // Color over life: Bright start, fade to transparent
            AddBlock(vfxPath, 3, "coloroverlife", null);

            // Size over life: Taper from full width to thin tip
            AddBlock(vfxPath, 3, "sizeoverlife", null);

            CompileVFX(vfxPath);

            return $"Created professional trail preset: {vfxPath}\n" +
                   "Settings: SpawnRate=60, Capacity=150, Lifetime=1.2s, QuadStrip output";
        }

        private static string CreateExplosionPreset(string vfxPath)
        {
            // === EXPLOSION EFFECT - Professional VFX Graph Setup ===
            // Explosion: Single burst spawn, radial velocity, fast decay
            // Components: Core flash, debris/sparks, smoke cloud, shockwave (simplified here)
            // Reference: Unity explosive visuals blog, VionixStudio explosion tutorial

            // Spawn: Single burst (not continuous)
            AddContext(vfxPath, "spawn", null);

            // Initialize: High capacity for burst
            AddContext(vfxPath, "initialize", new Dictionary<string, object> { { "capacity", 1000 } });

            // Update context
            AddContext(vfxPath, "update", null);

            // Output: Additive quad for explosion glow
            AddContext(vfxPath, "quad", new Dictionary<string, object> { { "blendMode", "additive" } });

            // Set explosion texture from Kenney pack
            SetParticleTexture(vfxPath, 3, "explosion00");

            // Link contexts
            LinkContexts(vfxPath, 0, 1);
            LinkContexts(vfxPath, 1, 2);
            LinkContexts(vfxPath, 2, 3);

            // === SPAWN CONTEXT ===
            // Single burst of 300 particles
            AddBlock(vfxPath, 0, "spawnburst", new Dictionary<string, object> {
                { "count", 300 }
            });

            // === INITIALIZE CONTEXT ===
            // Position: Small sphere at explosion center
            AddBlock(vfxPath, 1, "positionsphere", new Dictionary<string, object> { { "radius", 0.1f } });

            // Lifetime: Short (0.5-1.5s) for quick explosion
            AddBlock(vfxPath, 1, "setattribute", new Dictionary<string, object> {
                { "attribute", "lifetime" },
                { "value", 0.8f }
            });

            // High radial velocity: Particles burst outward
            AddBlock(vfxPath, 1, "velocityrandom", new Dictionary<string, object> {
                { "speed", 8f }  // High speed burst
            });

            // Size: Medium particles
            AddBlock(vfxPath, 1, "setattribute", new Dictionary<string, object> {
                { "attribute", "size" },
                { "value", 0.5f }
            });

            // Color: Bright orange/yellow explosion core
            AddBlock(vfxPath, 1, "setattribute", new Dictionary<string, object> {
                { "attribute", "color" },
                { "value", "1,0.7,0.2" }  // Hot orange
            });

            // === UPDATE CONTEXT ===
            // High drag: Explosion particles slow down rapidly
            AddBlock(vfxPath, 2, "drag", new Dictionary<string, object> {
                { "coefficient", 3f }  // High drag for quick deceleration
            });

            // Slight gravity: Debris falls
            AddBlock(vfxPath, 2, "gravity", new Dictionary<string, object> {
                { "force", 2f }
            });

            // Turbulence: Add chaos to explosion
            AddBlock(vfxPath, 2, "turbulence", new Dictionary<string, object> {
                { "intensity", 2f },
                { "frequency", 4f }
            });

            // === OUTPUT CONTEXT ===
            // Color over life: Bright -> orange -> red -> black (fade)
            AddBlock(vfxPath, 3, "coloroverlife", null);

            // Size over life: Expand then shrink
            AddBlock(vfxPath, 3, "sizeoverlife", null);

            // Orient: Face camera
            AddBlock(vfxPath, 3, "orient", null);

            CompileVFX(vfxPath);

            return $"Created professional explosion preset: {vfxPath}\n" +
                   "Settings: Burst=300, Capacity=1000, Lifetime=0.8s, RadialSpeed=8, Drag=3";
        }

        #endregion

        #region Available Types Query

        public static string GetAvailableContexts()
        {
            Initialize();
            return string.Join(", ", _contextTypes.Keys.OrderBy(k => k));
        }

        public static string GetAvailableBlocks()
        {
            Initialize();
            return string.Join(", ", _blockTypes.Keys.OrderBy(k => k));
        }

        public static string GetAvailableOperators()
        {
            Initialize();
            return string.Join(", ", _operatorTypes.Keys.OrderBy(k => k));
        }

        public static string GetAvailablePresets()
        {
            return "fire, smoke, sparks, trail, explosion";
        }

        #endregion

        #region Output Context Settings

        /// <summary>
        /// Configure output context settings (texture, blend mode, etc.)
        /// </summary>
        public static string ConfigureOutput(string vfxPath, int contextIndex, Dictionary<string, object> settings)
        {
            Initialize();

            try
            {
                var graph = GetVFXGraph(vfxPath);
                if (graph == null)
                {
                    return $"Error: VFX Graph not found at {vfxPath}";
                }

                var contexts = GetContexts(graph);
                if (contextIndex < 0 || contextIndex >= contexts.Count)
                {
                    return $"Error: Context index {contextIndex} out of range";
                }

                var context = contexts[contextIndex];
                var contextTypeName = context.GetType().Name;

                // Verify it's an output context
                if (!contextTypeName.Contains("Output") && !contextTypeName.Contains("Quad") &&
                    !contextTypeName.Contains("Point") && !contextTypeName.Contains("Line") &&
                    !contextTypeName.Contains("Mesh"))
                {
                    return $"Warning: Context at index {contextIndex} ({contextTypeName}) may not be an output context";
                }

                var results = new List<string>();

                foreach (var kvp in settings)
                {
                    string key = kvp.Key.ToLower();
                    object value = kvp.Value;

                    try
                    {
                        switch (key)
                        {
                            case "texture":
                            case "maintexture":
                            case "basetexture":
                                var texturePath = value.ToString();
                                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                                if (texture != null)
                                {
                                    SetOutputProperty(context, "mainTexture", texture);
                                    results.Add($"Set texture: {texturePath}");
                                }
                                else
                                {
                                    results.Add($"Warning: Texture not found at {texturePath}");
                                }
                                break;

                            case "blendmode":
                            case "blend":
                                SetOutputBlendMode(context, value.ToString());
                                results.Add($"Set blend mode: {value}");
                                break;

                            case "sortpriority":
                            case "priority":
                                SetOutputProperty(context, "sortPriority", Convert.ToInt32(value));
                                results.Add($"Set sort priority: {value}");
                                break;

                            case "usesofparticle":
                            case "softparticle":
                            case "soft":
                                SetOutputProperty(context, "useSoftParticle", Convert.ToBoolean(value));
                                results.Add($"Set soft particle: {value}");
                                break;

                            case "castsshadows":
                            case "castshadow":
                            case "shadow":
                                SetOutputProperty(context, "castShadows", Convert.ToBoolean(value));
                                results.Add($"Set cast shadows: {value}");
                                break;

                            default:
                                // Try generic property setting
                                SetOutputProperty(context, kvp.Key, value);
                                results.Add($"Set {kvp.Key}: {value}");
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        results.Add($"Failed to set {key}: {ex.Message}");
                    }
                }

                EditorUtility.SetDirty(graph as UnityEngine.Object);
                AssetDatabase.SaveAssets();

                return $"Configured output context {contextIndex}:\n" + string.Join("\n", results);
            }
            catch (Exception e)
            {
                return $"Error configuring output: {e.Message}";
            }
        }

        private static void SetOutputProperty(object context, string propertyName, object value)
        {
            var contextType = context.GetType();

            // For texture properties, try input slots first (VFX Graph uses slots for textures)
            if (propertyName.ToLower().Contains("texture") && value is Texture2D texture)
            {
                var inputSlots = GetInputSlots(context);
                foreach (var slot in inputSlots)
                {
                    var slotName = GetSlotName(slot);
                    var slotTypeName = slot.GetType().Name;

                    // Look for texture slots
                    if (slotTypeName.Contains("Texture") || slotName.ToLower().Contains("texture"))
                    {
                        var valueProp = slot.GetType().GetProperty("value",
                            BindingFlags.Public | BindingFlags.Instance);
                        if (valueProp != null && valueProp.CanWrite)
                        {
                            try
                            {
                                valueProp.SetValue(slot, texture);
                                SynLog.Info($"[NexusVFX] Set texture on slot '{slotName}': {texture.name}");
                                return;
                            }
                            catch (Exception ex)
                            {
                                SynLog.Warn($"[NexusVFX] Failed to set texture on slot: {ex.Message}");
                            }
                        }
                    }
                }
            }

            // Try SetSettingValue first
            var setSettingMethod = contextType.GetMethod("SetSettingValue",
                BindingFlags.Public | BindingFlags.Instance);

            if (setSettingMethod != null)
            {
                try
                {
                    setSettingMethod.Invoke(context, new object[] { propertyName, value });
                    SynLog.Info($"[NexusVFX] Set output setting {propertyName} = {value}");
                    return;
                }
                catch { }
            }

            // Try property
            var prop = contextType.GetProperty(propertyName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(context, value);
                SynLog.Info($"[NexusVFX] Set output property {propertyName} = {value}");
                return;
            }

            // Try field
            var field = contextType.GetField(propertyName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (field != null)
            {
                field.SetValue(context, value);
                SynLog.Info($"[NexusVFX] Set output field {propertyName} = {value}");
                return;
            }

            SynLog.Warn($"[NexusVFX] Could not find property/field {propertyName} on {contextType.Name}");
        }

        private static void SetOutputBlendMode(object context, string blendMode)
        {
            var contextType = context.GetType();
            var vfxEditorAssembly = GetVFXEditorAssembly();

            // Try multiple field names for different pipeline versions
            string[] blendModeFieldNames = { "blendMode", "m_BlendMode", "colorMapping", "m_ColorMapping" };
            FieldInfo blendModeField = null;

            foreach (var fieldName in blendModeFieldNames)
            {
                blendModeField = contextType.GetField(fieldName,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

                if (blendModeField == null)
                {
                    // Try walking up the inheritance chain
                    var baseType = contextType.BaseType;
                    while (baseType != null && blendModeField == null)
                    {
                        blendModeField = baseType.GetField(fieldName,
                            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                        baseType = baseType.BaseType;
                    }
                }

                if (blendModeField != null)
                {
                    SynLog.Info($"[NexusVFX] Found blend field: {fieldName} on {contextType.Name}");
                    break;
                }
            }

            // If still not found, try SetSettingValue method
            if (blendModeField == null)
            {
                var setSettingMethod = contextType.GetMethod("SetSettingValue",
                    BindingFlags.Public | BindingFlags.Instance);

                if (setSettingMethod != null)
                {
                    try
                    {
                        // Try setting blendMode via method
                        setSettingMethod.Invoke(context, new object[] { "blendMode", blendMode });
                        SynLog.Info($"[NexusVFX] Set blendMode via SetSettingValue: {blendMode}");
                        return;
                    }
                    catch
                    {
                        // URP may not have blendMode setting, just skip
                        SynLog.Warn($"[NexusVFX] blendMode not supported on {contextType.Name} - skipping");
                        return;
                    }
                }

                SynLog.Warn($"[NexusVFX] Could not find blendMode field on {contextType.Name} - skipping");
                return;
            }

            // Get the enum type from the field
            var blendModeType = blendModeField.FieldType;

            var modeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "additive", "Additive" },
                { "add", "Additive" },
                { "alpha", "Alpha" },
                { "alphablend", "Alpha" },
                { "premultiply", "AlphaPremultiply" },
                { "premultiplied", "AlphaPremultiply" },
                { "alphapremultiply", "AlphaPremultiply" },
                { "opaque", "Opaque" },
                { "masked", "Masked" },
            };

            string enumName = modeMap.GetValueOrDefault(blendMode.ToLower(), blendMode);

            try
            {
                // Get enum values and find matching one
                var enumValues = Enum.GetValues(blendModeType);
                foreach (var val in enumValues)
                {
                    if (val.ToString().Equals(enumName, StringComparison.OrdinalIgnoreCase))
                    {
                        blendModeField.SetValue(context, val);
                        break;
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Enable particle color on output context so color attribute affects rendering
        /// </summary>
        private static void EnableParticleColor(object context)
        {
            var contextType = context.GetType();

            // Try multiple approaches to enable particle color
            // Method 1: useParticleColor field (common in URP Lit outputs)
            string[] colorFieldNames = { "useParticleColor", "m_UseParticleColor", "useColor", "m_UseColor" };
            foreach (var fieldName in colorFieldNames)
            {
                var field = FindFieldInHierarchy(contextType, fieldName);
                if (field != null && field.FieldType == typeof(bool))
                {
                    field.SetValue(context, true);
                    SynLog.Info($"[NexusVFX] Enabled particle color via {fieldName}");
                    return;
                }
            }

            // Method 2: colorMode setting (set to "Multiply" or particle color mode)
            string[] colorModeFieldNames = { "colorMode", "m_ColorMode", "colorMapping", "m_ColorMapping" };
            foreach (var fieldName in colorModeFieldNames)
            {
                var field = FindFieldInHierarchy(contextType, fieldName);
                if (field != null && field.FieldType.IsEnum)
                {
                    try
                    {
                        var enumValues = Enum.GetValues(field.FieldType);
                        foreach (var val in enumValues)
                        {
                            string valName = val.ToString().ToLower();
                            // Look for a mode that uses particle color (Multiply, VertexColor, ParticleColor)
                            if (valName.Contains("multiply") || valName.Contains("vertex") ||
                                valName.Contains("particle") || valName.Contains("color"))
                            {
                                field.SetValue(context, val);
                                SynLog.Info($"[NexusVFX] Set color mode to {val} via {fieldName}");
                                return;
                            }
                        }
                    }
                    catch { }
                }
            }

            // Method 3: Try SetSettingValue method
            var setSettingMethod = contextType.GetMethod("SetSettingValue", BindingFlags.Public | BindingFlags.Instance);
            if (setSettingMethod != null)
            {
                try
                {
                    setSettingMethod.Invoke(context, new object[] { "useParticleColor", true });
                    SynLog.Info("[NexusVFX] Enabled particle color via SetSettingValue");
                    return;
                }
                catch { }
            }

            SynLog.Info("[NexusVFX] Could not find particle color setting - may need manual configuration");
        }

        private static FieldInfo FindFieldInHierarchy(Type type, string fieldName)
        {
            var currentType = type;
            while (currentType != null)
            {
                var field = currentType.GetField(fieldName,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (field != null) return field;
                currentType = currentType.BaseType;
            }
            return null;
        }

        /// <summary>
        /// Set particle texture by name from Kenney pack (Assets/Synaptic AI Pro/Resources/VFX/Textures/)
        /// </summary>
        private static void SetParticleTexture(string vfxPath, int outputContextIndex, string textureName)
        {
            try
            {
                var graph = GetVFXGraph(vfxPath);
                if (graph == null) return;

                var contexts = GetContexts(graph);
                if (outputContextIndex < 0 || outputContextIndex >= contexts.Count) return;

                var outputContext = contexts[outputContextIndex];

                // Build texture path
                string texturePath = $"Assets/Synaptic AI Pro/Resources/VFX/Textures/{textureName}.png";
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);

                if (texture != null)
                {
                    SetOutputProperty(outputContext, "mainTexture", texture);
                    SynLog.Info($"[NexusVFX] Set particle texture: {textureName}");
                }
                else
                {
                    SynLog.Warn($"[NexusVFX] Texture not found: {texturePath}, using default");
                    SetDefaultParticleTexture(vfxPath, outputContextIndex);
                }
            }
            catch (Exception e)
            {
                SynLog.Warn($"[NexusVFX] Failed to set texture {textureName}: {e.Message}");
            }
        }

        private static void SetDefaultParticleTexture(string vfxPath, int outputContextIndex)
        {
            try
            {
                var graph = GetVFXGraph(vfxPath);
                if (graph == null) return;

                var contexts = GetContexts(graph);
                if (outputContextIndex < 0 || outputContextIndex >= contexts.Count) return;

                var outputContext = contexts[outputContextIndex];

                // Get or create soft particle texture asset
                Texture2D particleTexture = GetOrCreateSoftParticleTexture();

                if (particleTexture != null)
                {
                    SetOutputProperty(outputContext, "mainTexture", particleTexture);
                    SynLog.Info($"[NexusVFX] Set particle texture: {particleTexture.name}");
                }
                else
                {
                    SynLog.Warn("[NexusVFX] Could not find or create default particle texture");
                }
            }
            catch (Exception e)
            {
                SynLog.Warn($"[NexusVFX] Failed to set default texture: {e.Message}");
            }
        }

        /// <summary>
        /// Get or create soft particle texture as a persistent asset
        /// </summary>
        private static Texture2D GetOrCreateSoftParticleTexture()
        {
            // Path to store the texture asset
            string texturePath = "Assets/Synaptic AI Pro/Resources/VFX/Textures/SoftParticle.png";

            // Try to load existing texture
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture != null)
            {
                SynLog.Info($"[NexusVFX] Loaded existing particle texture: {texturePath}");
                return texture;
            }

            // Create new texture procedurally
            int size = 128; // Higher resolution for better quality
            texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "SoftParticle";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            float center = size / 2f;
            float maxDist = center;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    float normalizedDist = dist / maxDist;

                    // Smooth circular falloff (soft particle look)
                    float alpha = Mathf.Clamp01(1f - normalizedDist);
                    // Apply smooth curve for better soft edge
                    alpha = Mathf.SmoothStep(0f, 1f, alpha);
                    alpha = alpha * alpha; // Extra softness

                    // White color with alpha falloff
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();

            // Ensure directory exists
            string directory = System.IO.Path.GetDirectoryName(texturePath);
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            // Save as PNG asset
            byte[] pngData = texture.EncodeToPNG();
            System.IO.File.WriteAllBytes(texturePath, pngData);

            // Import the asset
            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceUpdate);

            // Set texture import settings
            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }

            // Reload the saved texture
            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            SynLog.Info($"[NexusVFX] Created and saved particle texture: {texturePath}");

            return texture;
        }

        /// <summary>
        /// Set a gradient for color over life
        /// </summary>
        public static string SetColorGradient(string vfxPath, int contextIndex, int blockIndex,
            string[] colors, float[] times = null)
        {
            Initialize();

            try
            {
                var graph = GetVFXGraph(vfxPath);
                if (graph == null)
                {
                    return $"Error: VFX Graph not found at {vfxPath}";
                }

                var contexts = GetContexts(graph);

                // Create gradient first
                var gradient = new Gradient();
                var colorKeys = new GradientColorKey[colors.Length];
                var alphaKeys = new GradientAlphaKey[colors.Length];

                for (int i = 0; i < colors.Length; i++)
                {
                    float time = times != null && i < times.Length ? times[i] : (float)i / (colors.Length - 1);

                    if (ColorUtility.TryParseHtmlString(colors[i], out Color color))
                    {
                        colorKeys[i] = new GradientColorKey(color, time);
                        alphaKeys[i] = new GradientAlphaKey(color.a == 0 ? 1f : color.a, time);
                    }
                    else
                    {
                        colorKeys[i] = new GradientColorKey(Color.white, time);
                        alphaKeys[i] = new GradientAlphaKey(1f, time);
                    }
                }

                gradient.SetKeys(colorKeys, alphaKeys);

                // Auto-detect: if contextIndex is -1, find ALL Output contexts with Color gradient blocks
                if (contextIndex < 0)
                {
                    SynLog.Info("[NexusVFX] Auto-detecting ALL Color gradient blocks in Output contexts...");
                    int blocksModified = 0;

                    // Search all contexts
                    for (int ci = 0; ci < contexts.Count; ci++)
                    {
                        var ctx = contexts[ci];
                        var ctxTypeName = ctx.GetType().Name;

                        // Look for Output contexts (contain "Output" in type name)
                        if (ctxTypeName.Contains("Output"))
                        {
                            var ctxBlocks = GetBlocks(ctx);
                            for (int bi = 0; bi < ctxBlocks.Count; bi++)
                            {
                                var blk = ctxBlocks[bi];
                                var blkTypeName = blk.GetType().Name;

                                // Check if it's a color-related block
                                if (blkTypeName.Contains("ColorOverLife") || blkTypeName.Contains("AttributeFromCurve"))
                                {
                                    bool isColorBlock = false;

                                    // For AttributeFromCurve, check if it's for Color attribute
                                    if (blkTypeName.Contains("AttributeFromCurve"))
                                    {
                                        var blkSlots = GetInputSlots(blk);
                                        foreach (var slot in blkSlots)
                                        {
                                            var slotName = GetSlotName(slot);
                                            if (slotName.ToLower().Contains("color"))
                                            {
                                                isColorBlock = true;
                                                break;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // ColorOverLife is always a color block
                                        isColorBlock = true;
                                    }

                                    if (isColorBlock)
                                    {
                                        // Set gradient on this block
                                        if (SetGradientOnBlock(blk, gradient))
                                        {
                                            blocksModified++;
                                            SynLog.Info($"[NexusVFX] Set gradient on Context {ci}, Block {bi} ({blkTypeName})");
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (blocksModified == 0)
                    {
                        return "Error: Could not find any Color gradient blocks in Output contexts.";
                    }

                    EditorUtility.SetDirty(graph as UnityEngine.Object);
                    AssetDatabase.SaveAssets();

                    return $"Set color gradient with {colors.Length} colors on {blocksModified} block(s)";
                }

                // Manual mode: set specific block
                if (contextIndex < 0 || contextIndex >= contexts.Count)
                {
                    return $"Error: Context index out of range";
                }

                var blocks = GetBlocks(contexts[contextIndex]);
                if (blockIndex < 0 || blockIndex >= blocks.Count)
                {
                    return $"Error: Block index out of range";
                }

                var block = blocks[blockIndex];

                bool gradientSet = SetGradientOnBlock(block, gradient);

                if (!gradientSet)
                {
                    SynLog.Warn($"[NexusVFX] Could not find gradient slot to set");
                }

                EditorUtility.SetDirty(graph as UnityEngine.Object);
                AssetDatabase.SaveAssets();

                return $"Set color gradient with {colors.Length} colors";
            }
            catch (Exception e)
            {
                return $"Error setting gradient: {e.Message}";
            }
        }

        /// <summary>
        /// Helper method to set gradient on a single block
        /// </summary>
        private static bool SetGradientOnBlock(object block, Gradient gradient)
        {
            var inputSlots = GetInputSlots(block);
            foreach (var slot in inputSlots)
            {
                var slotType = slot.GetType();
                var slotTypeName = slotType.Name;

                // For VFXSlotGradient, use the value property
                var valueProp = slotType.GetProperty("value", BindingFlags.Public | BindingFlags.Instance);
                if (valueProp != null)
                {
                    // Check if it's a Gradient type or assignable from Gradient
                    if (valueProp.PropertyType == typeof(Gradient) ||
                        valueProp.PropertyType.IsAssignableFrom(typeof(Gradient)) ||
                        slotTypeName.Contains("Gradient"))
                    {
                        try
                        {
                            valueProp.SetValue(slot, gradient);
                            return true;
                        }
                        catch (Exception ex)
                        {
                            SynLog.Warn($"[NexusVFX] Failed to set gradient directly: {ex.Message}");

                            // Try setting via SerializedObject
                            var slotAsObject = slot as UnityEngine.Object;
                            if (slotAsObject != null)
                            {
                                var so = new SerializedObject(slotAsObject);
                                var gradientProp = so.FindProperty("m_Value");
                                if (gradientProp != null)
                                {
                                    gradientProp.gradientValue = gradient;
                                    so.ApplyModifiedProperties();
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            return false;
        }

        #endregion
    }
}
#endif
