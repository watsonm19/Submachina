using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Drives a UI Image fill + status label showing the submarine's ballast
     * tank state (Sub.Ballast).
     *
     * Fill mirrors BallastTank.AirFraction (0 = flooded, sinking; 1 = full of
     * air, maximum lift) and tints from a deep "flooded" blue toward a light
     * "air" color as the tank fills. The companion TextMeshProUGUI shows the
     * commanded gear plus the intake-pump destination, e.g. "FULL ▲ → BALLAST",
     * refreshed only when either changes.
     *
     * Subs without a BallastTank simply don't show this element: the Image is
     * disabled and the label is cleared, so the bar quietly disappears rather
     * than showing a broken empty fill.
     *
     * Setup:
     *   1. Place under the submarine hierarchy (e.g. Player Canvas), like O2Bar.
     *   2. Set Image Type to Filled on the Image this script is attached to.
     *   3. Assign a small TextMeshProUGUI to destinationText for the label.
     */
    [RequireComponent(typeof(Image))]
    public class BallastBar : SubmarineObserver
    {
        // =====================
        // References
        // =====================

        [FoldoutGroup("References")]
        [Tooltip("Label showing the current pump destination, e.g. '→ BALLAST'.")]
        [SerializeField] private TextMeshProUGUI destinationText;

        // =====================
        // Colors
        // =====================

        [FoldoutGroup("Colors")]
        [Tooltip("Bar color at air fraction 1 (tank full of air — maximum lift).")]
        [SerializeField] private Color airColor = new Color(0.65f, 0.85f, 0.95f);

        [FoldoutGroup("Colors")]
        [Tooltip("Bar color at air fraction 0 (tank fully flooded — heavy, sinking).")]
        [SerializeField] private Color floodedColor = new Color(0.02f, 0.08f, 0.35f);

        // =====================
        // Labels
        // =====================

        [FoldoutGroup("Labels")]
        [Tooltip("Destination label text while pumped air routes to the O2 reserve.")]
        [SerializeField] private string o2Label = "→ O2";

        [FoldoutGroup("Labels")]
        [Tooltip("Destination label text while pumped air routes to the ballast tank.")]
        [SerializeField] private string ballastLabel = "→ BALLAST";

        // =====================
        // State
        // =====================

        private Image _barImage;
        private PumpDestination _lastDestination;
        private BallastMode _lastMode;
        private bool _hasLabelState;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();   // resolves Sub from the hierarchy
            _barImage = GetComponent<Image>();
        }

        private void Update()
        {
            UpdateBar();
        }

        // -------------------------------------------------------
        // Display
        // -------------------------------------------------------

        /**
         * Polls the ballast tank each frame (subsystem slots settle after
         * registration, same caveat as every other SubUI observer). With no
         * tank present the fill image and label are hidden; otherwise the fill
         * amount, color, and destination label are kept in sync.
         */
        private void UpdateBar()
        {
            BallastTank ballast = Sub != null ? Sub.Ballast : null;

            if (ballast == null)
            {
                _barImage.enabled = false;
                if (destinationText != null) destinationText.text = string.Empty;
                _hasLabelState = false;
                return;
            }

            _barImage.enabled = true;
            _barImage.fillAmount = ballast.AirFraction;
            _barImage.color = Color.Lerp(floodedColor, airColor, ballast.AirFraction);

            RefreshLabel(ballast.Mode, ballast.Destination);
        }

        /** Edge-triggered label refresh — only touches the text when the gear or destination flips. */
        private void RefreshLabel(BallastMode mode, PumpDestination destination)
        {
            if (destinationText == null) return;
            if (_hasLabelState && destination == _lastDestination && mode == _lastMode) return;

            string gear = mode switch
            {
                BallastMode.Full => "FULL ▲",
                BallastMode.Empty => "EMPTY ▼",
                _ => "NEUTRAL —",
            };
            destinationText.text = gear + "  " + (destination == PumpDestination.Ballast ? ballastLabel : o2Label);
            _lastDestination = destination;
            _lastMode = mode;
            _hasLabelState = true;
        }
    }
}
