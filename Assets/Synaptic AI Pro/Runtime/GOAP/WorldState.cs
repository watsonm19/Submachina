using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SynapticPro.GOAP
{
    /// <summary>
    /// World State - Manages the state of the world for GOAP planning
    /// </summary>
    [Serializable]
    public class WorldState
    {
        private Dictionary<string, object> states = new Dictionary<string, object>();

        /// <summary>
        /// Set a state value
        /// </summary>
        public void SetState(string key, object value)
        {
            if (string.IsNullOrEmpty(key)) return;

            if (states.ContainsKey(key))
            {
                states[key] = value;
            }
            else
            {
                states.Add(key, value);
            }
        }

        /// <summary>
        /// Get a state value
        /// </summary>
        public T GetState<T>(string key)
        {
            if (states.TryGetValue(key, out var value))
            {
                if (value is T typedValue)
                {
                    return typedValue;
                }

                // Try conversion
                try
                {
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    return default(T);
                }
            }

            return default(T);
        }

        /// <summary>
        /// Check if state exists
        /// </summary>
        public bool HasState(string key)
        {
            return states.ContainsKey(key);
        }

        /// <summary>
        /// Remove a state
        /// </summary>
        public void RemoveState(string key)
        {
            if (states.ContainsKey(key))
            {
                states.Remove(key);
            }
        }

        /// <summary>
        /// Clear all states
        /// </summary>
        public void Clear()
        {
            states.Clear();
        }

        /// <summary>
        /// Get all states as dictionary
        /// </summary>
        public Dictionary<string, object> GetAllStates()
        {
            return new Dictionary<string, object>(states);
        }

        /// <summary>
        /// Clone this world state
        /// </summary>
        public WorldState Clone()
        {
            var clone = new WorldState();
            foreach (var kvp in states)
            {
                clone.states[kvp.Key] = kvp.Value;
            }
            return clone;
        }

        /// <summary>
        /// Apply another state on top of this one
        /// </summary>
        public void ApplyState(WorldState other)
        {
            if (other == null) return;

            foreach (var kvp in other.states)
            {
                states[kvp.Key] = kvp.Value;
            }
        }

        /// <summary>
        /// Check if this state satisfies the conditions
        /// </summary>
        public bool Satisfies(Dictionary<string, object> conditions)
        {
            if (conditions == null || conditions.Count == 0)
            {
                return true;
            }

            foreach (var condition in conditions)
            {
                if (!HasState(condition.Key))
                {
                    return false;
                }

                var currentValue = states[condition.Key];
                if (!ValuesEqual(currentValue, condition.Value))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Compare two values for equality
        /// </summary>
        private bool ValuesEqual(object a, object b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;

            // Handle numeric comparisons
            if (IsNumeric(a) && IsNumeric(b))
            {
                return Convert.ToDouble(a) == Convert.ToDouble(b);
            }

            return a.Equals(b);
        }

        private bool IsNumeric(object obj)
        {
            return obj is int || obj is float || obj is double || obj is long || obj is short || obj is byte;
        }

        /// <summary>
        /// Get hash code for state comparison
        /// </summary>
        public override int GetHashCode()
        {
            int hash = 17;
            foreach (var kvp in states.OrderBy(x => x.Key))
            {
                hash = hash * 31 + kvp.Key.GetHashCode();
                hash = hash * 31 + (kvp.Value?.GetHashCode() ?? 0);
            }
            return hash;
        }

        /// <summary>
        /// String representation for debugging
        /// </summary>
        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("WorldState:");
            foreach (var kvp in states)
            {
                sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Initialize from dictionary
        /// </summary>
        public static WorldState FromDictionary(Dictionary<string, object> dict)
        {
            var state = new WorldState();
            if (dict != null)
            {
                foreach (var kvp in dict)
                {
                    state.SetState(kvp.Key, kvp.Value);
                }
            }
            return state;
        }
    }
}
