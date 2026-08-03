using UnityEngine;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace Core.Modulation
{
    /**
     * Inspector-wireable wrapper around EnvironmentDirector.AddModifier(): lets UnityEvents,
     * rules, and scripted phases apply a temporary envelope-driven parameter influence without
     * code. Example: encounter begins → Apply() ramps Intensity to 1 over 20s and holds
     * until ReleaseModifier() is called.
     */
    public class ParameterModifierTrigger : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Director to modify. Leave empty to auto-find (parents first, then scene).")]
        [SerializeField] private EnvironmentDirector director;

        [SerializeField] private DirectorParameterDef parameter;

        [Header("Modifier")]
        [SerializeField] private ParameterBlendMode blendMode = ParameterBlendMode.Add;

        [Tooltip("Contribution value at full envelope weight.")]
        [SerializeField] private float value = 1f;

        [Tooltip("Seconds to ramp the influence in.")]
        [SerializeField] private float attackSeconds = 1f;

        [Tooltip("Seconds to hold at full strength. Negative = hold until ReleaseModifier() is called.")]
        [SerializeField] private float holdSeconds = -1f;

        [Tooltip("Seconds to fade the influence back out.")]
        [SerializeField] private float releaseSeconds = 2f;

        [Tooltip("Used for Override blending — highest priority wins.")]
        [SerializeField] private int priority;

        private ParameterModifier _active;

        /**
         * Applies the configured modifier. Re-applying while one is live releases the old
         * one first so influences never stack accidentally from repeated event firings.
         */
#if ODIN_INSPECTOR
        [Button("Apply (test)")]
#endif
        public void Apply()
        {
            if (director == null) director = EnvironmentDirector.FindFor(this);
            if (director == null || parameter == null) return;
            if (_active != null && _active.IsActive) _active.Release();
            _active = director.AddModifier(parameter, blendMode, value, attackSeconds, holdSeconds, releaseSeconds, priority, this);
        }

        /// <summary>Starts the release fade of the active modifier, if any.</summary>
#if ODIN_INSPECTOR
        [Button("Release (test)")]
#endif
        public void ReleaseModifier() => _active?.Release();

        /// <summary>Drops the active modifier instantly with no fade.</summary>
        public void CancelModifier() => _active?.CancelImmediate();

        private void OnDisable() => CancelModifier();
    }
}
