using UnityEngine;
using TMPro;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * HUD readout of current depth against the hull's rated depth, e.g.
     * "142m / 160m" — the player's primary answer to "how deep can I go?".
     *
     * Polls Sub.Hull every frame (SubmarineObserver pattern — each player's
     * HUD tracks its own sub) and repaints only when the rounded values or
     * danger state change. Color escalates as depth approaches rated depth:
     *
     *   below warningFraction of rated  → safeColor
     *   above warningFraction           → warningColor (getting close)
     *   past rated depth (InCrushZone)  → dangerColor (pressure damage active)
     *
     * Rated depth is live, so pump-to-hull boosts visibly push the number
     * deeper while they last — the gauge doubles as boost feedback.
     *
     * Setup: attach to a TextMeshProUGUI under the sub's Player Canvas.
     */
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class DepthGauge : SubmarineObserver
    {
        // =====================
        // Colors
        // =====================

        [FoldoutGroup("Colors")]
        [Tooltip("Text color while comfortably above rated depth.")]
        [SerializeField] private Color safeColor = new Color(0.7f, 0.9f, 1f);

        [FoldoutGroup("Colors")]
        [Tooltip("Text color while nearing rated depth (past warningFraction).")]
        [SerializeField] private Color warningColor = new Color(1f, 0.8f, 0.1f);

        [FoldoutGroup("Colors")]
        [Tooltip("Text color while past rated depth and taking pressure strain.")]
        [SerializeField] private Color dangerColor = new Color(1f, 0.2f, 0.1f);

        [FoldoutGroup("Colors")]
        [Tooltip("Fraction of rated depth where the readout turns to the warning color. Example: 0.85 → amber from 136 m on a 160 m rating.")]
        [SerializeField, Range(0f, 1f)] private float warningFraction = 0.85f;

        // =====================
        // State
        // =====================

        private TextMeshProUGUI _text;
        private int _lastDepth = int.MinValue;
        private int _lastRated = int.MinValue;
        private bool _lastDanger;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();   // resolves Sub from the hierarchy
            _text = GetComponent<TextMeshProUGUI>();
        }

        private void Update()
        {
            // Resolve hull off the facade; hide the readout until the sub has one
            HullSystem hull = Sub != null ? Sub.Hull : null;
            if (hull == null) { if (_text.text.Length > 0) _text.text = string.Empty; return; }

            // Repaint only when the rounded meters or the danger state change
            int depth = Mathf.RoundToInt(hull.Depth);
            int rated = Mathf.RoundToInt(hull.RatedDepth);
            bool danger = hull.InCrushZone;
            if (depth == _lastDepth && rated == _lastRated && danger == _lastDanger) return;

            _lastDepth = depth;
            _lastRated = rated;
            _lastDanger = danger;
            _text.text = $"{depth}m / {rated}m";
            _text.color = danger ? dangerColor
                : rated > 0 && depth >= rated * warningFraction ? warningColor
                : safeColor;
        }
    }
}
