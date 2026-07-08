using System;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Optional per-instance setup applied right after a prefab is spawned.
     * Concrete configurators are chosen per-rule via Odin's [SerializeReference]
     * type picker. This generalizes the existing DropConfig.Configure() hook
     * (O2 bubbles rolling a size) so any spawn rule can post-process its
     * instances — sizing rocks, sizing bubbles by depth, etc.
     *
     * Leave a rule's configurator set to None when the prefab needs no
     * post-processing (most enemies, resources, pickups).
     */
    [Serializable]
    public abstract class InstanceConfigurator
    {
        /** Applies type-specific setup to a freshly spawned instance. */
        public abstract void Configure(GameObject instance, float depth, System.Random rng);
    }

    /**
     * Sizes an O2 bubble by calling O2Pickup.SetSize(). The smallest AND largest
     * possible size both grow as you descend, and a distribution curve biases
     * which sizes are common vs rare. The bubble's *reward* scaling lives on the
     * O2Pickup prefab itself (its sizeRewardCurve) — this configurator only
     * chooses the visual size that gets passed in.
     *
     * All four size endpoints are explicit values (no magic baked into curves):
     * you set the min/max size in the shallows and the min/max size deep down,
     * and they interpolate linearly with depth.
     *
     * Requires an O2Pickup on the instance.
     */
    [Serializable]
    public class DepthSizeConfigurator : InstanceConfigurator
    {
        [InfoBox("Picks a bubble size, then calls O2Pickup.SetSize(). The size RANGE widens with depth " +
                 "(deeper bubbles can be bigger), and the distribution curve decides where in that range " +
                 "most bubbles land. See the live examples at the bottom.",
            InfoMessageType.None, "@Submachina.Core.SpawnDocs.ShowHelp")]
        [HorizontalGroup("Depth"), LabelWidth(120)]
        [Tooltip("Depth (m) at which the 'shallow' size endpoints apply.")]
        public float refMinDepth = 0f;

        [HorizontalGroup("Depth"), LabelWidth(120)]
        [Tooltip("Depth (m) at which the 'deep' size endpoints apply (clamped beyond).")]
        public float refMaxDepth = 300f;

        [Title("Minimum size (smallest a bubble can roll)")]
        [HorizontalGroup("Min"), LabelWidth(120)]
        [Tooltip("Minimum bubble size in the shallows (at refMinDepth).")]
        public float minSizeShallow = 0.5f;

        [HorizontalGroup("Min"), LabelWidth(120)] [Tooltip("Minimum bubble size deep down (at refMaxDepth).")]
        public float minSizeDeep = 1.0f;

        [Title("Maximum size (largest a bubble can roll)")]
        [HorizontalGroup("Max"), LabelWidth(120)]
        [Tooltip("Maximum bubble size in the shallows (at refMinDepth).")]
        public float maxSizeShallow = 1.5f;

        [HorizontalGroup("Max"), LabelWidth(120)] [Tooltip("Maximum bubble size deep down (at refMaxDepth).")]
        public float maxSizeDeep = 4.0f;

        [Tooltip("Biases which bubble sizes are common vs rare. See the help box for what each axis means.")]
        [InfoBox("DISTRIBUTION CURVE — what the axes mean:\n" +
                 "• X axis (horizontal) = a uniform random 0–1 roll, made once per bubble.\n" +
                 "• Y axis (vertical) = where that roll lands within this depth's size range: " +
                 "Y=0 gives the minimum size, Y=1 gives the maximum size.\n\n" +
                 "Straight diagonal = uniform (every size equally likely).\n" +
                 "Flat-then-steep = most bubbles near the minimum, rare ones large.",
            InfoMessageType.None, "@Submachina.Core.SpawnDocs.ShowHelp")]
        [InfoBox("$Preview", InfoMessageType.None)]
        public AnimationCurve sizeDistribution = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        public override void Configure(GameObject instance, float depth, System.Random rng)
        {
            // Bubbles only — silently ignore prefabs without an O2Pickup
            O2Pickup pickup = instance.GetComponent<O2Pickup>();
            if (pickup == null) return;

            // Interpolate the size range for this depth, then sample through the distribution curve
            float t = DepthT(depth);
            float min = Mathf.Lerp(minSizeShallow, minSizeDeep, t);
            float max = Mathf.Lerp(maxSizeShallow, maxSizeDeep, t);
            float size = SizeSampler.Sample(min, max, sizeDistribution, rng.NextFloat01());
            pickup.SetSize(size);
        }

        /** Normalized 0–1 position of a depth within the reference window. */
        private float DepthT(float depth)
            => Mathf.Approximately(refMaxDepth, refMinDepth)
                ? 1f
                : Mathf.Clamp01((depth - refMinDepth) / (refMaxDepth - refMinDepth));

        // Live editor preview: size range + median at three sample depths
        private string Preview
        {
            get
            {
                float mid = (refMinDepth + refMaxDepth) * 0.5f;
                float[] depths = { refMinDepth, mid, refMaxDepth };
                string s = "Example bubble sizes by depth:";
                foreach (float d in depths)
                {
                    float t = DepthT(d);
                    float min = Mathf.Lerp(minSizeShallow, minSizeDeep, t);
                    float max = Mathf.Lerp(maxSizeShallow, maxSizeDeep, t);
                    float median = SizeSampler.Sample(min, max, sizeDistribution, 0.5f);
                    s += $"\n  {d:F0}m:  range {min:F2}–{max:F2}  (median ~{median:F2})";
                }

                return s;
            }
        }
    }

    /**
     * Sets an instance's localScale from a depth-scaled size range — the
     * generic form of the original "center obstacle" rock sizing. Width and
     * height each roll randomly between a fixed minimum and a maximum that
     * grows with depth, so objects get larger AND more varied the deeper you go.
     *
     * The max-size endpoints are explicit values (shallow vs deep) rather than
     * keyframes hidden inside a curve, so you can read and set them directly.
     */
    [Serializable]
    public class DepthScaleConfigurator : InstanceConfigurator
    {
        [InfoBox("Sets localScale. Width rolls between 'Min Width' and a depth-scaled maximum; height rolls " +
                 "between 'Min Height' and that same maximum times 'Height Factor'. The maximum grows from " +
                 "'Max Size Shallow' to 'Max Size Deep' as you descend. See live examples at the bottom.",
            InfoMessageType.None, "@Submachina.Core.SpawnDocs.ShowHelp")]
        [HorizontalGroup("Depth"), LabelWidth(120)]
        [Tooltip("Depth (m) at which 'Max Size Shallow' applies.")]
        public float refMinDepth = 0f;

        [HorizontalGroup("Depth"), LabelWidth(120)]
        [Tooltip("Depth (m) at which 'Max Size Deep' applies (clamped beyond).")]
        public float refMaxDepth = 300f;

        [Title("Maximum size ceiling (grows with depth)")]
        [HorizontalGroup("Max"), LabelWidth(120)]
        [Tooltip("Upper bound of width in the shallows (at refMinDepth).")]
        public float maxSizeShallow = 1.2f;

        [HorizontalGroup("Max"), LabelWidth(120)] [Tooltip("Upper bound of width deep down (at refMaxDepth).")]
        public float maxSizeDeep = 3.5f;

        [Title("Minimum size floor (constant)")]
        [HorizontalGroup("Min"), LabelWidth(120)]
        [Tooltip("Smallest width, at any depth.")]
        public float minWidth = 0.8f;

        [HorizontalGroup("Min"), LabelWidth(120)] [Tooltip("Smallest height, at any depth.")]
        public float minHeight = 0.5f;

        [Tooltip("Height's ceiling as a fraction of the width ceiling (0.75 = objects are wider than tall).")]
        [Range(0f, 2f)]
        public float heightFactor = 0.75f;

        [Tooltip("Optional shaping of how the max size grows from shallow→deep. " +
                 "X = normalized depth [0,1], Y = normalized size [0,1]. Straight diagonal = linear growth.")]
        [InfoBox("$Preview", InfoMessageType.None)]
        public AnimationCurve depthShaping = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        public override void Configure(GameObject instance, float depth, System.Random rng)
        {
            // Depth sets the ceiling; width and height each roll independently under it
            float maxSize = MaxSizeAt(depth);
            float w = rng.NextFloat(minWidth, Mathf.Max(minWidth, maxSize));
            float h = rng.NextFloat(minHeight, Mathf.Max(minHeight, maxSize * heightFactor));
            instance.transform.localScale = new Vector3(w, h, 1f);
        }

        /** The depth-scaled (and curve-shaped) maximum size at a given depth. */
        private float MaxSizeAt(float depth)
        {
            float raw = Mathf.Approximately(refMaxDepth, refMinDepth)
                ? 1f
                : Mathf.Clamp01((depth - refMinDepth) / (refMaxDepth - refMinDepth));
            float t = depthShaping != null ? depthShaping.Evaluate(raw) : raw;
            return Mathf.Lerp(maxSizeShallow, maxSizeDeep, t);
        }

        // Live editor preview: width/height ranges at three sample depths
        private string Preview
        {
            get
            {
                float mid = (refMinDepth + refMaxDepth) * 0.5f;
                float[] depths = { refMinDepth, mid, refMaxDepth };
                string s = "Example sizes by depth (width × height):";
                foreach (float d in depths)
                {
                    float maxSize = MaxSizeAt(d);
                    s += $"\n  {d:F0}m:  W {minWidth:F1}–{maxSize:F1}   H {minHeight:F1}–{(maxSize * heightFactor):F1}";
                }

                return s;
            }
        }
    }

    /**
     * Triggers a ClusterBuilder's procedural build using the chunk's
     * deterministic RNG. The builder consumes exactly one draw from the chunk
     * stream and derives its own internal streams from it, so cluster tuning
     * (rock counts, pity state, config edits) never perturbs the draws seen by
     * other rules in the same chunk.
     *
     * Silently ignores prefabs without a ClusterBuilder (mirrors the O2Pickup
     * guard above). Depth is forwarded for future depth-aware cluster configs.
     */
    [Serializable]
    public class ClusterBuildConfigurator : InstanceConfigurator
    {
        public override void Configure(GameObject instance, float depth, System.Random rng)
        {
            // Clusters only — silently ignore prefabs without a builder
            ClusterBuilder builder = instance.GetComponent<ClusterBuilder>();
            if (builder == null) return;
            builder.Build(depth, rng);
        }
    }
}