using System;
using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * An authorable depth window with soft edges: the rate is 0 at startDepth,
     * ramps to full by fullRateDepth, optionally holds until fadeStartDepth,
     * then falls back to 0 at stopDepth — or continues at full rate forever
     * when hasStop is off ("from 400m and below" style rules).
     *
     * Converts into the primitives SpawnRuleData already understands
     * (DepthRange gate + prevalenceByDepth curve), so any rule builder can
     * adopt it — mission resources today, creatures/enemies later.
     */
    [Serializable]
    public class DepthEnvelope
    {
        [HorizontalGroup("In"), LabelWidth(110)]
        [Tooltip("Depth (m) where spawning begins (rate 0, ramping in).")]
        [Min(0f)] public float startDepth = 100f;

        [HorizontalGroup("In"), LabelWidth(110)]
        [Tooltip("Depth (m) where the configured full rate is reached.")]
        [Min(0f)] public float fullRateDepth = 150f;

        [Tooltip("If off, the envelope never ends — full rate continues all the way down.")]
        public bool hasStop;

        [ShowIf(nameof(hasStop)), HorizontalGroup("Out"), LabelWidth(110)]
        [Tooltip("Depth (m) where the rate starts fading back out (full rate holds until here).")]
        [Min(0f)] public float fadeStartDepth = 250f;

        [ShowIf(nameof(hasStop)), HorizontalGroup("Out"), LabelWidth(110)]
        [Tooltip("Depth (m) where spawning stops entirely.")]
        [Min(0f)] public float stopDepth = 300f;

        /** Hard spawn window for the depth gate: [start, stop], or open-ended below start. */
        public DepthRange ToDepthRange()
            => new DepthRange { minDepth = startDepth, hasMax = hasStop, maxDepth = Mathf.Max(stopDepth, startDepth) };

        /**
         * Builds the matching prevalence curve (a trapezoid). Out-of-order
         * depths are clamped into sequence so a half-edited inspector never
         * inverts the shape.
         * Example: start 400, full 450, no stop → 0@400m → 1@450m → 1 forever.
         */
        public AnimationCurve BuildPrevalence()
        {
            // Clamp into a monotonic sequence: start ≤ full ≤ fade ≤ stop
            float full = Mathf.Max(fullRateDepth, startDepth);
            float stop = hasStop ? Mathf.Max(stopDepth, full) : 0f;
            float fade = hasStop ? Mathf.Clamp(fadeStartDepth, full, stop) : 0f;

            // Ramp-in (skipped when start and full coincide → instant full rate)
            var keys = new List<Keyframe>(4);
            if (full - startDepth > 0.01f) keys.Add(new Keyframe(startDepth, 0f));
            keys.Add(new Keyframe(full, 1f));

            // Optional hold plateau + ramp-out (the DepthRange gate hard-cuts at stop regardless)
            if (hasStop)
            {
                if (fade - full > 0.01f) keys.Add(new Keyframe(fade, 1f));
                if (stop - fade > 0.01f) keys.Add(new Keyframe(stop, 0f));
            }

            return new AnimationCurve(keys.ToArray());
        }
    }
}
