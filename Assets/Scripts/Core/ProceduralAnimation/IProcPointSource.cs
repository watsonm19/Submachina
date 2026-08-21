using UnityEngine;

namespace Core.ProceduralAnimation
{
    /**
     * A run of world-space points a procedural renderer can skin — implemented by
     * ChainSimulator (follow-the-leader chains) and IKLeg (2-bone legs), so
     * ChainStripRenderer can ribbon either without caring how the points move.
     */
    public interface IProcPointSource
    {
        /** Number of points in the run (head/hip first). */
        int PointCount { get; }

        /** Approximate distance between consecutive points — used for end-cap edge math. */
        float SegmentLength { get; }

        /** World-space position of point i. */
        Vector2 GetPoint(int i);

        /** Smoothed world-space tangent at point i (start → end direction). */
        Vector2 GetTangent(int i);

        /** World-space left-perpendicular at point i — the ribbon width axis. */
        Vector2 GetNormal(int i);

        /** Lays out a sensible rest pose — used for edit-mode previews and post-teleport snaps. */
        void PrepareEditorPreview();
    }
}
