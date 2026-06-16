#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Submachina.Core.Editor
{
    /**
     * Odin value drawer that renders FeedbackId fields as a searchable
     * categorized dropdown instead of a raw integer.
     *
     * Reflects over SubFeedbacks to discover all declared FeedbackId fields,
     * groups them by category using SubFeedbacks.CategoryNames, and rebuilds
     * the list on domain reload.
     */
    public class FeedbackIdDrawer : OdinValueDrawer<FeedbackId>
    {
        // Shared across all drawer instances — rebuilt once per domain reload.
        private static string[] _displayNames;
        private static FeedbackId[] _values;
        private static bool _built;

        protected override void DrawPropertyLayout(GUIContent label)
        {
            if (!_built) BuildEntries();

            // Read current value from the property.
            FeedbackId current = this.ValueEntry.SmartValue;

            // Resolve current selection index (uses FeedbackId.Equals via IEquatable).
            int currentIndex = Array.IndexOf(_values, current);
            if (currentIndex < 0) currentIndex = 0;

            // Draw a dropdown.
            var rect = EditorGUILayout.GetControlRect(label != null);
            if (label != null && label != GUIContent.none)
                rect = EditorGUI.PrefixLabel(rect, label);

            int newIndex = EditorGUI.Popup(rect, currentIndex, _displayNames);
            if (newIndex != currentIndex)
                this.ValueEntry.SmartValue = _values[newIndex];
        }

        /**
         * Discovers every static readonly FeedbackId field on SubFeedbacks,
         * groups by category, and builds the parallel display/value arrays.
         */
        private static void BuildEntries()
        {
            var entries = new List<(string display, FeedbackId id, int category)>();

            // "(None)" sentinel for unset / default-constructed FeedbackId.
            entries.Add(("(None)", default, -1));

            foreach (var field in typeof(SubFeedbacks).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(FeedbackId)) continue;

                var id = (FeedbackId)field.GetValue(null);
                string category = SubFeedbacks.CategoryNames.TryGetValue(id.Category, out var catName)
                    ? catName
                    : $"Category {id.Category}";

                entries.Add(($"{category}/{field.Name}", id, id.Category));
            }

            // Sort by category, then by name within each category.
            entries.Sort((a, b) =>
            {
                int cmp = a.category.CompareTo(b.category);
                return cmp != 0 ? cmp : string.Compare(a.display, b.display, StringComparison.Ordinal);
            });

            _displayNames = new string[entries.Count];
            _values = new FeedbackId[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                _displayNames[i] = entries[i].display;
                _values[i] = entries[i].id;
            }

            _built = true;
        }

        /** Reset on domain reload so new feedback definitions are picked up. */
        [InitializeOnLoadMethod]
        private static void Reset() => _built = false;
    }
}
#endif
