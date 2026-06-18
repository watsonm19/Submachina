using UnityEngine;
using UnityEngine.UI;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Drives a UI Image fill to display its submarine's current health.
     *
     * Reads the normalised HealthPercent (0-1) live off the owning submarine
     * (Sub.Health) and repaints only when it changes — health changes
     * infrequently, so a cheap per-frame compare avoids needless work without
     * any event wiring. As a SubmarineObserver it resolves its sub from the
     * hierarchy, so each player's HUD tracks its own health with no shared
     * global state.
     *
     * Setup:
     *   1. Place this Image under the submarine root (e.g. its Player Canvas).
     *   2. Set Image Type → Filled, Fill Method → Horizontal.
     */
    [RequireComponent(typeof(Image))]
    public class HealthBar : SubmarineObserver
    {
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
        private float _lastFill = -1f;   // sentinel so the first valid read always paints

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
            // Resolve health off the facade; bail until the sub is ready
            Health health = Sub != null ? Sub.Health : null;
            if (health == null) return;

            // Repaint only when the value actually changes
            float fill = health.HealthPercent;
            if (!Mathf.Approximately(fill, _lastFill)) UpdateBar(fill);
        }

        // -------------------------------------------------------
        // Display
        // -------------------------------------------------------

        /**
         * Sets fill amount and tint from a normalized health value (already 0-1).
         *
         * Color transitions:
         *   fill > lowThreshold       → healthyColor
         *   fill <= lowThreshold      → lerp toward lowColor
         *   fill <= criticalThreshold → lerp toward criticalColor
         */
        private void UpdateBar(float fill)
        {
            _lastFill = fill;
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
