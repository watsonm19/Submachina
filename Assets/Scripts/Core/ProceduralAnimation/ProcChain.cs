using UnityEngine;

namespace Core.ProceduralAnimation
{
    /**
     * Pure follow-the-leader constraint chain — the numeric heart of all procedural
     * creature animation (fish bodies, tentacles, snakes, trailing fins).
     *
     * The head point is placed externally each step; every subsequent point trails
     * its parent at a fixed segment length, with an optional per-joint bend limit
     * (stiffness) and a straightening bias (whip-like relaxation). Callers may nudge
     * points directly before Solve() to add waves, noise sway, gravity droop, etc. —
     * the constraint pass afterwards restores segment lengths, which is what turns
     * raw offsets into smooth organic S-curves.
     *
     * Deliberately a plain class (no Unity object lifetime), zero allocations after
     * construction, all points in world space.
     */
    public sealed class ProcChain
    {
        /** Solved point positions, index 0 = head. Callers may nudge these before Solve(). */
        public readonly Vector2[] Points;

        /** Per-segment normalized trail directions: Directions[i] points from Points[i-1] to Points[i]. Index 0 mirrors index 1. */
        public readonly Vector2[] Directions;

        /** Fixed distance maintained between consecutive points. */
        public float SegmentLength;

        /** Number of points in the chain (segments + 1). */
        public int Count => Points.Length;

        /** Total rest length of the chain in world units. */
        public float TotalLength => SegmentLength * (Points.Length - 1);

        public ProcChain(int pointCount, float segmentLength)
        {
            Points = new Vector2[Mathf.Max(2, pointCount)];
            Directions = new Vector2[Points.Length];
            SegmentLength = Mathf.Max(0.001f, segmentLength);
        }

        /**
         * Instantly lays the chain out in a straight line trailing behind the head —
         * used on spawn and when re-activating after culling so the chain doesn't
         * visibly whip across the screen from its stale position.
         */
        public void Teleport(Vector2 head, Vector2 trailDirection)
        {
            if (trailDirection.sqrMagnitude < 1e-6f) trailDirection = Vector2.left;
            trailDirection.Normalize();

            for (int i = 0; i < Points.Length; i++)
            {
                Points[i] = head + trailDirection * (SegmentLength * i);
                Directions[i] = trailDirection;
            }
        }

        /**
         * One constraint pass. Pins the head, then walks down the chain restoring
         * segment lengths while clamping each joint's bend angle and easing every
         * segment back toward its parent's direction.
         *
         *   headFacing    — the head's forward direction; the first segment trails
         *                   opposite this. Pass Vector2.zero to leave the first
         *                   segment unconstrained (free-hanging tentacle root).
         *   maxBendRad    — per-joint angle limit in radians (e.g. 0.5 ≈ 30°).
         *                   Larger = floppier, smaller = stiffer rod.
         *   straighten    — 0..1 fraction each segment eases toward its parent's
         *                   direction this step (whip relaxation). 0 = pure trailing.
         */
        public void Solve(Vector2 head, Vector2 headFacing, float maxBendRad, float straighten)
        {
            Points[0] = head;

            // Reference direction the first segment bends relative to: straight back from the head.
            bool hasFacing = headFacing.sqrMagnitude > 1e-6f;
            Vector2 prevDir = hasFacing ? -headFacing.normalized : Directions[1];

            for (int i = 1; i < Points.Length; i++)
            {
                // Raw pull direction from parent to this point; degenerate overlap keeps last frame's direction.
                Vector2 raw = Points[i] - Points[i - 1];
                Vector2 dir = raw.sqrMagnitude > 1e-8f ? raw.normalized : Directions[i];

                // Straightening bias — ease toward the parent segment's direction, then re-normalize.
                if (straighten > 0f)
                {
                    dir = Vector2.Lerp(dir, prevDir, straighten);
                    if (dir.sqrMagnitude < 1e-8f) dir = prevDir;
                    dir.Normalize();
                }

                // Bend limit — clamp the signed angle between parent and this segment.
                // e.g. parent pointing right, maxBend 30°: this segment stays within ±30° of right.
                if (maxBendRad < Mathf.PI)
                {
                    float angle = SignedAngle(prevDir, dir);
                    if (angle > maxBendRad) dir = Rotate(prevDir, maxBendRad);
                    else if (angle < -maxBendRad) dir = Rotate(prevDir, -maxBendRad);
                }

                Points[i] = Points[i - 1] + dir * SegmentLength;
                Directions[i] = dir;
                prevDir = dir;
            }

            Directions[0] = Directions[1];
        }

        /**
         * Smoothed tangent at a point (average of adjacent segment directions) —
         * what renderers should use for ribbon cross-sections so joints don't crease.
         */
        public Vector2 TangentAt(int i)
        {
            if (i <= 0) return Directions[1];
            if (i >= Points.Length - 1) return Directions[Points.Length - 1];
            Vector2 t = Directions[i] + Directions[i + 1];
            return t.sqrMagnitude > 1e-8f ? t.normalized : Directions[i];
        }

        /** Left-hand perpendicular of the tangent at a point — the ribbon "side" axis. */
        public Vector2 NormalAt(int i)
        {
            Vector2 t = TangentAt(i);
            return new Vector2(-t.y, t.x);
        }

        // -------------------------------------------------------
        // Math helpers
        // -------------------------------------------------------

        /** Signed angle in radians from a to b (both assumed normalized). */
        private static float SignedAngle(Vector2 a, Vector2 b)
        {
            return Mathf.Atan2(a.x * b.y - a.y * b.x, Vector2.Dot(a, b));
        }

        /** Rotates a vector by an angle in radians. */
        private static Vector2 Rotate(Vector2 v, float rad)
        {
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }
    }
}
