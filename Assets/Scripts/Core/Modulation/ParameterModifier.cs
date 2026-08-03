using UnityEngine;

namespace Core.Modulation
{
    /**
     * A temporary, code-created influence on a semantic parameter with an attack/hold/release
     * envelope (e.g. "sonar pulse reduces Darkness for 0.8s", "finale forces Darkness to max").
     * Created via EnvironmentDirector.AddModifier(); the returned instance is its own handle:
     * call Release() to begin the release phase or CancelImmediate() to drop it instantly.
     */
    public class ParameterModifier : IParameterContribution
    {
        // --- Authoring values (fixed at creation) ---
        private readonly DirectorParameterDef _parameter;
        private readonly ParameterBlendMode _blend;
        private readonly float _value;
        private readonly float _attack;
        private readonly float _hold;      // < 0 means hold until Release() is called
        private readonly float _release;
        private readonly int _priority;
        private readonly Object _owner;
        private readonly bool _hasOwner;

        // --- Envelope state ---
        private float _age;
        private bool _releasing;
        private float _releaseStartAge;
        private float _releaseStartWeight;
        private bool _cancelled;

        public ParameterModifier(DirectorParameterDef parameter, ParameterBlendMode blend, float value,
            float attack, float hold, float release, int priority = 0, Object owner = null)
        {
            _parameter = parameter;
            _blend = blend;
            _value = value;
            _attack = Mathf.Max(0f, attack);
            _hold = hold;
            _release = Mathf.Max(0f, release);
            _priority = priority;
            _owner = owner;
            _hasOwner = owner != null;
        }

        public DirectorParameterDef Parameter => _parameter;
        public ParameterBlendMode Blend => _blend;
        public int Priority => _priority;
        public float Weight { get; private set; }

        // Dead when cancelled, fully released, or the owning object was destroyed.
        public bool IsActive => !Finished;

        public bool Finished =>
            _cancelled
            || (_releasing && _age - _releaseStartAge >= _release)
            || (_hasOwner && _owner == null);

        /**
         * Advances the envelope and returns the modifier's value. Weight ramps 0→1 over the
         * attack, holds, then ramps back to 0 over the release. The director applies Weight
         * as blend strength, so the influence fades in and out smoothly.
         */
        public float Evaluate(float deltaTime)
        {
            _age += deltaTime;

            // Auto-release once a finite hold period expires.
            if (!_releasing && _hold >= 0f && _age >= _attack + _hold) Release();

            // Compute envelope weight for the current phase.
            if (_releasing)
            {
                float t = _release <= 0f ? 1f : (_age - _releaseStartAge) / _release;
                Weight = Mathf.Lerp(_releaseStartWeight, 0f, Mathf.Clamp01(t));
            }
            else
            {
                Weight = _attack <= 0f ? 1f : Mathf.Clamp01(_age / _attack);
            }

            return _value;
        }

        /// <summary>Begins the release phase from the current envelope weight.</summary>
        public void Release()
        {
            if (_releasing) return;
            _releasing = true;
            _releaseStartAge = _age;
            _releaseStartWeight = Weight;
        }

        /// <summary>Drops the modifier immediately with no release fade.</summary>
        public void CancelImmediate() => _cancelled = true;
    }
}
