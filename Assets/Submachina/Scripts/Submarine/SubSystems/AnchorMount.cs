using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Binds the GameObject it lives on to a semantic submarine anchor at runtime.
     *
     * Generic counterpart to FeedbackAnchorBinder: where that re-points a feedback's
     * particle spawn, this relocates this whole GameObject onto the anchor's
     * transform. It doesn't care what the object is — a light, a sprite, a trail, a
     * child rig, or a UI element — because all it does is position (and optionally
     * parent) it.
     *
     * Mount modes:
     *   - Reparent: re-parents this object under the anchor so it inherits the
     *     anchor's motion. Best when the object should ride with the sub.
     *   - MatchWorldPosition: leaves the parent untouched and only matches the
     *     anchor's world position. Best when the object must visually sit on the
     *     anchor but stay owned by its current hierarchy (e.g. to avoid inheriting
     *     sub motion twice, or to keep an existing parent's lifecycle/sorting).
     *   - MatchWorldOnCanvas: for UI elements. A RectTransform's position is in
     *     canvas/screen space, not world space, so a raw world position never lines
     *     up. This projects the anchor's world position through a camera into the
     *     canvas, centering the UI element over the world point.
     *
     * This is what lets a self-contained prefab ship without a hard reference into
     * the sub hierarchy: it names a mount point by key and relocates there on Start.
     * To move the object to the tail instead, change the anchor key — nothing else.
     *
     * Setup:
     *   1. Add to the GameObject you want mounted (e.g. the dash light or a UI tag).
     *   2. Pick the anchor key and the mount mode.
     */
    public class AnchorMount : MonoBehaviour
    {
        /** How this object attaches to its anchor. */
        public enum MountMode
        {
            // Re-parent this object under the anchor transform.
            Reparent,

            // Keep the current parent; only match the anchor's world position.
            MatchWorldPosition,

            // UI element: project the anchor's world position into this object's canvas.
            MatchWorldOnCanvas
        }

        [Tooltip("Semantic anchor this object mounts onto.")]
        [SerializeField] private AnchorId anchor;

        [Tooltip("Submarine that owns the anchor. Leave unset to find one in this " +
                 "object's parents. UI elements live in a separate canvas (outside " +
                 "the sub hierarchy), so for canvas mode this must be assigned in the " +
                 "inspector or wired at runtime via SetSource().")]
        [SerializeField] private Submarine targetSub;

        [EnumToggleButtons]
        [Tooltip("Reparent: re-parent under the anchor (rides with it). " +
                 "MatchWorldPosition: keep current parent, only match world position. " +
                 "MatchWorldOnCanvas: project the world position into a UI canvas.")]
        [SerializeField] private MountMode mountMode = MountMode.Reparent;

        [ShowIf(nameof(mountMode), MountMode.Reparent)]
        [Tooltip("Snap onto the anchor's position when mounting. " +
                 "Off = keep current world position and only re-parent.")]
        [SerializeField] private bool snapToAnchor = true;

        [ShowIf(nameof(WillSnap))]
        [Tooltip("Also match the anchor's rotation when snapping.")]
        [SerializeField] private bool matchRotation;

        [ShowIf(nameof(ShowOffset))]
        [Tooltip("Position offset. World modes: applied in the anchor's local space. " +
                 "Canvas mode: a screen-pixel (x, y) nudge after projection.")]
        [SerializeField] private Vector3 localOffset;

        [ShowIf(nameof(IsWorldMatch))]
        [Tooltip("Re-match the anchor every LateUpdate so this object tracks a moving " +
                 "anchor without being parented to it. Off = match once when mounting.")]
        [SerializeField] private bool continuouslyFollow;

        [Title("Canvas")]
        [ShowIf(nameof(mountMode), MountMode.MatchWorldOnCanvas)]
        [Tooltip("Camera that views the world anchor, used to project it into screen " +
                 "space. Defaults to Camera.main when left unset.")]
        [SerializeField] private Camera worldCamera;

        [Title("Scale")]
        [ShowIf(nameof(mountMode), MountMode.Reparent)]
        [Tooltip("Keep this object's world size after mounting, even if the anchor's " +
                 "parent chain is scaled. Captures the current lossy scale before " +
                 "re-parenting and counter-scales localScale to preserve it.")]
        [SerializeField] private bool preserveWorldScale;

        [DisableIf(nameof(preserveWorldScale))]
        [HideIf(nameof(mountMode), MountMode.MatchWorldOnCanvas)]
        [Tooltip("Apply an explicit local scale after mounting. Off = leave localScale " +
                 "untouched. Ignored when 'Preserve World Scale' is on.")]
        [SerializeField] private bool overrideLocalScale;

        [ShowIf(nameof(overrideLocalScale))]
        [Tooltip("Local scale to apply after mounting.")]
        [SerializeField] private Vector3 localScaleOverride = Vector3.one;

        // Cached UI pieces for canvas mode: the RectTransform we drive and its Canvas.
        private RectTransform _rect;
        private Canvas _canvas;

        // --- Inspector visibility helpers ---

        // True for the modes that match the anchor each mount (and optionally follow).
        private bool IsWorldMatch =>
            mountMode == MountMode.MatchWorldPosition || mountMode == MountMode.MatchWorldOnCanvas;

        // True whenever the active mode will snap to the anchor's transform (world modes).
        private bool WillSnap =>
            mountMode == MountMode.MatchWorldPosition ||
            (mountMode == MountMode.Reparent && snapToAnchor);

        // The offset field is used by any snapping world mode and by canvas mode.
        private bool ShowOffset => WillSnap || mountMode == MountMode.MatchWorldOnCanvas;

        // =====================
        // Lifecycle
        // =====================

        /** Resolve and mount once everything (including anchors) has registered. */
        private void Start()
        {
            Mount();
        }

        /** Track a moving anchor when following without a parent link. */
        private void LateUpdate()
        {
            // Only the un-parented match modes need per-frame updates; reparented
            // objects ride the anchor automatically.
            if (!IsWorldMatch || !continuouslyFollow) return;
            if (TryResolveAnchor(out var point)) MatchPoint(point);
        }

        // =====================
        // Mounting
        // =====================

        /**
         * Resolves the anchor transform and attaches this object using the active
         * mount mode. Safe no-op if the sub or the anchor key can't be resolved.
         */
        [Button]
        public void Mount()
        {
            // Resolve the anchor; a missing key is a clean no-op.
            if (!TryResolveAnchor(out var point)) return;

            // Reparenting changes the hierarchy; the match modes only move us.
            if (mountMode == MountMode.Reparent)
            {
                Reparent(point);
                return;
            }

            MatchPoint(point);
        }

        /** Dispatches to the right "match without reparenting" routine for the mode. */
        private void MatchPoint(Transform point)
        {
            if (mountMode == MountMode.MatchWorldOnCanvas) MatchWorldOnCanvas(point);
            else MatchWorld(point);
        }

        /**
         * Re-parents this object onto the anchor, snapping and resolving scale per
         * the configured options.
         */
        private void Reparent(Transform point)
        {
            // Capture the world (lossy) size before re-parenting, so we can restore
            // it afterwards if the anchor's parent chain is scaled.
            var worldScale = transform.lossyScale;

            // Re-parent under the anchor. Keep world position when not snapping so
            // the object stays put visually and only its parent changes.
            transform.SetParent(point, worldPositionStays: !snapToAnchor);

            // Snap => drive the local transform from the anchor origin plus offset.
            if (snapToAnchor)
            {
                transform.localPosition = localOffset;
                if (matchRotation) transform.localRotation = Quaternion.identity;
            }

            // Resolve scale after parenting (mutually exclusive options).
            if (preserveWorldScale)
            {
                // Counter-scale against the new parent so lossyScale matches the
                // pre-mount size. E.g. parent lossy 2 + desired world 1 => local 0.5.
                // Components are clamped against a divide-by-zero on a flat parent.
                var parentLossy = point.lossyScale;
                transform.localScale = new Vector3(
                    worldScale.x / SafeDivisor(parentLossy.x),
                    worldScale.y / SafeDivisor(parentLossy.y),
                    worldScale.z / SafeDivisor(parentLossy.z));
            }
            else if (overrideLocalScale)
            {
                // Explicit local scale, inheriting any parent scaling as normal.
                transform.localScale = localScaleOverride;
            }
        }

        /**
         * Moves this object to the anchor's world position without changing its
         * parent. The local offset is expressed in the anchor's space so it rotates
         * with the anchor (e.g. "0.5 up" stays up relative to the mount point).
         */
        private void MatchWorld(Transform point)
        {
            // Match world rotation first so the offset lands in the final orientation.
            if (matchRotation) transform.rotation = point.rotation;

            // World position = anchor origin + offset rotated into anchor space.
            transform.position = point.position + point.rotation * localOffset;

            // Optional explicit local scale (no parent change => no world-scale math).
            if (overrideLocalScale) transform.localScale = localScaleOverride;
        }

        /**
         * Centers this UI element over the anchor's world position by projecting
         * that world point through a camera into screen space, then placing the
         * element at the matching point inside its canvas.
         *
         * A RectTransform's position lives in canvas/screen space, so assigning a
         * raw world position never lines up. Handles both:
         *   - Screen Space - Overlay: canvas coords ARE screen pixels, so the
         *     screen point can be assigned to the RectTransform directly.
         *   - Screen Space - Camera / World Space: the screen point must be mapped
         *     back into the canvas plane via the canvas camera.
         */
        private void MatchWorldOnCanvas(Transform point)
        {
            // Camera that views the world anchor (defaults to the main camera).
            var cam = worldCamera != null ? worldCamera : Camera.main;
            if (cam == null) return;

            // Cache the UI pieces we drive (RectTransform) and read (owning Canvas).
            if (_rect == null) _rect = transform as RectTransform;
            if (_rect == null) return; // not a UI object — nothing to position
            if (_canvas == null) _canvas = _rect.GetComponentInParent<Canvas>();

            // Project the world anchor into screen pixels, nudged by the screen offset.
            var screenPoint = cam.WorldToScreenPoint(point.position);
            screenPoint.x += localOffset.x;
            screenPoint.y += localOffset.y;

            // Overlay canvases use screen pixels as their own coordinate space, so
            // the screen point can be assigned to the RectTransform position directly.
            if (_canvas == null || _canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                _rect.position = screenPoint;
                return;
            }

            // Screen Space - Camera / World Space: map the screen point into the
            // parent rectangle's local space using the canvas camera, then place it.
            var parentRect = _rect.parent as RectTransform;
            if (parentRect != null &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parentRect, screenPoint, _canvas.worldCamera, out var local))
            {
                _rect.localPosition = local;
            }
        }

        /**
         * Resolves the owning sub and the anchor transform for our key.
         * TryGet (not Get) so a missing key fails cleanly rather than silently
         * resolving onto the sub root.
         */
        private bool TryResolveAnchor(out Transform point)
        {
            point = null;

            // Prefer an explicitly assigned sub (required when this object lives
            // outside the sub hierarchy, e.g. UI in its own canvas); otherwise walk
            // up our parents to find one.
            var sub = targetSub != null ? targetSub : GetComponentInParent<Submarine>();
            return sub?.Anchors != null && sub.Anchors.TryGet(anchor, out point);
        }

        /**
         * Wires the owning sub at runtime and (re)mounts onto it. Spawn/binding code
         * uses this to point a UI tag at the specific sub it belongs to.
         */
        public void SetSource(Submarine sub)
        {
            targetSub = sub;
            Mount();
        }

        /** Guards scale division against a zero parent axis by falling back to 1. */
        private static float SafeDivisor(float value) => Mathf.Approximately(value, 0f) ? 1f : value;
    }
}
