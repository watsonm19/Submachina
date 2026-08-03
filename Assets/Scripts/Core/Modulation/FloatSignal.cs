using UnityEngine;

namespace Core.Modulation
{
    /**
     * Base class for raw world signals: measurable game facts (depth, speed, distance, timers).
     * A signal only reports state — it never decides what the value means creatively.
     * Interpretation (normalization, curves, weighting) happens in SignalContribution.
     */
    public abstract class FloatSignal : MonoBehaviour
    {
        /// <summary>Current raw value of this signal, in whatever native unit the signal measures.</summary>
        public abstract float Value { get; }

        /// <summary>False when the signal has no meaningful value yet (e.g. missing target). Invalid signals are skipped.</summary>
        public virtual bool IsValid => isActiveAndEnabled;
    }
}
