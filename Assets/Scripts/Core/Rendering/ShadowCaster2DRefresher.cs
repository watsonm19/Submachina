using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Core.Rendering
{
    /**
     * Forces URP 2D ShadowCaster2D components to rebuild their shadow mesh at
     * runtime after they are Instantiate()'d.
     *
     * Why this is needed:
     *   ShadowCaster2D never serializes its actual shadow Mesh into the prefab
     *   (m_ShadowMesh.m_Mesh is always {fileID: 0}); the mesh is regenerated at
     *   load time. Regeneration only happens inside ShadowCaster2D.Update() when
     *   it detects a *change* (casting source / edge processing / trim edge /
     *   shape component differ from the serialized "previous" values, or the
     *   internal m_ForceShadowMeshRebuild flag is set). A cleanly-serialized
     *   caster whose "previous" values already match itself detects no change on
     *   a fresh clone, so it keeps the null mesh and casts no shadow. That is why
     *   the breakage is intermittent, and why toggling the casting source in the
     *   Inspector (which forces a change) fixes it by hand.
     *
     * The fix: set ShadowCaster2D's internal m_ForceShadowMeshRebuild = true via
     * reflection, so the very next Update() rebuilds the mesh from the shape
     * provider. No public API exists for this, hence the reflection.
     *
     * Usage: drop this on a prefab root that has (or contains) shadow casters and
     * it self-heals on Start; or call ShadowCaster2DRefresher.RefreshHierarchy
     * directly from a spawner right after Instantiate.
     */
    public class ShadowCaster2DRefresher : MonoBehaviour
    {
        // Only run at runtime — in the editor ExecuteInEditMode already keeps the mesh rebuilt.
        [Tooltip("Also rebuild casters on inactive child objects.")]
        [SerializeField] private bool includeInactive = true;

        // Cached reflection handle to ShadowCaster2D's internal rebuild flag (URP 17.x).
        private static readonly FieldInfo ForceRebuildField =
            typeof(ShadowCaster2D).GetField("m_ForceShadowMeshRebuild",
                BindingFlags.Instance | BindingFlags.NonPublic);

        // Warn at most once if the field name changes in a future URP version.
        private static bool _warnedMissingField;

        /**
         * Runs after Awake/OnEnable of the freshly instantiated caster, so setting
         * the rebuild flag here is guaranteed to be picked up by the caster's first
         * meaningful Update() this frame.
         */
        private void Start() => RefreshHierarchy(gameObject, includeInactive);

        /**
         * Forces every ShadowCaster2D on `root` and (optionally) its children to
         * rebuild its shadow mesh on the next frame. Safe no-op in edit mode.
         */
        public static void RefreshHierarchy(GameObject root, bool includeInactive = true)
        {
            if (root == null || !Application.isPlaying) return;

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
    }
}
