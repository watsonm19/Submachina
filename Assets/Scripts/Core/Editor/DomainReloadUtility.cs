using UnityEditor;
using UnityEngine;

namespace Core.Editor
{
    /**
     * Editor-only utility to force a script domain reload on demand.
     *
     * Why this exists: stale or half-built dynamic assemblies (e.g. Mono.CSharp
     * evaluator snippets emitted by editor tooling) linger in the AppDomain until
     * the next reload and can break editor code that scans assembly types
     * (ReflectionTypeLoadException blanking inspectors). Ctrl+R only refreshes the
     * asset database and skips the reload when no scripts changed, so this exposes
     * the reload directly instead of having to touch a script to trigger one.
     */
    public static class DomainReloadUtility
    {
        /**
         * Force a full script domain reload (Ctrl+Alt+R). Rebindable via
         * Edit > Shortcuts under "Tools/Custom/Reload Domain".
         */
        [MenuItem("Tools/Custom/Reload Domain %&r")]
        public static void ReloadDomain()
        {
            // Guard: reloading mid-play would rip state out from under the game.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[DomainReloadUtility] Refusing to reload the domain during play mode — exit play mode first.");
                return;
            }

            // Request the reload; Unity tears down and rebuilds the scripting AppDomain.
            Debug.Log("[DomainReloadUtility] Forcing script domain reload...");
            EditorUtility.RequestScriptReload();
        }
    }
}
