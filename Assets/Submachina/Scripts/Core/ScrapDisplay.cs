using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Displays the player's banked scrap as a row of dot indicators.
     *
     * Each dot slot corresponds to one scrap capacity. Filled dots show
     * banked scrap; empty dots show remaining capacity. The layout rebuilds
     * automatically when MaxScrap changes (e.g. from an upgrade), so the
     * number of dots always matches the current cap.
     *
     * The ScrapManager source resolves to the owning Submarine (Sub.Scrap) via
     * GetComponentInParent, so place this display inside the submarine hierarchy.
     * An explicit override may still be assigned. The Submarine reference is
     * cached in Awake, but Sub.Scrap is polled in Update so the display tolerates
     * Awake ordering and runtime scrap-module swaps.
     *
     * Setup:
     *   1. Create an empty GameObject under the submarine root — this is the root.
     *   2. Add a child GameObject with a HorizontalLayoutGroup for dot spacing.
     *   3. Attach this script to the root and assign the sprite/layout references.
     *   4. Supply two small circle/dot sprites: one empty, one filled.
     */
    public class ScrapDisplay : MonoBehaviour
    {
        // =====================
        // References
        // =====================

        /** Owning submarine, resolved once in Awake. Null if not under a Submarine. */
        private Submarine _sub;

        [FoldoutGroup("References")]
        [Tooltip("Parent transform with a HorizontalLayoutGroup. Dots are spawned as children of this.")]
        [SerializeField] private Transform dotContainer;

        // =====================
        // Sprites
        // =====================

        [FoldoutGroup("Sprites")]
        [Tooltip("Sprite shown for an empty scrap slot (no scrap banked in this slot).")]
        [SerializeField] private Sprite emptyDotSprite;

        [FoldoutGroup("Sprites")]
        [Tooltip("Sprite shown for a filled scrap slot (scrap banked).")]
        [SerializeField] private Sprite filledDotSprite;

        // =====================
        // Settings
        // =====================

        [FoldoutGroup("Settings")]
        [Tooltip("Size of each dot in pixels.")]
        [SerializeField] private Vector2 dotSize = new Vector2(24f, 24f);

        [FoldoutGroup("Settings")]
        [Tooltip("Color tint applied to filled dots.")]
        [SerializeField] private Color filledColor = Color.white;

        [FoldoutGroup("Settings")]
        [Tooltip("Color tint applied to empty dots.")]
        [SerializeField] private Color emptyColor = new Color(1f, 1f, 1f, 0.3f);

        // =====================
        // State
        // =====================

        private readonly List<Image> _dots = new List<Image>();
        private int _lastMaxScrap = -1;
        private int _lastCount = -1;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            // Resolve the owning submarine now (safe regardless of Awake order);
            // its Scrap slot is read later in Update, after registration settles.
            _sub = GetComponentInParent<Submarine>();
        }

        private void Update()
        {
            // Prefer an explicit override; fall back to the submarine facade.
            ScrapManager scrap = _sub != null ? _sub.Scrap : null;
            if (scrap == null) return;

            // Rebuild dot layout if the capacity has changed (e.g. from an upgrade)
            if (scrap.MaxScrap != _lastMaxScrap)
                RebuildDots(scrap);

            // Update sprite states only when the count changes
            if (scrap.ScrapCount != _lastCount)
                RefreshDots(scrap);
        }

        // -------------------------------------------------------
        // Layout
        // -------------------------------------------------------

        /**
         * Destroys all existing dot Images and recreates them to match
         * the current MaxScrap value. Called once at startup and again
         * whenever MaxScrap changes.
         */
        private void RebuildDots(ScrapManager scrap)
        {
            // Clear existing dots
            foreach (Image dot in _dots)
            {
                if (dot != null) Destroy(dot.gameObject);
            }
            _dots.Clear();

            // Spawn one dot per scrap slot
            for (int i = 0; i < scrap.MaxScrap; i++)
            {
                GameObject go = new GameObject($"ScrapDot_{i}", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(dotContainer, false);

                RectTransform rt = go.GetComponent<RectTransform>();
                rt.sizeDelta = dotSize;

                Image img = go.GetComponent<Image>();
                img.sprite = emptyDotSprite;
                img.color = emptyColor;

                _dots.Add(img);
            }

            _lastMaxScrap = scrap.MaxScrap;
            _lastCount = -1; // Force a sprite refresh on next Update
        }

        /**
         * Updates each dot's sprite and color to reflect the current
         * banked scrap count. Filled for indices below ScrapCount,
         * empty for the rest.
         *
         * Example: MaxScrap=3, ScrapCount=1 → [filled, empty, empty]
         */
        private void RefreshDots(ScrapManager scrap)
        {
            int count = scrap.ScrapCount;

            for (int i = 0; i < _dots.Count; i++)
            {
                bool filled = i < count;
                _dots[i].sprite = filled ? filledDotSprite : emptyDotSprite;
                _dots[i].color  = filled ? filledColor : emptyColor;
            }

            _lastCount = count;
        }
    }
}
