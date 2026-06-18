using System;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Decides WHERE a single instance is placed within a chunk. Concrete
     * strategies are chosen per-rule via Odin's [SerializeReference] type
     * picker, so each exposes only the fields it actually needs — replacing
     * the hardcoded wall/center/scatter logic that used to live in WorldChunk.
     *
     * Vertical insets keep instances off the exact chunk edges (the original
     * code used a 1-unit inset for most things, 2 units for enemies).
     */
    [Serializable]
    public abstract class PlacementStrategy
    {
        [HorizontalGroup("Vert"), LabelWidth(90)]
        [Tooltip("World-units kept clear at the chunk's top edge.")]
        [Min(0f)] public float topInset = 1f;

        [HorizontalGroup("Vert"), LabelWidth(90)]
        [Tooltip("World-units kept clear at the chunk's bottom edge.")]
        [Min(0f)] public float bottomInset = 1f;

        /** Returns a world position (and optional scale) for one instance. */
        public abstract PlacementResult GetPlacement(in SpawnContext ctx, System.Random rng);

        /** Shared helper: a random world Y within the chunk's usable vertical band. */
        protected float RandomY(in SpawnContext ctx, System.Random rng)
            => rng.NextFloat(ctx.BottomUsable(bottomInset), ctx.TopUsable(topInset));
    }

    /**
     * Scatters an instance freely across a horizontal band of the chunk.
     * widthFraction trims the band inward from the walls (e.g. 0.8 = central
     * 80%). Covers resources, passive O2, enemies, and passive creatures —
     * each just used a different band/inset in the original code.
     */
    [Serializable]
    public class ScatterPlacement : PlacementStrategy
    {
        [PropertyOrder(-10)]
        [InfoBox("SCATTER: drops instances freely across a horizontal band centered on the chunk.\n\n" +
                 "'Width Fraction' is measured against the chunk's HALF-WIDTH — the distance from the center " +
                 "line out to one trench wall (the full chunk is twice that). So 1 = wall-to-wall, " +
                 "0.8 = central 80%, 0.5 = only the middle half. Each instance gets a random X in that band " +
                 "and a random Y within the chunk's height. Best for loosely, evenly spread things " +
                 "(resources, enemies, bubbles).", InfoMessageType.None, "@Submachina.Core.SpawnDocs.ShowHelp")]
        [Tooltip("Fraction of the chunk's half-width (center-to-wall distance) to scatter across. " +
                 "1 = full width (wall to wall), 0.8 = central 80%.")]
        [Range(0f, 1f)] public float widthFraction = 0.8f;

        public override PlacementResult GetPlacement(in SpawnContext ctx, System.Random rng)
        {
            // Random X within the central band, random Y in the usable vertical range
            float x = ctx.worldX + rng.NextFloat(-ctx.halfWidth * widthFraction, ctx.halfWidth * widthFraction);
            float y = RandomY(in ctx, rng);
            return new PlacementResult(new Vector2(x, y));
        }
    }

    /**
     * Spawns a rectangle jutting inward from the left or right trench wall.
     * The protrusion amount (and thus the instance's width) scales with depth
     * via maxProtrusionFractionByDepth, so walls close in as the player
     * descends. Returns a scaleOverride of (protrusion, height) because the
     * geometry itself defines the size — this is what preserves the original
     * wall-rock behavior exactly.
     */
    [Serializable]
    public class WallProtrusionPlacement : PlacementStrategy
    {
        [PropertyOrder(-10)]
        [InfoBox("WALL PROTRUSION: spawns rectangles jutting inward from the LEFT or RIGHT wall (50/50).\n\n" +
                 "How far a rock reaches into the trench is measured as a fraction of the chunk's HALF-WIDTH — " +
                 "the distance from the center line to a wall (the full chunk is twice that). So 0.5 = a rock " +
                 "reaching halfway to the center, 0.15 = a small nub on the wall. That reach also becomes the " +
                 "object's width, so it sits flush against the wall, and the max reach grows with depth so the " +
                 "walls close in as you descend. This strategy sizes its own instances — no size configurator needed.",
            InfoMessageType.None, "@Submachina.Core.SpawnDocs.ShowHelp")]
        [Tooltip("Minimum protrusion as a fraction of the chunk's half-width (center-to-wall distance). " +
                 "0.15 = a small nub; 0.5 = reaches halfway to the center.")]
        [Range(0f, 1f)] public float minProtrusionFraction = 0.15f;

        [Tooltip("Maximum protrusion as a fraction of the chunk's half-width (center-to-wall distance), " +
                 "by depth (m). X = depth, Y = fraction. Example: 0m → 0.25, 300m → 0.6 (walls close in).")]
        public AnimationCurve maxProtrusionFractionByDepth = AnimationCurve.Linear(0f, 0.25f, 300f, 0.6f);

        [HorizontalGroup("Height"), LabelWidth(80)]
        [Tooltip("Minimum vertical size of the wall rock.")]
        public float heightMin = 1.5f;

        [HorizontalGroup("Height"), LabelWidth(80)]
        [Tooltip("Maximum vertical size of the wall rock.")]
        public float heightMax = 4.5f;

        public WallProtrusionPlacement()
        {
            // Wall rocks reached closer to the edges than the default 1-unit inset allowed
            topInset = 1f;
            bottomInset = 1f;
        }

        public override PlacementResult GetPlacement(in SpawnContext ctx, System.Random rng)
        {
            // Pick a side and a depth-scaled protrusion depth
            bool leftSide = rng.NextBool();
            float maxFraction = maxProtrusionFractionByDepth.Evaluate(ctx.depth);
            float protrusion = rng.NextFloat(ctx.halfWidth * minProtrusionFraction, ctx.halfWidth * maxFraction);
            float height = rng.NextFloat(heightMin, heightMax);

            // Center the rock on the wall edge so it appears flush (local X, offset by chunk center)
            float localX = leftSide
                ? -ctx.halfWidth + protrusion * 0.5f
                : ctx.halfWidth - protrusion * 0.5f;
            float x = ctx.worldX + localX;
            float y = RandomY(in ctx, rng);

            // Width = protrusion so the rectangle spans exactly its protrusion into the trench
            return new PlacementResult(new Vector2(x, y), new Vector2(protrusion, height));
        }
    }

    /**
     * Scatters an instance within the central band of the chunk, kept tighter
     * than ScatterPlacement so it doesn't overlap wall protrusions. Used for
     * the original "center obstacle" rocks (their depth-scaled size comes from
     * a DepthScaleConfigurator on the rule, not from placement).
     */
    [Serializable]
    public class CenterBandPlacement : PlacementStrategy
    {
        [PropertyOrder(-10)]
        [InfoBox("CENTER BAND: scatters instances within a TIGHT central band, kept clear of the walls so " +
                 "they don't overlap wall protrusions.\n\n" +
                 "'Band Fraction' is measured against the chunk's HALF-WIDTH — the distance from the center " +
                 "line to a wall (the full chunk is twice that). So 0.55 keeps everything within the central " +
                 "55%, well away from the walls. Pair with a Depth Scale configurator to size them. " +
                 "Used for the central weave-through obstacles.",
            InfoMessageType.None, "@Submachina.Core.SpawnDocs.ShowHelp")]
        [Tooltip("Fraction of the chunk's half-width (center-to-wall distance) for the central band. " +
                 "0.55 = central 55%, staying clear of the walls.")]
        [Range(0f, 1f)] public float bandFraction = 0.55f;

        public override PlacementResult GetPlacement(in SpawnContext ctx, System.Random rng)
        {
            // Random X within the tighter central band, random Y in the usable vertical range
            float x = ctx.worldX + rng.NextFloat(-ctx.halfWidth * bandFraction, ctx.halfWidth * bandFraction);
            float y = RandomY(in ctx, rng);
            return new PlacementResult(new Vector2(x, y));
        }
    }
}
