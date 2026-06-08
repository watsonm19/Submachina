using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace SynapticPro.GOAP
{
    /// <summary>
    /// GOAP Agent - Main component that executes GOAP planning and actions
    /// Attach to any GameObject to give it GOAP AI capabilities
    /// </summary>
    public class GOAPAgent : MonoBehaviour
    {
        [Header("Agent Settings")]
        [SerializeField] private bool autoStart = true;
        [SerializeField] private float replanInterval = 1f;
        [SerializeField] private float actionTimeout = 30f;

        [Header("Planning Settings")]
        [SerializeField] private int maxPlanIterations = 1000;
        [SerializeField] private int maxPlanDepth = 15;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float stoppingDistance = 0.5f;

        [Header("Debug")]
        [SerializeField] private bool debugMode = false;

        [Header("Events")]
        public UnityEvent<GOAPGoal> OnGoalChanged;
        public UnityEvent<GOAPActionBase> OnActionStarted;
        public UnityEvent<GOAPActionBase> OnActionCompleted;
        public UnityEvent<GOAPActionBase> OnActionFailed;
        public UnityEvent OnPlanCreated;
        public UnityEvent OnPlanFailed;

        // Runtime state
        private GOAPPlanner planner;
        private WorldState worldState;
        private HashSet<GOAPActionBase> availableActions;
        private List<GOAPGoal> goals;
        private Queue<GOAPActionBase> currentPlan;
        private GOAPActionBase currentAction;
        private GOAPGoal currentGoal;
        private float lastPlanTime;
        private float actionStartTime;
        private bool isRunning;

        /// <summary>
        /// Current world state
        /// </summary>
        public WorldState WorldState => worldState;

        /// <summary>
        /// Currently executing action
        /// </summary>
        public GOAPActionBase CurrentAction => currentAction;

        /// <summary>
        /// Current goal being pursued
        /// </summary>
        public GOAPGoal CurrentGoal => currentGoal;

        /// <summary>
        /// Whether agent is currently running
        /// </summary>
        public bool IsRunning => isRunning;

        /// <summary>
        /// Whether agent has a valid plan
        /// </summary>
        public bool HasPlan => currentPlan != null && currentPlan.Count > 0;

        /// <summary>
        /// Movement speed
        /// </summary>
        public float MoveSpeed
        {
            get => moveSpeed;
            set => moveSpeed = Mathf.Max(0, value);
        }

        private void Awake()
        {
            planner = new GOAPPlanner();
            planner.SetMaxIterations(maxPlanIterations);
            planner.SetMaxDepth(maxPlanDepth);

            worldState = new WorldState();
            availableActions = new HashSet<GOAPActionBase>();
            goals = new List<GOAPGoal>();
            currentPlan = new Queue<GOAPActionBase>();

            // Collect actions from this GameObject
            CollectActions();

            // Collect goals from components
            CollectGoals();
        }

        private void Start()
        {
            if (autoStart)
            {
                StartAgent();
            }
        }

        private void Update()
        {
            if (!isRunning) return;

            // Check if replanning is needed
            if (Time.time - lastPlanTime > replanInterval)
            {
                TryReplan();
            }

            // Execute current action
            ExecuteCurrentAction();
        }

        /// <summary>
        /// Start the GOAP agent
        /// </summary>
        public void StartAgent()
        {
            isRunning = true;
            TryReplan();
        }

        /// <summary>
        /// Stop the GOAP agent
        /// </summary>
        public void StopAgent()
        {
            isRunning = false;
            if (currentAction != null)
            {
                currentAction.OnInterrupted(this);
                currentAction = null;
            }
            currentPlan?.Clear();
        }

        /// <summary>
        /// Collect all GOAPActionBase components
        /// </summary>
        private void CollectActions()
        {
            var actions = GetComponents<GOAPActionBase>();
            foreach (var action in actions)
            {
                availableActions.Add(action);
            }

            // Also check children
            var childActions = GetComponentsInChildren<GOAPActionBase>();
            foreach (var action in childActions)
            {
                availableActions.Add(action);
            }

            if (debugMode)
            {
                Debug.Log($"[GOAPAgent] Collected {availableActions.Count} actions");
            }
        }

        /// <summary>
        /// Collect all GOAPGoalComponent goals
        /// </summary>
        private void CollectGoals()
        {
            var goalComponents = GetComponents<GOAPGoalComponent>();
            foreach (var gc in goalComponents)
            {
                goals.Add(gc.Goal);
            }

            // Also check children
            var childGoals = GetComponentsInChildren<GOAPGoalComponent>();
            foreach (var gc in childGoals)
            {
                if (!goals.Contains(gc.Goal))
                {
                    goals.Add(gc.Goal);
                }
            }

            if (debugMode)
            {
                Debug.Log($"[GOAPAgent] Collected {goals.Count} goals");
            }
        }

        /// <summary>
        /// Add an action at runtime
        /// </summary>
        public void AddAction(GOAPActionBase action)
        {
            if (action != null)
            {
                availableActions.Add(action);
            }
        }

        /// <summary>
        /// Remove an action at runtime
        /// </summary>
        public void RemoveAction(GOAPActionBase action)
        {
            if (action != null)
            {
                availableActions.Remove(action);
            }
        }

        /// <summary>
        /// Add a goal at runtime
        /// </summary>
        public void AddGoal(GOAPGoal goal)
        {
            if (goal != null && !goals.Contains(goal))
            {
                goals.Add(goal);
            }
        }

        /// <summary>
        /// Remove a goal at runtime
        /// </summary>
        public void RemoveGoal(GOAPGoal goal)
        {
            if (goal != null)
            {
                goals.Remove(goal);
                if (currentGoal == goal)
                {
                    currentGoal = null;
                    currentPlan?.Clear();
                }
            }
        }

        /// <summary>
        /// Set world state value
        /// </summary>
        public void SetWorldState(string key, object value)
        {
            worldState.SetState(key, value);
        }

        /// <summary>
        /// Get world state value
        /// </summary>
        public T GetWorldState<T>(string key)
        {
            return worldState.GetState<T>(key);
        }

        /// <summary>
        /// Try to create a new plan
        /// </summary>
        private void TryReplan()
        {
            lastPlanTime = Time.time;

            // Find highest priority goal
            var bestGoal = SelectGoal();
            if (bestGoal == null)
            {
                if (debugMode)
                {
                    Debug.Log("[GOAPAgent] No active goals");
                }
                return;
            }

            // Check if goal already satisfied
            if (bestGoal.IsSatisfied(worldState))
            {
                if (debugMode)
                {
                    Debug.Log($"[GOAPAgent] Goal '{bestGoal.GoalName}' already satisfied");
                }
                bestGoal.OnAchieved(this);
                return;
            }

            // Check if we need to replan
            bool needsReplan = currentGoal != bestGoal || !HasPlan;

            if (!needsReplan && currentAction != null)
            {
                // Check if current action is still valid
                if (!currentAction.CheckProceduralPrecondition(this))
                {
                    needsReplan = true;
                }
            }

            if (needsReplan)
            {
                CreatePlan(bestGoal);
            }
        }

        /// <summary>
        /// Select the best goal to pursue
        /// </summary>
        private GOAPGoal SelectGoal()
        {
            GOAPGoal bestGoal = null;
            float bestRelevance = float.MinValue;

            foreach (var goal in goals)
            {
                if (!goal.IsActive) continue;

                float relevance = goal.GetRelevance(this);
                if (relevance > bestRelevance)
                {
                    bestRelevance = relevance;
                    bestGoal = goal;
                }
            }

            return bestGoal;
        }

        /// <summary>
        /// Create a plan to achieve the goal
        /// </summary>
        private void CreatePlan(GOAPGoal goal)
        {
            if (currentAction != null)
            {
                currentAction.OnInterrupted(this);
                currentAction = null;
            }

            if (currentGoal != null && currentGoal != goal)
            {
                currentGoal.OnDeactivate(this);
            }

            currentGoal = goal;
            currentGoal.OnActivate(this);
            OnGoalChanged?.Invoke(currentGoal);

            var plan = planner.Plan(this, availableActions, worldState, goal);

            if (plan != null && plan.Count > 0)
            {
                currentPlan = plan;
                OnPlanCreated?.Invoke();

                if (debugMode)
                {
                    Debug.Log($"[GOAPAgent] Plan created for '{goal.GoalName}': {string.Join(" -> ", plan.Select(a => a.ActionName))}");
                }
            }
            else
            {
                currentPlan = new Queue<GOAPActionBase>();
                OnPlanFailed?.Invoke();

                if (debugMode)
                {
                    Debug.LogWarning($"[GOAPAgent] Failed to create plan for '{goal.GoalName}'");
                }
            }
        }

        /// <summary>
        /// Execute the current action
        /// </summary>
        private void ExecuteCurrentAction()
        {
            if (currentAction == null)
            {
                // Get next action from plan
                if (currentPlan != null && currentPlan.Count > 0)
                {
                    currentAction = currentPlan.Dequeue();
                    actionStartTime = Time.time;

                    // Check if we need to move to target
                    if (!currentAction.IsInRange(this))
                    {
                        // Move towards target
                        MoveTowardsTarget(currentAction.Target);
                        return;
                    }

                    // Start action
                    if (currentAction.PrePerform(this))
                    {
                        OnActionStarted?.Invoke(currentAction);
                        if (debugMode)
                        {
                            Debug.Log($"[GOAPAgent] Started action: {currentAction.ActionName}");
                        }
                    }
                    else
                    {
                        // Action failed to start
                        OnActionFailed?.Invoke(currentAction);
                        currentAction = null;
                        currentPlan.Clear(); // Force replan
                    }
                }
                return;
            }

            // Check timeout
            if (Time.time - actionStartTime > actionTimeout)
            {
                if (debugMode)
                {
                    Debug.LogWarning($"[GOAPAgent] Action '{currentAction.ActionName}' timed out");
                }
                currentAction.OnInterrupted(this);
                OnActionFailed?.Invoke(currentAction);
                currentAction = null;
                currentPlan.Clear();
                return;
            }

            // Check if we need to move to target
            if (!currentAction.IsInRange(this))
            {
                MoveTowardsTarget(currentAction.Target);
                return;
            }

            // Execute action
            bool completed = currentAction.Perform(this);

            if (completed)
            {
                // Action completed
                if (currentAction.PostPerform(this))
                {
                    // Apply effects to world state
                    foreach (var effect in currentAction.Effects)
                    {
                        worldState.SetState(effect.Key, effect.Value);
                    }

                    OnActionCompleted?.Invoke(currentAction);
                    if (debugMode)
                    {
                        Debug.Log($"[GOAPAgent] Completed action: {currentAction.ActionName}");
                    }
                }
                else
                {
                    OnActionFailed?.Invoke(currentAction);
                }

                currentAction = null;

                // Check if goal is achieved
                if (currentGoal != null && currentGoal.IsSatisfied(worldState))
                {
                    currentGoal.OnAchieved(this);
                    if (debugMode)
                    {
                        Debug.Log($"[GOAPAgent] Goal achieved: {currentGoal.GoalName}");
                    }
                }
            }
        }

        /// <summary>
        /// Move towards target
        /// </summary>
        private void MoveTowardsTarget(GameObject target)
        {
            if (target == null) return;

            Vector3 direction = (target.transform.position - transform.position).normalized;
            direction.y = 0; // Keep on ground

            float distance = Vector3.Distance(transform.position, target.transform.position);

            if (distance > stoppingDistance)
            {
                transform.position += direction * moveSpeed * Time.deltaTime;
                transform.forward = direction;
            }
        }

        /// <summary>
        /// Force immediate replan
        /// </summary>
        public void ForceReplan()
        {
            if (currentAction != null)
            {
                currentAction.OnInterrupted(this);
                currentAction = null;
            }
            currentPlan?.Clear();
            TryReplan();
        }

        /// <summary>
        /// Interrupt current action
        /// </summary>
        public void InterruptCurrentAction()
        {
            if (currentAction != null)
            {
                currentAction.OnInterrupted(this);
                OnActionFailed?.Invoke(currentAction);
                currentAction = null;
            }
        }

        /// <summary>
        /// Get remaining actions in plan
        /// </summary>
        public List<GOAPActionBase> GetRemainingPlan()
        {
            var remaining = new List<GOAPActionBase>();
            if (currentAction != null)
            {
                remaining.Add(currentAction);
            }
            if (currentPlan != null)
            {
                remaining.AddRange(currentPlan);
            }
            return remaining;
        }

        private void OnDrawGizmosSelected()
        {
            if (!debugMode) return;

            // Draw current action target
            if (currentAction != null && currentAction.Target != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(transform.position, currentAction.Target.transform.position);
                Gizmos.DrawWireSphere(currentAction.Target.transform.position, 0.5f);
            }
        }
    }
}
