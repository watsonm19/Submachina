using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SynapticPro.GOAP
{
    /// <summary>
    /// GOAP Planner - A* based action planning system
    /// Finds optimal sequence of actions to achieve goals
    /// </summary>
    public class GOAPPlanner
    {
        private int maxPlanningIterations = 1000;
        private int maxPlanDepth = 15;

        /// <summary>
        /// Plan node for A* search
        /// </summary>
        private class PlanNode : IComparable<PlanNode>
        {
            public WorldState State;
            public GOAPActionBase Action;
            public PlanNode Parent;
            public float GCost; // Cost from start
            public float HCost; // Heuristic cost to goal
            public float FCost => GCost + HCost;
            public int Depth;

            public int CompareTo(PlanNode other)
            {
                int compare = FCost.CompareTo(other.FCost);
                if (compare == 0)
                {
                    compare = HCost.CompareTo(other.HCost);
                }
                return compare;
            }
        }

        /// <summary>
        /// Create a plan to achieve the goal from current state
        /// </summary>
        /// <param name="agent">The GOAP agent</param>
        /// <param name="availableActions">Actions the agent can perform</param>
        /// <param name="currentState">Current world state</param>
        /// <param name="goal">Goal to achieve</param>
        /// <returns>Queue of actions to execute, or null if no plan found</returns>
        public Queue<GOAPActionBase> Plan(
            GOAPAgent agent,
            HashSet<GOAPActionBase> availableActions,
            WorldState currentState,
            GOAPGoal goal)
        {
            if (goal == null || availableActions == null || availableActions.Count == 0)
            {
                return null;
            }

            // Reset actions
            foreach (var action in availableActions)
            {
                action.Reset();
            }

            // Get usable actions (procedural preconditions check)
            var usableActions = new HashSet<GOAPActionBase>();
            foreach (var action in availableActions)
            {
                if (action.CheckProceduralPrecondition(agent))
                {
                    usableActions.Add(action);
                }
            }

            if (usableActions.Count == 0)
            {
                return null;
            }

            // A* search
            var openList = new List<PlanNode>();
            var closedSet = new HashSet<string>();

            // Start node
            var startNode = new PlanNode
            {
                State = currentState.Clone(),
                Action = null,
                Parent = null,
                GCost = 0,
                HCost = CalculateHeuristic(currentState, goal),
                Depth = 0
            };

            openList.Add(startNode);

            int iterations = 0;
            PlanNode goalNode = null;

            while (openList.Count > 0 && iterations < maxPlanningIterations)
            {
                iterations++;

                // Get node with lowest F cost
                openList.Sort();
                var currentNode = openList[0];
                openList.RemoveAt(0);

                // Check if goal is satisfied
                if (GoalSatisfied(currentNode.State, goal))
                {
                    goalNode = currentNode;
                    break;
                }

                // Skip if max depth reached
                if (currentNode.Depth >= maxPlanDepth)
                {
                    continue;
                }

                // Generate state hash for closed set
                string stateHash = currentNode.State.GetHashCode().ToString();
                if (closedSet.Contains(stateHash))
                {
                    continue;
                }
                closedSet.Add(stateHash);

                // Expand node - try each action
                foreach (var action in usableActions)
                {
                    // Check if action's preconditions are met
                    if (!PreconditionsMet(currentNode.State, action))
                    {
                        continue;
                    }

                    // Apply action effects to create new state
                    var newState = currentNode.State.Clone();
                    ApplyEffects(newState, action);

                    // Create new node
                    var newNode = new PlanNode
                    {
                        State = newState,
                        Action = action,
                        Parent = currentNode,
                        GCost = currentNode.GCost + action.Cost,
                        HCost = CalculateHeuristic(newState, goal),
                        Depth = currentNode.Depth + 1
                    };

                    // Check if already in open list with better cost
                    string newStateHash = newState.GetHashCode().ToString();
                    if (!closedSet.Contains(newStateHash))
                    {
                        openList.Add(newNode);
                    }
                }
            }

            // Build plan from goal node
            if (goalNode != null)
            {
                return BuildPlan(goalNode);
            }

            return null;
        }

        /// <summary>
        /// Calculate heuristic (estimated cost to goal)
        /// </summary>
        private float CalculateHeuristic(WorldState state, GOAPGoal goal)
        {
            if (goal.DesiredState == null || goal.DesiredState.Count == 0)
            {
                return 0;
            }

            int unsatisfiedConditions = 0;
            foreach (var condition in goal.DesiredState)
            {
                if (!state.HasState(condition.Key) ||
                    !state.GetState<object>(condition.Key).Equals(condition.Value))
                {
                    unsatisfiedConditions++;
                }
            }

            return unsatisfiedConditions;
        }

        /// <summary>
        /// Check if goal is satisfied by current state
        /// </summary>
        private bool GoalSatisfied(WorldState state, GOAPGoal goal)
        {
            if (goal.DesiredState == null || goal.DesiredState.Count == 0)
            {
                return true;
            }

            foreach (var condition in goal.DesiredState)
            {
                if (!state.HasState(condition.Key))
                {
                    return false;
                }

                var currentValue = state.GetState<object>(condition.Key);
                if (!currentValue.Equals(condition.Value))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Check if action's preconditions are met
        /// </summary>
        private bool PreconditionsMet(WorldState state, GOAPActionBase action)
        {
            if (action.Preconditions == null || action.Preconditions.Count == 0)
            {
                return true;
            }

            foreach (var precondition in action.Preconditions)
            {
                if (!state.HasState(precondition.Key))
                {
                    return false;
                }

                var currentValue = state.GetState<object>(precondition.Key);
                if (!currentValue.Equals(precondition.Value))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Apply action effects to state
        /// </summary>
        private void ApplyEffects(WorldState state, GOAPActionBase action)
        {
            if (action.Effects == null)
            {
                return;
            }

            foreach (var effect in action.Effects)
            {
                state.SetState(effect.Key, effect.Value);
            }
        }

        /// <summary>
        /// Build plan queue from goal node by backtracking
        /// </summary>
        private Queue<GOAPActionBase> BuildPlan(PlanNode goalNode)
        {
            var plan = new List<GOAPActionBase>();
            var node = goalNode;

            while (node != null)
            {
                if (node.Action != null)
                {
                    plan.Add(node.Action);
                }
                node = node.Parent;
            }

            plan.Reverse();

            var queue = new Queue<GOAPActionBase>();
            foreach (var action in plan)
            {
                queue.Enqueue(action);
            }

            return queue;
        }

        /// <summary>
        /// Set maximum planning iterations
        /// </summary>
        public void SetMaxIterations(int max)
        {
            maxPlanningIterations = Mathf.Max(100, max);
        }

        /// <summary>
        /// Set maximum plan depth
        /// </summary>
        public void SetMaxDepth(int depth)
        {
            maxPlanDepth = Mathf.Max(1, depth);
        }
    }
}
