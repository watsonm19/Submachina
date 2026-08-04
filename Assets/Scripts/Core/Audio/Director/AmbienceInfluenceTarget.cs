using UnityEngine;

namespace Core.Audio
{
    /**
     * Routes a ModulatedFloatTarget's composited value into AudioDirector.SetAmbienceInfluence
     * for one ambience layer. Typically fed by a FloatRoute reading an EnvironmentDirector
     * parameter (e.g. Depth or Threat) so ambience beds crossfade automatically as that
     * parameter changes, without the parameter system knowing anything about audio.
     */
    public class AmbienceInfluenceTarget : Core.Modulation.ModulatedFloatTarget
    {
        [Header("Ambience")]
        [Tooltip("Resolved automatically via AudioDirector.FindFor() when left empty.")]
        [SerializeField] private AudioDirector audioDirector;

        [Tooltip("Ambience layer this target drives the influence of.")]
        [SerializeField] private AmbienceLayerDef layer;

        // Read-only wiring accessors for editor tooling (Director Graph window).
        public AmbienceLayerDef Layer => layer;
        public AudioDirector Director => audioDirector;

        public override string TargetDescription => "Ambience · " + (layer != null ? layer.name : "(no layer)");

        private void Awake()
        {
            if (audioDirector == null) audioDirector = AudioDirector.FindFor(this);
        }

        protected override void OnEnable()
        {
            base.OnEnable(); // resolve the parameter binding in the base class
            if (audioDirector == null) audioDirector = AudioDirector.FindFor(this);
        }

        /**
         * Pushes zero influence for this target's key when disabled so the ambience layer can
         * fade back out even though this target has stopped writing new values entirely.
         */
        private void OnDisable()
        {
            if (audioDirector != null && layer != null) audioDirector.SetAmbienceInfluence(layer, this, 0f);
        }

        /// <summary>Forwards the composited value (clamped to 0..1) as this layer's influence, keyed by this target.</summary>
        protected override void ApplyValue(float value)
        {
            if (audioDirector == null || layer == null) return;
            audioDirector.SetAmbienceInfluence(layer, this, Mathf.Clamp01(value));
        }
    }
}
