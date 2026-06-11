using System.Globalization;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;

namespace Core.UI
{
    /// <summary>
    /// Flexible text receiver for a TextMeshPro element. Any value sent to it — via UnityEvents,
    /// Unity Atoms listeners, or code — is run through an optional format string and written to
    /// the TMP_Text on this GameObject. Pairs with AtomVariableTextBinder for automatic binding,
    /// but works standalone with any event source.
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    public class ValueTextDisplay : MonoBehaviour
    {
        [Title("Formatting")]
        [Tooltip("Composite format applied to incoming values, e.g. \"{0:0.0} m\" or \"Depth: {0:N0}\". Leave as {0} (or empty) for the raw value.")]
        [SerializeField] private string format = "{0}";

        // Cached TMP component so frequent updates don't repeat GetComponent lookups
        private TMP_Text _text;

        /// <summary>
        /// Set the text from a string value. (Distinct method names per type keep the
        /// UnityEvent dropdown unambiguous when wiring in the inspector.)
        /// </summary>
        public void SetText(string value) => Apply(value);

        /** Set the text from a float value, e.g. wired to a FloatVariable's Changed event. */
        public void SetFloat(float value) => Apply(value);

        /** Set the text from an int value, e.g. wired to an IntVariable's Changed event. */
        public void SetInt(int value) => Apply(value);

        /** Set the text from a bool value. */
        public void SetBool(bool value) => Apply(value);

        /// <summary>
        /// Set the text from any boxed value — the untyped entry point used by AtomVariableTextBinder.
        /// </summary>
        public void SetValue(object value) => Apply(value);

        /**
         * Formats the incoming value and pushes it to the TMP_Text.
         * Example: format "{0:0.0} m" with value 12.345f renders "12.3 m".
         */
        private void Apply(object value)
        {
            // Resolve the TMP component lazily so this works in edit mode too (Odin test button)
            if (_text == null) _text = GetComponent<TMP_Text>();

            // Format with invariant culture for consistent numeric output across machines
            _text.text = string.IsNullOrEmpty(format)
                ? value?.ToString() ?? string.Empty
                : string.Format(CultureInfo.InvariantCulture, format, value);
        }

        // --- Editor testing ---

        [Title("Testing")]
        [Tooltip("Sample value pushed through the formatter by the test button below.")]
        [SerializeField] private string testValue = "12.345";

        /**
         * Editor convenience: pushes the test value through the format string so the
         * result can be previewed without entering play mode. Parses numerics first so
         * numeric format specifiers (e.g. {0:0.0}) behave like they will at runtime.
         */
        [Button("Apply Test Value")]
        private void ApplyTestValue()
        {
            if (float.TryParse(testValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) Apply(f);
            else Apply(testValue);
        }
    }
}
