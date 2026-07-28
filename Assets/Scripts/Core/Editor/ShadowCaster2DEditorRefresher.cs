using Core.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Core.Editor
{
    /**
     * Edit-mode counterpart to ShadowCaster2DRefresher. URP's ShadowCaster2D never
     * serializes its shadow mesh, and in the editor nothing reliably rebuilds it
     * after a scene opens or a sprite is reimported — casters can silently sit
     * with an empty mesh and cast no shadow until something "changes" in the
     * Inspector. This hook heals all casters automatically:
     *   - when a scene is opened,
     *   - after every domain reload (script recompiles wipe the meshes too),
     *   - after any texture reimport (sprite outline edits change the mesh source),
     *   - on demand via Tools/Custom/Refresh 2D Shadows.
     *
     * Each caster's rebuild is driven by calling its public Update() directly
     * rather than ticking the editor player loop — a player-loop tick would also
     * run every other ExecuteAlways script in the scene (spawners etc.), which
     * this must not do. A rebuild immediately after a reimport can come up empty
     * once (the shape provider re-initializes on the first pass), so each pass is
     * verified and stragglers are retried a bounded number of times.
     */
    [InitializeOnLoad]
    public static class ShadowCaster2DEditorRefresher
    {
        private const int MaxPasses = 3;

        static ShadowCaster2DEditorRefresher()
        {
            EditorSceneManager.sceneOpened += (scene, mode) => RequestRefresh();

            // Domain reloads (script recompiles) also wipe rebuilt meshes — this ctor
            // reruns after every reload, so schedule a refresh once the editor settles
            EditorApplication.delayCall += RequestRefresh;
        }

        [MenuItem("Tools/Custom/Refresh 2D Shadows")]
        private static void RefreshFromMenu() => RequestRefresh();

        /**
         * Rebuilds the shadow mesh of every ShadowCaster2D in the loaded scenes,
         * synchronously. Empty meshes can be legitimate (e.g. no sprite assigned),
         * so retry passes are capped rather than looping until clean.
         */
        public static void RequestRefresh()
        {
            // Play mode has the runtime refresher; only heal while editing
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            var casters = Object.FindObjectsByType<ShadowCaster2D>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            // Flag + drive each caster's own Update; retry only the still-empty ones
            for (int pass = 0; pass < MaxPasses; pass++)
            {
                bool anyEmpty = false;
                foreach (ShadowCaster2D caster in casters)
                {
                    if (pass > 0 && ShadowCaster2DRefresher.HasShadowGeometry(caster)) continue;
                    if (!caster.isActiveAndEnabled) continue;

                    ShadowCaster2DRefresher.ForceRebuild(caster);
                    caster.Update();
                    if (!ShadowCaster2DRefresher.HasShadowGeometry(caster)) anyEmpty = true;
                }
                if (!anyEmpty) break;
            }

            SceneView.RepaintAll();
        }
    }

    /**
     * Refreshes 2D shadows after texture imports, since editing a sprite's
     * custom outline or physics shape changes the geometry casters build from
     * and can leave existing casters with stale empty meshes.
     */
    internal sealed class ShadowCaster2DReimportWatcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            // One refresh covers the whole batch; bail on the first texture found
            foreach (string path in importedAssets)
            {
                if (AssetDatabase.GetMainAssetTypeAtPath(path) != typeof(Texture2D)) continue;
                ShadowCaster2DEditorRefresher.RequestRefresh();
                return;
            }
        }
    }
}
