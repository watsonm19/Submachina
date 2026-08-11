using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Compass-ring HUD for submarine sonar contacts.
     *
     * Watches its owning sub's SonarSystem (resolved through the hierarchy, like every
     * other SubmarineObserver) and spawns a fading blip on a ring around the sub each
     * time an echo returns. How much a blip reveals is gated by the current sonar tier,
     * so the same HUD progressively unlocks meaning as the player upgrades:
     *
     *   Presence  — a generic blip parked in the centre of the ring (no bearing, no range).
     *   Direction — the blip is placed by the contact's bearing around the ring.
     *   Size      — the blip's distance from centre encodes range; a distance label shows.
     *   Identify  — the blip takes the signature's icon, colour, and name.
     *
     * Independently of tier, an optional proximity tint blends each blip's colour from its
     * base colour (a contact at max range) toward the proximity colour (a contact right on
     * top of the sub), so closeness reads even before range is unlocked.
     *
     * Blips are built in code (no prefab required) so the HUD works out of the box; assign
     * a label font to enable the distance/name text. The outgoing pulse visual is left to
     * event wiring (SonarSystem.onPingEmitted → MMF / ripple), per the project's
     * compose-juice-from-events convention.
     *
     * Per-sub by construction: place this inside a submarine's Player Canvas and it tracks
     * that sub's sonar only — two subs each get an independent compass with no shared state.
     */
    public class SonarHud : SubmarineObserver
    {
        // =====================
        // References
        // =====================

        [FoldoutGroup("References")]
        [Tooltip("Centre of the compass ring. Blips are spawned as children and positioned " +
                 "relative to its centre. Leave empty to use this object's RectTransform.")]
        [SerializeField] private RectTransform ringContainer;

        // =====================
        // Layout
        // =====================

        [FoldoutGroup("Layout")]
        [Tooltip("Ring radius in canvas units — where a contact at max range sits (Size tier), " +
                 "and the fixed radius used at the Presence/Direction tiers.")]
        [SerializeField, Min(1f)] private float ringRadius = 120f;

        [FoldoutGroup("Layout")]
        [Tooltip("Smallest radius a blip can sit at (a contact right on top of the sub), " +
                 "so close contacts don't collapse onto the centre. Size tier only.")]
        [SerializeField, Min(0f)] private float minRadius = 24f;

        [FoldoutGroup("Layout")]
        [Tooltip("Size of each blip in canvas units.")]
        [SerializeField] private Vector2 blipSize = new Vector2(28f, 28f);

        // =====================
        // Appearance
        // =====================

        [FoldoutGroup("Appearance")]
        [Tooltip("Fallback blip sprite used below the Identify tier (or when a signature has no icon). " +
                 "A soft dot reads best. Leave empty for a plain square.")]
        [SerializeField] private Sprite blipSprite;

        [FoldoutGroup("Appearance")]
        [Tooltip("Colour of a blip before the Identify tier reveals the contact's own colour. " +
                 "With proximity colouring on, this is the colour of a contact at maximum range.")]
        [SerializeField, ColorUsage(true, true)] private Color neutralColor = new Color(0.4f, 0.9f, 1f, 1f);

        [FoldoutGroup("Appearance")]
        [Tooltip("Tint blips by how close the contact is: far contacts keep their base colour, " +
                 "close ones blend toward the proximity colour. Applies at every tier.")]
        [SerializeField] private bool colorByProximity = true;

        [FoldoutGroup("Appearance"), ShowIf(nameof(colorByProximity))]
        [Tooltip("Colour of a contact sitting right on top of the sub (distance 0).")]
        [SerializeField, ColorUsage(true, true)] private Color proximityCloseColor = new Color(1f, 0.25f, 0.2f, 1f);

        [FoldoutGroup("Appearance")]
        [Tooltip("Optional font for the distance/name label (Size tier and up). " +
                 "Leave empty to show blips without text.")]
        [SerializeField] private TMP_FontAsset labelFont;

        [FoldoutGroup("Appearance")]
        [Tooltip("Label font size in canvas units.")]
        [SerializeField, Min(1f)] private float labelFontSize = 12f;

        // =====================
        // Fade
        // =====================

        [FoldoutGroup("Fade")]
        [Tooltip("Seconds a blip lingers before fully fading out. Match this to the sonar's " +
                 "contact fade window for a consistent feel.")]
        [SerializeField, Min(0.1f)] private float blipFadeDuration = 4f;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private int LiveBlips => _blips.Count;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private SonarTier Tier => _sonar != null ? _sonar.CurrentTier : SonarTier.None;

        // =====================
        // State
        // =====================

        /** A live on-screen blip with its fade bookkeeping. */
        private class Blip
        {
            public GameObject go;
            public Image image;
            public TMP_Text label;
            public Color baseColor;
            public float expiry;
        }

        private SonarSystem _sonar;
        private readonly List<Blip> _blips = new();

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();
            if (ringContainer == null) ringContainer = transform as RectTransform;
        }

        private void Update()
        {
            // The SonarSystem slot settles after its own Awake registers, so lazily
            // bind the first frame it becomes available (mirrors the other observers).
            if (_sonar == null && Sub != null && Sub.Sonar != null)
            {
                _sonar = Sub.Sonar;
                _sonar.ContactReturned += OnContactReturned;
            }

            FadeBlips();
        }

        private void OnDestroy()
        {
            if (_sonar != null) _sonar.ContactReturned -= OnContactReturned;
        }

        // -------------------------------------------------------
        // Contact → blip
        // -------------------------------------------------------

        /**
         * Spawns a blip for a returned contact, styled and placed for the current tier.
         * Bearing, distance, colour, icon, and label are each gated so the HUD reveals
         * exactly as much as the player has unlocked.
         */
        private void OnContactReturned(SonarContact contact)
        {
            SonarTier tier = _sonar.CurrentTier;
            if (tier == SonarTier.None || ringContainer == null) return;

            // Normalised range of the contact (0 = on top of the sub, 1 = at max range).
            // Used for both radial placement and the proximity tint.
            float rangeT = Mathf.Clamp01(contact.Distance / Mathf.Max(0.01f, _sonar.ResolvedRange));

            // Bearing: only placed by direction once the Direction tier is unlocked,
            // otherwise pinned to the top of the ring as a generic "contact" mark.
            float angle = tier >= SonarTier.Direction && contact.Direction.sqrMagnitude > 0.0001f
                ? Mathf.Atan2(contact.Direction.y, contact.Direction.x)
                : Mathf.PI * 0.5f;

            // Radius by tier: Presence knows neither bearing nor range, so its blip sits dead
            // centre ("something is out there"); Direction rides the ring edge; Size and up
            // map the contact's actual distance onto the ring.
            float radius = tier <= SonarTier.Presence
                ? 0f
                : tier >= SonarTier.Size
                    ? Mathf.Lerp(minRadius, ringRadius, rangeT)
                    : ringRadius;
            Vector2 pos = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

            // Identity: colour + icon only revealed at the Identify tier.
            bool identified = tier >= SonarTier.Identify && contact.Signature != null;
            Color color = identified ? contact.Signature.blipColor : neutralColor;
            Sprite sprite = identified && contact.Signature.blipIcon != null
                ? contact.Signature.blipIcon : blipSprite;

            // Proximity tint: blend the base colour toward the close colour as range shrinks,
            // e.g. a contact at 25% of max range reads as 75% of the way to red. Alpha is left
            // to the base colour so the fade logic still owns transparency.
            if (colorByProximity)
            {
                float a = color.a;
                color = Color.Lerp(proximityCloseColor, color, rangeT);
                color.a = a;
            }

            // Label: distance at Size+, prefixed with the name once identified.
            string labelText = BuildLabel(tier, contact);

            _blips.Add(CreateBlip(pos, color, sprite, labelText));
        }

        /**
         * Builds the blip's text for the current tier: the contact name (Identify only)
         * over its distance (Size and up). Returns empty when nothing should be shown or
         * no label font is assigned.
         */
        private string BuildLabel(SonarTier tier, SonarContact contact)
        {
            if (labelFont == null || tier < SonarTier.Size) return string.Empty;

            string text = string.Empty;
            if (tier >= SonarTier.Identify && contact.Signature != null &&
                !string.IsNullOrEmpty(contact.Signature.displayName))
                text = contact.Signature.displayName + "\n";

            return text + Mathf.RoundToInt(contact.Distance) + "m";
        }

        /**
         * Constructs a blip GameObject (Image, optional TMP label) centred in the ring
         * container at the given anchored position. Built in code so no prefab is required.
         */
        private Blip CreateBlip(Vector2 pos, Color color, Sprite sprite, string labelText)
        {
            // Root image — the dot or signature icon.
            var go = new GameObject("SonarBlip", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(ringContainer, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = blipSize;
            rt.anchoredPosition = pos;

            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;

            // Optional label beneath the blip.
            TMP_Text label = null;
            if (!string.IsNullOrEmpty(labelText))
            {
                var lgo = new GameObject("Label", typeof(RectTransform));
                var lrt = (RectTransform)lgo.transform;
                lrt.SetParent(rt, false);
                lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0.5f, 1f);
                lrt.anchoredPosition = new Vector2(0f, -blipSize.y * 0.5f);
                lrt.sizeDelta = new Vector2(120f, 32f);

                label = lgo.AddComponent<TextMeshProUGUI>();
                label.font = labelFont;
                label.fontSize = labelFontSize;
                label.alignment = TextAlignmentOptions.Top;
                label.color = color;
                label.raycastTarget = false;
                label.text = labelText;
            }

            return new Blip { go = go, image = img, label = label, baseColor = color, expiry = Time.time + blipFadeDuration };
        }

        // -------------------------------------------------------
        // Fade
        // -------------------------------------------------------

        /**
         * Eases every blip's alpha toward zero across its fade window, destroying it once
         * elapsed. Iterates back-to-front so removals don't disturb the loop.
         */
        private void FadeBlips()
        {
            for (int i = _blips.Count - 1; i >= 0; i--)
            {
                var blip = _blips[i];
                if (blip.go == null) { _blips.RemoveAt(i); continue; }

                // Remaining lifetime as a 0..1 alpha (1 fresh, 0 expired).
                float alpha = Mathf.Clamp01((blip.expiry - Time.time) / blipFadeDuration);
                if (alpha <= 0f)
                {
                    Destroy(blip.go);
                    _blips.RemoveAt(i);
                    continue;
                }

                ApplyAlpha(blip, alpha);
            }
        }

        /** Pushes the fade alpha onto the blip's image and label. */
        private void ApplyAlpha(Blip blip, float alpha)
        {
            Color c = blip.baseColor;
            c.a = blip.baseColor.a * alpha;
            if (blip.image != null) blip.image.color = c;
            if (blip.label != null) blip.label.color = c;
        }
    }
}
