using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Marks a world object as detectable by submarine sonar.
     *
     * Drop this on any entity prefab (fish, octopus, resource, scrap, O2, rock)
     * and assign a SonarSignature. The SonarSystem discovers these via an
     * OverlapCircle scan when a ping is emitted — exactly the detection idiom
     * PickupRangeDetector uses for pickups — and reads the signature to build a
     * contact. No entity base class is involved, so wildly different objects can
     * all be made detectable just by adding this one component.
     *
     * The reflect point defaults to this transform but can be overridden when the
     * visual centre differs from the pivot (e.g. a long creature whose origin sits
     * at its head).
     */
    public class SonarTarget : MonoBehaviour
    {
        [Required]
        [Tooltip("Identity of this object as it appears in a returning sonar wave.")]
        [SerializeField] private SonarSignature signature;

        [Tooltip("Optional point the ping reflects from. Leave empty to use this transform.")]
        [SerializeField] private Transform reflectionPoint;

        /** The sonic signature describing how this object reads on sonar. */
        public SonarSignature Signature => signature;

        /** World-space point a ping reflects from (visual centre of the contact). */
        public Vector2 ReflectionOrigin =>
            reflectionPoint != null ? (Vector2)reflectionPoint.position : (Vector2)transform.position;

        /**
         * The furthest distance at which this object still reflects a ping, given a
         * sonar's base range. Larger, denser objects reflect from further out:
         *   maxReflect = baseRange × sizeFactor × reflectionStrength
         * Example: a Huge rock (1.6 × 1.0) reflects from 60% beyond the base range,
         * while a Tiny soft creature (0.4 × 0.8) only shows up at ~third the range.
         */
        public float MaxReflectRange(float baseRange)
        {
            if (signature == null) return 0f;
            return baseRange * signature.SizeRangeFactor * signature.reflectionStrength;
        }

        // -------------------------------------------------------
        // Gizmos
        // -------------------------------------------------------

        /** Draws the reflect origin tinted by the signature color for quick scene-view ID. */
        private void OnDrawGizmosSelected()
        {
            if (signature == null) return;
            Gizmos.color = signature.blipColor;
            Gizmos.DrawWireSphere(ReflectionOrigin, 0.3f);
        }
    }
}
