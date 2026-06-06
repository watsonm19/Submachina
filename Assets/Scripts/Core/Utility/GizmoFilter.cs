using UnityEngine;
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
#endif

/**
 * Gizmo visibility filter — a static, scene-view-aware gating utility.
 *
 * Custom components call GizmoFilter.IsVisible(this) at the top of OnDrawGizmos to
 * respect the currently active filter mode.  In non-editor builds the method is a
 * trivial inline that always returns true, so there is zero runtime cost.
 *
 * Three modes are available (set via the Scene View Overlay or Tools > Gizmo Filter):
 *   Default           — all gizmos render normally (no filtering).
 *   SelectedObject    — only the currently selected GameObject(s) draw gizmos.
 *   SelectedHierarchy — selected GameObject(s) AND all of their descendants draw gizmos.
 */
public static class GizmoFilter
{
    // -------------------------------------------------------------------------
    // Runtime-safe public API
    // -------------------------------------------------------------------------

    /**
     * Returns true when the calling component's gizmos should be drawn.
     * In non-editor builds this is always true; filtering only runs in the editor.
     *
     * Usage inside OnDrawGizmos:
     *   if (!GizmoFilter.IsVisible(this)) return;
     */
    public static bool IsVisible(MonoBehaviour component)
    {
#if UNITY_EDITOR
        if (_mode == Mode.Default) return true;

        GameObject go = component.gameObject;

        // SelectedObject: check the live selection directly so there is no stale-cache risk.
        // Selection arrays are small so a linear scan is negligible.
        if (_mode == Mode.SelectedObject)
        {
            foreach (GameObject selected in Selection.gameObjects)
                if (selected == go) return true;
            return false;
        }

        // SelectedHierarchy: use the pre-built HashSet (populated from GetComponentsInChildren
        // which is too expensive to re-run every OnDrawGizmos call).
        return _visibleIds.Contains(go.GetInstanceID());
#else
        return true;
#endif
    }

    // -------------------------------------------------------------------------
    // Editor-only state and helpers
    // -------------------------------------------------------------------------

#if UNITY_EDITOR

    /** The three gizmo-filter modes the user can select. */
    public enum Mode
    {
        Default,            // Show all gizmos — no change from Unity default behaviour
        SelectedObject,     // Gizmos only on the selected object(s)
        SelectedHierarchy   // Gizmos on the selected object(s) and all children
    }

    // Active mode and the set of instance-IDs that are currently allowed to draw
    private static Mode _mode = Mode.Default;
    private static readonly HashSet<int> _visibleIds = new HashSet<int>();

    /** Gets or sets the active filter mode and refreshes the visible set immediately. */
    public static Mode CurrentMode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            RefreshVisibleSet();
            SceneView.RepaintAll();
        }
    }

    /**
     * Rebuilds the set of visible instance IDs from the current editor selection.
     * Called automatically when the selection changes or the mode is switched.
     */
    internal static void RefreshVisibleSet()
    {
        _visibleIds.Clear();
        if (_mode == Mode.Default) return;

        foreach (GameObject go in Selection.gameObjects)
        {
            _visibleIds.Add(go.GetInstanceID());

            // Hierarchy mode: include all descendants (inactive ones too)
            if (_mode == Mode.SelectedHierarchy)
            {
                foreach (Transform child in go.GetComponentsInChildren<Transform>(true))
                    _visibleIds.Add(child.gameObject.GetInstanceID());
            }
        }
    }

    /**
     * Subscribes GizmoFilter to Unity's selection-changed callback.
     * Called once by GizmoFilterManager's [InitializeOnLoad] constructor.
     */
    public static void Initialize()
    {
        // Guard against double-subscription across domain reloads
        Selection.selectionChanged -= RefreshVisibleSet;
        Selection.selectionChanged += RefreshVisibleSet;
    }

    /** Removes the selection-changed subscription. */
    public static void Cleanup()
    {
        Selection.selectionChanged -= RefreshVisibleSet;
    }

#endif // UNITY_EDITOR
}
