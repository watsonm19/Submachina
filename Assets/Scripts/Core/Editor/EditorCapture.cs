using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Core.Editor
{
    /**
     * Editor-only screenshot utility for reliably capturing a camera's view.
     *
     * Why this exists: the usual capture paths (and some MCP screenshot tools) force a
     * Play-mode transition and grab the frame via ScreenCapture, which races the render
     * and can return a blank/cleared buffer. Instead we issue a URP RenderPipeline render
     * request straight into a RenderTexture in edit mode — deterministic, no Play mode,
     * exact pixels. Output PNGs land under Assets/Captures/.
     */
    public static class EditorCapture
    {
        // Default output location and capture sizes used by the menu items.
        private const string OutputFolder = "Assets/Captures";
        private const int DefaultWidth = 1920;
        private const int DefaultHeight = 1080;

        // ---- Menu items -----------------------------------------------------

        /**
         * Capture the active scene's main game camera at 1920x1080.
         */
        [MenuItem("Tools/Custom/Capture Game Camera (1920x1080)")]
        public static void CaptureGameCamera()
        {
            // Resolve the camera to shoot: prefer Camera.main, fall back to any camera.
            var cam = ResolveGameCamera();
            if (cam == null) { Debug.LogError("[EditorCapture] No camera found to capture."); return; }

            // Render and save, then reveal the result in the Project window.
            var path = Capture(cam, DefaultWidth, DefaultHeight);
            PingResult(path);
        }

        /**
         * Capture the active scene's main game camera as a 1024x1024 square — handy for
         * inspecting tightly-framed lighting/shadow detail without letterboxing.
         */
        [MenuItem("Tools/Custom/Capture Game Camera (Square 1024)")]
        public static void CaptureGameCameraSquare()
        {
            var cam = ResolveGameCamera();
            if (cam == null) { Debug.LogError("[EditorCapture] No camera found to capture."); return; }

            var path = Capture(cam, 1024, 1024);
            PingResult(path);
        }

        /**
         * Capture whatever the last active Scene View is currently showing — useful for
         * framing an arbitrary region by just moving the editor view.
         */
        [MenuItem("Tools/Custom/Capture Scene View")]
        public static void CaptureSceneView()
        {
            // The Scene View hosts its own camera; bail if no Scene View is open.
            var sv = SceneView.lastActiveSceneView;
            if (sv == null || sv.camera == null) { Debug.LogError("[EditorCapture] No active Scene View."); return; }

            var path = Capture(sv.camera, DefaultWidth, DefaultHeight);
            PingResult(path);
        }

        // ---- Reusable API ---------------------------------------------------

        /**
         * Render the given camera to a RenderTexture and write it out as a PNG.
         *
         * Uses URP's RenderPipeline render-request API so no Play-mode transition is needed.
         * Pass an explicit filePath to control the destination; otherwise a timestamped file
         * is created under Assets/Captures/. Returns the asset path written, or null
         * on failure.
         *
         * Example: EditorCapture.Capture(Camera.main, 1920, 1080, "Assets/Shot.png");
         */
        public static string Capture(Camera cam, int width, int height, string filePath = null)
        {
            if (cam == null) { Debug.LogError("[EditorCapture] Capture called with a null camera."); return null; }

            // Pick a destination path, defaulting to a timestamped file in the output folder.
            if (string.IsNullOrEmpty(filePath))
            {
                var stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                filePath = $"{OutputFolder}/Capture_{stamp}.png";
            }

            // Ensure the destination directory exists for both default and caller-supplied paths.
            var destDir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

            // Allocate an offscreen target with a depth buffer so 2D lighting/shadows resolve.
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            rt.Create();

            // Issue a URP render request that renders this camera into our RenderTexture.
            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request))
            {
                RenderPipeline.SubmitRenderRequest(cam, request);
            }
            else
            {
                // Not on a Scriptable Render Pipeline (or request unsupported) — clean up and warn.
                Object.DestroyImmediate(rt);
                Debug.LogError("[EditorCapture] Camera does not support render requests (is URP active?).");
                return null;
            }

            // Read the rendered pixels back from the active RenderTexture into a CPU texture.
            var previousActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = previousActive;

            // Encode to PNG and write to disk.
            File.WriteAllBytes(filePath, tex.EncodeToPNG());

            // Release temporary GPU/CPU resources now that the bytes are on disk.
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);

            // Import so the new PNG shows up in the Project window immediately.
            AssetDatabase.ImportAsset(filePath, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"[EditorCapture] Saved {width}x{height} capture -> {filePath}");
            return filePath;
        }

        // ---- Helpers --------------------------------------------------------

        /**
         * Find a sensible game camera to capture: Camera.main first, then any camera in the scene.
         */
        private static Camera ResolveGameCamera()
        {
            if (Camera.main != null) return Camera.main;
            var cams = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            return cams.Length > 0 ? cams[0] : null;
        }

        /**
         * Select and highlight the freshly written PNG in the Project window.
         */
        private static void PingResult(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var asset = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (asset != null) EditorGUIUtility.PingObject(asset);
        }
    }
}
