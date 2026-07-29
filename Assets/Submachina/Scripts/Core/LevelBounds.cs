using System;
using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * The authoritative definition of the level's spatial extents.
     *
     * Each side (left / right / bottom / top) can be independently bounded or
     * unbounded, so the same component describes a fully walled arena, an
     * endless descent with side walls, or a completely open ocean.
     *
     * Consumers:
     *   - MultiTargetCamera2D / CameraFollow clamp the camera view rect (and
     *     zoom) so the player can never see past a bounded edge.
     *   - ParallaxLayer fit tooling sizes the furthest backdrop so it exactly
     *     spans the camera's travel range.
     *
     * Place on the GameManager object next to ChunkSpawner. Values can be
     * authored directly or derived from a LevelConfig asset with the button.
     */
    public class LevelBounds : MonoBehaviour
    {
        /** One edge of the level: a world coordinate that may or may not apply. */
        [Serializable]
        public struct Edge
        {
            [Tooltip("When off, this side is unbounded — the camera can travel forever this way.")]
            public bool bounded;

            [Tooltip("World-space coordinate of this edge (X for left/right, Y for top/bottom).")]
            [EnableIf(nameof(bounded))]
            public float value;

            public Edge(bool bounded, float value)
            {
                this.bounded = bounded;
                this.value = value;
            }
        }

        // =====================
        // Bounds
        // =====================

        [FoldoutGroup("Bounds")]
        [InfoBox("Each side can be bounded (hard camera limit) or unbounded (infinite travel). " +
                 "Top defaults to 0 — the ocean surface.")]
        [SerializeField] private Edge left = new Edge(true, -100f);

        [FoldoutGroup("Bounds")]
        [SerializeField] private Edge right = new Edge(true, 100f);

        [FoldoutGroup("Bounds")]
        [SerializeField] private Edge bottom = new Edge(true, -400f);

        [FoldoutGroup("Bounds")]
        [SerializeField] private Edge top = new Edge(true, 0f);

        // =====================
        // Level Config (optional)
        // =====================

        [FoldoutGroup("Level Config")]
        [InfoBox("Optionally derive edges from a LevelConfig asset: left/right = ±HalfWidth, " +
                 "top = 0, bottom = -TotalDepth.")]
        [SerializeField] private LevelConfig levelConfig;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired whenever the bounds change at runtime (e.g. DeriveFromLevelConfig or SetEdges).")]
        public UnityEvent onBoundsChanged;

        // =====================
        // Public queries
        // =====================

        public Edge Left   => left;
        public Edge Right  => right;
        public Edge Bottom => bottom;
        public Edge Top    => top;

        public bool HorizontallyBounded => left.bounded && right.bounded;
        public bool VerticallyBounded   => top.bounded && bottom.bounded;
        public bool FullyBounded        => HorizontallyBounded && VerticallyBounded;

        /** Centre of the bounded region — only meaningful per fully bounded axis. */
        public Vector2 Centre => new Vector2(
            HorizontallyBounded ? (left.value + right.value) * 0.5f : 0f,
            VerticallyBounded ? (bottom.value + top.value) * 0.5f : 0f);

        /** Size of the bounded region — infinity on any axis that isn't fully bounded. */
        public Vector2 Size => new Vector2(
            HorizontallyBounded ? right.value - left.value : float.PositiveInfinity,
            VerticallyBounded ? top.value - bottom.value : float.PositiveInfinity);

        // -------------------------------------------------------
        // Camera clamping
        // -------------------------------------------------------

        /**
         * The largest orthographic size whose view still fits inside every
         * bounded axis. Unbounded axes impose no limit.
         *
         * Example: level 60 wide, aspect 16:9 → size ≤ 30 / 1.778 ≈ 16.9
         */
        public float MaxOrthoSize(float aspect)
        {
            float max = float.PositiveInfinity;

            // Horizontal fit: half the level width converted through aspect to a size
            if (HorizontallyBounded)
                max = Mathf.Min(max, (right.value - left.value) * 0.5f / Mathf.Max(0.0001f, aspect));

            // Vertical fit: half the level height IS the orthographic size
            if (VerticallyBounded)
                max = Mathf.Min(max, (top.value - bottom.value) * 0.5f);

            return max;
        }

        /**
         * Clamps a camera centre so the whole view rect (given orthographic
         * size + aspect) stays inside every bounded edge. If an axis is
         * narrower than the view itself, the camera centres on that axis.
         */
        public Vector3 ClampCameraCentre(Vector3 centre, float orthoSize, float aspect)
        {
            Vector2 half = CameraViewUtil.ViewHalfExtents(orthoSize, aspect);

            centre.x = ClampAxis(centre.x, left, right, half.x);
            centre.y = ClampAxis(centre.y, bottom, top, half.y);
            return centre;
        }

        /**
         * Clamps one axis between its two edges, inset by the view half-extent.
         * Handles all four bounded/unbounded combinations; when both edges are
         * bounded but the span is narrower than the view, snaps to the middle.
         */
        private static float ClampAxis(float value, Edge low, Edge high, float halfExtent)
        {
            float min = low.bounded ? low.value + halfExtent : float.NegativeInfinity;
            float max = high.bounded ? high.value - halfExtent : float.PositiveInfinity;

            // View wider than the level on this axis — centre it rather than jitter between edges
            if (min > max) return (low.value + high.value) * 0.5f;

            return Mathf.Clamp(value, min, max);
        }

        // -------------------------------------------------------
        // Authoring
        // -------------------------------------------------------

        /** Overwrites all four edges at runtime and notifies listeners (e.g. on level transition). */
        public void SetEdges(Edge newLeft, Edge newRight, Edge newBottom, Edge newTop)
        {
            left = newLeft;
            right = newRight;
            bottom = newBottom;
            top = newTop;
            onBoundsChanged?.Invoke();
        }

        [FoldoutGroup("Level Config")]
        [Button("Derive From Level Config"), GUIColor(0.6f, 0.8f, 1f)]
        [EnableIf(nameof(levelConfig))]
        private void DeriveFromLevelConfig()
        {
            if (levelConfig == null) return;

            SetEdges(
                new Edge(true, -levelConfig.HalfWidth),
                new Edge(true, levelConfig.HalfWidth),
                new Edge(true, -levelConfig.TotalDepth),
                new Edge(true, 0f));
        }

        // -------------------------------------------------------
        // Scene lookup
        // -------------------------------------------------------

        private static LevelBounds _cached;

        /**
         * Cached scene lookup for consumers that can't be wired in the
         * inspector. Prefer serialized references where possible.
         */
        public static LevelBounds Find()
        {
            if (_cached == null)
                _cached = FindFirstObjectByType<LevelBounds>();
            return _cached;
        }

        private void OnDestroy()
        {
            if (_cached == this) _cached = null;
        }

        // -------------------------------------------------------
        // Gizmos
        // -------------------------------------------------------

#if UNITY_EDITOR
        /**
         * Draws the level rect: solid green lines for bounded edges, faint
         * dashed-looking stubs for unbounded ones so it's obvious which sides
         * are open. Unbounded axes fall back to ±500 for display only.
         */
        private void OnDrawGizmos()
        {
            const float openExtent = 500f;

            float l = left.bounded   ? left.value   : -openExtent;
            float r = right.bounded  ? right.value  : openExtent;
            float b = bottom.bounded ? bottom.value : -openExtent;
            float t = top.bounded    ? top.value    : openExtent;

            DrawEdge(new Vector3(l, b), new Vector3(l, t), left.bounded);    // left
            DrawEdge(new Vector3(r, b), new Vector3(r, t), right.bounded);   // right
            DrawEdge(new Vector3(l, b), new Vector3(r, b), bottom.bounded);  // bottom
            DrawEdge(new Vector3(l, t), new Vector3(r, t), top.bounded);     // top
        }

        /** Bounded edges draw solid green; unbounded edges draw as sparse grey dashes. */
        private static void DrawEdge(Vector3 a, Vector3 b, bool bounded)
        {
            if (bounded)
            {
                Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.9f);
                Gizmos.DrawLine(a, b);
                return;
            }

            // Dash the open edge: draw every other segment of 20 subdivisions
            Gizmos.color = new Color(0.6f, 0.6f, 0.6f, 0.35f);
            const int segments = 20;
            for (int i = 0; i < segments; i += 2)
            {
                Vector3 p0 = Vector3.Lerp(a, b, (float)i / segments);
                Vector3 p1 = Vector3.Lerp(a, b, (float)(i + 1) / segments);
                Gizmos.DrawLine(p0, p1);
            }
        }
#endif
    }
}
