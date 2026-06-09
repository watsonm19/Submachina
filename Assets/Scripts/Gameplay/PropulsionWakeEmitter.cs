using UnityEngine;
using Sirenix.OdinInspector;
using Core.Rendering;

namespace Gameplay
{
    /**
     * Streams underwater turbulence "wakes" behind this object while it moves fast — the
     * exaggerated light-distortion you'd see trailing something under propulsion while
     * you're submerged. The alternate to SpeedRippleEmitter: it emits directional wakes
     * (along the velocity) rather than concentric ripples.
     *
     * While speed is above the threshold it emits a fresh wake every short interval (wakes
     * are short-lived, so a stream of them reads as a continuous churning trail). Strength
     * scales with speed. Any gameplay code can drive this directly via DistortionWakeBus.Emit.
     */
    public class PropulsionWakeEmitter : MonoBehaviour
    {
        // ─── Source ──────────────────────────────────────────────────────────
        [TitleGroup("Source")]
        [Tooltip("Optional. If set, velocity is read from it; otherwise from transform movement.")]
        public Rigidbody2D body;

        // ─── Trigger ─────────────────────────────────────────────────────────
        [TitleGroup("Trigger")]
        [Tooltip("Speed (world units/sec) the object must exceed to start trailing a wake.")]
        [MinValue(0f)]
        public float speedThreshold = 5f;

        [TitleGroup("Trigger")]
        [Tooltip("Seconds between successive wake puffs while moving — small values give a denser trail.")]
        [MinValue(0.01f)]
        public float emitInterval = 0.06f;

        // ─── Wake parameters ─────────────────────────────────────────────────
        [TitleGroup("Wake", "Scaled up with speed between the threshold and Speed For Max Strength.")]
        [Tooltip("Wake displacement strength right at the threshold.")]
        [Range(0f, 0.2f)]
        public float minStrength = 0.025f;

        [TitleGroup("Wake")]
        [Tooltip("Wake displacement strength at or above Speed For Max Strength.")]
        [Range(0f, 0.2f)]
        public float maxStrength = 0.08f;

        [TitleGroup("Wake")]
        [Tooltip("Speed at which the wake reaches its maximum strength.")]
        [MinValue(0f)]
        public float speedForMaxStrength = 16f;

        [TitleGroup("Wake")]
        [Tooltip("Plume half-length in viewport units (<=0 uses the controller's default).")]
        [Range(0f, 0.5f)]
        public float length = 0.13f;

        [TitleGroup("Wake")]
        [Tooltip("Turbulence frequency inside the plume (<=0 uses the controller's default).")]
        [Range(0f, 40f)]
        public float frequency = 16f;

        [TitleGroup("Wake")]
        [Tooltip("Seconds until each emitted wake puff fades out.")]
        [Range(0.1f, 4f)]
        public float lifetime = 0.8f;

        // ─── Debug ───────────────────────────────────────────────────────────
        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly]
        private float _currentSpeed;

        private Vector3 _lastPos;
        private float _lastEmitTime;

        /** Cache the start position so the transform-fallback velocity has a baseline. */
        private void OnEnable()
        {
            _lastPos = transform.position;
        }

        /**
         * Sample velocity and, while fast enough, emit a wake along the travel direction at
         * the emit interval.
         */
        private void Update()
        {
            // Measure velocity (direction + speed) from the rigidbody or transform delta.
            Vector2 velocity = MeasureVelocity();
            _currentSpeed = velocity.magnitude;

            // Gate: must be moving fast enough, and not still within the emit interval.
            if (_currentSpeed < speedThreshold) return;
            if (Time.time - _lastEmitTime < emitInterval) return;

            // Map speed (threshold..speedForMaxStrength) to wake strength (min..max).
            float k = Mathf.InverseLerp(speedThreshold, Mathf.Max(speedThreshold, speedForMaxStrength), _currentSpeed);
            float strength = Mathf.Lerp(minStrength, maxStrength, k);

            // Emit a wake travelling along our velocity; the trail forms behind us.
            Vector3 dir = new Vector3(velocity.x, velocity.y, 0f);
            DistortionWakeBus.Emit(transform.position, dir, strength, length, frequency, lifetime);
            _lastEmitTime = Time.time;
        }

        /** Current velocity in world units/sec, from the rigidbody or transform delta. */
        private Vector2 MeasureVelocity()
        {
            // Prefer the rigidbody's authoritative velocity when present.
            if (body != null) return body.linearVelocity;

            // Fallback: displacement since last frame over delta time.
            if (Time.deltaTime <= 0f) return Vector2.zero;
            Vector2 v = (transform.position - _lastPos) / Time.deltaTime;
            _lastPos = transform.position;
            return v;
        }
    }
}
