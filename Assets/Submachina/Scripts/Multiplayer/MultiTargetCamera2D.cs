using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Shared 2D camera for local multiplayer.
     *
     * Follows the centroid of every registered (active) player and zooms the
     * orthographic size so they all stay framed with a configurable margin.
     * Players register/unregister as they drop in and out, so the framing
     * adapts automatically — one player feels like a normal follow camera,
     * multiple players pull the view back to keep everyone on screen.
     *
     * Also exposes ClampIntoView, which LocalPlayerManager uses to drop a
     * joining player inside the current frame so they never spawn off-screen.
     *
     * Coexists with the single-target CameraFollow; use one or the other.
     * Runs in LateUpdate so it reads final positions after movement.
     */
    public class MultiTargetCamera2D : MonoBehaviour
    {
        // =====================
        // Camera
        // =====================

        [FoldoutGroup("Camera")]
        [InfoBox("Drives the assigned camera's transform directly, so this component can live on the camera OR on a separate manager object. Leave empty to auto-resolve at runtime: camera on this GameObject, else Camera.main (cached once found).")]
        [Tooltip("The orthographic camera to drive. Defaults to the camera on this GameObject, else Camera.main.")]
        [SerializeField] private Camera cam;

        // =====================
        // Follow
        // =====================

        [FoldoutGroup("Follow")]
        [Tooltip("How quickly the camera position catches up to the targets' centroid. Higher = snappier.")]
        [SerializeField, Min(0.1f)] private float positionSmoothSpeed = 4f;

        [FoldoutGroup("Follow")]
        [Tooltip("How quickly the orthographic size eases toward the size needed to frame all players.")]
        [SerializeField, Min(0.1f)] private float zoomSmoothSpeed = 3f;

        [FoldoutGroup("Follow")]
        [Tooltip("World-space offset applied to the centroid before following (e.g. (0,-2) shows more of what's below).")]
        [SerializeField] private Vector2 offset = new Vector2(0f, -2f);

        // =====================
        // Zoom Bounds
        // =====================

        [FoldoutGroup("Zoom")]
        [Tooltip("World-space padding added around the players when fitting the view. Larger = more breathing room.")]
        [SerializeField, Min(0f)] private float framingPadding = 4f;

        [FoldoutGroup("Zoom")]
        [Tooltip("Smallest orthographic size — the most zoomed-in the camera will get (single player / players close together).")]
        [SerializeField, Min(1f)] private float minSize = 6f;

        [FoldoutGroup("Zoom")]
        [Tooltip("Largest orthographic size — the most zoomed-out the camera will get when players spread far apart.")]
        [SerializeField, Min(1f)] private float maxSize = 16f;

        // =====================
        // Bounds (optional)
        // =====================

        [FoldoutGroup("Bounds")]
        [Tooltip("Clamp how far up the camera centre can travel (0 = ocean surface — never show above water).")]
        [SerializeField] private bool clampTop = true;

        [FoldoutGroup("Bounds"), ShowIf("clampTop")]
        [Tooltip("Maximum world Y the camera centre can reach.")]
        [SerializeField] private float topBoundY = 0f;

        // =====================
        // State
        // =====================

        private readonly List<Transform> _targets = new();

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private int TargetCount => _targets.Count;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            // Resolve the camera up front so framing works from the first frame
            EnsureCamera();
        }

        private void LateUpdate()
        {
            // Nothing live to frame (no players, or all dead/disabled) — hold the current view
            if (!HasLiveTarget()) return;

            // Make sure we have a camera (Camera.main may have appeared after Awake)
            EnsureCamera();

            // Ease the camera position toward the framed centre and size toward the fit zoom
            Vector3 desiredCentre = ResolveDesiredCentre();
            CameraTransform.position = Vector3.Lerp(CameraTransform.position, desiredCentre, Time.unscaledDeltaTime * positionSmoothSpeed);

            if (cam != null && cam.orthographic)
            {
                float desiredSize = ResolveDesiredSize();
                cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, desiredSize, Time.unscaledDeltaTime * zoomSmoothSpeed);
            }
        }

        /**
         * Resolves and caches the camera to drive when one isn't already assigned.
         * Resolution order: existing reference → camera on this GameObject → Camera.main.
         * Safe to call every frame — it only does work while 'cam' is still null, so
         * a main camera that spawns after Awake (e.g. scene load order) still gets picked up.
         */
        private void EnsureCamera()
        {
            if (cam != null) return;
            cam = GetComponent<Camera>();
            if (cam == null) cam = Camera.main;
        }

        /**
         * The transform this component actually drives. We move the CAMERA's transform,
         * not our own, so the component works whether it lives on the camera object or on
         * a separate manager object. Falls back to our own transform only if no camera is
         * resolved yet, so reads (e.g. ViewCentre) never null-ref.
         */
        private Transform CameraTransform => cam != null ? cam.transform : transform;

        // -------------------------------------------------------
        // Registration
        // -------------------------------------------------------

        /** Adds a player transform to the framed set (called by LocalPlayerManager on join). */
        public void Register(Transform target)
        {
            if (target == null || _targets.Contains(target)) return;
            _targets.Add(target);
        }

        /** Removes a player transform from the framed set (called on drop-out). */
        public void Unregister(Transform target)
        {
            _targets.Remove(target);
        }

        // -------------------------------------------------------
        // Framing helpers
        // -------------------------------------------------------

        /**
         * Snaps the camera instantly to the current targets with no smoothing.
         * Useful right after a join so the new player is framed immediately.
         */
        public void SnapToTargets()
        {
            if (!HasLiveTarget()) return;

            CameraTransform.position = ResolveDesiredCentre();
            if (cam != null && cam.orthographic)
                cam.orthographicSize = ResolveDesiredSize();
        }

        /**
         * Clamps a world position to lie inside the current camera view, inset by
         * 'edgePadding'. Used to place a joining player within the visible frame
         * rather than letting them spawn off-screen.
         *
         * Example: desired = activePlayer + (3,0); if that lands past the right
         * edge it is pulled back so the sub appears just inside the frame.
         */
        public Vector3 ClampIntoView(Vector3 worldPosition, float edgePadding = 1.5f)
        {
            if (cam == null || !cam.orthographic) return worldPosition;

            // Half-extents of the current view, inset by the padding on each axis
            float halfHeight = Mathf.Max(0f, cam.orthographicSize - edgePadding);
            float halfWidth = Mathf.Max(0f, cam.orthographicSize * cam.aspect - edgePadding);

            Vector3 c = CameraTransform.position;
            worldPosition.x = Mathf.Clamp(worldPosition.x, c.x - halfWidth, c.x + halfWidth);
            worldPosition.y = Mathf.Clamp(worldPosition.y, c.y - halfHeight, c.y + halfHeight);
            return worldPosition;
        }

        /** The current world-space centre of the view — a sensible default spawn point with no active players. */
        public Vector3 ViewCentre => CameraTransform.position;

        // -------------------------------------------------------
        // Internals
        // -------------------------------------------------------

        /** Computes the smoothed-toward centre: average of all targets + offset, clamped to the top bound. */
        private Vector3 ResolveDesiredCentre()
        {
            // Average all live target positions (skip any destroyed or deactivated — e.g. a dead player)
            Vector3 sum = Vector3.zero;
            int count = 0;
            for (int i = 0; i < _targets.Count; i++)
            {
                if (!IsLive(_targets[i])) continue;
                sum += _targets[i].position;
                count++;
            }

            Vector3 centre = count > 0 ? sum / count : CameraTransform.position;

            // Apply offset and keep the existing Z (2D camera depth stays fixed)
            float x = centre.x + offset.x;
            float y = centre.y + offset.y;
            if (clampTop) y = Mathf.Min(y, topBoundY);

            return new Vector3(x, y, CameraTransform.position.z);
        }

        /**
         * Computes the orthographic size needed to frame every target with padding.
         * Takes the max horizontal/vertical spread from the centre, converts the
         * horizontal need through the aspect ratio, and clamps to [minSize, maxSize].
         */
        private float ResolveDesiredSize()
        {
            Vector3 centre = ResolveDesiredCentre();

            // Largest distance from centre on each axis across all live targets
            float maxDx = 0f;
            float maxDy = 0f;
            for (int i = 0; i < _targets.Count; i++)
            {
                if (!IsLive(_targets[i])) continue;
                maxDx = Mathf.Max(maxDx, Mathf.Abs(_targets[i].position.x - centre.x));
                maxDy = Mathf.Max(maxDy, Mathf.Abs(_targets[i].position.y - centre.y));
            }

            // Vertical need is direct; horizontal need is divided by aspect to become a size
            float aspect = cam != null ? Mathf.Max(0.0001f, cam.aspect) : 1.7778f;
            float sizeForHeight = maxDy + framingPadding;
            float sizeForWidth = (maxDx + framingPadding) / aspect;

            return Mathf.Clamp(Mathf.Max(sizeForHeight, sizeForWidth), minSize, maxSize);
        }

        /**
         * A target counts toward framing only while it's alive: not destroyed and its
         * GameObject active in the hierarchy. A dead player whose root is deactivated is
         * skipped, so the view stops being pulled toward their frozen last position —
         * regardless of whether the manager has formally unregistered them yet.
         */
        private static bool IsLive(Transform target)
        {
            return target != null && target.gameObject.activeInHierarchy;
        }

        /** True when at least one registered target is currently live (drives the LateUpdate/Snap guards). */
        private bool HasLiveTarget()
        {
            for (int i = 0; i < _targets.Count; i++)
                if (IsLive(_targets[i])) return true;
            return false;
        }
    }
}
