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
        [Tooltip("Clamp how far up the camera can travel. " +
                 "Set to 0 to represent the ocean surface — the camera never shows above water.")]
        [SerializeField] private bool clampTop = true;

        [FoldoutGroup("Bounds")]
        [ShowIf("clampTop")]
        [Tooltip("Maximum world Y the camera can reach. 0 = ocean surface.")]
        [SerializeField] private float topBoundY = 0f;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private Vector2 TargetPosition => target != null
            ? (Vector2)target.position + offset
            : Vector2.zero;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

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
            // Build the desired position from target + offset
            float desiredX = lockX ? transform.position.x : target.position.x + offset.x;
            float desiredY = target.position.y + offset.y;

            // Apply top bound — never show above the ocean surface
            if (clampTop) desiredY = Mathf.Min(desiredY, topBoundY);

            Vector3 desired = new Vector3(desiredX, desiredY, transform.position.z);

            // Smooth lerp toward desired position
            transform.position = Vector3.Lerp(transform.position, desired, Time.deltaTime * smoothSpeed);
        }

        // -------------------------------------------------------
        // Public API
        // -------------------------------------------------------

        /** Snaps the camera instantly to the target with no smoothing. Useful on scene load or respawn. */
        public void SnapToTarget()
        {
            if (target == null) return;

            float snapX = lockX ? transform.position.x : target.position.x + offset.x;
            float snapY = target.position.y + offset.y;
            if (clampTop) snapY = Mathf.Min(snapY, topBoundY);

            transform.position = new Vector3(snapX, snapY, transform.position.z);
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
