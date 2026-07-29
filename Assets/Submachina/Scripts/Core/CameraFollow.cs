using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Smoothly follows a target transform with configurable lag.
     *
     * Designed for the submarine camera — the camera tracks the player
     * with a slight delay so fast movement feels weighty rather than
     * glued. A vertical offset lets you show more of the world below
     * the sub (where the player is heading) than above.
     *
     * Runs in LateUpdate so it always reads the final position after
     * physics and player scripts have moved the target that frame.
     *
     * Setup:
     *   - Attach to the Main Camera.
     *   - Assign the submarine root as Target.
     *   - Z position is locked to the Inspector value — never follows target Z.
     */
    public class CameraFollow : MonoBehaviour
    {
        // =====================
        // Target
        // =====================

        [FoldoutGroup("Target")]
        [Tooltip("The transform the camera follows. Assign the submarine root.")]
        [SerializeField] private Transform target;

        // =====================
        // Follow Settings
        // =====================

        [FoldoutGroup("Follow")]
        [Tooltip("How quickly the camera catches up to the target. Lower = more lag/weight, higher = snappier. " +
                 "Example: 3 feels cinematic, 8 feels tight.")]
        [SerializeField, Min(0.1f)] private float smoothSpeed = 4f;

        [FoldoutGroup("Follow")]
        [Tooltip("World-space offset applied to the target position before following. " +
                 "Positive Y shifts the camera upward, showing more of the world below. " +
                 "Example: (0, -2) keeps the sub in the upper half, revealing what's ahead.")]
        [SerializeField] private Vector2 offset = new Vector2(0f, -2f);

        [FoldoutGroup("Follow")]
        [Tooltip("Lock the X axis so the camera only follows vertically. " +
                 "Useful early on before lateral world generation is in place.")]
        [SerializeField] private bool lockX = false;

        // =====================
        // Bounds (optional)
        // =====================

        [FoldoutGroup("Bounds")]
        [InfoBox("When a LevelBounds is assigned (or found in the scene), the FULL view rect is clamped " +
                 "inside it. The legacy clampTop fields below only apply when no LevelBounds is available.")]
        [Tooltip("The level's authoritative extents. Auto-resolved from the scene at Awake when left empty.")]
        [SerializeField] private LevelBounds levelBounds;

        [FoldoutGroup("Bounds")]
        [Tooltip("LEGACY (superseded by LevelBounds): clamp how far up the camera can travel. " +
                 "Set to 0 to represent the ocean surface — the camera never shows above water.")]
        [SerializeField] private bool clampTop = true;

        [FoldoutGroup("Bounds")]
        [ShowIf("clampTop")]
        [Tooltip("LEGACY (superseded by LevelBounds): maximum world Y the camera can reach. 0 = ocean surface.")]
        [SerializeField] private float topBoundY = 0f;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private Vector2 TargetPosition => target != null
            ? (Vector2)target.position + offset
            : Vector2.zero;

        // =====================
        // State
        // =====================

        // Cached camera on this object — needed for view-rect clamping against LevelBounds
        private Camera _cam;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            // Cache references once: the camera we ride on and the scene's level bounds
            _cam = GetComponent<Camera>();
            if (levelBounds == null) levelBounds = LevelBounds.Find();
        }

        private void LateUpdate()
        {
            if (target == null) return;

            MoveTowardsTarget();
        }

        // -------------------------------------------------------
        // Core
        // -------------------------------------------------------

        /**
         * Lerps the camera toward the target position each frame.
         *
         * Uses Time.deltaTime * smoothSpeed as the lerp factor — this
         * gives frame-rate-independent smoothing with an exponential
         * approach feel (closes a fraction of the gap each frame).
         *
         * Example: gap=10 units, smoothSpeed=4 → after 0.25s, gap ≈ 3.7 units
         */
        private void MoveTowardsTarget()
        {
            // Build the desired position from target + offset, clamped inside the level
            Vector3 desired = ClampDesired(
                lockX ? transform.position.x : target.position.x + offset.x,
                target.position.y + offset.y);

            // Smooth lerp toward desired position
            transform.position = Vector3.Lerp(transform.position, desired, Time.deltaTime * smoothSpeed);
        }

        /**
         * Applies whichever bounding is available: full view-rect clamping via
         * LevelBounds when present (needs the camera for view size), else the
         * legacy top-only clamp.
         */
        private Vector3 ClampDesired(float x, float y)
        {
            Vector3 desired = new Vector3(x, y, transform.position.z);

            // Preferred path: clamp the whole view rect inside the level bounds
            if (levelBounds != null && _cam != null && _cam.orthographic)
                return levelBounds.ClampCameraCentre(desired, _cam.orthographicSize, _cam.aspect);

            // Legacy fallback — never show above the ocean surface
            if (clampTop) desired.y = Mathf.Min(desired.y, topBoundY);
            return desired;
        }

        // -------------------------------------------------------
        // Public API
        // -------------------------------------------------------

        /** Snaps the camera instantly to the target with no smoothing. Useful on scene load or respawn. */
        public void SnapToTarget()
        {
            if (target == null) return;

            transform.position = ClampDesired(
                lockX ? transform.position.x : target.position.x + offset.x,
                target.position.y + offset.y);
        }

        /** Reassigns the follow target at runtime. Call when switching control to a different object. */
        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
        }

        // -------------------------------------------------------
        // Editor Utilities
        // -------------------------------------------------------

#if UNITY_EDITOR
        [FoldoutGroup("Debug")]
        [Button("Snap to Target"), GUIColor(0.6f, 0.8f, 1f)]
        private void DebugSnap()
        {
            if (!Application.isPlaying) { Debug.Log("[CameraFollow] Play mode only."); return; }
            SnapToTarget();
        }

        private void OnDrawGizmosSelected()
        {
            if (target == null) return;

            // Draw the desired camera position as a cyan crosshair
            Vector3 desired = (Vector3)((Vector2)target.position + offset);
            desired.z = transform.position.z;

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(desired, 0.3f);
            Gizmos.DrawLine(transform.position, desired);

            // Draw top bound as a horizontal line if enabled
            if (!clampTop) return;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.5f);
            Gizmos.DrawLine(new Vector3(-50f, topBoundY, 0f), new Vector3(50f, topBoundY, 0f));
        }
#endif
    }
}
