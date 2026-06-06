using System;
using UnityEngine;

namespace Gameplay.Pointer
{
    /**
     * Global pub/sub bus for pointer world-position broadcasts.
     *
     * PointerWorldTracker calls the Publish methods each frame and on input events.
     * Listeners (PointerWorldListener or any custom MonoBehaviour) subscribe in
     * OnEnable and unsubscribe in OnDisable — no inspector wiring or scene
     * references needed, so prefabs can receive pointer data without coupling
     * to scene-specific objects.
     *
     * A static event is intentional: it keeps publisher and listener wiring
     * completely decoupled, matching the same pattern used by BeatHitBus,
     * ResourceBus, and SequencerStepBus elsewhere in the project.
     */
    public static class PointerWorldBus
    {
        /** Fired every frame with the pointer's projected world position. */
        public static event Action<Vector3> OnPositionUpdated;

        /** True while the primary input is held — set by the tracker each frame. */
        public static bool IsPrimaryHeld { get; set; }

        /** True while the secondary input is held — set by the tracker each frame. */
        public static bool IsSecondaryHeld { get; set; }

        /** Fired when the primary input action performs (e.g. left click). */
        public static event Action<Vector3> OnPrimaryInput;

        /** Fired when the primary input action is released. */
        public static event Action<Vector3> OnPrimaryReleased;

        /** Fired when the secondary input action performs (e.g. right click). */
        public static event Action<Vector3> OnSecondaryInput;

        /** Fired when the secondary input action is released. */
        public static event Action<Vector3> OnSecondaryReleased;

        // ── Publish ─────────────────────────────────────────────────────────

        /** Broadcast the pointer's current world position (called every frame). */
        public static void PublishPosition(Vector3 worldPos)
        {
            OnPositionUpdated?.Invoke(worldPos);
        }

        /** Broadcast a primary input event at the given world position. */
        public static void PublishPrimary(Vector3 worldPos)
        {
            OnPrimaryInput?.Invoke(worldPos);
        }

        /** Broadcast a secondary input event at the given world position. */
        public static void PublishSecondary(Vector3 worldPos)
        {
            OnSecondaryInput?.Invoke(worldPos);
        }

        /** Broadcast that the primary input was released. */
        public static void PublishPrimaryReleased(Vector3 worldPos)
        {
            OnPrimaryReleased?.Invoke(worldPos);
        }

        /** Broadcast that the secondary input was released. */
        public static void PublishSecondaryReleased(Vector3 worldPos)
        {
            OnSecondaryReleased?.Invoke(worldPos);
        }

        // ── Cleanup ─────────────────────────────────────────────────────────

        /**
         * Wipe all subscribers. Intended for editor/domain-reload safety;
         * gameplay code should not need to call this.
         */
        public static void ClearAllSubscribers()
        {
            OnPositionUpdated = null;
            OnPrimaryInput = null;
            OnPrimaryReleased = null;
            OnSecondaryInput = null;
            OnSecondaryReleased = null;
            IsPrimaryHeld = false;
            IsSecondaryHeld = false;
        }
    }
}
