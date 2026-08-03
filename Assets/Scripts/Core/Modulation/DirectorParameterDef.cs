using UnityEngine;

namespace Core.Modulation
{
    /**
     * Authoring definition of a semantic parameter (Darkness, Threat, Tension...).
     * Pure edit-time data — runtime values live in the EnvironmentDirector, never in this asset.
     */
    [CreateAssetMenu(fileName = "Parameter", menuName = "Submachina/Director/Parameter Def")]
    public class DirectorParameterDef : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable id used for lookups and debug display. Defaults to the asset name when empty.")]
        [SerializeField] private string id;

        [TextArea]
        [Tooltip("What this parameter means creatively and what should feed/consume it.")]
        [SerializeField] private string description;

        [Header("Range")]
        [Tooltip("Composition starts from this value each frame before contributions are applied.")]
        public float baseValue = 0f;
        public float minValue = 0f;
        public float maxValue = 1f;

        [Header("Response")]
        [Tooltip("Smoothing time constant (seconds) used when the value is rising. 0 = snap.")]
        public float attackSeconds = 0.25f;

        [Tooltip("Smoothing time constant (seconds) used when the value is falling. 0 = snap.")]
        public float releaseSeconds = 1.5f;

        public string Id => string.IsNullOrEmpty(id) ? name : id;

        /// <summary>Clamps a composed value into this parameter's authored range.</summary>
        public float Clamp(float value) => Mathf.Clamp(value, minValue, maxValue);
    }
}
