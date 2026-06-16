using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * A semantic mount point on the submarine's visual hierarchy.
     *
     * Place this on any child GameObject (the nose, the tail, a turret muzzle)
     * and assign its AnchorId key. The component self-registers with the sub's
     * SubmarineAnchorRouter so anyone — gameplay modules or feedback prefabs —
     * can resolve the live Transform by key via Sub.Anchors.Get(key), without
     * holding a hard reference across prefab boundaries.
     *
     * Self-registers in OnEnable / unregisters in OnDisable (the same lifecycle
     * the pumps use), so module swaps keep the registry current.
     *
     * Setup:
     *   1. Add to a child transform where an effect should originate.
     *   2. Pick its key from the AnchorId dropdown (e.g. SubAnchors.Muzzle).
     */
    public class SubmarineAnchor : SubmarineComponent
    {
        [Tooltip("Semantic key other systems use to resolve this transform via Sub.Anchors.")] [SerializeField]
        private AnchorId anchor;

        /** The semantic key this anchor is registered under. */
        public AnchorId Key => anchor;

        /** The live transform this anchor marks — what callers ultimately want. */
        public Transform Point => transform;

        // =====================
        // Lifecycle — self-register with the router like pumps do
        // =====================

        /** Join the sub's anchor registry once the sub (and its router) exist. */
        protected virtual void OnEnable()
        {
            // Sub is resolved in base.Awake, which runs before OnEnable for
            // scene-loaded objects, so Sub.Anchors is populated by now.
            Sub?.Anchors?.Register(this);
        }

        /** Leave the registry when disabled or swapped out. */
        protected virtual void OnDisable()
        {
            Sub?.Anchors?.Unregister(this);
        }

        // =====================
        // Editor Gizmo
        // =====================

        /** Draws a small marker + label so anchors are visible in the Scene view. */
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.30f, 0.75f, 0.95f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.15f);
            Gizmos.DrawLine(transform.position, transform.position + transform.right * 0.3f);

#if UNITY_EDITOR
            UnityEditor.Handles.Label(transform.position + Vector3.up * 0.2f, anchor.ToString());
#endif
        }
    }
}