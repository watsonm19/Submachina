using UnityEngine;
using UnityEngine.UI;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Drives a UI Image fill to display the player's current health.
     *
     * Reads HealthPercent directly from the Health component each frame
     * and updates the bar's fill and tint color accordingly.
     *
     * Setup:
     *   1. On the same Canvas as O2Bar, add another Image.
     *   2. Set Image Type → Filled, Fill Method → Horizontal.
     *   3. Attach this script and assign the player's Health component.
     */
    [RequireComponent(typeof(Image))]
    public class HealthBar : MonoBehaviour
    {
        // =====================
        // References
        // =====================

        [FoldoutGroup("References")]
        [Tooltip("The player's Health component.")]
        [SerializeField] private Health playerHealth;

        // =====================
        // Colors
        // =====================

        [FoldoutGroup("Colors")]
        [Tooltip("Bar color at full health.")]
        [SerializeField] private Color healthyColor = new Color(0.2f, 1f, 0.4f);

        [FoldoutGroup("Colors")]
        [Tooltip("Bar color when health drops to or below the low threshold.")]
        [SerializeField] private Color lowColor = new Color(1f, 0.8f, 0.1f);

        [FoldoutGroup("Colors")]
        [Tooltip("Bar color at critical health (near zero).")]
        [SerializeField] private Color criticalColor = new Color(1f, 0.2f, 0.1f);

        [FoldoutGroup("Colors")]
        [Tooltip("Health percent at which the bar starts showing the low color. " +
                 "Example: 0.5 = turns yellow at 50% health.")]
        [SerializeField, Range(0f, 1f)] private float lowThreshold = 0.5f;

        [FoldoutGroup("Colors")]
        [Tooltip("Health percent at which the bar transitions to critical color. " +
                 "Example: 0.25 = turns red at 25% health.")]
        [SerializeField, Range(0f, 1f)] private float criticalThreshold = 0.25f;

        // =====================
        // State
        // =====================

        private Image _barImage;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
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
         * Sets fill amount directly from Health.HealthPercent (already 0-1).
         *
         * Color transitions:
         *   fill > lowThreshold      → healthyColor
         *   fill <= lowThreshold     → lerp toward lowColor
         *   fill <= criticalThreshold → lerp toward criticalColor
         */
        private void UpdateBar()
        {
            if (playerHealth == null) return;

            float fill = playerHealth.HealthPercent;
            _barImage.fillAmount = fill;

            if (fill <= criticalThreshold)
                _barImage.color = Color.Lerp(criticalColor, lowColor, fill / criticalThreshold);
            else if (fill <= lowThreshold)
                _barImage.color = Color.Lerp(lowColor, healthyColor, (fill - criticalThreshold) / (lowThreshold - criticalThreshold));
            else
                _barImage.color = healthyColor;
        }
    }
}
