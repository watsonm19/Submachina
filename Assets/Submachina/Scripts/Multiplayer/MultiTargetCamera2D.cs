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
        [InfoBox("When a LevelBounds is assigned (or found in the scene), the FULL view rect is clamped " +
                 "inside it — including zoom, so a narrow level can never be zoomed out past its edges. " +
                 "The legacy clampTop fields below only apply when no LevelBounds is available.")]
        [Tooltip("The level's authoritative extents. Auto-resolved from the scene at Awake when left empty.")]
        [SerializeField] private LevelBounds levelBounds;

        [FoldoutGroup("Bounds")]
        [Tooltip("LEGACY (superseded by LevelBounds): clamp how far up the camera centre can travel.")]
        [SerializeField] private bool clampTop = true;

        [FoldoutGroup("Bounds"), ShowIf("clampTop")]
        [Tooltip("LEGACY (superseded by LevelBounds): maximum world Y the camera centre can reach.")]
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

            // Auto-resolve level bounds when not wired in the inspector
            if (levelBounds == null) levelBounds = LevelBounds.Find();
        }

        private void LateUpdate()
        {
            // Nothing live to frame (no players, or all dead/disabled) — hold the current view
            if (!HasLiveTarget()) return;

            // Make sure we have a camera (Camera.main may have appeared after Awake)
            EnsureCamera();

            // Ease the camera position toward the framed centre and size toward the fit zoom
            ResolveDesiredView(out Vector3 desiredCentre, out float desiredSize);
            CameraTransform.position = Vector3.Lerp(CameraTransform.position, desiredCentre, Time.unscaledDeltaTime * positionSmoothSpeed);

            if (cam != null && cam.orthographic)
                cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, desiredSize, Time.unscaledDeltaTime * zoomSmoothSpeed);

            // Final hard clamp with the ACTUAL current size: the lerped position and lerped
            // size are momentarily inconsistent, and this guarantees no transient frame ever
            // reveals past a bounded edge (e.g. past the parallax backdrop).
            if (levelBounds != null && cam != null && cam.orthographic)
                CameraTransform.position = levelBounds.ClampCameraCentre(
                    CameraTransform.position, cam.orthographicSize, Aspect);
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

            ResolveDesiredView(out Vector3 centre, out float size);
            CameraTransform.position = centre;
            if (cam != null && cam.orthographic)
                cam.orthographicSize = size;

            // Reposition parallax layers immediately so the snap doesn't show one frame
            // of pre-snap parallax (layers normally update in LateUpdate order 100)
            FindFirstObjectByType<ParallaxController>()?.ForceUpdate();
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

        /** Live camera aspect with a sensible 16:9 fallback while no camera is resolved. */
        private float Aspect => cam != null ? Mathf.Max(0.0001f, cam.aspect) : 1.7778f;

        /**
         * Resolves the target view in dependency order: raw centroid → framing size
         * (clamped so a narrow level can't be zoomed out past its own edges) → centre
         * clamped so the whole view rect stays inside LevelBounds. Size must be known
         * before the centre can be rect-clamped, which is why this is one pass.
         */
        private void ResolveDesiredView(out Vector3 centre, out float size)
        {
            Vector3 raw = ResolveCentroid();
            size = ResolveSizeFor(raw);

            if (levelBounds != null)
            {
                // Zoom can never exceed what the bounded axes allow, then rect-clamp the centre
                size = Mathf.Min(size, levelBounds.MaxOrthoSize(Aspect));
                centre = levelBounds.ClampCameraCentre(raw, size, Aspect);
                centre.z = CameraTransform.position.z;
                return;
            }

            // Legacy fallback: top-only clamp when no LevelBounds exists in the scene
            if (clampTop) raw.y = Mathf.Min(raw.y, topBoundY);
            centre = raw;
        }

        /** Average of all live targets + offset, unclamped (bounds are applied by ResolveDesiredView). */
        private Vector3 ResolveCentroid()
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
            return new Vector3(centre.x + offset.x, centre.y + offset.y, CameraTransform.position.z);
        }

        /**
         * Computes the orthographic size needed to frame every target with padding.
         * Takes the max horizontal/vertical spread from the centre, converts the
         * horizontal need through the aspect ratio, and clamps to [minSize, maxSize].
         *
         * Note: in a bounded level with physical walls, player spread can exceed what
         * the bounds-clamped zoom is able to frame — players at extremes may go
         * off-screen. Pre-existing wide-spread behaviour; revisit only if it bites.
         */
        private float ResolveSizeFor(Vector3 centre)
        {
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
            float sizeForHeight = maxDy + framingPadding;
            float sizeForWidth = (maxDx + framingPadding) / Aspect;

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
