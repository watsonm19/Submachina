using UnityEngine;
using UnityEngine.Audio;

namespace Core.Audio
{
    /**
     * Static definition of a one-shot sound effect: a pool of candidate clips, randomization
     * ranges, selection strategy, and spatialization. Carries no runtime state — the shuffle-bag
     * and cooldown state that go with this def live in AudioDirector, keyed by this asset, so the
     * same def can be triggered from many places without stepping on itself.
     */
    [CreateAssetMenu(fileName = "OneShot", menuName = "Submachina/Audio/One-Shot Def")]
    public class AudioOneShotDef : ScriptableObject
    {
        /// <summary>How a clip is chosen from Clips each time this def plays.</summary>
        public enum SelectionMode
        {
            /// <summary>Uniform random pick every time — clips can repeat back to back.</summary>
            Random,

            /// <summary>Draws from a shuffled bag that is refilled once emptied — even coverage, no immediate repeats.</summary>
            ShuffleBag
        }

        [Header("Clips")]
        public AudioClip[] clips;

        [Header("Randomization")]
        public Vector2 volumeRange = new Vector2(0.8f, 1f);
        public Vector2 pitchRange = new Vector2(0.95f, 1.05f);
        public SelectionMode selectionMode = SelectionMode.ShuffleBag;

        [Header("Throttling")]
        [Tooltip("Minimum seconds between plays of this def. 0 = no cooldown.")]
        public float cooldownSeconds = 0f;

        [Header("Spatialization")]
        [Tooltip("Only used by PlayOneShotAt — PlayOneShot is always 2D.")]
        [Range(0f, 1f)] public float spatialBlend = 0f;
        public float minDistance = 5f;
        public float maxDistance = 60f;

        [Header("Routing")]
        [Tooltip("Optional — assigned to the pooled AudioSource when set.")]
        public AudioMixerGroup mixerGroup;
    }
}
