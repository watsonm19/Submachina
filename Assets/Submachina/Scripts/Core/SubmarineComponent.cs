using UnityEngine;
using Sirenix.OdinInspector;
#if UNITY_EDITOR
using UnityEditor;
using Sirenix.Utilities.Editor;
#endif

namespace Submachina.Core
{
    /**
     * Base class for any MonoBehaviour that belongs to a submarine.
     *
     * Automatically locates the parent Submarine via GetComponentInParent
     * and registers itself on Awake so the Submarine knows about all of
     * its subsystems. Unregisters on destroy to support runtime swapping —
     * destroy the old module, instantiate the new one, and the Submarine's
     * registry stays current.
     *
     * Derived classes that override Awake must call base.Awake() first
     * so that Sub is populated before any sibling lookups.
     */
    public abstract class SubmarineComponent : MonoBehaviour
    {
        /** The Submarine this component belongs to. Null if not under a Submarine hierarchy. */
        protected Submarine Sub { get; private set; }

        protected virtual void Awake()
        {
            Sub = GetComponentInParent<Submarine>();
            Sub?.Register(this);
        }

        protected virtual void OnDestroy()
        {
            Sub?.Unregister(this);
        }

#if UNITY_EDITOR
        // -------------------------------------------------------
        // Editor — identifying banner
        // -------------------------------------------------------

        // Cached styles, built lazily on first paint so we don't allocate every OnInspectorGUI call
        private static GUIStyle _bannerTitleStyle;
        private static GUIStyle _bannerTypeStyle;

        /**
         * Draws an ocean-blue identifying banner at the very top of the inspector
         * for every component that inherits SubmarineComponent.
         *
         * Because Odin renders inherited members, this single base-class method
         * decorates the whole family automatically — no per-component wiring. The
         * far-negative PropertyOrder guarantees the banner sits above each derived
         * component's own fields. Editor-only, so it is stripped from builds.
         */
        [PropertyOrder(-10000), OnInspectorGUI]
        private void DrawSubmarineComponentBanner()
        {
            // Build the styles once: a bold white title and a dim right-aligned type label
            if (_bannerTitleStyle == null)
            {
                _bannerTitleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    normal = { textColor = Color.white }
                };
                _bannerTypeStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleRight,
                    normal = { textColor = new Color(0.62f, 0.82f, 0.95f) }
                };
            }

            // Reserve the bar and paint the ocean-blue background
            Rect rect = EditorGUILayout.GetControlRect(false, 24f);
            SirenixEditorGUI.DrawSolidRect(rect, new Color(0.09f, 0.27f, 0.40f));

            // Bright accent stripe down the left edge for a little polish
            Rect accent = rect;
            accent.width = 4f;
            SirenixEditorGUI.DrawSolidRect(accent, new Color(0.30f, 0.75f, 0.95f));

            // Icon square, vertically centered in the bar
            Rect iconRect = rect;
            iconRect.x += 12f;
            iconRect.y += (rect.height - 16f) * 0.5f;
            iconRect.width = iconRect.height = 16f;
            EditorIcons.Globe.Draw(iconRect);

            // Title text, starting just past the icon
            Rect titleRect = rect;
            titleRect.xMin = iconRect.xMax + 6f;
            GUI.Label(titleRect, "SUBMARINE COMPONENT", _bannerTitleStyle);

            // Concrete component type name on the right, e.g. "DepthTracker"
            Rect typeRect = rect;
            typeRect.xMax -= 8f;
            GUI.Label(typeRect, GetType().Name, _bannerTypeStyle);
        }
#endif
    }
}
