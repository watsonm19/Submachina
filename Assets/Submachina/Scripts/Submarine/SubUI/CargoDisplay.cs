using System.Text;
using UnityEngine;
using TMPro;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Displays the submarine's current cargo hold contents as rich-text lines
     * on a TextMeshProUGUI: a "CARGO used/capacity" header followed by one line
     * per resource type currently held, tinted with that type's signature color.
     *
     * As a SubmarineObserver, Sub.Cargo is polled in Update (null-tolerant) so
     * the display tolerates Awake ordering and survives the hold not yet being
     * registered — it simply shows nothing until Sub.Cargo resolves. The text
     * is only rebuilt when the hold's total units or capacity actually change,
     * avoiding per-frame string churn for a display that updates rarely.
     *
     * Setup:
     *   1. Place under the submarine hierarchy (or set an explicit override).
     *   2. Add a TextMeshProUGUI and assign it to cargoText.
     */
    public class CargoDisplay : SubmarineObserver
    {
        // =====================
        // References
        // =====================

        [FoldoutGroup("References")]
        [Tooltip("Text element the cargo readout is rendered into.")]
        [SerializeField] private TextMeshProUGUI cargoText;

        // =====================
        // Settings
        // =====================

        [FoldoutGroup("Settings")]
        [Tooltip("Header label prefixed to the used/capacity line, e.g. 'CARGO 7/20'.")]
        [SerializeField] private string headerLabel = "CARGO";

        // =====================
        // State
        // =====================

        private readonly StringBuilder _builder = new StringBuilder();
        private int _lastTotalUnits = -1;
        private int _lastCapacity = -1;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Update()
        {
            // Resolve the cargo hold off the facade; its slot settles after
            // registration, so we poll here rather than caching in Awake.
            CargoHold cargo = Sub != null ? Sub.Cargo : null;
            if (cargo == null || cargoText == null) return;

            // Cheap change check: total units + capacity together imply the
            // contents dictionary hasn't changed shape without hashing it.
            if (cargo.TotalUnits == _lastTotalUnits && cargo.Capacity == _lastCapacity) return;

            RefreshText(cargo);
            _lastTotalUnits = cargo.TotalUnits;
            _lastCapacity = cargo.Capacity;
        }

        // -------------------------------------------------------
        // Display
        // -------------------------------------------------------

        /**
         * Rebuilds the display string: a header line showing total units used
         * out of capacity, then one tinted line per held resource type.
         * Example:
         *   CARGO 7/20
         *   <color=#8A7F72>Ferrite Nodules x5</color>
         *   <color=#C9A227>Vent Brass x2</color>
         */
        private void RefreshText(CargoHold cargo)
        {
            _builder.Clear();
            _builder.Append(headerLabel).Append(' ').Append(cargo.TotalUnits).Append('/').Append(cargo.Capacity);

            foreach (var kvp in cargo.Contents)
            {
                ResourceType type = kvp.Key;
                if (type == null || kvp.Value <= 0) continue;

                string hexColor = ColorUtility.ToHtmlStringRGB(type.tint);
                _builder.Append('\n')
                    .Append("<color=#").Append(hexColor).Append('>')
                    .Append(type.displayName).Append(" x").Append(kvp.Value)
                    .Append("</color>");
            }

            cargoText.text = _builder.ToString();
        }
    }
}
