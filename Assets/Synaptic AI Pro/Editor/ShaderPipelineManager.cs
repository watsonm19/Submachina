using UnityEngine;
using SynapticAIPro;
using UnityEditor;
using UnityEngine.Rendering;
using System.IO;
using System.Linq;

namespace Synaptic.Editor
{
    /// <summary>
    /// Automatically manages shader files based on the current render pipeline.
    /// Disables shaders for pipelines that are not installed to prevent compilation errors.
    /// </summary>
    [InitializeOnLoad]
    public static class ShaderPipelineManager
    {
        public enum RenderPipelineType
        {
            BuiltIn,
            URP,
            HDRP
        }

        private const string SHADER_BASE_PATH = "Assets/Synaptic AI Pro/Shaders";
        private const string BUILTIN_FOLDER = "BuiltIn";
        private const string URP_FOLDER = "URP";
        private const string HDRP_FOLDER = "HDRP";
        private const string DISABLED_EXTENSION = ".disabled";

        private static readonly string[] PIPELINE_FOLDERS = { BUILTIN_FOLDER, URP_FOLDER, HDRP_FOLDER };

        static ShaderPipelineManager()
        {
            // Delay execution to ensure Unity is fully initialized
            EditorApplication.delayCall += OnEditorInitialized;
        }

        private static void OnEditorInitialized()
        {
            EditorApplication.delayCall -= OnEditorInitialized;

            // Check and update shaders on editor load
            UpdateShadersForCurrentPipeline();

            // Subscribe to pipeline changes
            RenderPipelineManager.activeRenderPipelineTypeChanged += OnPipelineChanged;
        }

        private static void OnPipelineChanged()
        {
            SynLog.Info("[Synaptic] Render pipeline changed, updating shaders...");
            UpdateShadersForCurrentPipeline();
        }

        /// <summary>
        /// Detects the currently active render pipeline.
        /// </summary>
        public static RenderPipelineType DetectCurrentPipeline()
        {
            var pipelineAsset = GraphicsSettings.currentRenderPipeline;

            if (pipelineAsset == null)
            {
                return RenderPipelineType.BuiltIn;
            }

            var typeName = pipelineAsset.GetType().FullName;

            if (typeName.Contains("Universal") || typeName.Contains("URP"))
            {
                return RenderPipelineType.URP;
            }

            if (typeName.Contains("HighDefinition") || typeName.Contains("HDRenderPipeline") || typeName.Contains("HDRP"))
            {
                return RenderPipelineType.HDRP;
            }

            return RenderPipelineType.BuiltIn;
        }

        /// <summary>
        /// Updates shader files based on the current pipeline.
        /// Enables shaders for the current pipeline and disables others.
        /// </summary>
        [MenuItem("Tools/Synaptic Pro/Update Shaders for Pipeline")]
        public static void UpdateShadersForCurrentPipeline()
        {
            var currentPipeline = DetectCurrentPipeline();
            SynLog.Info($"[Synaptic] Detected render pipeline: {currentPipeline}");

            bool changed = false;

            foreach (var folder in PIPELINE_FOLDERS)
            {
                string folderPath = Path.Combine(SHADER_BASE_PATH, folder);

                if (!Directory.Exists(folderPath))
                {
                    continue;
                }

                bool shouldEnable = ShouldEnableFolder(folder, currentPipeline);
                changed |= UpdateShaderFolder(folderPath, shouldEnable);
            }

            if (changed)
            {
                AssetDatabase.Refresh();
                SynLog.Info($"[Synaptic] Shaders updated for {currentPipeline} pipeline");
            }
        }

        private static bool ShouldEnableFolder(string folder, RenderPipelineType currentPipeline)
        {
            switch (folder)
            {
                case BUILTIN_FOLDER:
                    return currentPipeline == RenderPipelineType.BuiltIn;
                case URP_FOLDER:
                    return currentPipeline == RenderPipelineType.URP;
                case HDRP_FOLDER:
                    return currentPipeline == RenderPipelineType.HDRP;
                default:
                    return true;
            }
        }

        private static bool UpdateShaderFolder(string folderPath, bool enable)
        {
            bool changed = false;

            // Get all shader files (both enabled and disabled)
            var shaderFiles = Directory.GetFiles(folderPath, "*.shader", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(folderPath, "*.shader" + DISABLED_EXTENSION, SearchOption.AllDirectories))
                .ToArray();

            foreach (var filePath in shaderFiles)
            {
                bool isDisabled = filePath.EndsWith(DISABLED_EXTENSION);

                if (enable && isDisabled)
                {
                    // Enable: remove .disabled extension
                    string newPath = filePath.Substring(0, filePath.Length - DISABLED_EXTENSION.Length);
                    MoveFile(filePath, newPath);
                    changed = true;
                }
                else if (!enable && !isDisabled)
                {
                    // Disable: add .disabled extension
                    string newPath = filePath + DISABLED_EXTENSION;
                    MoveFile(filePath, newPath);
                    changed = true;
                }
            }

            return changed;
        }

        private static void MoveFile(string sourcePath, string destPath)
        {
            // Also handle .meta files
            string sourceMetaPath = sourcePath + ".meta";
            string destMetaPath = destPath + ".meta";

            try
            {
                if (File.Exists(sourcePath))
                {
                    File.Move(sourcePath, destPath);
                }

                if (File.Exists(sourceMetaPath))
                {
                    File.Move(sourceMetaPath, destMetaPath);
                }
            }
            catch (System.Exception e)
            {
                SynLog.Warn($"[Synaptic] Failed to move shader file: {e.Message}");
            }
        }

        /// <summary>
        /// Gets the appropriate shader name for the current pipeline.
        /// </summary>
        public static string GetShaderName(string baseShaderName)
        {
            var pipeline = DetectCurrentPipeline();
            string pipelineFolder = GetPipelineFolderName(pipeline);
            return $"Synaptic/{pipelineFolder}/{baseShaderName}";
        }

        /// <summary>
        /// Gets the folder name for a specific pipeline type.
        /// </summary>
        public static string GetPipelineFolderName(RenderPipelineType pipeline)
        {
            switch (pipeline)
            {
                case RenderPipelineType.URP:
                    return URP_FOLDER;
                case RenderPipelineType.HDRP:
                    return HDRP_FOLDER;
                default:
                    return BUILTIN_FOLDER;
            }
        }

        /// <summary>
        /// Finds a shader compatible with the current pipeline.
        /// Uses PackageRequirements-enabled multi-SubShader approach.
        /// </summary>
        public static Shader FindShaderForCurrentPipeline(string baseShaderName)
        {
            // Shaders now use PackageRequirements tags, so Unity automatically
            // skips SubShaders for missing render pipelines.
            // Just use the standard shader name.
            return Shader.Find($"Synaptic/{baseShaderName}");
        }
    }
}
