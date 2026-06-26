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

        /**
         * Maps the size bucket to a reflect-range multiplier.
         * Tiny barely shows up; Huge reflects from well outside the base range.
         * Example: Medium = 1.0 (base range), Huge = 1.6 (60% further).
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
