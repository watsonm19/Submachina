using System;
using UnityEngine;

namespace Core.Rendering
{
    /**
     * A request to spawn a propulsion "wake" — an elongated turbulence trail that streams
     * behind a moving source and refracts light, as opposed to a concentric ripple.
     *
     * `worldDir` is the source's travel direction; the trail is placed behind it. A length
     * or frequency of <= 0 tells the controller to use its own default for that field.
     */
    public readonly struct WakeRequest
    {
        public readonly Vector3 worldPos;   // emission point, world space
        public readonly Vector3 worldDir;   // travel direction (need not be normalized)
        public readonly float strength;     // peak displacement amplitude
        public readonly float length;       // plume half-length in viewport units (<=0 = default)
        public readonly float frequency;    // turbulence frequency (<=0 = default)
        public readonly float lifetime;     // seconds until the wake fully fades

        public WakeRequest(Vector3 worldPos, Vector3 worldDir, float strength, float length, float frequency, float lifetime)
        {
            this.worldPos = worldPos;
            this.worldDir = worldDir;
            this.strength = strength;
            this.length = length;
            this.frequency = frequency;
            this.lifetime = lifetime;
        }
    }

    /**
     * Global pub/sub bus for propulsion-wake events — the turbulence counterpart to
     * DistortionRippleBus. Gameplay code (e.g. PropulsionWakeEmitter, a thruster, a dash)
     * calls Emit(); the UnderwaterDistortionController is the single listener.
     */
    public static class DistortionWakeBus
    {
        /** Fired when something requests a wake. The controller is the listener. */
        public static event Action<WakeRequest> OnWake;

        /** Request a wake travelling along worldDir from worldPos. */
        public static void Emit(Vector3 worldPos, Vector3 worldDir, float strength, float length, float frequency, float lifetime)
        {
            OnWake?.Invoke(new WakeRequest(worldPos, worldDir, strength, length, frequency, lifetime));
        }

        /** Request a wake from a pre-built struct. */
        public static void Emit(WakeRequest request)
        {
            OnWake?.Invoke(request);
        }

        /**
         * Wipe all subscribers. Intended for editor/domain-reload safety;
         * gameplay code should not need to call this.
         */
        public static void ClearAllSubscribers()
        {
            OnWake = null;
        }
    }
}
