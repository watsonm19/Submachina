using System.Collections.Generic;
using UnityEngine;

namespace Submachina.Core
{
    /**
     * Immutable per-chunk geometry handed to every placement strategy.
     *
     * Coordinates mirror the original WorldChunk math:
     *   - worldX is the chunk's center X in world space.
     *   - topY is the chunk's top edge (world Y); the chunk extends down to
     *     topY - height.
     *   - halfWidth is the half-extent from center to either trench wall.
     *   - depth is the positive metres below the surface at the chunk top.
     *
     * `placed` accumulates the world positions chosen so far this chunk so
     * strategies / spacing checks can avoid clustering instances.
     */
    public readonly struct SpawnContext
    {
        public readonly float topY;
        public readonly float height;
        public readonly float halfWidth;
        public readonly float worldX;
        public readonly float depth;
        public readonly List<Vector2> placed;

        public SpawnContext(float topY, float height, float halfWidth, float worldX, float depth, List<Vector2> placed)
        {
            this.topY = topY;
            this.height = height;
            this.halfWidth = halfWidth;
            this.worldX = worldX;
            this.depth = depth;
            this.placed = placed;
        }

        /** World-space top edge usable for vertical placement (with inset). */
        public float TopUsable(float inset) => topY - inset;

        /** World-space bottom edge usable for vertical placement (with inset). */
        public float BottomUsable(float inset) => topY - height + inset;
    }

    /**
     * The result of a single placement decision: where to spawn, and an
     * optional localScale override for strategies whose geometry implies a
     * size (e.g. a wall protrusion's width). When scaleOverride is null the
     * prefab keeps its own scale (or a configurator sets it afterward).
     */
    public struct PlacementResult
    {
        /** World-space position to instantiate at. */
        public Vector2 position;

        /** Optional localScale (x, y) — used by wall rocks whose width is the protrusion. */
        public Vector2? scaleOverride;

        public PlacementResult(Vector2 position, Vector2? scaleOverride = null)
        {
            this.position = position;
            this.scaleOverride = scaleOverride;
        }
    }
}
