using UnityEngine;
using UnityEngine.UI;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Drives a UI Image fill to show progress toward the next level up.
     *
     * Reads its submarine's resource progress live each frame (Sub.Resources):
     * fill = CurrentResources / CurrentThreshold, giving a 0-1 progress bar that
     * resets each level as the threshold increases. As a SubmarineObserver it
     * resolves its sub from the hierarchy, so each player's bar tracks its own
     * progression with no shared global state.
     *
     * Setup:
     *   1. Add an Image to the sub's Player Canvas, set Type → Filled, Horizontal.
     *   2. Attach this script.
     */
    [RequireComponent(typeof(Image))]
    public class ResourceBar : SubmarineObserver
    {
        // =====================
        // Colors
        // =====================

        [FoldoutGroup("Colors")]
        [Tooltip("Bar color at low progress.")]
        [SerializeField] private Color emptyColor = new Color(0.6f, 0.4f, 0.1f);

        [FoldoutGroup("Colors")]
        [Tooltip("Bar color when the bar is nearly full (close to level up).")]
        [SerializeField] private Color fullColor = new Color(1f, 0.9f, 0.2f);

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
            _barImage.fillAmount = 0f;
        }

        private void Update()
        {
            UpdateBar();
        }

        // -------------------------------------------------------
        // Display
        // -------------------------------------------------------

        private void UpdateBar()
        {
            // Resolve the resource manager off the facade; bail until the sub is ready
            ResourceManager resources = Sub != null ? Sub.Resources : null;
            if (resources == null) return;

            // Guard against a zero threshold (no valid progression target yet)
            if (resources.CurrentThreshold <= 0f) { _barImage.fillAmount = 0f; return; }

            float fill = Mathf.Clamp01(resources.CurrentResources / resources.CurrentThreshold);
            _barImage.fillAmount = fill;
            _barImage.color = Color.Lerp(emptyColor, fullColor, fill);
        }
    }
}
