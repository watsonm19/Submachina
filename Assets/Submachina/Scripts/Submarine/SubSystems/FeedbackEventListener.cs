using System;
using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Generic reactor to a single submarine feedback key.
     *
     * Turns the one-way SubmarineFeedbackRouter into a publish/subscribe bus:
     * instead of only firing MMF_Players, any object can listen for a FeedbackId
     * and respond through UnityEvents wired in the Inspector. Gameplay code that
     * calls Sub.Feedbacks.Play(key) never knows these listeners exist.
     *
     * Example — a dash light with no reference to CavitationBurst:
     *   - one listener on DashStart  → light off
     *   - one listener on DashReady  → light on
     *
     * The play events come in two flavours so both wiring styles work:
     *   - onPlayed: parameterless, for simple reactions (toggle, SetActive, ...).
     *   - onPlayedAtPosition / onPlayedWithIntensity: the position and intensity
     *     are passed through, so a method taking that type can be bound dynamically.
     *
     * Setup:
     *   1. Add to any GameObject under a Submarine.
     *   2. Pick the FeedbackId to listen for.
     *   3. Wire onPlayed / onStopped to whatever should react.
     */
    public class FeedbackEventListener : SubmarineComponent
    {
        // Concrete UnityEvent subclasses so the parameterised events expose both
        // static and dynamic (param passthrough) binding in the Inspector.
        [Serializable] public class Vector3Event : UnityEvent<Vector3> { }
        [Serializable] public class FloatEvent : UnityEvent<float> { }

        // =====================
        // Configuration
        // =====================

        [Tooltip("The feedback key this listener reacts to.")]
        [SerializeField] private FeedbackId key;

        // =====================
        // Events
        // =====================

        [Tooltip("Fired when the key is played. Use for simple, parameterless reactions.")]
        [SerializeField] private UnityEvent onPlayed;

        [Tooltip("Fired when the key is stopped (looping feedbacks only).")]
        [SerializeField] private UnityEvent onStopped;

        [FoldoutGroup("Parameter Passthrough")]
        [Tooltip("Fired on play, passing the world position. Pick a method taking a " +
                 "Vector3 in the dropdown for dynamic passthrough.")]
        [SerializeField] private Vector3Event onPlayedAtPosition;

        [FoldoutGroup("Parameter Passthrough")]
        [Tooltip("Fired on play, passing the intensity. Pick a method taking a float " +
                 "in the dropdown for dynamic passthrough.")]
        [SerializeField] private FloatEvent onPlayedWithIntensity;

        // =====================
        // State
        // =====================

        private SubmarineFeedbackRouter _router;

        // =====================
        // Subscription Lifecycle
        // =====================

        /** Resolve the router and subscribe once the sub has registered its slots. */
        private void Start()
        {
            _router = Sub?.Feedbacks;
            if (_router == null) return;

            _router.FeedbackPlayed += HandlePlayed;
            _router.FeedbackStopped += HandleStopped;
        }

        /** Unsubscribe so a destroyed/swapped listener leaves no dangling handler. */
        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_router == null) return;

            _router.FeedbackPlayed -= HandlePlayed;
            _router.FeedbackStopped -= HandleStopped;
        }

        // =====================
        // Handlers
        // =====================

        /** Invoke the play events only when the broadcast key matches ours. */
        private void HandlePlayed(FeedbackId firedKey, Vector3 position, float intensity)
        {
            if (firedKey != key) return;

            onPlayed?.Invoke();
            onPlayedAtPosition?.Invoke(position);
            onPlayedWithIntensity?.Invoke(intensity);
        }

        /** Invoke the stop event only when the broadcast key matches ours. */
        private void HandleStopped(FeedbackId firedKey)
        {
            if (firedKey != key) return;
            onStopped?.Invoke();
        }
    }
}
