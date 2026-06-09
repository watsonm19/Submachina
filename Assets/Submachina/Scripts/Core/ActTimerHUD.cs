using UnityEngine;
using TMPro;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Displays the act countdown timer and current act number in the HUD.
     *
     * Reads from an ActManager in the scene — assign it in the Inspector.
     * The timer text transitions from white → yellow → red as time runs low,
     * giving the player a clear urgency signal before the boss spawns.
     *
     * Setup:
     *   1. Add a TextMeshProUGUI element to the HUD Canvas.
     *   2. Attach this script, assign the ActManager reference and the TMP text.
     *   3. Optionally add a second TMP for the act label (Act 1, Act 2 ...).
     */
    public class ActTimerHUD : MonoBehaviour
    {
        // =====================
        // References
        // =====================

        [FoldoutGroup("References")]
        [Tooltip("The ActManager that owns the timer. Drag the GameManager object here.")]
        [SerializeField] private ActManager actManager;

        [FoldoutGroup("References")]
        [Tooltip("TMP text element that shows the MM:SS countdown.")]
        [SerializeField] private TextMeshProUGUI timerText;

        [FoldoutGroup("References")]
        [Tooltip("Optional TMP label showing 'Act 1', 'Act 2', etc. Leave empty to disable.")]
        [SerializeField] private TextMeshProUGUI actLabel;

        // =====================
        // Colors
        // =====================

        [FoldoutGroup("Colors")]
        [Tooltip("Timer color when plenty of time remains (above urgencyThreshold).")]
        [SerializeField] private Color normalColor = Color.white;

        [FoldoutGroup("Colors")]
        [Tooltip("Timer color when below urgencyThreshold — interpolates toward this.")]
        [SerializeField] private Color urgentColor = new Color(1f, 0.25f, 0.1f);

        [FoldoutGroup("Colors")]
        [Tooltip("Fraction of act duration at which color starts shifting toward urgentColor. " +
                 "Example: 0.2 → starts changing at 20% of time remaining.")]
        [SerializeField, Range(0.05f, 0.5f)] private float urgencyThreshold = 0.2f;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void LateUpdate()
        {
            if (actManager == null || timerText == null) return;

            UpdateTimer();
            UpdateActLabel();
        }

        // -------------------------------------------------------
        // Display
        // -------------------------------------------------------

        /**
         * Formats remaining time as MM:SS and transitions the color from
         * normal → urgent as the remaining fraction drops below urgencyThreshold.
         *
         * Example urgency lerp: remaining=60s, duration=420s → fraction=0.143.
         *   urgency = 1 - (0.143 / 0.2) = 0.285 → ~28% toward urgentColor.
         */
        private void UpdateTimer()
        {
            float remaining = actManager.RemainingTime;
            int minutes = Mathf.FloorToInt(remaining / 60f);
            int seconds = Mathf.FloorToInt(remaining % 60f);
            timerText.text = $"{minutes:D2}:{seconds:D2}";

            // Color urgency — 0 = normal, 1 = fully urgent
            float fraction = actManager.ActDuration > 0f
                ? remaining / actManager.ActDuration
                : 0f;

            float urgency = fraction < urgencyThreshold
                ? 1f - Mathf.Clamp01(fraction / urgencyThreshold)
                : 0f;

            timerText.color = Color.Lerp(normalColor, urgentColor, urgency);
        }

        /**
         * Updates the optional act label text. Does nothing if actLabel is null.
         */
        private void UpdateActLabel()
        {
            if (actLabel == null) return;
            actLabel.text = $"Act {actManager.Act}";
        }
    }
}
