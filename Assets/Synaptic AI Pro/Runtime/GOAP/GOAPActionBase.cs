using System.Collections.Generic;
using UnityEngine;

namespace SynapticPro.GOAP
{
    /// <summary>
    /// Base class for all GOAP actions
    /// Inherit from this to create custom actions
    /// </summary>
    public abstract class GOAPActionBase : MonoBehaviour
    {
        [Header("Action Settings")]
        [SerializeField] protected string actionName = "Action";
        [SerializeField] protected float cost = 1f;
        [SerializeField] protected float duration = 1f;

        [Header("Target")]
        [SerializeField] protected GameObject target;
        [SerializeField] protected bool requiresTarget = false;
        [SerializeField] protected float targetRange = 2f;

        /// <summary>
        /// Preconditions that must be true for this action to run
        /// </summary>
        public Dictionary<string, object> Preconditions { get; protected set; } = new Dictionary<string, object>();

        /// <summary>
        /// Effects this action has on the world state
        /// </summary>
        public Dictionary<string, object> Effects { get; protected set; } = new Dictionary<string, object>();

        /// <summary>
        /// Cost of performing this action
        /// </summary>
        public float Cost
        {
            get => cost;
            set => cost = Mathf.Max(0.01f, value);
        }

        /// <summary>
        /// Name of this action
        /// </summary>
        public string ActionName => actionName;

        /// <summary>
        /// Duration of this action
        /// </summary>
        public float Duration => duration;

        /// <summary>
        /// Current target
        /// </summary>
        public GameObject Target
        {
            get => target;
            set => target = value;
        }

        /// <summary>
        /// Whether action is currently running
        /// </summary>
        public bool IsRunning { get; protected set; }

        /// <summary>
        /// Whether action has completed
        /// </summary>
        public bool IsDone { get; protected set; }

        protected virtual void Awake()
        {
            SetupPreconditionsAndEffects();
        }

        /// <summary>
        /// Override to setup preconditions and effects
        /// </summary>
        protected virtual void SetupPreconditionsAndEffects()
        {
            // Override in derived classes
        }

        /// <summary>
        /// Add a precondition
        /// </summary>
        public void AddPrecondition(string key, object value)
        {
            if (!Preconditions.ContainsKey(key))
            {
                Preconditions.Add(key, value);
            }
            else
            {
                Preconditions[key] = value;
            }
        }

        /// <summary>
        /// Remove a precondition
        /// </summary>
        public void RemovePrecondition(string key)
        {
            if (Preconditions.ContainsKey(key))
            {
                Preconditions.Remove(key);
            }
        }

        /// <summary>
        /// Add an effect
        /// </summary>
        public void AddEffect(string key, object value)
        {
            if (!Effects.ContainsKey(key))
            {
                Effects.Add(key, value);
            }
            else
            {
                Effects[key] = value;
            }
        }

        /// <summary>
        /// Remove an effect
        /// </summary>
        public void RemoveEffect(string key)
        {
            if (Effects.ContainsKey(key))
            {
                Effects.Remove(key);
            }
        }

        /// <summary>
        /// Reset action state
        /// </summary>
        public virtual void Reset()
        {
            IsRunning = false;
            IsDone = false;
        }

        /// <summary>
        /// Check if agent is in range of target
        /// </summary>
        public bool IsInRange(GOAPAgent agent)
        {
            if (!requiresTarget || target == null)
            {
                return true;
            }

            float distance = Vector3.Distance(agent.transform.position, target.transform.position);
            return distance <= targetRange;
        }

        /// <summary>
        /// Check procedural preconditions (runtime checks)
        /// Override for custom runtime conditions
        /// </summary>
        public virtual bool CheckProceduralPrecondition(GOAPAgent agent)
        {
            return true;
        }

        /// <summary>
        /// Called before action starts
        /// Return true if action can start
        /// </summary>
        public virtual bool PrePerform(GOAPAgent agent)
        {
            IsRunning = true;
            IsDone = false;
            return true;
        }

        /// <summary>
        /// Called each frame while action is running
        /// Return true when action is complete
        /// </summary>
        public abstract bool Perform(GOAPAgent agent);

        /// <summary>
        /// Called after action completes
        /// Return true if action succeeded
        /// </summary>
        public virtual bool PostPerform(GOAPAgent agent)
        {
            IsRunning = false;
            IsDone = true;
            return true;
        }

        /// <summary>
        /// Called when action is interrupted
        /// </summary>
        public virtual void OnInterrupted(GOAPAgent agent)
        {
            IsRunning = false;
            IsDone = false;
        }

        /// <summary>
        /// Get string representation
        /// </summary>
        public override string ToString()
        {
            return $"{actionName} (Cost: {cost})";
        }
    }

    /// <summary>
    /// Simple action that completes after duration
    /// Use for basic actions without complex logic
    /// </summary>
    public class GOAPSimpleAction : GOAPActionBase
    {
        private float elapsedTime;

        public override bool Perform(GOAPAgent agent)
        {
            elapsedTime += Time.deltaTime;

            if (elapsedTime >= duration)
            {
                elapsedTime = 0f;
                return true;
            }

            return false;
        }

        public override void Reset()
        {
            base.Reset();
            elapsedTime = 0f;
        }
    }
}
