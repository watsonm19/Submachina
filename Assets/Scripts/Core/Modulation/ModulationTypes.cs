using UnityEngine;

namespace Core.Modulation
{
    /// <summary>
    /// How a contribution combines into a semantic parameter during composition.
    /// Evaluation order in the director: Add → Max → Min → Multiply → Override.
    /// </summary>
    public enum ParameterBlendMode
    {
        Add,
        Max,
        Min,
        Multiply,
        Override
    }

    /**
     * A single influence feeding a semantic parameter. Implemented by scene components
     * (SignalContribution) and by runtime ParameterModifiers (temporary scripted influences).
     * Weight is the blend strength in 0..1 — modifiers animate it via attack/hold/release envelopes.
     */
    public interface IParameterContribution
    {
        DirectorParameterDef Parameter { get; }
        ParameterBlendMode Blend { get; }
        int Priority { get; }
        bool IsActive { get; }
        float Weight { get; }

        /// <summary>Returns the contribution value for this frame. deltaTime lets envelope-driven contributions advance.</summary>
        float Evaluate(float deltaTime);
    }
}
