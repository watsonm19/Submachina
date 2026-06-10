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
        [Tooltip("Oscillation (phase) speed of the wave — higher feels sharper/faster.")]
        [Range(0f, 30f)]
        public float waveSpeed = 14f;

        [TitleGroup("Ripple")]
        [Tooltip("Seconds until the emitted ripple fully fades out.")]
        [Range(0.1f, 6f)]
        public float lifetime = 1.5f;

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
            DistortionRippleBus.Emit(transform.position, strength, frequency, waveSpeed, lifetime);
        }

        /**
         * Emit a ripple at an explicit world position (still using this emitter's
         * configured wave parameters and randomized strength). Useful when the
         * effect should originate somewhere other than this transform.
         */
        public void EmitAt(Vector3 worldPosition)
        {
            float strength = Random.Range(minStrength, maxStrength);
            DistortionRippleBus.Emit(worldPosition, strength, frequency, waveSpeed, lifetime);
        }
    }
}
