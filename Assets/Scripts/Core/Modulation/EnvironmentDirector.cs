using System.Collections.Generic;
using System.Text;
using UnityEngine;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace Core.Modulation
{
    /**
     * Central evaluator for semantic parameters (Darkness, Threat, Tension...).
     * Contributions register themselves (SignalContribution components, runtime
     * ParameterModifiers); each frame the director composes them per parameter,
     * clamps to the authored range, then applies attack/release smoothing.
     *
     * Composition order (documented in the dev plan):
     *   base value → Add → Max → Min → Multiply → Override (highest priority) → clamp → smooth.
     *
     * Not a singleton: consumers resolve a director via FindFor(), preferring one in
     * their own hierarchy so multiple directors can coexist (e.g. split-screen rigs).
     */
    public class EnvironmentDirector : MonoBehaviour
    {
        /**
         * Per-parameter runtime state. Lives here, never in the ScriptableObject definition,
         * so edit-time assets stay clean of play-mode values.
         */
        private class ParameterRuntime
        {
            public DirectorParameterDef Def;
            public readonly List<IParameterContribution> Contributions = new List<IParameterContribution>();
            public float Current;
            public float Target;
            public bool Initialized;
        }

        private readonly Dictionary<DirectorParameterDef, ParameterRuntime> _params =
            new Dictionary<DirectorParameterDef, ParameterRuntime>();

        // Scratch list reused each frame to avoid allocation while pruning dead contributions.
        private static readonly List<IParameterContribution> Scratch = new List<IParameterContribution>(32);

        // ------------------------------------------------------------------ discovery

        /**
         * Resolves the director a component should talk to: one in its parent hierarchy first
         * (supports multiple rigs), otherwise the first director in the scene. No singletons.
         */
        public static EnvironmentDirector FindFor(Component context)
        {
            if (context == null) return FindFirstObjectByType<EnvironmentDirector>();
            var inParents = context.GetComponentInParent<EnvironmentDirector>();
            return inParents != null ? inParents : FindFirstObjectByType<EnvironmentDirector>();
        }

        // ------------------------------------------------------------------ registration

        /// <summary>Adds a contribution. Scene components call this from OnEnable and remove in OnDisable.</summary>
        public void RegisterContribution(IParameterContribution contribution)
        {
            if (contribution?.Parameter == null) return;
            var runtime = EnsureRuntime(contribution.Parameter);
            if (!runtime.Contributions.Contains(contribution)) runtime.Contributions.Add(contribution);
        }

        /// <summary>Removes a contribution registered earlier. Safe to call redundantly.</summary>
        public void UnregisterContribution(IParameterContribution contribution)
        {
            if (contribution?.Parameter == null) return;
            if (_params.TryGetValue(contribution.Parameter, out var runtime)) runtime.Contributions.Remove(contribution);
        }

        /**
         * Creates and registers a temporary envelope-driven modifier (attack/hold/release).
         * hold < 0 holds until Release() is called on the returned handle. The modifier
         * auto-unregisters when its envelope finishes or its owner is destroyed.
         */
        public ParameterModifier AddModifier(DirectorParameterDef parameter, ParameterBlendMode blend, float value,
            float attack, float hold, float release, int priority = 0, Object owner = null)
        {
            var modifier = new ParameterModifier(parameter, blend, value, attack, hold, release, priority, owner);
            RegisterContribution(modifier);
            return modifier;
        }

        // ------------------------------------------------------------------ reads

        /// <summary>Current smoothed value of a parameter. Unregistered parameters report their base value.</summary>
        public float GetValue(DirectorParameterDef parameter)
        {
            if (parameter == null) return 0f;
            return _params.TryGetValue(parameter, out var runtime) ? runtime.Current : parameter.Clamp(parameter.baseValue);
        }

        /// <summary>Pre-smoothing composed target — useful for rules that must react instantly.</summary>
        public float GetTarget(DirectorParameterDef parameter)
        {
            if (parameter == null) return 0f;
            return _params.TryGetValue(parameter, out var runtime) ? runtime.Target : parameter.Clamp(parameter.baseValue);
        }

        /// <summary>Ensures a parameter is tracked (routes call this so they update even with zero contributions).</summary>
        public void Track(DirectorParameterDef parameter)
        {
            if (parameter != null) EnsureRuntime(parameter);
        }

        // ------------------------------------------------------------------ evaluation

        private void Update()
        {
            float dt = Time.deltaTime;
            foreach (var pair in _params) EvaluateParameter(pair.Value, dt);
        }

        /**
         * Composes one parameter from its contributions and advances smoothing.
         * Example: Darkness with base 0, depth Add 0.6, cave Add 0.2, sonar-pulse
         * Override 0.1@weight0.5 → target = lerp(0.8, 0.1, 0.5) = 0.45 → smoothed.
         */
        private void EvaluateParameter(ParameterRuntime runtime, float dt)
        {
            var def = runtime.Def;
            float result = def.baseValue;

            // Evaluate every live contribution once, pruning dead/finished entries as we go.
            Scratch.Clear();
            var list = runtime.Contributions;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var c = list[i];
                bool dead = c == null || !c.IsActive || (c is Object unityObj && unityObj == null);
                if (dead) { list.RemoveAt(i); continue; }
                Scratch.Add(c);
            }

            // Pass 1: additive influences stack on the base value.
            foreach (var c in Scratch)
                if (c.Blend == ParameterBlendMode.Add) result += c.Evaluate(dt) * c.Weight;

            // Pass 2: Max then Min — one severe influence can dominate, limits can cap.
            foreach (var c in Scratch)
                if (c.Blend == ParameterBlendMode.Max) result = Mathf.Max(result, Mathf.Lerp(def.minValue, c.Evaluate(dt), c.Weight));
            foreach (var c in Scratch)
                if (c.Blend == ParameterBlendMode.Min) result = Mathf.Min(result, Mathf.Lerp(def.maxValue, c.Evaluate(dt), c.Weight));

            // Pass 3: multiplicative suppression/amplification (weight lerps toward neutral 1).
            foreach (var c in Scratch)
                if (c.Blend == ParameterBlendMode.Multiply) result *= Mathf.Lerp(1f, c.Evaluate(dt), c.Weight);

            // Pass 4: highest-priority override wins, blended by its envelope weight.
            IParameterContribution best = null;
            foreach (var c in Scratch)
                if (c.Blend == ParameterBlendMode.Override && (best == null || c.Priority > best.Priority)) best = c;
            if (best != null) result = Mathf.Lerp(result, best.Evaluate(dt), best.Weight);

            runtime.Target = def.Clamp(result);

            // Attack/release exponential smoothing — fast rise, slow fall (or per-def tuning).
            if (!runtime.Initialized)
            {
                runtime.Current = runtime.Target;
                runtime.Initialized = true;
                return;
            }

            float tau = runtime.Target > runtime.Current ? def.attackSeconds : def.releaseSeconds;
            if (tau <= 0f) runtime.Current = runtime.Target;
            else runtime.Current = Mathf.Lerp(runtime.Current, runtime.Target, 1f - Mathf.Exp(-dt / tau));
        }

        private ParameterRuntime EnsureRuntime(DirectorParameterDef def)
        {
            if (_params.TryGetValue(def, out var runtime)) return runtime;
            runtime = new ParameterRuntime
            {
                Def = def,
                Current = def.Clamp(def.baseValue),
                Target = def.Clamp(def.baseValue),
                Initialized = true
            };
            _params.Add(def, runtime);
            return runtime;
        }

        // ------------------------------------------------------------------ introspection (editor tooling)

        /// <summary>Read-only view of one tracked parameter for editor tools and debug panels.</summary>
        public struct ParameterSnapshot
        {
            public DirectorParameterDef Def;
            public float Current;
            public float Target;
            public int ContributionCount;
            public int ModifierCount;
        }

        /// <summary>Fills the buffer with a snapshot of every tracked parameter (allocation-free for callers that reuse the list).</summary>
        public void GetParameterSnapshots(List<ParameterSnapshot> buffer)
        {
            buffer.Clear();
            foreach (var pair in _params)
            {
                int modifiers = 0;
                foreach (var c in pair.Value.Contributions)
                    if (c is ParameterModifier) modifiers++;
                buffer.Add(new ParameterSnapshot
                {
                    Def = pair.Key,
                    Current = pair.Value.Current,
                    Target = pair.Value.Target,
                    ContributionCount = pair.Value.Contributions.Count,
                    ModifierCount = modifiers
                });
            }
        }

        /// <summary>Copies the live contribution list for one parameter into the buffer (empty if untracked).</summary>
        public void GetContributions(DirectorParameterDef parameter, List<IParameterContribution> buffer)
        {
            buffer.Clear();
            if (parameter != null && _params.TryGetValue(parameter, out var runtime)) buffer.AddRange(runtime.Contributions);
        }

        // ------------------------------------------------------------------ debug overrides (editor tooling)

        private readonly Dictionary<DirectorParameterDef, ParameterModifier> _debugOverrides =
            new Dictionary<DirectorParameterDef, ParameterModifier>();

        /**
         * Forces a parameter to an exact value via a top-priority Override modifier — used by the
         * Director Graph window's test sliders. Re-applying just moves the value; ClearDebugOverride
         * releases the parameter back to its normal composition.
         */
        public void SetDebugOverride(DirectorParameterDef parameter, float value)
        {
            if (parameter == null) return;
            ClearDebugOverride(parameter);
            _debugOverrides[parameter] = AddModifier(parameter, ParameterBlendMode.Override, value, 0f, -1f, 0f, int.MaxValue, this);
        }

        public void ClearDebugOverride(DirectorParameterDef parameter)
        {
            if (parameter == null || !_debugOverrides.TryGetValue(parameter, out var modifier)) return;
            modifier.CancelImmediate();
            _debugOverrides.Remove(parameter);
        }

        public bool HasDebugOverride(DirectorParameterDef parameter) =>
            parameter != null && _debugOverrides.TryGetValue(parameter, out var m) && m.IsActive;

        // ------------------------------------------------------------------ debugging

#if ODIN_INSPECTOR
        [ShowInInspector, ReadOnly, PropertyOrder(100), LabelText("Live Parameters")]
#endif
        public string DebugReadout => BuildDebugReadout();

        /// <summary>Human-readable dump of every tracked parameter — used by inspectors and bug-report snapshots.</summary>
        public string BuildDebugReadout()
        {
            if (_params.Count == 0) return "(no parameters tracked)";
            var sb = new StringBuilder();
            foreach (var pair in _params)
            {
                var r = pair.Value;
                sb.AppendLine($"{r.Def.Id}: {r.Current:0.000} (target {r.Target:0.000}, {r.Contributions.Count} contrib)");
            }
            return sb.ToString();
        }
    }
}
