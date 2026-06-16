using UnityEngine;
using UnityEngine.UI;
using UnityAtoms.BaseAtoms;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Drives a UI Image fill to display its submarine's current health.
     *
     * Subscribes to the currentHealth atom — a normalized 0-1 value written by
     * Health whenever HP changes — and repaints only on change. No Submarine
     * lookup, no explicit Health reference, and no per-frame polling: the bar is
     * fully decoupled from the submarine hierarchy. Mirrors O2Bar's atom-driven
     * approach (O2Bar polls its atom each frame; this bar is event-driven since
     * health changes infrequently).
     *
     * Setup:
     *   1. Place this Image somewhere under the submarine root (e.g. a
     *      per-sub world/screen-space Canvas in the submarine hierarchy).
     *   2. Set Image Type → Filled, Fill Method → Horizontal.
     *   3. Assign the same currentHealth atom that the submarine's Health writes.
     */
    [RequireComponent(typeof(Image))]
    public class HealthBar : MonoBehaviour
    {
        // =====================
        // References
        // =====================

        [FoldoutGroup("References")]
        [Tooltip("Normalized health atom (0-1) written by Health. The bar subscribes " +
                 "to this atom's Changed event and repaints whenever health changes.")]
        [SerializeField] private FloatVariable currentHealth;

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

        /**
         * Subscribes to the atom's Changed event and paints the current value
         * immediately, so the bar is correct before the first change fires.
         */
        private void OnEnable()
        {
            if (currentHealth != null)
            {
                currentHealth.Changed.Register(UpdateBar);
                UpdateBar(currentHealth.Value);
            }
        }

        /** Unsubscribes to avoid dangling listeners when disabled or destroyed. */
        private void OnDisable()
        {
            if (currentHealth != null) currentHealth.Changed.Unregister(UpdateBar);
        }

        // -------------------------------------------------------
        // Display
        // -------------------------------------------------------

        /**
         * Sets fill amount and tint from a normalized health value (already 0-1).
         * Invoked by the currentHealth atom's Changed event.
         *
         * Color transitions:
         *   fill > lowThreshold       → healthyColor
         *   fill <= lowThreshold      → lerp toward lowColor
         *   fill <= criticalThreshold → lerp toward criticalColor
         */
        private void UpdateBar(float fill)
        {
            if (_barImage == null) return;

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
