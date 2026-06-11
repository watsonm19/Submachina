using UnityEngine;
using UnityEngine.UI;
using UnityAtoms.BaseAtoms;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Drives a UI Image fill to display the player's current O2 level.
     *
     * Reads the CurrentO2 atom each frame and sets the Image's fillAmount
     * to a 0-1 normalised value. Also tints the bar based on O2 level:
     * healthy (cyan) → low (yellow) → critical/empty (red).
     *
     * Setup:
     *   1. Create a Canvas (Screen Space – Overlay).
     *   2. Add a child Image, set Image Type to Filled, Fill Method to Horizontal.
     *   3. Attach this script to that Image's GameObject.
     *   4. Assign the CurrentO2 atom and O2System reference.
     */
    [RequireComponent(typeof(Image))]
    public class O2Bar : MonoBehaviour
    {
        // =====================
        // References
        // =====================

        [FoldoutGroup("References")]
        [Tooltip("The CurrentO2 atom written by O2System. Used to read the live O2 value.")]
        [SerializeField] private FloatVariable currentO2;

        [FoldoutGroup("References")]
        [Tooltip("Reference to ManualBellowsPump to read MaxAir for normalisation.")]
        [SerializeField] private ManualBellowsPump pump;

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
         * Calculates the normalised fill (0-1) from the atom value and maxO2,
         * then updates both the fill amount and the tint color.
         *
         * Color transitions:
         *   fill > lowThreshold  → healthyColor
         *   fill <= lowThreshold → lowColor
         *   fill == 0            → criticalColor
         */
        private void UpdateBar()
        {
            if (currentO2 == null || pump == null) return;

            float fill    = pump.OriginalMaxAir > 0f ? currentO2.Value / pump.OriginalMaxAir : 0f;
            float maxFill = pump.OriginalMaxAir > 0f ? pump.MaxAir     / pump.OriginalMaxAir : 0f;

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
