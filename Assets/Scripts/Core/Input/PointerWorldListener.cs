using Sirenix.OdinInspector;
using UnityEngine;

namespace Gameplay.Pointer
{
    /**
     * Prefab-friendly subscriber for pointer world-position events.
     * Subscribes to PointerWorldBus and sets this transform's position
     * when the selected events fire — no scene references required.
     *
     * Typical prefab setup for grid-based movement:
     *   PointerWorldListener (listenToPrimaryInput: true)
     *     ↓ sets own transform position on click
     *   TransformGridNavigator (tracks own transform by default)
     *     ↓ detects which cell the position falls on
     *   GridMoveTrigger (bridges navigator → mover)
     *     ↓ calls MoveToCell
     *   ArcingBeatMover / StraightBeatMover
     *
     * Also usable for any prefab that needs the pointer's world position
     * without referencing a scene-specific PointerWorldTracker.
     */
    [AddComponentMenu("Gameplay/Input/Pointer World Listener")]
    public class PointerWorldListener : MonoBehaviour
    {
        // ── Configuration ───────────────────────────────────────────────────

        [TitleGroup("Pointer World Listener")]
        [InfoBox("Receives pointer world-position events via PointerWorldBus " +
                 "and sets this transform's position.\n\n" +
                 "Enable the events you want to respond to. For click-to-target " +
                 "workflows, enable Primary Input only.\n\n" +
                 "Enable Continuous While Held to keep updating position " +
                 "every frame while a listened button is held down.")]

        [TitleGroup("Pointer World Listener")]
        [SerializeField, Tooltip("Update position every frame with the pointer's world position.")]
        bool listenToPosition;

        [TitleGroup("Pointer World Listener")]
        [SerializeField, Tooltip("Update position when the primary input fires (e.g. left click).")]
        bool listenToPrimaryInput = true;

        [TitleGroup("Pointer World Listener")]
        [SerializeField, Tooltip("Update position when the secondary input fires (e.g. right click).")]
        bool listenToSecondaryInput;

        [TitleGroup("Pointer World Listener")]
        [SerializeField, Tooltip("While a listened button is held down, continuously update " +
                                 "position every frame. Without this, position only updates " +
                                 "on the initial press.")]
        bool continuousWhileHeld;

        [TitleGroup("Pointer World Listener")]
        [SerializeField, Tooltip("Preserve this object's Z position instead of using the pointer's Z. " +
                                 "Useful for 2D setups where the pointer plane differs from the object plane.")]
        bool preserveZ = true;

        // ── Debug ───────────────────────────────────────────────────────────

        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly, LabelText("Last Received Position")]
        public Vector3 LastReceivedPosition { get; private set; }

        // ── Lifecycle ───────────────────────────────────────────────────────

        void OnEnable()
        {
            // Always-track mode: update every frame regardless of input.
            if (listenToPosition) PointerWorldBus.OnPositionUpdated += HandlePosition;

            // Press handlers fire on the initial click in both one-shot and continuous modes.
            if (listenToPrimaryInput) PointerWorldBus.OnPrimaryInput += HandlePosition;
            if (listenToSecondaryInput) PointerWorldBus.OnSecondaryInput += HandlePosition;

            // Continuous mode: also track position every frame while a button is held.
            // HandleContinuousPosition reads IsPrimaryHeld / IsSecondaryHeld from the
            // bus each frame, so it stops immediately when the button is released.
            if (continuousWhileHeld)
                PointerWorldBus.OnPositionUpdated += HandleContinuousPosition;
        }

        void OnDisable()
        {
            // Always unsubscribe from all — safe even if never subscribed.
            PointerWorldBus.OnPositionUpdated -= HandlePosition;
            PointerWorldBus.OnPositionUpdated -= HandleContinuousPosition;
            PointerWorldBus.OnPrimaryInput -= HandlePosition;
            PointerWorldBus.OnSecondaryInput -= HandlePosition;
        }

        // ── Handlers ────────────────────────────────────────────────────────

        /** Applies the received world position to this transform. */
        void HandlePosition(Vector3 worldPos)
        {
            if (preserveZ) worldPos.z = transform.position.z;

            transform.position = worldPos;
            LastReceivedPosition = worldPos;
        }

        /**
         * Updates position every frame, but only while a listened button is held.
         * Reads held state directly from the bus (set by the tracker via
         * InputAction.IsPressed()) so there's no reliance on event ordering.
         */
        void HandleContinuousPosition(Vector3 worldPos)
        {
            bool held = (listenToPrimaryInput && PointerWorldBus.IsPrimaryHeld)
                     || (listenToSecondaryInput && PointerWorldBus.IsSecondaryHeld);
            if (held) HandlePosition(worldPos);
        }
    }
}
