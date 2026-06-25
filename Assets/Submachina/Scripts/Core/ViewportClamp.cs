using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Constrains the submarine's Rigidbody2D position to the visible camera viewport.
     *
     * Designed for forced-scroll mode (AutoScrollCamera). The camera defines the
     * playfield and the sub cannot leave it. When the sub hits an edge, the velocity
     * component pushing further out is zeroed to avoid a "pressing against glass" feel.
     *
     * Runs in LateUpdate after physics have settled, then corrects position and velocity.
     *
     * Setup:
     *   - Place on the submarine root, or as a child prefab nested under it — the
     *     Rigidbody2D is resolved from the first parent that has one.
     *   - Assign the AutoScrollCamera, or let it auto-find the first one in the
     *     scene (default); falls back to Camera.main if none is found.
     */
    public class ViewportClamp : MonoBehaviour
    {
        // =====================
        // Camera Source
        // =====================

        [FoldoutGroup("Camera")]
        [Tooltip("The AutoScrollCamera providing viewport bounds. If unset, Camera.main is used with no padding.")]
        [SerializeField] private AutoScrollCamera scrollCamera;

        [FoldoutGroup("Camera")]
        [Tooltip("When no AutoScrollCamera is assigned, automatically grab the first one found in the scene at startup.")]
        [SerializeField] private bool autoFindScrollCamera = true;

        [FoldoutGroup("Camera")]
        [Tooltip("Fallback used when AutoScrollCamera is not assigned.")]
        [SerializeField] private Camera fallbackCamera;

        // =====================
        // Per-Edge Margins
        // =====================

        [FoldoutGroup("Margins")]
        [Tooltip("Extra inset from the left and bottom viewport edges (world units). " +
                 "Example: (1.5, 1) keeps the sub 1.5u from the left wall and 1u from the bottom.")]
        [SerializeField] private Vector2 extraMarginMin = new Vector2(1f, 1f);

        [FoldoutGroup("Margins")]
        [Tooltip("Extra inset from the right and top viewport edges (world units).")]
        [SerializeField] private Vector2 extraMarginMax = new Vector2(1f, 1f);

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private Rect LiveBounds => ComputeClampBounds();

        // =====================
        // Internals
        // =====================

        private Rigidbody2D _rb;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            // Resolve the Rigidbody2D from this object or the first parent that has one (supports being a child prefab).
            _rb = GetComponentInParent<Rigidbody2D>();

            // Try to auto-discover an AutoScrollCamera in the scene when none was wired up in the inspector.
            if (scrollCamera == null && autoFindScrollCamera)
                scrollCamera = FindFirstObjectByType<AutoScrollCamera>();

            if (fallbackCamera == null) fallbackCamera = Camera.main;
        }

        private void LateUpdate()
        {
            ClampToViewport();
        }

        // -------------------------------------------------------
        // Core
        // -------------------------------------------------------

        /**
         * Reads the current world-space viewport bounds and clamps the sub inside them.
         *
         * When a boundary is breached the position is snapped back and the velocity
         * component in the breached direction is zeroed — this prevents the sub from
         * "piling up" momentum against the edge.
         *
         * Example: sub exits right edge at x=12, xMax=10 → pos.x set to 10, vx set to 0.
         */
        private void ClampToViewport()
        {
            Rect bounds = ComputeClampBounds();
            if (bounds == Rect.zero) return;

            Vector2 pos = _rb.position;
            Vector2 vel = _rb.linearVelocity;
            bool clamped = false;

            // Horizontal clamping
            if (pos.x < bounds.xMin)
            {
                pos.x = bounds.xMin;
                if (vel.x < 0f) vel.x = 0f;
                clamped = true;
            }
            else if (pos.x > bounds.xMax)
            {
                pos.x = bounds.xMax;
                if (vel.x > 0f) vel.x = 0f;
                clamped = true;
            }

            // Vertical clamping
            if (pos.y < bounds.yMin)
            {
                pos.y = bounds.yMin;
                if (vel.y < 0f) vel.y = 0f;
                clamped = true;
            }
            else if (pos.y > bounds.yMax)
            {
                pos.y = bounds.yMax;
                if (vel.y > 0f) vel.y = 0f;
                clamped = true;
            }

            if (!clamped) return;

            _rb.position = pos;
            _rb.linearVelocity = vel;
        }

        /**
         * Builds the world-space clamping Rect this frame.
         *
         * Prefers AutoScrollCamera.GetWorldBounds() (which already bakes in the
         * camera's viewportPadding); extra per-edge margins are added on top.
         * Falls back to raw camera bounds when no AutoScrollCamera is assigned.
         */
        private Rect ComputeClampBounds()
        {
            Rect raw;

            if (scrollCamera != null)
            {
                raw = scrollCamera.GetWorldBounds();
            }
            else
            {
                Camera cam = fallbackCamera != null ? fallbackCamera : Camera.main;
                if (cam == null) return Rect.zero;

                float z = Mathf.Abs(cam.transform.position.z);
                Vector3 bl = cam.ScreenToWorldPoint(new Vector3(0f, 0f, z));
                Vector3 tr = cam.ScreenToWorldPoint(new Vector3(Screen.width, Screen.height, z));
                raw = Rect.MinMaxRect(bl.x, bl.y, tr.x, tr.y);
            }

            return Rect.MinMaxRect(
                raw.xMin + extraMarginMin.x,
                raw.yMin + extraMarginMin.y,
                raw.xMax - extraMarginMax.x,
                raw.yMax - extraMarginMax.y
            );
        }

        // -------------------------------------------------------
        // Editor
        // -------------------------------------------------------

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Rect b = ComputeClampBounds();
            if (b == Rect.zero) return;

            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.5f);
            Gizmos.DrawWireCube(
                new Vector3(b.center.x, b.center.y, 0f),
                new Vector3(b.width, b.height, 0f)
            );
        }
#endif
    }
}
