using UnityEngine;
using UnityEngine.Audio;

namespace Core.Audio
{
    /**
     * Static definition of a stinger: a short, attention-grabbing cue (e.g. a threat sting) that
     * gates itself against spamming via per-def, per-category, and global cooldowns, and ducks
     * ambience while it plays. Carries no runtime state — AudioDirector owns cooldown timers,
     * shuffle-bag state, and the duck envelope so this asset can be shared safely.
     */
    [CreateAssetMenu(fileName = "Stinger", menuName = "Submachina/Audio/Stinger Def")]
    public class AudioStingerDef : ScriptableObject
    {
        [Header("Clips")]
        public AudioClip[] clips;

        [Header("Randomization")]
        public Vector2 volumeRange = new Vector2(0.9f, 1f);
        public Vector2 pitchRange = new Vector2(0.97f, 1.03f);
        public AudioOneShotDef.SelectionMode selectionMode = AudioOneShotDef.SelectionMode.ShuffleBag;

        [Header("Throttling")]
        [Tooltip("Minimum seconds between plays of this specific stinger.")]
        public float cooldownSeconds = 45f;

        [Tooltip("Stingers sharing a category throttle each other too, independent of their own cooldown.")]
        public string category = "Threat";
        public float categoryCooldownSeconds = 20f;

        [Header("Ducking")]
        [Tooltip("At full duck, ambience volume is multiplied by (1 - duckAmount).")]
        [Range(0f, 1f)] public float duckAmount = 0.6f;
        [Tooltip("Seconds to ramp ambience down into the duck.")]
        public float duckAttackSeconds = 0.15f;
        [Tooltip("Seconds to hold ambience at the fully ducked level.")]
        public float duckHoldSeconds = 1f;
        [Tooltip("Seconds to ramp ambience back up to unity gain.")]
        public float duckReleaseSeconds = 2.5f;

        [Header("Priority")]
        [Tooltip("Reserved for future arbitration between simultaneously requested stingers.")]
        public int priority = 0;

        [Header("Routing")]
        [Tooltip("Optional — assigned to the pooled AudioSource when set.")]
        public AudioMixerGroup mixerGroup;
    }
}
