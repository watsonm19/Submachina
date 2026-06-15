using UnityEngine;
using UnityAtoms.BaseAtoms;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Converts the submarine's world Y position into a "meters below surface"
     * depth value and writes it to a shared FloatVariable Atom each frame.
     *
     * Surface is assumed to be Y=0. As the submarine descends (negative Y),
     * depth increases positively — e.g., Y=-25 becomes 25 metres depth.
     *
     * All other systems that care about depth (difficulty scaling, visual
     * darkening, depth bonus checks, UI display) should read the Atom rather
     * than referencing this script directly.
     *
     * As a SubmarineComponent, this lives under the submarine hierarchy and
     * resolves the tracked transform from its sibling physics controller
     * (Sub.Physics) — no direct transform wiring required.
     *
     * Setup:
     *   - Attach to a GameObject under the submarine root.
     *   - Assign or create a FloatVariable asset named CurrentDepth.
     */
    public class DepthTracker : SubmarineComponent
    {
        // =====================
        // References
        // =====================

        [FoldoutGroup("References")]
        [Tooltip("Atom written every frame with the current depth in metres. " +
                 "All other systems read from here.")]
        [SerializeField] private FloatVariable currentDepth;

        // =====================
        // Settings
        // =====================

        [FoldoutGroup("Settings")]
        [Tooltip("Y position treated as the ocean surface (depth = 0). " +
                 "Should match the SurfaceBoundary collider Y position.")]
        [SerializeField] private float surfaceY = 0f;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float DepthMetres => TrackedTransform != null ? Mathf.Max(0f, surfaceY - TrackedTransform.position.y) : 0f;

        // -------------------------------------------------------
        // References
        // -------------------------------------------------------

        /** Transform whose Y position defines depth — sourced from the sub's physics controller. */
        private Transform TrackedTransform => Sub != null && Sub.Physics != null ? Sub.Physics.transform : null;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Update()
        {
            WriteDepth();
        }

        // -------------------------------------------------------
        // Core
        // -------------------------------------------------------

        /**
         * Calculates depth as the vertical distance below the surface and
         * writes it to the Atom. Clamped to zero so depth never goes negative
         * if the submarine somehow ends up above the surface.
         *
         * Formula: depth = surfaceY - target.Y
         * Example: surfaceY=0, target.Y=-42 → depth = 42 metres
         */
        private void WriteDepth()
        {
            if (TrackedTransform == null || currentDepth == null) return;

            currentDepth.Value = Mathf.Max(0f, surfaceY - TrackedTransform.position.y);
        }
    }
}
