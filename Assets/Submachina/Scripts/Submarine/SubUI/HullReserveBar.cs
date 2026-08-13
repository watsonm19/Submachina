using UnityEngine;
using UnityEngine.UI;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Drives a UI Image fill to display the submarine's structural reserve —
     * how much margin is left before pressure + impact loads crack the hull.
     *
     * Reads HullSystem.ReserveFraction (0-1) live off the owning submarine
     * (Sub.Hull) and repaints only when it changes, mirroring HealthBar's cheap
     * per-frame compare. As a SubmarineObserver it resolves its sub from the
     * hierarchy, so each player's HUD tracks its own hull independently.
     *
     * The bar forces its critical color while the sub is InCrushZone (past its
     * rated depth, accruing pressure strain), even if the reserve fraction
     * itself sits above the critical threshold — active pressure damage is
     * always an emergency worth flagging.
     *
     * Setup:
     *   1. Place this Image under the submarine root (e.g. its Player Canvas).
     *   2. Set Image Type → Filled, Fill Method → Horizontal.
     */
    [RequireComponent(typeof(Image))]
    public class HullReserveBar : SubmarineObserver
    {
        // =====================
        // Colors
        // =====================

        [FoldoutGroup("Colors")]
        [Tooltip("Bar color at full structural reserve.")]
        [SerializeField] private Color healthyColor = new Color(0.2f, 0.85f, 1f);

        [FoldoutGroup("Colors")]
        [Tooltip("Bar color when reserve drops to or below the low threshold.")]
        [SerializeField] private Color lowColor = new Color(1f, 0.8f, 0.1f);

        [FoldoutGroup("Colors")]
        [Tooltip("Bar color at critical reserve (near zero), and forced while InCrushZone.")]
        [SerializeField] private Color criticalColor = new Color(1f, 0.2f, 0.1f);

        [FoldoutGroup("Colors")]
        [Tooltip("Reserve fraction at which the bar starts showing the low color. " +
                 "Example: 0.5 = turns amber at 50% reserve.")]
        [SerializeField, Range(0f, 1f)] private float lowThreshold = 0.5f;

        [FoldoutGroup("Colors")]
        [Tooltip("Reserve fraction at which the bar transitions to critical color. " +
                 "Example: 0.25 = turns red at 25% reserve.")]
        [SerializeField, Range(0f, 1f)] private float criticalThreshold = 0.25f;

        // =====================
        // State
        // =====================

        private Image _barImage;
        private float _lastFill = -1f;      // sentinel so the first valid read always paints
        private bool _lastCrushZone;

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
            // Resolve hull off the facade; bail until the sub has one
            HullSystem hull = Sub != null ? Sub.Hull : null;
            if (hull == null) return;

            // Repaint when either the fill value or the crush-zone state changes
            float fill = hull.ReserveFraction;
            bool crushZone = hull.InCrushZone;
            if (!Mathf.Approximately(fill, _lastFill) || crushZone != _lastCrushZone)
                UpdateBar(fill, crushZone);
        }

        // -------------------------------------------------------
        // Display
        // -------------------------------------------------------

        /**
         * Sets fill amount and tint from a normalized reserve value (already 0-1).
         *
         * Color transitions:
         *   fill > lowThreshold       → healthyColor
         *   fill <= lowThreshold      → lerp toward lowColor
         *   fill <= criticalThreshold → lerp toward criticalColor
         *   inCrushZone               → forced criticalColor, regardless of fill
         */
        private void UpdateBar(float fill, bool inCrushZone)
        {
            _lastFill = fill;
            _lastCrushZone = inCrushZone;
            _barImage.fillAmount = fill;

            if (inCrushZone)
                _barImage.color = criticalColor;
            else if (fill <= criticalThreshold)
                _barImage.color = Color.Lerp(criticalColor, lowColor, fill / criticalThreshold);
            else if (fill <= lowThreshold)
                _barImage.color = Color.Lerp(lowColor, healthyColor, (fill - criticalThreshold) / (lowThreshold - criticalThreshold));
            else
                _barImage.color = healthyColor;
        }
    }
}
