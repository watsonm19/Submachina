using UnityEngine;
using UnityEngine.Audio;

namespace Core.Audio
{
    /**
     * Static definition of one ambience bed: what clip to loop, how it fades in/out, and how the
     * director's combined influence value (0..1) maps to output volume via a curve. Carries no
     * runtime state — AudioDirector owns all playback state so this asset can be shared safely
     * across scenes and director instances.
     */
    [CreateAssetMenu(fileName = "AmbienceLayer", menuName = "Submachina/Audio/Ambience Layer Def")]
    public class AmbienceLayerDef : ScriptableObject
    {
        [Header("Clip")]
        public AudioClip clip;
        public bool loop = true;
        [Range(0f, 1f)] public float baseVolume = 1f;

        [Header("Influence Mapping")]
        [Tooltip("Maps the director's combined influence (0..1) to a volume fraction of baseVolume.")]
        public AnimationCurve influenceCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Header("Fading")]
        [Tooltip("Seconds to cross a full 0..1 volume rise. 0 = snap instantly.")]
        public float fadeInSeconds = 2f;
        [Tooltip("Seconds to cross a full 1..0 volume fall. 0 = snap instantly.")]
        public float fadeOutSeconds = 3f;

        [Header("Playback")]
        [Range(-3f, 3f)] public float pitch = 1f;
        [Tooltip("Start playback at a random point in the clip instead of from the beginning.")]
        public bool randomStartPosition = true;

        [Header("Routing")]
        [Tooltip("Optional — assigned to the voice's AudioSource when set.")]
        public AudioMixerGroup mixerGroup;
    }
}
