using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Coarse size buckets for a sonar contact.
     *
     * Drives two things: how far away the object can still reflect a ping
     * (bigger = detectable from further out — see SonarTarget.MaxReflectRange)
     * and the size readout the player earns at the Size progression tier.
     */
    public enum SonarSizeClass { Tiny, Small, Medium, Large, Huge }

    /**
     * Rhythm of a signature's return-ripple train — the most readable identity channel
     * (sonar operators identify by rhythm, players do too):
     *
     *   DistanceDriven — no identity rhythm; pulse count comes from proximity (the default).
     *   Single         — one crisp ring (hard inert objects: scrap, metal).
     *   DoubleBeat     — two quick pulses, a "heartbeat" (biological contacts).
     *   TripleBeat     — three quick pulses (agitated / unusual contacts).
     */
    public enum RipplePulsePattern { DistanceDriven, Single, DoubleBeat, TripleBeat }

    /**
     * The "sonic signature" of a sonar-detectable object — its identity as it
     * appears in a returning sonar wave.
     *
     * One asset per archetype (Fish, Octopus, Resource, Scrap, O2, Rock), created
     * via Create > Submachina > Sonar Signature and dropped onto a SonarTarget on
     * the entity's prefab. The same asset is shared by every instance of that
     * archetype, so designers author the look/sound of a contact once.
     *
     * The fields map onto the progression tiers: blipColor/sizeClass feed the
     * lower tiers (direction, size), while displayName/blipIcon are only revealed
     * once the player unlocks Identification.
     */
    [CreateAssetMenu(menuName = "Submachina/Sonar Signature")]
    public class SonarSignature : ScriptableObject
    {
        [Tooltip("Human-readable name revealed at the Identify tier (e.g. \"Octopus\").")]
        public string displayName;

        [PreviewField(48)]
        [Tooltip("Shape/icon identity drawn on the HUD blip once the Identify tier is unlocked.")]
        public Sprite blipIcon;

        [ColorUsage(true, true)]
        [Tooltip("Blip tint. HDR — push values above 1 so contacts bloom on the HUD.")]
        public Color blipColor = Color.cyan;

        [Tooltip("Coarse size bucket. Larger objects reflect pings from further away " +
                 "and surface a size readout at the Size tier.")]
        public SonarSizeClass sizeClass = SonarSizeClass.Medium;

        [Tooltip("Optional per-archetype return-ping cue played at the Identify tier so " +
                 "different objects 'sound' different. Leave empty to use the generic return ping.")]
        public FeedbackId returnPingFeedback;

        [Tooltip("Optional per-archetype return echo clip for the direct-AudioSource sonar " +
                 "(SonarReturnAudio, the documented MMF fallback). When set, this contact 'sounds' " +
                 "like itself instead of the generic beep. Leave empty to use SonarReturnAudio's " +
                 "own return/emit clip. This is the AudioClip analogue of returnPingFeedback above.")]
        public AudioClip returnPingClip;

        [Range(0f, 2f)]
        [Tooltip("Multiplies this object's reflect range. Harder/denser objects (rock, metal) " +
                 "reflect more strongly; soft creatures reflect less. 1 = neutral.")]
        public float reflectionStrength = 1f;

        // =====================
        // Ripple Voice
        // =====================
        // How this archetype's return WAVE looks (SonarReturnRipples applies these on top of
        // its distance mapping, gated behind a single identity tier). Neutral defaults mean
        // an unauthored signature ripples generically.

        [FoldoutGroup("Ripple Voice")]
        [Tooltip("Rhythm of the ripple train — the strongest identity cue. DistanceDriven keeps " +
                 "the generic proximity-based pulse count.")]
        public RipplePulsePattern ripplePulsePattern = RipplePulsePattern.DistanceDriven;

        [FoldoutGroup("Ripple Voice"), Range(0f, 4f)]
        [Tooltip("How strongly the return wave glows with this signature's blip color " +
                 "(hue is normalized, so this alone sets intensity). 0 = colorless refraction " +
                 "only; ~0.1–0.3 = subtle shimmer; >1 = overdriven beacon.")]
        public float rippleTintStrength = 0.2f;

        [FoldoutGroup("Ripple Voice"), Range(1f, 16f)]
        [Tooltip("Extra chromatic (R/B) fringing on the wavefront. 1 = neutral water; " +
                 "push up for metallic/crystalline contacts that 'glint'.")]
        public float rippleChromaticBoost = 1f;

        [FoldoutGroup("Ripple Voice"), Range(0.05f, 10f)]
        [Tooltip("Scales the wave cycles in the ring. >1 = tight ringing (hard/dense), " +
                 "<1 = one broad swell (soft/organic).")]
        public float rippleFrequencyScale = 1f;

        [FoldoutGroup("Ripple Voice"), Range(0.05f, 10f)]
        [Tooltip("Scales the wave's oscillation rate. >1 = jittery shimmer (metal), " +
                 "<1 = slow rolling swell (organic).")]
        public float ripplePhaseSpeedScale = 1f;

        [FoldoutGroup("Ripple Voice"), Range(0.05f, 10f)]
        [Tooltip("Scales the ring band width. <1 = thin precise line (hard), " +
                 ">1 = fat soft front (soft-bodied).")]
        public float rippleWidthScale = 1f;

        /**
         * Maps the size bucket to a presentation weight (Tiny 0.4 … Huge 1.6) used by the
         * return-wave visuals (SonarReturnRipples size-strength bias). NOT detection —
         * how far each size class can actually be detected is the sonar's own
         * "Detection by Size" config on SonarSystem.
         */
        public float SizeRangeFactor => sizeClass switch
        {
            SonarSizeClass.Tiny   => 0.4f,
            SonarSizeClass.Small  => 0.7f,
            SonarSizeClass.Medium => 1.0f,
            SonarSizeClass.Large  => 1.3f,
            SonarSizeClass.Huge   => 1.6f,
            _ => 1.0f
        };
    }
}
