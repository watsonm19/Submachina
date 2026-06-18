using UnityEngine;

namespace Submachina.Core
{
    /**
     * Shared size-sampling math used wherever a [min, max] range is biased by
     * a distribution curve. Extracted so O2DropConfig (point drops) and
     * DepthSizeConfigurator (chunk spawning) use one implementation instead
     * of duplicating the roll → remap → lerp pattern.
     *
     * Sampling is two steps:
     *   1. Take a uniform roll in [0, 1].
     *   2. Remap it through the distribution curve to a normalized position,
     *      then lerp between min and max.
     *
     * A straight diagonal curve = uniform distribution. A curve that stays low
     * then rises steeply = most results small, rare results large.
     */
    public static class SizeSampler
    {
        /**
         * Samples a value in [min, max] using the distribution curve to bias a
         * uniform roll. Returns min if the range is degenerate (min >= max) so
         * callers never need to special-case equal bounds.
         */
        public static float Sample(float min, float max, AnimationCurve distribution, float roll01)
        {
            // Degenerate range — every result is the same size
            if (min >= max) return min;

            // Remap the uniform roll through the curve, then lerp into the range
            float t = distribution != null ? distribution.Evaluate(roll01) : roll01;
            return Mathf.Lerp(min, max, t);
        }
    }
}
