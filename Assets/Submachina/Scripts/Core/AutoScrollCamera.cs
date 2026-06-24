using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Scrolls the camera downward at a configurable speed, independent of any
     * target transform. Designed for forced-scroll prototype mode where the camera
     * defines the descent pace and the player maneuvers freely within the window.
     *
     * Setup:
     *   - Attach to the Main Camera.
     *   - Disable CameraFollow on the same object.
     *   - Add ViewportClamp to the submarine root and assign this component.
     *
     * Pairs with ViewportClamp, which reads GetWorldBounds() to constrain the sub.
     */
    public class AutoScrollCamera : MonoBehaviour
    {
        // =====================
        // Scroll Settings
        // =====================

        [FoldoutGroup("Scroll")]
        [Tooltip("Downward scroll speed in world units per second at full throttle.")]
        [SerializeField, Min(0f)] private float scrollSpeed = 3f;

        [FoldoutGroup("Scroll")]
        [Tooltip("Seconds before scrolling begins — gives the player a moment to orient.")]
        [SerializeField, Min(0f)] private float startDelay = 1f;

        [FoldoutGroup("Scroll")]
        [Tooltip("Seconds to ramp from zero to full scroll speed after the delay. 0 = instant.")]
        [SerializeField, Min(0f)] private float rampUpTime = 2f;

        // =====================
        // Viewport
        // =====================

        [FoldoutGroup("Viewport")]
        [Tooltip("Override the camera's orthographic size on start. Larger = wider view / more zoomed out. " +
                 "Example: default is typically 5; try 7–10 for a wider playfield.")]
        [SerializeField] private bool overrideOrthographicSize = true;

        [FoldoutGroup("Viewport"), ShowIf("overrideOrthographicSize")]
        [Tooltip("Target orthographic size. Applied immediately on Awake.")]
        [SerializeField, Min(1f)] private float orthographicSize = 8f;

        [FoldoutGroup("Viewport")]
        [Tooltip("World-space inset from each camera edge applied to GetWorldBounds(). " +
                 "ViewportClamp adds its own per-edge margins on top of this.")]
        [SerializeField, Min(0f)] private float viewportPadding = 0.5f;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float LiveSpeed => _elapsed > startDelay
            ? scrollSpeed * Mathf.Clamp01(rampUpTime > 0f ? (_elapsed - startDelay) / rampUpTime : 1f)
            : 0f;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float TotalScrolled => _totalScrolled;

        // =====================
        // Internals
        // =====================

        private Camera _cam;
        private float _elapsed;
        private float _totalScrolled;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            if (_cam == null) _cam = Camera.main;

            if (overrideOrthographicSize && _cam != null)
                _cam.orthographicSize = orthographicSize;
        }

        private void LateUpdate()
        {
            _elapsed += Time.deltaTime;

            // Wait for the start delay before scrolling begins
            if (_elapsed < startDelay) return;

            // Ramp from zero to full speed over rampUpTime
            // Example: rampUpTime=2, elapsed=1s past delay → 50% speed
            float t = rampUpTime > 0f ? Mathf.Clamp01((_elapsed - startDelay) / rampUpTime) : 1f;
            float delta = scrollSpeed * t * Time.deltaTime;

            transform.position += Vector3.down * delta;
            _totalScrolled += delta;
        }

        // -------------------------------------------------------
        // Public API
        // -------------------------------------------------------

        /**
         * Returns the camera's visible world-space area as a Rect, shrunk by
         * viewportPadding on all sides. Read by ViewportClamp each frame.
         *
         * For an orthographic 2D camera, ScreenToWorldPoint only needs a z
         * value for the camera-to-plane distance; using the camera's world Z
         * magnitude covers the standard setup where the camera sits at Z=-10.
         */
        public Rect GetWorldBounds()
        {
            if (_cam == null) return Rect.zero;

            // Distance from camera plane to the world's Z=0 plane
            float z = Mathf.Abs(_cam.transform.position.z);

            Vector3 bottomLeft = _cam.ScreenToWorldPoint(new Vector3(0f, 0f, z));
            Vector3 topRight   = _cam.ScreenToWorldPoint(new Vector3(Screen.width, Screen.height, z));

            return Rect.MinMaxRect(
                bottomLeft.x + viewportPadding,
                bottomLeft.y + viewportPadding,
                topRight.x   - viewportPadding,
                topRight.y   - viewportPadding
            );
        }

        // -------------------------------------------------------
        // Editor
        // -------------------------------------------------------

#if UNITY_EDITOR
        [FoldoutGroup("Debug")]
        [Button("Reset Scroll"), GUIColor(1f, 0.5f, 0.2f)]
        private void DebugReset()
        {
            _elapsed = 0f;
            _totalScrolled = 0f;
        }

        private void OnDrawGizmosSelected()
        {
            Camera cam = _cam != null ? _cam : Camera.main;
            if (cam == null) return;

            float z = Mathf.Abs(cam.transform.position.z);
            Vector3 bl = cam.ScreenToWorldPoint(new Vector3(0f, 0f, z));
            Vector3 tr = cam.ScreenToWorldPoint(new Vector3(Screen.width, Screen.height, z));

            // Draw padded viewport rect in cyan
            float p = viewportPadding;
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.4f);
            Gizmos.DrawWireCube(
                new Vector3((bl.x + tr.x) * 0.5f, (bl.y + tr.y) * 0.5f, 0f),
                new Vector3(tr.x - bl.x - p * 2f, tr.y - bl.y - p * 2f, 0f)
            );
        }
#endif
    }
}
