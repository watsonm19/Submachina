using UnityEngine;
using UnityEngine.UI;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Drives a UI Image fill to display its submarine's current health.
     *
     * Resolves the owning Submarine via GetComponentInParent (this bar lives
     * inside a submarine hierarchy) and reads HealthPercent from Sub.Health
     * each frame, updating the bar's fill and tint color accordingly. Polling
     * the facade per-frame keeps the bar correct across runtime health swaps
     * and works regardless of Awake ordering between this bar and the Submarine.
     *
     * Setup:
     *   1. Place this Image somewhere under the submarine root (e.g. a
     *      per-sub world/screen-space Canvas in the submarine hierarchy).
     *   2. Set Image Type → Filled, Fill Method → Horizontal.
     *   3. Attach this script — the Health source is found automatically.
     */
    [RequireComponent(typeof(Image))]
    public class HealthBar : MonoBehaviour
    {
        // =====================
        // References
        // =====================

        [FoldoutGroup("References")]
        [Tooltip("Optional explicit Health source. Leave empty to read from the " +
                 "owning Submarine (Sub.Health), resolved via GetComponentInParent.")]
        [SerializeField] private Health playerHealth;

        /** Owning submarine, resolved once in Awake. Null if not under a Submarine. */
        private Submarine _sub;

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

            // Resolve the owning submarine now (safe regardless of Awake order);
            // its Health slot is read later in Update, after registration settles.
            _sub = GetComponentInParent<Submarine>();
        }

        private void Update()
        {
            UpdateBar();
        }

        // -------------------------------------------------------
        // Display
        // -------------------------------------------------------

        /**
         * Sets fill amount from the active Health's HealthPercent (already 0-1).
         *
         * Health source resolves to the serialized override if assigned,
         * otherwise the owning submarine's Sub.Health.
         *
         * Color transitions:
         *   fill > lowThreshold      → healthyColor
         *   fill <= lowThreshold     → lerp toward lowColor
         *   fill <= criticalThreshold → lerp toward criticalColor
         */
        private void UpdateBar()
        {
            // Prefer an explicit override; fall back to the submarine facade.
            Health health = playerHealth != null ? playerHealth : _sub != null ? _sub.Health : null;
            if (health == null) return;

            float fill = health.HealthPercent;
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
