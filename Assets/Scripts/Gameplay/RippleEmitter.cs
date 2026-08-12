using UnityEngine;
using Sirenix.OdinInspector;
using Core.Rendering;

namespace Gameplay
{
    /**
     * Emits an underwater-distortion ripple at this object's position on demand.
     *
     * Unlike SpeedRippleEmitter (which watches movement and fires automatically),
     * this is a manual trigger: call Emit() from any gameplay code, a UnityEvent,
     * or the Odin test button below. Each emit fires a ripple through
     * DistortionRippleBus with a strength randomized between Min Strength and
     * Max Strength — useful for impacts, pickups, pops, bursts, etc.
     */
    public class RippleEmitter : MonoBehaviour
    {
        // ─── Ripple parameters ───────────────────────────────────────────────
        [TitleGroup("Ripple", "Strength is randomized between Min and Max on each emit.")]
        [Tooltip("Lower bound of the randomized ripple amplitude.")]
        [Range(0f, 0.2f)]
        public float minStrength = 0.02f;

        [TitleGroup("Ripple")]
        [Tooltip("Upper bound of the randomized ripple amplitude.")]
        [Range(0f, 0.2f)]
        public float maxStrength = 0.07f;

        [TitleGroup("Ripple")]
        [Tooltip("Wave cycles packed into the ring.")]
        [Range(1f, 30f)]
        public float frequency = 12f;

        [TitleGroup("Ripple")]
        [Tooltip("Crest drift RELATIVE to the traveling ring: 0 = crests ride the ring at its " +
                 "expansion speed; positive = crests surge ahead (sharper); negative = crests trail " +
                 "behind (slow/heavy). ≈ -(ring expansion speed × frequency × 6.3) holds crests " +
                 "stationary in space.")]
        [Range(-100f, 100f)]
        public float waveSpeed = 14f;

        [TitleGroup("Ripple")]
        [Tooltip("Seconds until the emitted ripple fully fades out.")]
        [Range(0.1f, 6f)]
        public float lifetime = 1.5f;

        [TitleGroup("Ripple")]
        [Tooltip("How fast the ring travels outward, in viewport units/sec (1 ≈ one full screen " +
                 "height per second). 0 = inherit the UnderwaterDistortionController's global speed.")]
        [Range(0f, 4f)]
        public float expansionSpeed = 0f;

        /**
         * Emit a single ripple at this object's current position, with a strength
         * picked randomly in the [minStrength, maxStrength] range. Safe to wire to
         * a UnityEvent or call directly from other systems.
         */
        [Button("Emit Ripple", ButtonSizes.Medium), GUIColor(0.4f, 0.8f, 1f)]
        public void Emit()
        {
            // Randomize amplitude so repeated emits don't look identical.
            float strength = Random.Range(minStrength, maxStrength);

            // Fire the ripple at our position with the configured wave shape.
            EmitRequest(transform.position, strength);
        }


        public void EmitWithStrength(float strength)
        {
            EmitRequest(transform.position, strength);
        }

        /**
         * Emit a ripple at an explicit world position (still using this emitter's
         * configured wave parameters and randomized strength). Useful when the
         * effect should originate somewhere other than this transform.
         */
        public void EmitAt(Vector3 worldPosition)
        {
            float strength = Random.Range(minStrength, maxStrength);
            EmitRequest(worldPosition, strength);
        }

        /** Shared emit path: 0 expansion speed falls back to the controller's global (<= 0 sentinel). */
        private void EmitRequest(Vector3 worldPosition, float strength)
        {
            DistortionRippleBus.Emit(new RippleRequest(
                worldPosition, strength, frequency, waveSpeed, lifetime,
                expansionSpeed > 0f ? expansionSpeed : -1f, -1f));
        }
    }
}
