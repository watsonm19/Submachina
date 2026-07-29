using UnityEngine;

namespace Submachina.Core
{
    /**
     * Pure math for the parallax system — the single home for the positioning
     * and backdrop-fit formulas, shared by runtime movement and editor tooling.
     *
     * Factor convention (per axis):
     *   w = 0  → camera-locked: infinitely far away, never scrolls
     *   w = 1  → world-locked: moves exactly like gameplay geometry
     *   w > 1  → foreground: scrolls faster than the world (whizzes past)
     * Apparent on-screen scroll speed = w × camera speed.
     */
    public static class ParallaxMath
    {
        /**
         * Where a layer sits for a given camera position:
         *
         *   layerPos = rest + (camPos - anchor) * (1 - w)      per axis, z untouched
         *
         * A pure function of camera position — no delta accumulation — so
         * teleports, snaps, and edit-mode preview are always exactly correct.
         * 'rest' is the authored position, shown when the camera sits at 'anchor'.
         */
        public static Vector3 LayerPosition(Vector3 rest, Vector2 anchor, Vector2 factor, Vector3 camPos)
        {
            return new Vector3(
                rest.x + (camPos.x - anchor.x) * (1f - factor.x),
                rest.y + (camPos.y - anchor.y) * (1f - factor.y),
                rest.z);
        }

        /**
         * Content extent a layer needs for exact edge-to-edge coverage of a
         * bounded level: a lerp from the view extent (at w=0) to the level
         * extent (at w=1).
         *
         *   S = w·L + (1-w)·V
         *
         * Example: level 400 tall, max view 32 tall, w=0.1 → S = 40+28.8 = 68.8
         */
        public static float RequiredExtent(float levelExtent, float factor, float maxViewExtent)
        {
            return factor * levelExtent + (1f - factor) * maxViewExtent;
        }

        /**
         * The factor that makes a layer of extent S exactly span a level of
         * extent L over the camera's full travel (inverse of RequiredExtent):
         *
         *   w = (S - V) / (L - V)
         *
         * Worst case is max zoom-out, so V must be the LARGEST view extent.
         * When the level fits inside one view (L ≤ V) the camera can't travel
         * on that axis, so the layer should be camera-locked → returns 0.
         * Result is clamped to [0, 1]: an oversized image simply moves more.
         */
        public static float FitFactor(float layerExtent, float levelExtent, float maxViewExtent)
        {
            if (levelExtent <= maxViewExtent) return 0f;
            return Mathf.Clamp01((layerExtent - maxViewExtent) / (levelExtent - maxViewExtent));
        }
    }
}
