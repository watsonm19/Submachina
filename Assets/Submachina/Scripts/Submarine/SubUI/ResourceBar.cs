using UnityEngine;
using UnityEngine.UI;
using UnityAtoms.BaseAtoms;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Drives a UI Image fill to show progress toward the next level up.
     *
     * Reads currentResources and resourceThreshold atoms each frame.
     * Fill = currentResources / resourceThreshold, giving a 0-1 progress bar
     * that resets each level as the threshold increases.
     *
     * Setup:
     *   1. Add another Image to the HUD Canvas, set Type → Filled, Horizontal.
     *   2. Attach this script and assign both atoms from the Data/ folder.
     */
    [RequireComponent(typeof(Image))]
    public class ResourceBar : MonoBehaviour
    {
        // =====================
        // References
        // =====================

        [FoldoutGroup("References")]
        [Tooltip("CurrentResources atom — written by ResourceManager on each collection.")]
        [SerializeField] private FloatVariable currentResources;

        [FoldoutGroup("References")]
        [Tooltip("ResourceThreshold atom — the target for the current level.")]
        [SerializeField] private FloatVariable resourceThreshold;

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

        private void Awake()
        {
            _barImage = GetComponent<Image>();
            _barImage.fillAmount = 0f;
        }

        private void Start()
        {
            // Read atoms after all Awake calls have run so ResourceManager
            // has already written its initial values to the atoms
            UpdateBar();
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
            if (currentResources == null || resourceThreshold == null) return;
            if (resourceThreshold.Value <= 0f) { _barImage.fillAmount = 0f; return; }

            float fill = Mathf.Clamp01(currentResources.Value / resourceThreshold.Value);
            _barImage.fillAmount = fill;
            _barImage.color = Color.Lerp(emptyColor, fullColor, fill);
        }
    }
}
