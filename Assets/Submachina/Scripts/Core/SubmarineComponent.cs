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
        // Editor — identifying banner + feedback chips
        // -------------------------------------------------------

        private static GUIStyle _bannerTitleStyle;
        private static GUIStyle _bannerTypeStyle;
        private static GUIStyle _chipStyle;

        // Per-instance cache so we only reflect once
        private SubFeedback[] _cachedFeedbacks;
        private bool _feedbacksCached;

        /**
         * Draws an ocean-blue identifying banner at the very top of the inspector
         * for every component that inherits SubmarineComponent.
         *
         * If the concrete class carries a [UsesFeedbacks] attribute, the declared
         * feedback keys are rendered as colored chips below the banner so designers
         * can see at a glance which feedbacks this component triggers.
         */
        [PropertyOrder(-10000), OnInspectorGUI]
        private void DrawSubmarineComponentBanner()
        {
            EnsureStyles();

            // ── Main banner ──
            Rect rect = EditorGUILayout.GetControlRect(false, 24f);
            SirenixEditorGUI.DrawSolidRect(rect, new Color(0.09f, 0.27f, 0.40f));

            Rect accent = rect;
            accent.width = 4f;
            SirenixEditorGUI.DrawSolidRect(accent, new Color(0.30f, 0.75f, 0.95f));

            Rect iconRect = rect;
            iconRect.x += 12f;
            iconRect.y += (rect.height - 16f) * 0.5f;
            iconRect.width = iconRect.height = 16f;
            EditorIcons.Globe.Draw(iconRect);

            Rect titleRect = rect;
            titleRect.xMin = iconRect.xMax + 6f;
            GUI.Label(titleRect, "SUBMARINE COMPONENT", _bannerTitleStyle);

            Rect typeRect = rect;
            typeRect.xMax -= 8f;
            GUI.Label(typeRect, GetType().Name, _bannerTypeStyle);

            // ── Feedback chips ──
            if (!_feedbacksCached)
            {
                var attr = (UsesFeedbacksAttribute)System.Attribute.GetCustomAttribute(
                    GetType(), typeof(UsesFeedbacksAttribute));
                _cachedFeedbacks = attr?.Feedbacks;
                _feedbacksCached = true;
            }

            if (_cachedFeedbacks != null && _cachedFeedbacks.Length > 0)
                DrawFeedbackChips(_cachedFeedbacks);
        }

        /**
         * Renders a row of small colored chips showing each SubFeedback key
         * this component declares. Chips wrap onto a second line if the row
         * exceeds the available width.
         */
        private void DrawFeedbackChips(SubFeedback[] feedbacks)
        {
            // Dim bar behind the chips
            float chipHeight = 18f;
            float rowPad = 3f;

            // Measure chip widths to determine how many rows we need
            float labelWidth = 90f;
            float availWidth = EditorGUIUtility.currentViewWidth - 24f;
            float x = 8f + labelWidth + 4f;
            int rows = 1;
            for (int i = 0; i < feedbacks.Length; i++)
            {
                float w = _chipStyle.CalcSize(new GUIContent(feedbacks[i].ToString())).x + 10f;
                if (x + w > availWidth && i > 0) { rows++; x = 8f; }
                x += w + 4f;
            }

            float totalHeight = rows * (chipHeight + 2f) + rowPad * 2f;
            Rect bgRect = EditorGUILayout.GetControlRect(false, totalHeight);
            SirenixEditorGUI.DrawSolidRect(bgRect, new Color(0.12f, 0.12f, 0.14f));

            // Left accent — amber to visually separate from the blue banner
            Rect accentRect = bgRect;
            accentRect.width = 4f;
            SirenixEditorGUI.DrawSolidRect(accentRect, new Color(0.95f, 0.70f, 0.20f));

            // Row label
            GUI.color = new Color(0.95f, 0.70f, 0.20f);
            GUI.Label(new Rect(bgRect.x + 8f, bgRect.y + rowPad, labelWidth, chipHeight),
                "Feedback Keys", _bannerTitleStyle);
            GUI.color = Color.white;

            // Draw each chip after the label
            Color chipBg = new Color(0.22f, 0.22f, 0.26f);
            Color chipText = new Color(0.95f, 0.80f, 0.35f);
            var prevColor = GUI.color;

            x = bgRect.x + 8f + labelWidth + 4f;
            float y = bgRect.y + rowPad;

            for (int i = 0; i < feedbacks.Length; i++)
            {
                string label = feedbacks[i].ToString();
                float w = _chipStyle.CalcSize(new GUIContent(label)).x + 10f;

                if (x + w > bgRect.xMax - 4f && i > 0)
                {
                    x = bgRect.x + 8f;
                    y += chipHeight + 2f;
                }

                Rect chipRect = new Rect(x, y, w, chipHeight);
                SirenixEditorGUI.DrawSolidRect(chipRect, chipBg);

                GUI.color = chipText;
                GUI.Label(chipRect, label, _chipStyle);
                GUI.color = prevColor;

                x += w + 4f;
            }
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
                normal = { textColor = new Color(0.62f, 0.82f, 0.95f) }
            };
            _chipStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                normal = { textColor = new Color(0.95f, 0.80f, 0.35f) }
            };
        }
#endif
    }
}
