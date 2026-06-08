using UnityEngine;
using Sirenix.OdinInspector;
using Core.Rendering;

namespace Gameplay
{
    /**
     * Emits underwater-distortion ripples when this object moves fast.
     *
     * Watches the object's speed each frame (via a Rigidbody2D if assigned, otherwise
     * from raw transform motion) and, when it crosses a threshold, fires a ripple at
     * the object's position through DistortionRippleBus — representing something
     * suddenly darting through the water. A cooldown keeps a fast object from spamming
     * ripples every frame.
     *
     * This is an example trigger: any gameplay code can call DistortionRippleBus.Emit
     * directly (on impacts, dashes, explosions, etc.) the same way.
     */
    public class SpeedRippleEmitter : MonoBehaviour
    {
        // ─── Source ──────────────────────────────────────────────────────────
        [TitleGroup("Source")]
        [Tooltip("Optional. If set, speed is read from its velocity; otherwise from transform movement.")]
        public Rigidbody2D body;

        // ─── Trigger ─────────────────────────────────────────────────────────
        [TitleGroup("Trigger")]
        [Tooltip("Speed (world units/sec) the object must exceed to emit a ripple.")]
        [MinValue(0f)]
        public float speedThreshold = 6f;

        [TitleGroup("Trigger")]
        [Tooltip("Minimum seconds between emitted ripples, so a sustained fast move doesn't spam them.")]
        [MinValue(0f)]
        public float minIntervalBetweenEmits = 0.25f;

        // ─── Ripple parameters ───────────────────────────────────────────────
        [TitleGroup("Ripple", "Scaled up with speed between the threshold and Speed For Max Strength.")]
        [Tooltip("Ripple amplitude emitted right at the threshold.")]
        [Range(0f, 0.2f)]
        public float minStrength = 0.02f;

        [TitleGroup("Ripple")]
        [Tooltip("Ripple amplitude emitted at or above Speed For Max Strength.")]
        [Range(0f, 0.2f)]
        public float maxStrength = 0.07f;

        [TitleGroup("Ripple")]
        [Tooltip("Speed at which the ripple reaches its maximum strength.")]
        [MinValue(0f)]
        public float speedForMaxStrength = 18f;

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

        // ─── Debug ───────────────────────────────────────────────────────────
        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly]
        private float _currentSpeed;

        private Vector3 _lastPos;
        private float _lastEmitTime;

        /** Cache the start position so the transform-fallback speed has a baseline. */
        private void OnEnable()
        {
            _lastPos = transform.position;
        }

        /**
         * Sample speed, and emit a strength-scaled ripple when it exceeds the threshold
         * and the cooldown has elapsed.
         */
        private void Update()
        {
            // Measure speed from the rigidbody if we have one, else from frame-to-frame motion.
            _currentSpeed = MeasureSpeed();

            // Gate: must be moving fast enough, and not still in cooldown.
            if (_currentSpeed < speedThreshold) return;
            if (Time.time - _lastEmitTime < minIntervalBetweenEmits) return;

            // Map speed (threshold..speedForMaxStrength) to ripple strength (min..max).
            float k = Mathf.InverseLerp(speedThreshold, Mathf.Max(speedThreshold, speedForMaxStrength), _currentSpeed);
            float strength = Mathf.Lerp(minStrength, maxStrength, k);

            // Fire the ripple at our current position and reset the cooldown.
            DistortionRippleBus.Emit(transform.position, strength, frequency, waveSpeed, lifetime);
            _lastEmitTime = Time.time;
        }

        /** Current speed in world units/sec, from the rigidbody or transform delta. */
        private float MeasureSpeed()
        {
            // Prefer the rigidbody's authoritative velocity when present.
            if (body != null) return body.linearVelocity.magnitude;

            // Fallback: distance moved since last frame over delta time.
            if (Time.deltaTime <= 0f) return 0f;
            float speed = (transform.position - _lastPos).magnitude / Time.deltaTime;
            _lastPos = transform.position;
            return speed;
        }
    }
}
