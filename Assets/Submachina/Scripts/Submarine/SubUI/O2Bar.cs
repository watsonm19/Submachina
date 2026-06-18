using UnityEngine;
using UnityEngine.UI;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Drives a UI Image fill to display its submarine's current O2 level.
     *
     * Reads air state live off the owning submarine each frame (Sub.O2) and sets
     * the Image's fillAmount to a 0-1 normalised value. Also tints the bar based
     * on O2 level: healthy (cyan) → low (yellow) → critical/empty (red).
     *
     * As a SubmarineObserver it resolves its sub from the hierarchy, so each
     * player's HUD reads its own air tank with no shared global state — drop one
     * inside each sub's Player Canvas and local multiplayer just works.
     *
     * Setup:
     *   1. Place this Image under the submarine root (e.g. its Player Canvas).
     *   2. Set Image Type to Filled, Fill Method to Horizontal.
     *   3. Attach this script to that Image's GameObject.
     */
    [RequireComponent(typeof(Image))]
    public class O2Bar : SubmarineObserver
    {
        // =====================
        // References
        // =====================

        [FoldoutGroup("References")]
        [Tooltip("A second Filled Image (same rect as the main bar, placed behind it in the hierarchy) " +
                 "whose fillAmount tracks the current max capacity. Give it a dim/semi-transparent color " +
                 "so it peeks out beyond the main fill when capacity has degraded.")]
        [SerializeField] private Image capacityBar;

        // =====================
        // Colors
        // =====================

        [FoldoutGroup("Colors")]
        [Tooltip("Bar color when O2 is full or healthy (above lowThreshold).")]
        [SerializeField] private Color healthyColor = new Color(0.2f, 0.85f, 1f);

        [FoldoutGroup("Colors")]
        [Tooltip("Bar color when O2 is at or below the low threshold.")]
        [SerializeField] private Color lowColor = new Color(1f, 0.8f, 0.1f);

        [FoldoutGroup("Colors")]
        [Tooltip("Bar color when O2 is empty (health bleed active).")]
        [SerializeField] private Color criticalColor = new Color(1f, 0.2f, 0.1f);

        [FoldoutGroup("Colors")]
        [Tooltip("Normalised O2 level at which the bar starts showing the low color. " +
                 "Example: 0.3 = turns yellow at 30% O2.")]
        [SerializeField, Range(0f, 1f)] private float lowThreshold = 0.3f;

        // =====================
        // State
        // =====================

        private Image _barImage;

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
         * Calculates the normalised fill (0-1) from the sub's air state, then
         * updates both the fill amount and the tint color. The ghost capacity
         * bar tracks the current (degraded) max against the original ceiling.
         *
         * Color transitions:
         *   fill > lowThreshold  → healthyColor
         *   fill <= lowThreshold → lowColor
         *   fill == 0            → criticalColor
         */
        private void UpdateBar()
        {
            // Resolve the air system off the facade; bail until the sub is ready
            O2System o2 = Sub != null ? Sub.O2 : null;
            if (o2 == null) return;

            float ceiling = o2.OriginalMaxAir;
            float fill    = ceiling > 0f ? o2.CurrentAirPressure / ceiling : 0f;
            float maxFill = ceiling > 0f ? o2.MaxAir / ceiling : 0f;

            _barImage.fillAmount = fill;
            if (capacityBar != null) capacityBar.fillAmount = maxFill;

            if (fill <= 0f)
                _barImage.color = criticalColor;
            else if (fill <= lowThreshold)
                _barImage.color = Color.Lerp(criticalColor, lowColor, fill / lowThreshold);
            else
                _barImage.color = Color.Lerp(lowColor, healthyColor, (fill - lowThreshold) / (1f - lowThreshold));
        }
    }
}
