using UnityEngine;
using MoreMountains.Feedbacks;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Binds a feedback's particle spawn to a semantic submarine anchor at runtime.
     *
     * Lives on a self-contained feedback prefab (under the sub, but not a
     * SubmarineComponent itself). On Start it resolves its AnchorId via the sub's
     * SubmarineAnchorRouter and rewrites the MMF_Player's MMF_ParticlesInstantiation
     * so the effect spawns from — or follows — that anchor's transform.
     *
     * This is what lets feedbacks ship as their own prefabs: instead of holding a
     * hard reference into the sub hierarchy, the prefab names a location by key.
     * To fire the same feedback from the tail instead of the muzzle, swap the
     * prefab or just change the anchor key here — gameplay code never changes.
     *
     * Setup:
     *   1. Add to the feedback GameObject next to its MMF_Player.
     *   2. Assign that MMF_Player and pick the anchor key + bind mode.
     */
    public class FeedbackAnchorBinder : MonoBehaviour
    {
        public enum BindMode
        {
            /** Particles parent to the anchor and follow the sub as it moves/rotates. */
            Attach,

            /** Particles spawn at the anchor's position once, unparented. */
            PositionOnly,
        }

        [Tooltip("Semantic anchor this feedback's particles spawn from.")] [SerializeField]
        private AnchorId anchor;

        [Tooltip("Attach = particles follow the anchor; PositionOnly = one-shot at the anchor.")] [SerializeField]
        private BindMode mode = BindMode.Attach;

        [Required, Tooltip("The MMF_Player whose particle feedback gets re-pointed.")] [SerializeField]
        private MMF_Player player;

        // =====================
        // Lifecycle
        // =====================

        /** Resolve and wire once everything (including anchors) has registered. */
        private void Start()
        {
            Bind();
        }

        // =====================
        // Binding
        // =====================

        /**
         * Resolves the anchor transform and writes it into the player's
         * MMF_ParticlesInstantiation. Safe no-op if anything is missing.
         */
        [Button]
        public void Bind()
        {
            if (player == null) return;

            // Find the owning sub and the anchor transform for our key.
            var sub = GetComponentInParent<Submarine>();
            Transform point = sub?.Anchors != null ? sub.Anchors.Get(anchor) : null;
            if (point == null) return;

            // Grab the particle feedback we're re-pointing; nothing to do without one.
            var particles = player.GetFeedbackOfType<MMF_ParticlesInstantiation>();
            if (particles == null) return;

            // Always drive position from the anchor transform.
            particles.PositionMode = MMF_ParticlesInstantiation.PositionModes.Transform;
            particles.InstantiateParticlesPosition = point;

            // Attach => parent + nest so the effect follows the sub;
            // PositionOnly => spawn at the anchor but leave it unparented.
            if (mode == BindMode.Attach)
            {
                particles.ParentTransform = point;
                particles.NestParticles = true;
            }
            else
            {
                particles.ParentTransform = null;
                particles.NestParticles = false;
            }
        }
    }
}