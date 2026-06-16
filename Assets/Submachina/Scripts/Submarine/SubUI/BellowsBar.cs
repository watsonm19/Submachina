using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * World-space charge indicator that floats above the submarine.
     *
     * Shows a single bar for whichever pump is currently active on the sub —
     * resolved each frame from Sub.Pumps.Active, so it follows the manual bellows
     * pump or the O2 intake pump automatically as control hands off between them.
     * No pump reference is wired by hand.
     *
     * Two tick marks indicate the sweet spot window — the bar turns green when
     * charge is inside it, signalling the player to release for a Perfect Pump.
     * The bar shows whenever a pump holds control and is ready (the active pump is
     * not on cooldown), and hides when no pump has control or it's cooling down.
     *
     * Air pressure is shown separately by the screen-space O2Bar in the HUD corner.
     *
     * No external assets required — a 1×1 white texture is created at runtime.
     *
     * Setup:
     *   1. Add this component to a child GameObject of the submarine
     *      (or directly on the submarine root) — it self-resolves its Submarine.
     *   2. Ensure a SubmarinePumpRouter and at least one pump live on the sub.
     *   3. Tweak WorldOffset to position above the sub sprite.
     */
    public class BellowsBar : SubmarineComponent
    {
        // =====================
        // Layout
        // =====================

        [FoldoutGroup("Layout")]
        [Tooltip("Position of the bar in the submarine's local space. " +
                 "Increase Y to push the bar higher above the sprite.")]
        [SerializeField] private Vector2 worldOffset = new Vector2(0f, 0.9f);

        [FoldoutGroup("Layout")]
        [Tooltip("Total width of the bar in world units.")]
        [SerializeField, Min(0.1f)] private float barWidth = 1.4f;

        [FoldoutGroup("Layout")]
        [Tooltip("Height of the charge bar.")]
        [SerializeField, Min(0.01f)] private float barHeight = 0.1f;

        [FoldoutGroup("Layout")]
        [Tooltip("Sorting order for the bar renderers. Set high enough to draw above the sub sprite.")]
        [SerializeField] private int sortingOrder = 10;

        // =====================
        // Colors
        // =====================

        [FoldoutGroup("Colors")]
        [Tooltip("Charge fill color while building charge outside the sweet spot.")]
        [SerializeField] private Color chargeNormalColor = new Color(0.4f, 0.8f, 1f);

        [FoldoutGroup("Colors")]
        [Tooltip("Charge fill color when charge is inside the sweet spot — signals perfect release.")]
        [SerializeField] private Color chargeSweetSpotColor = new Color(0.2f, 1f, 0.25f);

        [FoldoutGroup("Colors")]
        [Tooltip("Fill color while Air Lock is active.")]
        [SerializeField] private Color airLockColor = new Color(1f, 0.15f, 0.15f);

        [FoldoutGroup("Colors")]
        [SerializeField] private Color backgroundBarColor = new Color(0.08f, 0.08f, 0.08f, 0.85f);

        [FoldoutGroup("Colors")]
        [SerializeField] private Color sweetSpotMarkerColor = new Color(0.2f, 1f, 0.25f, 0.9f);

        // =====================
        // State
        // =====================

        private SpriteRenderer _chargeBg, _chargeFill;
        private SpriteRenderer _sweetLeft, _sweetRight;
        private Sprite _whiteSprite;
        private bool _visible = true;

        /** The pump currently being displayed — tracked so we reposition markers on hand-off. */
        private ISweetSpotPump _activePump;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();
            _whiteSprite = CreateWhiteSprite();
            BuildBar();
            SetVisible(false);   // stay hidden until a pump actually engages
        }

        /**
         * Each frame: resolve the active pump from the router, follow control
         * hand-offs (repositioning the sweet spot markers for the new pump), and
         * show the bar only while that pump is engaged (charging/looping or locked).
         */
        private void Update()
        {
            ISweetSpotPump active = Sub != null && Sub.Pumps != null ? Sub.Pumps.Active : null;

            // Control handed off to a different pump — re-anchor the sweet spot markers
            if (!ReferenceEquals(active, _activePump))
            {
                _activePump = active;
                if (_activePump != null) PositionSweetSpotMarkers(_activePump);
            }

            // Visible whenever a pump holds control and is ready — i.e. the manual
            // pump is enabled and not preempted by the intake pump (both folded into
            // "is the active pump"), and not sitting in a cooldown. Hidden otherwise.
            bool visible = _activePump != null && !_activePump.IsOnCooldown;
            if (visible != _visible) SetVisible(visible);

            if (_visible && _activePump != null) UpdateChargeBar();
        }

        /**
         * Anchors the two sweet spot ticks to the active pump's window.
         * Example: sweetSpotMin=0.65, barWidth=1.4 → leftX = -0.7 + 0.65×1.4 = 0.21
         */
        private void PositionSweetSpotMarkers(ISweetSpotPump pump)
        {
            _sweetLeft.transform.localPosition  = new Vector3(BarFillX(pump.SweetSpotMin), worldOffset.y, -0.01f);
            _sweetRight.transform.localPosition = new Vector3(BarFillX(pump.SweetSpotMax), worldOffset.y, -0.01f);
        }

        // -------------------------------------------------------
        // Visibility
        // -------------------------------------------------------

        /** Toggles all bar renderers at once — background, fill, and sweet spot markers. */
        private void SetVisible(bool visible)
        {
            _visible = visible;
            if (_chargeBg == null) return;   // called before Awake built the bar

            _chargeBg.enabled    = visible;
            _chargeFill.enabled  = visible;
            _sweetLeft.enabled   = visible;
            _sweetRight.enabled  = visible;
        }

        // -------------------------------------------------------
        // Bar Construction
        // -------------------------------------------------------

        /**
         * Procedurally creates the charge bar and sweet spot markers as child GameObjects.
         * Sweet spot marker X positions are finalised per-frame as the active pump changes.
         */
        private void BuildBar()
        {
            // Background track
            _chargeBg = CreateBarRenderer("ChargeBar_BG", backgroundBarColor,
                new Vector3(worldOffset.x, worldOffset.y, 0f),
                new Vector3(barWidth, barHeight, 1f));

            // Fill (starts at zero width)
            _chargeFill = CreateBarRenderer("ChargeBar_Fill", chargeNormalColor,
                new Vector3(worldOffset.x, worldOffset.y, 0f),
                new Vector3(0f, barHeight, 1f));

            // Sweet spot markers — thin vertical ticks slightly taller than the bar
            float markerWidth = Mathf.Max(0.02f, barWidth * 0.02f);

            _sweetLeft  = CreateBarRenderer("SweetSpot_L", sweetSpotMarkerColor,
                Vector3.zero, new Vector3(markerWidth, barHeight * 1.5f, 1f));

            _sweetRight = CreateBarRenderer("SweetSpot_R", sweetSpotMarkerColor,
                Vector3.zero, new Vector3(markerWidth, barHeight * 1.5f, 1f));
        }

        // -------------------------------------------------------
        // Per-Frame Update
        // -------------------------------------------------------

        /**
         * Scales the charge fill left-to-right based on ChargeProgress (0–1).
         * Tint logic:
         *   Air Lock active  → red
         *   Idle (charge=0)  → transparent (bar hidden)
         *   In sweet spot    → green (release now!)
         *   Otherwise        → cyan
         */
        private void UpdateChargeBar()
        {
            float charge    = _activePump.ChargeProgress;
            float fillWidth = barWidth * charge;

            // Anchor fill to the left edge of the background
            _chargeFill.transform.localPosition = new Vector3(
                worldOffset.x - barWidth * 0.5f + fillWidth * 0.5f,
                worldOffset.y,
                -0.01f);
            _chargeFill.transform.localScale = new Vector3(fillWidth, barHeight, 1f);

            if (_activePump.IsAirLocked)
            {
                _chargeBg.color   = airLockColor;
                _chargeFill.color = airLockColor;
            }
            else
            {
                _chargeBg.color = backgroundBarColor;
                _chargeFill.color = charge <= 0f    ? Color.clear
                    : _activePump.IsInSweetSpot      ? chargeSweetSpotColor
                                                     : chargeNormalColor;
            }
        }

        // -------------------------------------------------------
        // Helpers
        // -------------------------------------------------------

        /**
         * Converts a normalised charge value (0–1) to a local X position on the bar.
         * Example: value=0.65, barWidth=1.4 → x = -0.7 + 0.91 = 0.21
         */
        private float BarFillX(float normalised) =>
            worldOffset.x - barWidth * 0.5f + barWidth * normalised;

        /** Creates a SpriteRenderer child at the given local position and scale. */
        private SpriteRenderer CreateBarRenderer(string goName, Color color,
            Vector3 localPos, Vector3 localScale)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale    = localScale;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite       = _whiteSprite;
            sr.color        = color;
            sr.sortingOrder = sortingOrder;
            return sr;
        }

        /** Creates a 1×1 white sprite at runtime — no external asset needed. */
        private static Sprite CreateWhiteSprite()
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }
    }
}
