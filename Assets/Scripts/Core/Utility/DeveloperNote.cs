using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Lightweight helper that lets designers attach free-form documentation to any GameObject.
/// Keeps notes searchable in the hierarchy and can optionally visualize them inside the Scene view.
/// </summary>
[AddComponentMenu("Utility/Developer Note")]
[DisallowMultipleComponent]
public sealed class DeveloperNote : MonoBehaviour
{
    [TextArea(4, 12)] [SerializeField]
    string noteText = "Describe the purpose, expectations, or open tasks for this object.";

#if UNITY_EDITOR
    [Header("Scene View Visualization")] [SerializeField]
    bool showInSceneView = true;

    [SerializeField] Color labelColor = Color.yellow;
    [SerializeField] Vector3 worldOffset = Vector3.up * 0.75f;
#endif

    /** Provides read-only access to the stored note text so other systems can surface it if desired. */
    public string NoteText => noteText;

#if UNITY_EDITOR
    /** Draws an editor-only label so the note is visible in the Scene view when desired. */
    void OnDrawGizmosSelected()
    {
        if (!GizmoFilter.IsVisible(this)) return;

        // Early-out if we do not want to visualize or there is nothing meaningful to show
        if (!showInSceneView || string.IsNullOrWhiteSpace(noteText)) return;

        // Build a lightweight style so the text is readable against most backgrounds
        var style = new GUIStyle(EditorStyles.helpBox)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = labelColor }
        };

        // Offset the label upward slightly so it does not overlap the object's pivot gizmo
        Handles.Label(transform.position + worldOffset, noteText, style);
    }
#endif
}

