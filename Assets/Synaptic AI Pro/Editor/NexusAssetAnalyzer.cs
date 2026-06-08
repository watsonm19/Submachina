using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using System.Text;
using Newtonsoft.Json;

namespace SynapticPro
{
    /// <summary>
    /// Detailed asset information analysis class
    /// Retrieves detailed information for textures, meshes, audio, animations, etc.
    /// </summary>
    public static class NexusAssetAnalyzer
    {
        /// <summary>
        /// Get detailed information for texture assets
        /// </summary>
        public static string GetTextureDetails(Dictionary<string, string> parameters)
        {
            try
            {
                var textureName = parameters.GetValueOrDefault("textureName", "");
                var includeAll = parameters.GetValueOrDefault("includeAll", "false") == "true";
                var includeMipmaps = parameters.GetValueOrDefault("includeMipmaps", "true") == "true";
                var includeCompressionInfo = parameters.GetValueOrDefault("includeCompressionInfo", "true") == "true";
                var includeMemoryUsage = parameters.GetValueOrDefault("includeMemoryUsage", "true") == "true";
                
                var textures = string.IsNullOrEmpty(textureName) && includeAll ? 
                    Resources.FindObjectsOfTypeAll<Texture2D>() :
                    Resources.FindObjectsOfTypeAll<Texture2D>().Where(t => 
                        string.IsNullOrEmpty(textureName) || t.name.Contains(textureName)).ToArray();
                
                var textureDetails = new Dictionary<string, object>
                {
                    ["total_count"] = textures.Length,
                    ["search_criteria"] = new Dictionary<string, object>
                    {
                        ["texture_name"] = textureName,
                        ["include_all"] = includeAll,
                        ["include_mipmaps"] = includeMipmaps,
                        ["include_compression_info"] = includeCompressionInfo,
                        ["include_memory_usage"] = includeMemoryUsage
                    },
                    ["textures"] = textures.Take(20).Select(texture => {
                        if (texture == null) return null;
                        
                        var textureData = new Dictionary<string, object>
                        {
                            ["name"] = texture.name,
                            ["size"] = new Dictionary<string, int>
                            {
                                ["width"] = texture.width,
                                ["height"] = texture.height
                            },
                            ["format"] = texture.format.ToString(),
                            ["filter_mode"] = texture.filterMode.ToString(),
                            ["wrap_mode"] = texture.wrapMode.ToString(),
                            ["is_readable"] = texture.isReadable
                        };
                        
                        if (includeMipmaps)
                        {
                            textureData["mipmap_info"] = new Dictionary<string, object>
                            {
                                ["mipmap_count"] = texture.mipmapCount,
                                ["has_mipmaps"] = texture.mipmapCount > 1
                            };
                        }
                        
                        if (includeMemoryUsage)
                        {
                            var memorySize = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture);
                            textureData["memory_usage"] = new Dictionary<string, object>
                            {
                                ["bytes"] = memorySize,
                                ["formatted"] = FormatBytes(memorySize)
                            };
                        }
                        
                        if (includeCompressionInfo)
                        {
                            var assetPath = AssetDatabase.GetAssetPath(texture);
                            if (!string.IsNullOrEmpty(assetPath))
                            {
                                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                                if (importer != null)
                                {
                                    textureData["import_settings"] = new Dictionary<string, object>
                                    {
                                        ["asset_path"] = assetPath,
                                        ["texture_type"] = importer.textureType.ToString(),
                                        ["max_texture_size"] = importer.maxTextureSize,
                                        ["compression"] = importer.textureCompression.ToString(),
                                        ["srgb"] = importer.sRGBTexture
                                    };
                                }
                            }
                        }
                        
                        return textureData;
                    }).Where(t => t != null).ToList()
                };
                
                return JsonConvert.SerializeObject(textureDetails, Formatting.Indented);
            }
            catch (Exception e)
            {
                return $"Error analyzing textures: {e.Message}";
            }
        }
        
        /// <summary>
        /// Get detailed information for mesh assets
        /// </summary>
        public static string GetMeshDetails(Dictionary<string, string> parameters)
        {
            try
            {
                var meshName = parameters.GetValueOrDefault("meshName", "");
                var includeAll = parameters.GetValueOrDefault("includeAll", "false") == "true";
                var includeVertexData = parameters.GetValueOrDefault("includeVertexData", "true") == "true";
                var includeSubMeshes = parameters.GetValueOrDefault("includeSubMeshes", "true") == "true";
                var includeBoneWeights = parameters.GetValueOrDefault("includeBoneWeights", "true") == "true";
                var includeBlendShapes = parameters.GetValueOrDefault("includeBlendShapes", "false") == "true";
                
                var meshes = string.IsNullOrEmpty(meshName) && includeAll ?
                    Resources.FindObjectsOfTypeAll<Mesh>() :
                    Resources.FindObjectsOfTypeAll<Mesh>().Where(m =>
                        string.IsNullOrEmpty(meshName) || m.name.Contains(meshName)).ToArray();
                
                var report = new StringBuilder();
                report.AppendLine("=== Mesh Details ===");
                report.AppendLine($"Found {meshes.Length} mesh(es)");
                report.AppendLine();
                
                foreach (var mesh in meshes.Take(20))
                {
                    if (mesh == null) continue;
                    
                    report.AppendLine($"Mesh: {mesh.name}");
                    
                    if (includeVertexData)
                    {
                        report.AppendLine($"  Vertex Count: {mesh.vertexCount:N0}");
                        report.AppendLine($"  Triangle Count: {mesh.triangles.Length / 3:N0}");
                        report.AppendLine($"  UV Channels: {GetUVChannelCount(mesh)}");
                        report.AppendLine($"  Has Normals: {mesh.normals.Length > 0}");
                        report.AppendLine($"  Has Tangents: {mesh.tangents.Length > 0}");
                        report.AppendLine($"  Has Colors: {mesh.colors.Length > 0}");
                    }
                    
                    if (includeSubMeshes)
                    {
                        report.AppendLine($"  SubMesh Count: {mesh.subMeshCount}");
                        for (int i = 0; i < mesh.subMeshCount; i++)
                        {
                            var subMesh = mesh.GetSubMesh(i);
                            report.AppendLine($"    SubMesh {i}: {subMesh.indexCount / 3:N0} triangles");
                        }
                    }
                    
                    if (includeBoneWeights)
                    {
                        var boneWeights = mesh.boneWeights;
                        report.AppendLine($"  Bone Weights: {boneWeights.Length}");
                        report.AppendLine($"  Bind Poses: {mesh.bindposes.Length}");
                    }
                    
                    if (includeBlendShapes)
                    {
                        report.AppendLine($"  Blend Shapes: {mesh.blendShapeCount}");
                        for (int i = 0; i < mesh.blendShapeCount; i++)
                        {
                            report.AppendLine($"    {mesh.GetBlendShapeName(i)}: {mesh.GetBlendShapeFrameCount(i)} frames");
                        }
                    }
                    
                    var memorySize = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(mesh);
                    report.AppendLine($"  Memory Usage: {FormatBytes(memorySize)}");
                    report.AppendLine($"  Readable: {mesh.isReadable}");
                    
                    report.AppendLine();
                }
                
                return report.ToString();
            }
            catch (Exception e)
            {
                return $"Error analyzing meshes: {e.Message}";
            }
        }
        
        /// <summary>
        /// Get detailed information for audio assets
        /// </summary>
        public static string GetAudioDetails(Dictionary<string, string> parameters)
        {
            try
            {
                var audioName = parameters.GetValueOrDefault("audioName", "");
                var includeAll = parameters.GetValueOrDefault("includeAll", "false") == "true";
                var includeCompressionInfo = parameters.GetValueOrDefault("includeCompressionInfo", "true") == "true";
                var includeMetadata = parameters.GetValueOrDefault("includeMetadata", "true") == "true";
                
                var audioClips = string.IsNullOrEmpty(audioName) && includeAll ?
                    Resources.FindObjectsOfTypeAll<AudioClip>() :
                    Resources.FindObjectsOfTypeAll<AudioClip>().Where(a =>
                        string.IsNullOrEmpty(audioName) || a.name.Contains(audioName)).ToArray();
                
                var report = new StringBuilder();
                report.AppendLine("=== Audio Clip Details ===");
                report.AppendLine($"Found {audioClips.Length} audio clip(s)");
                report.AppendLine();
                
                foreach (var clip in audioClips.Take(20))
                {
                    if (clip == null) continue;
                    
                    report.AppendLine($"Audio Clip: {clip.name}");
                    report.AppendLine($"  Length: {clip.length:F2} seconds");
                    report.AppendLine($"  Channels: {clip.channels}");
                    report.AppendLine($"  Frequency: {clip.frequency} Hz");
                    report.AppendLine($"  Samples: {clip.samples:N0}");
                    report.AppendLine($"  Load Type: {clip.loadType}");
                    report.AppendLine($"  3D: {!clip.ambisonic}");
                    
                    if (includeCompressionInfo)
                    {
                        var assetPath = AssetDatabase.GetAssetPath(clip);
                        if (!string.IsNullOrEmpty(assetPath))
                        {
                            var importer = AssetImporter.GetAtPath(assetPath) as AudioImporter;
                            if (importer != null)
                            {
                                report.AppendLine($"  Force Mono: {importer.forceToMono}");
                                var defaultSettings = importer.defaultSampleSettings;
                                report.AppendLine($"  Preload Audio Data: {defaultSettings.loadType == AudioClipLoadType.CompressedInMemory}");
                                
                                var settings = importer.defaultSampleSettings;
                                report.AppendLine($"  Compression Format: {settings.compressionFormat}");
                                report.AppendLine($"  Quality: {settings.quality}");
                                report.AppendLine($"  Sample Rate: {settings.sampleRateSetting}");
                            }
                        }
                    }
                    
                    var memorySize = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(clip);
                    report.AppendLine($"  Memory Usage: {FormatBytes(memorySize)}");
                    
                    if (includeMetadata)
                    {
                        var fileInfo = GetAssetFileInfo(AssetDatabase.GetAssetPath(clip));
                        if (fileInfo != null)
                        {
                            report.AppendLine($"  File Size: {FormatBytes(fileInfo.Length)}");
                        }
                    }
                    
                    report.AppendLine();
                }
                
                return report.ToString();
            }
            catch (Exception e)
            {
                return $"Error analyzing audio clips: {e.Message}";
            }
        }
        
        /// <summary>
        /// Get detailed information for animation assets
        /// </summary>
        public static string GetAnimationDetails(Dictionary<string, string> parameters)
        {
            try
            {
                var animationName = parameters.GetValueOrDefault("animationName", "");
                var includeAll = parameters.GetValueOrDefault("includeAll", "false") == "true";
                var includeKeyframes = parameters.GetValueOrDefault("includeKeyframes", "true") == "true";
                var includeEvents = parameters.GetValueOrDefault("includeEvents", "true") == "true";
                
                var animationClips = string.IsNullOrEmpty(animationName) && includeAll ?
                    Resources.FindObjectsOfTypeAll<AnimationClip>() :
                    Resources.FindObjectsOfTypeAll<AnimationClip>().Where(a =>
                        string.IsNullOrEmpty(animationName) || a.name.Contains(animationName)).ToArray();
                
                var report = new StringBuilder();
                report.AppendLine("=== Animation Clip Details ===");
                report.AppendLine($"Found {animationClips.Length} animation clip(s)");
                report.AppendLine();
                
                foreach (var clip in animationClips.Take(20))
                {
                    if (clip == null) continue;
                    
                    report.AppendLine($"Animation Clip: {clip.name}");
                    report.AppendLine($"  Length: {clip.length:F3} seconds");
                    report.AppendLine($"  Frame Rate: {clip.frameRate:F1} fps");
                    report.AppendLine($"  Legacy: {clip.legacy}");
                    report.AppendLine($"  Loop: {clip.isLooping}");
                    report.AppendLine($"  Humanoid: {clip.isHumanMotion}");
                    
                    if (includeEvents)
                    {
                        var events = AnimationUtility.GetAnimationEvents(clip);
                        report.AppendLine($"  Animation Events: {events.Length}");
                        foreach (var evt in events.Take(5))
                        {
                            report.AppendLine($"    {evt.time:F2}s: {evt.functionName}({evt.stringParameter})");
                        }
                    }
                    
                    if (includeKeyframes)
                    {
                        var bindings = AnimationUtility.GetCurveBindings(clip);
                        report.AppendLine($"  Curve Bindings: {bindings.Length}");
                        
                        int totalKeyframes = 0;
                        foreach (var binding in bindings.Take(10))
                        {
                            var curve = AnimationUtility.GetEditorCurve(clip, binding);
                            if (curve != null)
                            {
                                totalKeyframes += curve.keys.Length;
                                report.AppendLine($"    {binding.propertyName}: {curve.keys.Length} keys");
                            }
                        }
                        report.AppendLine($"  Total Keyframes: {totalKeyframes:N0}");
                    }
                    
                    var memorySize = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(clip);
                    report.AppendLine($"  Memory Usage: {FormatBytes(memorySize)}");
                    
                    report.AppendLine();
                }
                
                return report.ToString();
            }
            catch (Exception e)
            {
                return $"Error analyzing animation clips: {e.Message}";
            }
        }
        
        /// <summary>
        /// Get detailed information for material assets
        /// </summary>
        public static string GetMaterialDetails(Dictionary<string, string> parameters)
        {
            try
            {
                var materialName = parameters.GetValueOrDefault("materialName", "");
                var includeAll = parameters.GetValueOrDefault("includeAll", "false") == "true";
                var includeShaderInfo = parameters.GetValueOrDefault("includeShaderInfo", "true") == "true";
                var includeTextureReferences = parameters.GetValueOrDefault("includeTextureReferences", "true") == "true";
                var includePropertyValues = parameters.GetValueOrDefault("includePropertyValues", "true") == "true";
                
                var materials = string.IsNullOrEmpty(materialName) && includeAll ?
                    Resources.FindObjectsOfTypeAll<Material>() :
                    Resources.FindObjectsOfTypeAll<Material>().Where(m =>
                        string.IsNullOrEmpty(materialName) || m.name.Contains(materialName)).ToArray();
                
                var report = new StringBuilder();
                report.AppendLine("=== Material Details ===");
                report.AppendLine($"Found {materials.Length} material(s)");
                report.AppendLine();
                
                foreach (var material in materials.Take(20))
                {
                    if (material == null || material.shader == null) continue;
                    
                    report.AppendLine($"Material: {material.name}");
                    
                    if (includeShaderInfo)
                    {
                        report.AppendLine($"  Shader: {material.shader.name}");
                        report.AppendLine($"  Render Queue: {material.renderQueue}");
                        report.AppendLine($"  Keywords: {string.Join(", ", material.shaderKeywords)}");
                    }
                    
                    if (includeTextureReferences)
                    {
                        var texturePropertyNames = material.GetTexturePropertyNames();
                        report.AppendLine($"  Textures ({texturePropertyNames.Length}):");
                        foreach (var propName in texturePropertyNames)
                        {
                            var texture = material.GetTexture(propName);
                            if (texture != null)
                            {
                                report.AppendLine($"    {propName}: {texture.name} ({texture.width}x{texture.height})");
                            }
                        }
                    }
                    
                    if (includePropertyValues)
                    {
                        // Color properties
                        var colorProps = GetShaderColorProperties(material.shader);
                        foreach (var prop in colorProps.Take(5))
                        {
                            var color = material.GetColor(prop);
                            report.AppendLine($"    {prop}: RGBA({color.r:F2}, {color.g:F2}, {color.b:F2}, {color.a:F2})");
                        }
                        
                        // Float properties
                        var floatProps = GetShaderFloatProperties(material.shader);
                        foreach (var prop in floatProps.Take(5))
                        {
                            var value = material.GetFloat(prop);
                            report.AppendLine($"    {prop}: {value:F3}");
                        }
                    }
                    
                    var memorySize = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(material);
                    report.AppendLine($"  Memory Usage: {FormatBytes(memorySize)}");
                    
                    report.AppendLine();
                }
                
                return report.ToString();
            }
            catch (Exception e)
            {
                return $"Error analyzing materials: {e.Message}";
            }
        }
        
        /// <summary>
        /// Get asset file information
        /// </summary>
        public static string GetAssetFileInfo(Dictionary<string, string> parameters)
        {
            try
            {
                var assetPath = parameters.GetValueOrDefault("assetPath", "");
                var assetType = parameters.GetValueOrDefault("assetType", "all");
                var includeImportSettings = parameters.GetValueOrDefault("includeImportSettings", "true") == "true";
                var includeMetadata = parameters.GetValueOrDefault("includeMetadata", "true") == "true";
                var sortBy = parameters.GetValueOrDefault("sortBy", "name");
                
                var assetGuids = string.IsNullOrEmpty(assetPath) ?
                    AssetDatabase.FindAssets(GetAssetTypeFilter(assetType)) :
                    new[] { AssetDatabase.AssetPathToGUID(assetPath) };
                
                var assetInfos = new List<AssetFileInfo>();
                
                foreach (var guid in assetGuids.Take(100)) // Limit for performance
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path)) continue;
                    
                    var fileInfo = GetAssetFileInfo(path);
                    if (fileInfo != null)
                    {
                        var assetInfo = new AssetFileInfo
                        {
                            Path = path,
                            Name = Path.GetFileName(path),
                            Size = fileInfo.Length,
                            CreationTime = fileInfo.CreationTime,
                            LastWriteTime = fileInfo.LastWriteTime,
                            Extension = Path.GetExtension(path)
                        };
                        
                        if (includeImportSettings)
                        {
                            var importer = AssetImporter.GetAtPath(path);
                            assetInfo.ImporterType = importer?.GetType().Name ?? "Unknown";
                        }
                        
                        assetInfos.Add(assetInfo);
                    }
                }
                
                // Sort results
                assetInfos = SortAssetInfos(assetInfos, sortBy);
                
                return FormatAssetFileReport(assetInfos, includeMetadata);
            }
            catch (Exception e)
            {
                return $"Error getting asset file info: {e.Message}";
            }
        }
        
        /// <summary>
        /// Analyze asset usage
        /// </summary>
        public static string AnalyzeAssetUsage(Dictionary<string, string> parameters)
        {
            try
            {
                var assetType = parameters.GetValueOrDefault("assetType", "all");
                var findUnused = parameters.GetValueOrDefault("findUnused", "true") == "true";
                var findDuplicates = parameters.GetValueOrDefault("findDuplicates", "false") == "true";
                var includeSceneReferences = parameters.GetValueOrDefault("includeSceneReferences", "true") == "true";
                var includePrefabReferences = parameters.GetValueOrDefault("includePrefabReferences", "true") == "true";
                
                var report = new StringBuilder();
                report.AppendLine("=== Asset Usage Analysis ===");
                
                var assetGuids = AssetDatabase.FindAssets(GetAssetTypeFilter(assetType));
                report.AppendLine($"Analyzing {assetGuids.Length} assets of type '{assetType}'");
                report.AppendLine();
                
                if (findUnused)
                {
                    var unusedAssets = FindUnusedAssets(assetGuids, includeSceneReferences, includePrefabReferences);
                    report.AppendLine($"=== Unused Assets ({unusedAssets.Count}) ===");
                    foreach (var asset in unusedAssets.Take(20))
                    {
                        report.AppendLine($"  {asset}");
                    }
                    report.AppendLine();
                }
                
                if (findDuplicates)
                {
                    var duplicates = FindPotentialDuplicates(assetGuids);
                    report.AppendLine($"=== Potential Duplicates ({duplicates.Count}) ===");
                    foreach (var group in duplicates.Take(10))
                    {
                        report.AppendLine($"  Similar names:");
                        foreach (var asset in group)
                        {
                            report.AppendLine($"    {asset}");
                        }
                        report.AppendLine();
                    }
                }
                
                return report.ToString();
            }
            catch (Exception e)
            {
                return $"Error analyzing asset usage: {e.Message}";
            }
        }
        
        // ===== Helper Methods =====
        
        private static int GetUVChannelCount(Mesh mesh)
        {
            int count = 0;
            var uvs = new List<Vector2>();
            for (int i = 0; i < 8; i++)
            {
                mesh.GetUVs(i, uvs);
                if (uvs.Count > 0) count++;
                uvs.Clear();
            }
            return count;
        }
        
        private static string[] GetShaderColorProperties(Shader shader)
        {
            var properties = new List<string>();
            int count = ShaderUtil.GetPropertyCount(shader);
            for (int i = 0; i < count; i++)
            {
                if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.Color)
                {
                    properties.Add(ShaderUtil.GetPropertyName(shader, i));
                }
            }
            return properties.ToArray();
        }
        
        private static string[] GetShaderFloatProperties(Shader shader)
        {
            var properties = new List<string>();
            int count = ShaderUtil.GetPropertyCount(shader);
            for (int i = 0; i < count; i++)
            {
                var type = ShaderUtil.GetPropertyType(shader, i);
                if (type == ShaderUtil.ShaderPropertyType.Float || type == ShaderUtil.ShaderPropertyType.Range)
                {
                    properties.Add(ShaderUtil.GetPropertyName(shader, i));
                }
            }
            return properties.ToArray();
        }
        
        private static FileInfo GetAssetFileInfo(string assetPath)
        {
            try
            {
                var fullPath = Path.Combine(Application.dataPath, assetPath.Substring(7)); // Remove "Assets/"
                return new FileInfo(fullPath);
            }
            catch
            {
                return null;
            }
        }
        
        private static string GetAssetTypeFilter(string assetType)
        {
            return assetType switch
            {
                "textures" => "t:Texture2D",
                "meshes" => "t:Mesh",
                "audio" => "t:AudioClip",
                "scripts" => "t:MonoScript",
                "prefabs" => "t:Prefab",
                "materials" => "t:Material",
                _ => ""
            };
        }
        
        private static List<AssetFileInfo> SortAssetInfos(List<AssetFileInfo> assetInfos, string sortBy)
        {
            return sortBy switch
            {
                "size" => assetInfos.OrderByDescending(a => a.Size).ToList(),
                "date" => assetInfos.OrderByDescending(a => a.LastWriteTime).ToList(),
                "type" => assetInfos.OrderBy(a => a.Extension).ToList(),
                _ => assetInfos.OrderBy(a => a.Name).ToList()
            };
        }
        
        private static List<string> FindUnusedAssets(string[] assetGuids, bool includeScenes, bool includePrefabs)
        {
            var unusedAssets = new List<string>();
            var dependencies = AssetDatabase.GetDependencies(AssetDatabase.GetAllAssetPaths(), true);
            
            foreach (var guid in assetGuids.Take(50)) // Limit for performance
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                
                // Simple check - if asset is not in dependencies, it might be unused
                if (!dependencies.Contains(path))
                {
                    unusedAssets.Add(path);
                }
            }
            
            return unusedAssets;
        }
        
        private static List<List<string>> FindPotentialDuplicates(string[] assetGuids)
        {
            var duplicates = new List<List<string>>();
            var nameGroups = new Dictionary<string, List<string>>();
            
            foreach (var guid in assetGuids.Take(100))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                
                var name = Path.GetFileNameWithoutExtension(path).ToLower();
                if (!nameGroups.ContainsKey(name))
                {
                    nameGroups[name] = new List<string>();
                }
                nameGroups[name].Add(path);
            }
            
            foreach (var group in nameGroups.Values)
            {
                if (group.Count > 1)
                {
                    duplicates.Add(group);
                }
            }
            
            return duplicates;
        }
        
        private static string FormatAssetFileReport(List<AssetFileInfo> assetInfos, bool includeMetadata)
        {
            var report = new StringBuilder();
            report.AppendLine("=== Asset File Information ===");
            report.AppendLine($"Found {assetInfos.Count} asset(s)");
            report.AppendLine();
            
            long totalSize = 0;
            foreach (var asset in assetInfos.Take(50))
            {
                report.AppendLine($"Asset: {asset.Name}");
                report.AppendLine($"  Path: {asset.Path}");
                report.AppendLine($"  Size: {FormatBytes(asset.Size)}");
                report.AppendLine($"  Type: {asset.Extension}");
                
                if (includeMetadata)
                {
                    report.AppendLine($"  Created: {asset.CreationTime:yyyy-MM-dd HH:mm:ss}");
                    report.AppendLine($"  Modified: {asset.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
                    report.AppendLine($"  Importer: {asset.ImporterType}");
                }
                
                totalSize += asset.Size;
                report.AppendLine();
            }
            
            report.AppendLine($"Total Size: {FormatBytes(totalSize)}");
            return report.ToString();
        }
        
        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int counter = 0;
            decimal number = (decimal)bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number = number / 1024;
                counter++;
            }
            return string.Format("{0:n1} {1}", number, suffixes[counter]);
        }
        
        private class AssetFileInfo
        {
            public string Path { get; set; }
            public string Name { get; set; }
            public long Size { get; set; }
            public DateTime CreationTime { get; set; }
            public DateTime LastWriteTime { get; set; }
            public string Extension { get; set; }
            public string ImporterType { get; set; }
        }
    }
}