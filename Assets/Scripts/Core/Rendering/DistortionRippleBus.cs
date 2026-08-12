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
        public readonly Vector3 worldPos;      // where the ripple originates, in world space
        public readonly float strength;        // peak UV displacement amplitude
        public readonly float frequency;       // wave cycles packed into the ring
        public readonly float speed;           // how fast the wave oscillates (phase rate)
        public readonly float lifetime;        // seconds until the ripple fully fades out
        public readonly float expansionSpeed;  // ring growth in viewport units/sec; <= 0 → controller default
        public readonly float ringWidth;       // ring band width in viewport units; <= 0 → controller default
        public readonly Color tint;            // additive ring glow; rgb = color, a = intensity (clear = none)
        public readonly float chromaticBoost;  // extra R/B split on this ring (1 = neutral, >1 = metallic fringe)

        /** Standard request — ring expansion and band width use the controller's global settings. */
        public RippleRequest(Vector3 worldPos, float strength, float frequency, float speed, float lifetime)
            : this(worldPos, strength, frequency, speed, lifetime, -1f, -1f) { }

        /** Request with per-ripple ring shape overrides (pass <= 0 to keep a controller default). */
        public RippleRequest(Vector3 worldPos, float strength, float frequency, float speed, float lifetime,
                             float expansionSpeed, float ringWidth)
            : this(worldPos, strength, frequency, speed, lifetime, expansionSpeed, ringWidth, Color.clear, 1f) { }

        /** Full request with identity extras: a colored glow riding the ring and a chromatic-split boost. */
        public RippleRequest(Vector3 worldPos, float strength, float frequency, float speed, float lifetime,
                             float expansionSpeed, float ringWidth, Color tint, float chromaticBoost)
        {
            this.worldPos = worldPos;
            this.strength = strength;
            this.frequency = frequency;
            this.speed = speed;
            this.lifetime = lifetime;
            this.expansionSpeed = expansionSpeed;
            this.ringWidth = ringWidth;
            this.tint = tint;
            this.chromaticBoost = chromaticBoost;
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
