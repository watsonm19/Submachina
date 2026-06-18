using UnityEngine;
using Sirenix.OdinInspector;
#if UNITY_EDITOR
using UnityEditor;
using Sirenix.Utilities.Editor;
#endif

namespace Submachina.Core
{
    /**
     * Base class for a MonoBehaviour that *watches* a submarine without being
     * part of it.
     *
     * The semantic counterpart to SubmarineComponent: a component IS a piece of
     * the sub (it registers into the Submarine facade and provides function),
     * whereas an observer only LISTENS to a sub and contributes nothing back —
     * HUD bars, readouts, and other display-only elements. Observers never
     * register, so they never appear in the facade's subsystem slots.
     *
     * Because each player's UI lives inside its own submarine's hierarchy (e.g.
     * a per-sub "Player Canvas"), an observer resolves which sub it belongs to
     * purely by walking up the hierarchy — so two subs each get an independent,
     * correctly-wired HUD with no per-player asset duplication. An explicit
     * override may still be assigned for observers placed outside a sub
     * hierarchy (a shared minimap element, a spectator panel, etc.).
     *
     * Subsystem state is read live off the facade (Sub.O2, Sub.Health, ...),
     * so observers tolerate Awake ordering and runtime module swaps. Derived
     * classes that override Awake must call base.Awake() first so Sub is
     * populated before use.
     */
    public abstract class SubmarineObserver : MonoBehaviour
    {
        // =====================
        // Binding
        // =====================

        [FoldoutGroup("Observed Submarine")]
        [Tooltip("Optional explicit submarine to observe. Leave empty to auto-resolve " +
                 "the submarine this observer is nested under (the normal HUD case).")]
        [SerializeField] private Submarine submarineOverride;

        /** The submarine this observer watches. Null if not under a Submarine and no override is set. */
        protected Submarine Sub { get; private set; }

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected virtual void Awake()
        {
            ResolveSubmarine();
        }

        /**
         * Resolves the watched submarine: an explicit override wins, otherwise
         * the nearest Submarine in the parent chain (includes inactive parents
         * so the bind survives a disabled hierarchy at Awake time).
         */
        protected void ResolveSubmarine()
        {
            Sub = submarineOverride != null
                ? submarineOverride
                : GetComponentInParent<Submarine>(true);
        }

#if UNITY_EDITOR
        // -------------------------------------------------------
        // Editor — identifying banner
        // -------------------------------------------------------

        private static GUIStyle _bannerTitleStyle;
        private static GUIStyle _bannerTypeStyle;

        /**
         * Draws a teal identifying banner at the top of the inspector for every
         * SubmarineObserver — deliberately distinct from the blue SubmarineComponent
         * banner so designers can tell at a glance whether a script drives the sub
         * or merely watches it.
         */
        [PropertyOrder(-10000), OnInspectorGUI]
        private void DrawSubmarineObserverBanner()
        {
            EnsureStyles();

            Rect rect = EditorGUILayout.GetControlRect(false, 24f);
            SirenixEditorGUI.DrawSolidRect(rect, new Color(0.10f, 0.33f, 0.30f));

            // Left accent stripe
            Rect accent = rect;
            accent.width = 4f;
            SirenixEditorGUI.DrawSolidRect(accent, new Color(0.30f, 0.90f, 0.75f));

            Rect titleRect = rect;
            titleRect.xMin = rect.x + 12f;
            GUI.Label(titleRect, "SUBMARINE OBSERVER", _bannerTitleStyle);

            Rect typeRect = rect;
            typeRect.xMax -= 8f;
            GUI.Label(typeRect, GetType().Name, _bannerTypeStyle);
        }

        private static void EnsureStyles()
        {
            if (_bannerTitleStyle != null) return;

            _bannerTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white }
            };
            _bannerTypeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.62f, 0.95f, 0.85f) }
            };
        }
#endif
    }
}
