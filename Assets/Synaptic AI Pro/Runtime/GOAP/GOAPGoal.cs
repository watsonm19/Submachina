using System;
using System.Collections.Generic;
using UnityEngine;

namespace SynapticPro.GOAP
{
    /// <summary>
    /// GOAP Goal - Represents a goal the agent wants to achieve
    /// </summary>
    [Serializable]
    public class GOAPGoal
    {
        [SerializeField] private string goalName = "Goal";
        [SerializeField] private int priority = 1;
        [SerializeField] private bool isActive = true;

        /// <summary>
        /// Desired world state to achieve this goal
        /// </summary>
        public Dictionary<string, object> DesiredState { get; private set; } = new Dictionary<string, object>();

        /// <summary>
        /// Goal name for identification
        /// </summary>
        public string GoalName
        {
            get => goalName;
            set => goalName = value;
        }

        /// <summary>
        /// Priority (higher = more important)
        /// </summary>
        public int Priority
        {
            get => priority;
            set => priority = Mathf.Max(0, value);
        }

        /// <summary>
        /// Whether this goal is currently active
        /// </summary>
        public bool IsActive
        {
            get => isActive;
            set => isActive = value;
        }

        /// <summary>
        /// Create empty goal
        /// </summary>
        public GOAPGoal()
        {
        }

        /// <summary>
        /// Create goal with name and priority
        /// </summary>
        public GOAPGoal(string name, int priority = 1)
        {
            this.goalName = name;
            this.priority = priority;
        }

        /// <summary>
        /// Add a condition to the desired state
        /// </summary>
        public void AddCondition(string key, object value)
        {
            if (string.IsNullOrEmpty(key)) return;

            if (DesiredState.ContainsKey(key))
            {
                DesiredState[key] = value;
            }
            else
            {
                DesiredState.Add(key, value);
            }
        }

        /// <summary>
        /// Remove a condition from desired state
        /// </summary>
        public void RemoveCondition(string key)
        {
            if (DesiredState.ContainsKey(key))
            {
                DesiredState.Remove(key);
            }
        }

        /// <summary>
        /// Check if goal is satisfied by current world state
        /// </summary>
        public bool IsSatisfied(WorldState worldState)
        {
            if (worldState == null) return false;
            return worldState.Satisfies(DesiredState);
        }

        /// <summary>
        /// Get relevance of this goal (can be overridden for dynamic priority)
        /// </summary>
        public virtual float GetRelevance(GOAPAgent agent)
        {
            return isActive ? priority : 0;
        }

        /// <summary>
        /// Called when goal is activated
        /// </summary>
        public virtual void OnActivate(GOAPAgent agent)
        {
        }

        /// <summary>
        /// Called when goal is deactivated
        /// </summary>
        public virtual void OnDeactivate(GOAPAgent agent)
        {
        }

        /// <summary>
        /// Called when goal is achieved
        /// </summary>
        public virtual void OnAchieved(GOAPAgent agent)
        {
        }

        /// <summary>
        /// Clone this goal
        /// </summary>
        public GOAPGoal Clone()
        {
            var clone = new GOAPGoal(goalName, priority);
            clone.isActive = isActive;
            foreach (var kvp in DesiredState)
            {
                clone.DesiredState[kvp.Key] = kvp.Value;
            }
            return clone;
        }

        public override string ToString()
        {
            return $"{goalName} (Priority: {priority})";
        }
    }

    /// <summary>
    /// Goal that changes priority based on conditions
    /// </summary>
    public class DynamicGOAPGoal : GOAPGoal
    {
        private Func<GOAPAgent, float> relevanceCalculator;

        public DynamicGOAPGoal(string name, Func<GOAPAgent, float> calculator) : base(name)
        {
            relevanceCalculator = calculator;
        }

        public override float GetRelevance(GOAPAgent agent)
        {
            if (!IsActive) return 0;
            return relevanceCalculator?.Invoke(agent) ?? Priority;
        }
    }

    /// <summary>
    /// MonoBehaviour wrapper for GOAPGoal
    /// Attach to agent to define goals in Inspector
    /// </summary>
    public class GOAPGoalComponent : MonoBehaviour
    {
        [Header("Goal Settings")]
        [SerializeField] private string goalName = "Goal";
        [SerializeField] private int priority = 1;
        [SerializeField] private bool isActive = true;

        [Header("Desired State")]
        [SerializeField] private List<StateCondition> conditions = new List<StateCondition>();

        private GOAPGoal _goal;

        /// <summary>
        /// Get the GOAPGoal instance
        /// </summary>
        public GOAPGoal Goal
        {
            get
            {
                if (_goal == null)
                {
                    BuildGoal();
                }
                return _goal;
            }
        }

        private void Awake()
        {
            BuildGoal();
        }

        private void BuildGoal()
        {
            _goal = new GOAPGoal(goalName, priority);
            _goal.IsActive = isActive;

            foreach (var condition in conditions)
            {
                if (!string.IsNullOrEmpty(condition.key))
                {
                    _goal.AddCondition(condition.key, condition.GetValue());
                }
            }
        }

        /// <summary>
        /// Refresh goal from inspector values
        /// </summary>
        public void RefreshGoal()
        {
            BuildGoal();
        }
    }

    /// <summary>
    /// Serializable state condition for Inspector
    /// </summary>
    [Serializable]
    public class StateCondition
    {
        public string key;
        public StateValueType valueType = StateValueType.Bool;
        public bool boolValue = true;
        public int intValue = 0;
        public float floatValue = 0f;
        public string stringValue = "";

        public object GetValue()
        {
            switch (valueType)
            {
                case StateValueType.Bool:
                    return boolValue;
                case StateValueType.Int:
                    return intValue;
                case StateValueType.Float:
                    return floatValue;
                case StateValueType.String:
                    return stringValue;
                default:
                    return null;
            }
        }
    }

    public enum StateValueType
    {
        Bool,
        Int,
        Float,
        String
    }
}
