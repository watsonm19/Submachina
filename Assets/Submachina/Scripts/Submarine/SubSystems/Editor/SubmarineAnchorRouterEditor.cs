#if UNITY_EDITOR
using System.Collections.Generic;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Submachina.Core.Editor
{
    /**
     * Scene-view visualizer for SubmarineAnchorRouter.
     *
     * When the router is selected, draws a clickable marker at every anchor the
     * sub knows about. Clicking a marker selects and frames that anchor's
     * GameObject, so designers can jump straight to a mount point by name.
     *
     * Two data sources, picked automatically:
     *   • Play mode → the router's live Registry (reflects runtime module swaps).
     *   • Edit mode → a fresh GetComponentsInChildren sweep, since nothing has
     *                 registered yet (registration is a runtime-only path).
     *
     * Derives from OdinEditor so the router keeps its normal Odin inspector
     * (the SubmarineComponent banner, the Debug foldout) while we layer the
     * interactive scene handles on top.
     */
    [CustomEditor(typeof(SubmarineAnchorRouter))]
    public class SubmarineAnchorRouterEditor : OdinEditor
    {
        // Marker palette — matches the SubmarineAnchor gizmo blue for consistency.
        private static readonly Color MarkerColor = new(0.30f, 0.75f, 0.95f, 0.95f);
        private static readonly Color LineColor   = new(0.30f, 0.75f, 0.95f, 0.35f);

        private static GUIStyle _labelStyle;

        /**
         * Draws a clickable dot + label for each anchor whenever the router is
         * selected. Constant on-screen size keeps the markers readable at any
         * zoom level.
         */
        private void OnSceneGUI()
        {
            var router = (SubmarineAnchorRouter)target;
            EnsureStyle();

            // Walk whichever source matches the current mode (registry vs. sweep).
            foreach (var (key, point) in EnumerateAnchors(router))
            {
                if (point == null) continue;

                // Zoom-independent marker radius.
                float size = HandleUtility.GetHandleSize(point.position) * 0.15f;

                // Faint tether from the router root to the anchor for spatial context.
                Handles.color = LineColor;
                Handles.DrawLine(router.transform.position, point.position);

                // Clickable dot — select + ping + frame the anchor on click.
                Handles.color = MarkerColor;
                if (Handles.Button(point.position, Quaternion.identity, size, size * 1.4f, Handles.SphereHandleCap))
                {
                    Selection.activeGameObject = point.gameObject;
                    EditorGUIUtility.PingObject(point.gameObject);
                    if (SceneView.lastActiveSceneView != null)
                        SceneView.lastActiveSceneView.Frame(new Bounds(point.position, Vector3.one), false);
                }

                // Key label floating just above the dot.
                Handles.Label(point.position + Vector3.up * size * 2f, key.ToString(), _labelStyle);
            }
        }

        /**
         * Yields the (key, transform) pairs to draw. Play mode reads the live
         * registry so the view tracks runtime swaps; edit mode sweeps children
         * since nothing has registered yet. Both paths key off the same AnchorId,
         * so the displayed markers are identical either way.
         */
        private static IEnumerable<(AnchorId key, Transform point)> EnumerateAnchors(SubmarineAnchorRouter router)
        {
            if (Application.isPlaying)
            {
                foreach (var kv in router.Registry)
                    yield return (kv.Key, kv.Value != null ? kv.Value.Point : null);
            }
            else
            {
                var found = router.GetComponentsInChildren<SubmarineAnchor>(true);
                for (int i = 0; i < found.Length; i++)
                    yield return (found[i].Key, found[i].Point);
            }
        }

        /** Lazily builds the centered, blue-tinted label style (once per domain). */
        private static void EnsureStyle()
        {
            if (_labelStyle != null) return;
            _labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = MarkerColor }
            };
        }
    }
}
#endif
