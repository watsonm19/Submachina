using UnityEngine;
using UnityEngine.UI;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * HUD gauge for accumulated over-depth strain (Sub.Hull.StrainFraction) —
     * makes the pressure-damage ramp visible: the fuller the bar, the faster
     * the hull is bleeding HP, so the player can judge how long a dip past
     * rated depth is still survivable.
     *
     * Hidden entirely while there is no strain (the common case), it appears
     * on the first tick of strain and tints from earlyColor toward lateColor
     * as the ramp climbs. Polls per frame, repaints on change, one gauge per
     * sub (SubmarineObserver pattern).
     *
     * Setup:
     *   1. Place this Image under the sub's Player Canvas (near the depth gauge).
     *   2. Set Image Type → Filled, Fill Method → Horizontal.
     */
    [RequireComponent(typeof(Image))]
    public class PressureStrainBar : SubmarineObserver
    {
        // =====================
        // Colors
        // =====================

        [FoldoutGroup("Colors")]
        [Tooltip("Bar color at low strain — damage still trickling.")]
        [SerializeField] private Color earlyColor = new Color(1f, 0.8f, 0.1f);

        [FoldoutGroup("Colors")]
        [Tooltip("Bar color at full strain — damage ramp maxed out.")]
        [SerializeField] private Color lateColor = new Color(1f, 0.15f, 0.05f);

        // =====================
        // State
        // =====================

        private Image _barImage;
        private float _lastFill = -1f;      // sentinel so the first valid read always paints

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
            // Resolve hull off the facade; stay hidden until the sub has one
            HullSystem hull = Sub != null ? Sub.Hull : null;
            float fill = hull != null ? hull.StrainFraction : 0f;
            if (Mathf.Approximately(fill, _lastFill)) return;

            // Show only while strain exists — an empty gauge is just noise
            _lastFill = fill;
            _barImage.enabled = fill > 0f;
            _barImage.fillAmount = fill;
            _barImage.color = Color.Lerp(earlyColor, lateColor, fill);
        }
    }
}
