using System;
using UnityEngine;

namespace Core.Rendering
{
    /**
     * A single request to spawn an underwater-distortion ripple at a world point.
     *
     * Bundles the spatial origin with the wave's character so callers can describe
     * a "big slow swell" or a "sharp fast jolt" in one Emit call. The controller
     * converts worldPos to viewport space and drives the envelope over `lifetime`.
     */
    public readonly struct RippleRequest
    {
        public readonly Vector3 worldPos;   // where the ripple originates, in world space
        public readonly float strength;     // peak UV displacement amplitude
        public readonly float frequency;    // wave cycles packed into the ring
        public readonly float speed;        // how fast the wave oscillates (phase rate)
        public readonly float lifetime;     // seconds until the ripple fully fades out

        public RippleRequest(Vector3 worldPos, float strength, float frequency, float speed, float lifetime)
        {
            this.worldPos = worldPos;
            this.strength = strength;
            this.frequency = frequency;
            this.speed = speed;
            this.lifetime = lifetime;
        }
    }

    /**
     * Global pub/sub bus for underwater-distortion ripple events.
     *
     * Gameplay code calls Emit() to request a localized ripple; the single
     * UnderwaterDistortionController subscribes and feeds the GPU. A static event
     * keeps emitters (a fast-moving object, an impact, a UI button) fully decoupled
     * from the rendering controller — no scene references or inspector wiring, the
     * same pattern used by PointerWorldBus.
     */
    public static class DistortionRippleBus
    {
        /** Fired when something requests a ripple. The controller is the listener. */
        public static event Action<RippleRequest> OnRipple;

        /** Request a ripple at a world position with explicit wave parameters. */
        public static void Emit(Vector3 worldPos, float strength, float frequency, float speed, float lifetime)
        {
            OnRipple?.Invoke(new RippleRequest(worldPos, strength, frequency, speed, lifetime));
        }

        /** Request a ripple from a pre-built struct (e.g. when relaying defaults). */
        public static void Emit(RippleRequest request)
        {
            OnRipple?.Invoke(request);
        }

        /**
         * Wipe all subscribers. Intended for editor/domain-reload safety;
         * gameplay code should not need to call this.
         */
        public static void ClearAllSubscribers()
        {
            OnRipple = null;
        }
    }
}
