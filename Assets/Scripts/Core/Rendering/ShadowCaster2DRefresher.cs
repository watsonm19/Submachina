using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Core.Rendering
{
    /**
     * Forces URP 2D ShadowCaster2D components to rebuild their shadow mesh, both
     * at runtime after Instantiate() and on demand in the editor.
     *
     * Why this is needed:
     *   ShadowCaster2D never serializes its actual shadow Mesh into the prefab or
     *   scene (m_ShadowMesh.m_Mesh is always {fileID: 0}); the mesh is regenerated
     *   at load time. Regeneration only happens inside ShadowCaster2D.Update() when
     *   it detects a *change* (casting source / edge processing / trim edge /
     *   shape component differ from the serialized "previous" values, or the
     *   internal m_ForceShadowMeshRebuild flag is set). A cleanly-serialized
     *   caster whose "previous" values already match itself detects no change on
     *   load, so it keeps an empty mesh and casts no shadow. Sprite reimports
     *   (e.g. editing a custom outline) can also leave a caster with a stale,
     *   empty mesh. That is why the breakage is intermittent, and why toggling
     *   the casting source in the Inspector fixes it by hand.
     *
     * The fix: set ShadowCaster2D's internal m_ForceShadowMeshRebuild = true via
     * reflection, so the very next Update() rebuilds the mesh from the shape
     * provider. No public API exists for this, hence the reflection.
     *
     * Usage: drop this on any scene object (a manager works fine) and it heals
     * every caster in the loaded scenes on Start. Spawners can also call
     * RefreshHierarchy right after Instantiate for a targeted refresh.
     * Edit-mode healing is handled by Core.Editor.ShadowCaster2DEditorRefresher,
     * which calls into this class on scene open / sprite reimport.
     */
    public class ShadowCaster2DRefresher : MonoBehaviour
    {
        [Tooltip("Refresh every caster in the loaded scenes on Start, not just this hierarchy.")]
        [SerializeField] private bool refreshEntireScene = true;

        [Tooltip("Also rebuild casters on inactive objects.")]
        [SerializeField] private bool includeInactive = true;

        // Cached reflection handles into ShadowCaster2D internals (URP 17.x).
        private static readonly FieldInfo ForceRebuildField =
            typeof(ShadowCaster2D).GetField("m_ForceShadowMeshRebuild",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ShadowMeshField =
            typeof(ShadowCaster2D).GetField("m_ShadowMesh",
                BindingFlags.Instance | BindingFlags.NonPublic);

        // Lazy handle to the internal ShadowMesh2D.mesh property (type isn't public).
        private static PropertyInfo _meshProperty;

        // Warn at most once if the field name changes in a future URP version.
        private static bool _warnedMissingField;

        /**
         * Runs after Awake/OnEnable of freshly instantiated casters, so setting
         * the rebuild flag here is guaranteed to be picked up by each caster's
         * first meaningful Update() this frame.
         */
        private void Start()
        {
            if (refreshEntireScene) RefreshScene(includeInactive);
            else RefreshHierarchy(gameObject, includeInactive);
        }

        /**
         * Forces every ShadowCaster2D in all loaded scenes to rebuild its shadow
         * mesh on the next Update. Works in both play mode and edit mode (in edit
         * mode the caller is responsible for ticking the player loop, see
         * ShadowCaster2DEditorRefresher).
         */
        public static void RefreshScene(bool includeInactive = true)
        {
            var include = includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;
            foreach (ShadowCaster2D caster in FindObjectsByType<ShadowCaster2D>(include, FindObjectsSortMode.None))
                ForceRebuild(caster);
        }

        /**
         * Forces every ShadowCaster2D on `root` and (optionally) its children to
         * rebuild its shadow mesh on the next frame.
         */
        public static void RefreshHierarchy(GameObject root, bool includeInactive = true)
        {
            if (root == null) return;

            // One flag flip per caster; the caster's own Update() does the actual rebuild
            foreach (ShadowCaster2D caster in root.GetComponentsInChildren<ShadowCaster2D>(includeInactive))
                ForceRebuild(caster);
        }

        /**
         * Marks a single caster for shadow-mesh rebuild. Falls back to a public
         * trim-edge nudge if the internal flag can't be reached (defensive against
         * URP internals renaming).
         */
        public static void ForceRebuild(ShadowCaster2D caster)
        {
            if (caster == null) return;

            // Preferred path: set the internal force-rebuild flag the component already honors
            if (ForceRebuildField != null)
            {
                ForceRebuildField.SetValue(caster, true);
                return;
            }

            // Fallback: a real trim-edge change also trips Update()'s rebuild check
            if (!_warnedMissingField)
            {
                Debug.LogWarning("[ShadowCaster2DRefresher] ShadowCaster2D.m_ForceShadowMeshRebuild " +
                                 "not found (URP changed?); falling back to trimEdge nudge.");
                _warnedMissingField = true;
            }
            caster.trimEdge += 0.0001f;
        }

        /**
         * Reports whether the caster currently holds non-empty shadow geometry.
         * Used by the editor refresher to verify a rebuild actually produced a
         * mesh (a rebuild right after a sprite reimport can come up empty once).
         * Returns true when the internals can't be inspected, to avoid retry loops.
         */
        public static bool HasShadowGeometry(ShadowCaster2D caster)
        {
            if (caster == null || ShadowMeshField == null) return true;

            // Dig out the internal ShadowMesh2D wrapper, then its mesh property
            object wrapper = ShadowMeshField.GetValue(caster);
            if (wrapper == null) return false;
            if (_meshProperty == null) _meshProperty = wrapper.GetType().GetProperty("mesh");

            Mesh mesh = _meshProperty != null ? _meshProperty.GetValue(wrapper, null) as Mesh : null;
            return mesh != null && mesh.vertexCount > 0;
        }
    }
}
