using UnityEngine;

namespace Core.Audio
{
    /**
     * Reports seconds elapsed since a stinger last played, so EnvironmentDirector contributions
     * or other gameplay rules can react to recent stinger activity (e.g. suppress a spawn shortly
     * after a threat sting). An empty category reports seconds since ANY stinger played.
     */
    public class StingerTimerSignal : Core.Modulation.FloatSignal
    {
        [Header("Source")]
        [Tooltip("Resolved automatically via AudioDirector.FindFor() when left empty.")]
        [SerializeField] private AudioDirector audioDirector;

        [Tooltip("Stinger category to time. Empty = seconds since any stinger, of any category.")]
        [SerializeField] private string category;

        private void Awake()
        {
            if (audioDirector == null) audioDirector = AudioDirector.FindFor(this);
        }

        private void OnEnable()
        {
            if (audioDirector == null) audioDirector = AudioDirector.FindFor(this);
        }

        /// <summary>Seconds since the relevant stinger scope last played (or 99999 if never).</summary>
        public override float Value
        {
            get
            {
                if (audioDirector == null) return 99999f;
                return string.IsNullOrEmpty(category)
                    ? audioDirector.SecondsSinceAnyStinger
                    : audioDirector.SecondsSinceCategory(category);
            }
        }

        /// <summary>Invalid until an AudioDirector has been resolved.</summary>
        public override bool IsValid => isActiveAndEnabled && audioDirector != null;
    }
}
