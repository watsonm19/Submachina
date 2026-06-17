using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Mounts the GameObject it lives on onto a semantic submarine anchor at runtime.
     *
     * Generic counterpart to FeedbackAnchorBinder: where that re-points a feedback's
     * particle spawn, this simply re-parents this whole GameObject to the anchor's
     * transform. It doesn't care what the object is — a light, a sprite, a trail, a
     * child rig — because all it does is parent and (optionally) snap into place.
     *
     * This is what lets a self-contained prefab ship without a hard reference into
     * the sub hierarchy: it names a mount point by key and relocates there on Start.
     * To move the object to the tail instead, change the anchor key — nothing else.
     *
     * Setup:
     *   1. Add to the GameObject you want mounted (e.g. the dash light).
     *   2. Pick the anchor key and whether to snap onto the anchor's position.
     */
    public class AnchorMount : MonoBehaviour
    {
        [Tooltip("Semantic anchor this object mounts onto.")]
        [SerializeField] private AnchorId anchor;

        [Tooltip("Snap onto the anchor's position when mounting. " +
                 "Off = keep current world position and only re-parent.")]
        [SerializeField] private bool snapToAnchor = true;

        [ShowIf(nameof(snapToAnchor))]
        [Tooltip("Also match the anchor's rotation when snapping.")]
        [SerializeField] private bool matchRotation;

        [ShowIf(nameof(snapToAnchor))]
        [Tooltip("Local offset applied after snapping, in the anchor's local space.")]
        [SerializeField] private Vector3 localOffset;

        [Title("Scale")]
        [Tooltip("Keep this object's world size after mounting, even if the anchor's " +
                 "parent chain is scaled. Captures the current lossy scale before " +
                 "re-parenting and counter-scales localScale to preserve it.")]
        [SerializeField] private bool preserveWorldScale;

        [DisableIf(nameof(preserveWorldScale))]
        [Tooltip("Apply an explicit local scale after mounting. Off = leave localScale " +
                 "untouched. Ignored when 'Preserve World Scale' is on.")]
        [SerializeField] private bool overrideLocalScale;

        [ShowIf(nameof(overrideLocalScale))]
        [Tooltip("Local scale to apply after mounting.")]
        [SerializeField] private Vector3 localScaleOverride = Vector3.one;

        // =====================
        // Lifecycle
        // =====================

        /** Resolve and mount once everything (including anchors) has registered. */
        private void Start()
        {
            Mount();
        }

        // =====================
        // Mounting
        // =====================

        /**
         * Resolves the anchor transform and re-parents this object onto it.
         * Safe no-op if the sub or the anchor key can't be resolved.
         */
        [Button]
        public void Mount()
        {
            // Find the owning sub and the anchor transform for our key.
            // TryGet (not Get) so a missing key is a clean no-op rather than
            // silently mounting onto the sub root.
            var sub = GetComponentInParent<Submarine>();
            if (sub?.Anchors == null || !sub.Anchors.TryGet(anchor, out var point)) return;

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

        /** Guards scale division against a zero parent axis by falling back to 1. */
        private static float SafeDivisor(float value) => Mathf.Approximately(value, 0f) ? 1f : value;
    }
}
