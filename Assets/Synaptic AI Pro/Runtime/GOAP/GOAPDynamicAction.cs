using System;
using System.Collections.Generic;
using UnityEngine;

namespace SynapticPro.GOAP
{
    /// <summary>
    /// Dynamic GOAP Action - Can be configured at runtime via code
    /// Used for procedurally generated behaviors from natural language
    /// </summary>
    public class GOAPDynamicAction : GOAPActionBase
    {
        private Func<GOAPAgent, bool> performFunc;
        private Func<GOAPAgent, bool> checkPreconditionFunc;
        private Action<GOAPAgent> onCompleteFunc;
        private float elapsedTime;

        /// <summary>
        /// Configure the action at runtime
        /// </summary>
        public void Configure(
            string name,
            float actionCost,
            float actionDuration,
            Dictionary<string, object> preconditions,
            Dictionary<string, object> effects)
        {
            actionName = name;
            cost = actionCost;
            duration = actionDuration;

            if (preconditions != null)
            {
                foreach (var kvp in preconditions)
                {
                    AddPrecondition(kvp.Key, kvp.Value);
                }
            }

            if (effects != null)
            {
                foreach (var kvp in effects)
                {
                    AddEffect(kvp.Key, kvp.Value);
                }
            }
        }

        /// <summary>
        /// Set custom perform function
        /// </summary>
        public void SetPerformFunction(Func<GOAPAgent, bool> func)
        {
            performFunc = func;
        }

        /// <summary>
        /// Set custom precondition check
        /// </summary>
        public void SetPreconditionCheck(Func<GOAPAgent, bool> func)
        {
            checkPreconditionFunc = func;
        }

        /// <summary>
        /// Set on complete callback
        /// </summary>
        public void SetOnComplete(Action<GOAPAgent> func)
        {
            onCompleteFunc = func;
        }

        public override bool CheckProceduralPrecondition(GOAPAgent agent)
        {
            if (checkPreconditionFunc != null)
            {
                return checkPreconditionFunc(agent);
            }
            return base.CheckProceduralPrecondition(agent);
        }

        public override bool Perform(GOAPAgent agent)
        {
            if (performFunc != null)
            {
                return performFunc(agent);
            }

            // Default behavior: wait for duration
            elapsedTime += Time.deltaTime;
            if (elapsedTime >= duration)
            {
                elapsedTime = 0f;
                return true;
            }
            return false;
        }

        public override bool PostPerform(GOAPAgent agent)
        {
            onCompleteFunc?.Invoke(agent);
            return base.PostPerform(agent);
        }

        public override void Reset()
        {
            base.Reset();
            elapsedTime = 0f;
        }
    }

    /// <summary>
    /// Factory for creating dynamic GOAP actions
    /// </summary>
    public static class GOAPActionFactory
    {
        /// <summary>
        /// Create a simple wait action
        /// </summary>
        public static GOAPDynamicAction CreateWaitAction(GameObject parent, string name, float waitTime)
        {
            var action = parent.AddComponent<GOAPDynamicAction>();
            action.Configure(name, 0.1f, waitTime, null, null);
            return action;
        }

        /// <summary>
        /// Create a movement action
        /// </summary>
        public static GOAPDynamicAction CreateMoveAction(
            GameObject parent,
            string name,
            float cost,
            string[] preconditions,
            string[] effects)
        {
            var action = parent.AddComponent<GOAPDynamicAction>();

            var precondDict = new Dictionary<string, object>();
            foreach (var p in preconditions)
            {
                precondDict[p] = true;
            }

            var effectDict = new Dictionary<string, object>();
            foreach (var e in effects)
            {
                effectDict[e] = true;
            }

            action.Configure(name, cost, 1f, precondDict, effectDict);
            return action;
        }

        /// <summary>
        /// Create action from parsed behavior data
        /// </summary>
        public static GOAPDynamicAction CreateFromBehaviorData(
            GameObject parent,
            string name,
            float cost,
            string[] preconditions,
            string[] effects,
            float duration = 1f)
        {
            var action = parent.AddComponent<GOAPDynamicAction>();

            var precondDict = new Dictionary<string, object>();
            if (preconditions != null)
            {
                foreach (var p in preconditions)
                {
                    if (!string.IsNullOrEmpty(p))
                    {
                        precondDict[p] = true;
                    }
                }
            }

            var effectDict = new Dictionary<string, object>();
            if (effects != null)
            {
                foreach (var e in effects)
                {
                    if (!string.IsNullOrEmpty(e))
                    {
                        effectDict[e] = true;
                    }
                }
            }

            action.Configure(name, cost, duration, precondDict, effectDict);
            return action;
        }

        /// <summary>
        /// Create patrol action with waypoints
        /// </summary>
        public static GOAPDynamicAction CreatePatrolAction(GameObject parent, Transform[] waypoints)
        {
            var action = parent.AddComponent<GOAPDynamicAction>();
            action.Configure("Patrol", 1f, 2f,
                null,
                new Dictionary<string, object> { ["patrolling"] = true });

            int currentWaypoint = 0;

            action.SetPerformFunction((agent) =>
            {
                if (waypoints == null || waypoints.Length == 0)
                {
                    return true;
                }

                var target = waypoints[currentWaypoint];
                if (target == null)
                {
                    return true;
                }

                float distance = Vector3.Distance(agent.transform.position, target.position);
                if (distance < 1f)
                {
                    currentWaypoint = (currentWaypoint + 1) % waypoints.Length;
                    return true;
                }

                // Move towards waypoint
                Vector3 direction = (target.position - agent.transform.position).normalized;
                agent.transform.position += direction * agent.MoveSpeed * Time.deltaTime;

                return false;
            });

            return action;
        }

        /// <summary>
        /// Create attack action
        /// </summary>
        public static GOAPDynamicAction CreateAttackAction(GameObject parent, float damage, float attackRange)
        {
            var action = parent.AddComponent<GOAPDynamicAction>();
            action.Configure("Attack", 1.5f, 1f,
                new Dictionary<string, object>
                {
                    ["enemy_in_range"] = true,
                    ["has_weapon"] = true
                },
                new Dictionary<string, object>
                {
                    ["damage_dealt"] = true
                });

            action.SetPerformFunction((agent) =>
            {
                // Attack logic here
                Debug.Log($"[GOAP] {agent.name} performing attack");
                return true; // Attack is instant
            });

            return action;
        }

        /// <summary>
        /// Create flee action
        /// </summary>
        public static GOAPDynamicAction CreateFleeAction(GameObject parent)
        {
            var action = parent.AddComponent<GOAPDynamicAction>();
            action.Configure("Flee", 0.5f, 3f,
                new Dictionary<string, object>
                {
                    ["health_low"] = true
                },
                new Dictionary<string, object>
                {
                    ["is_safe"] = true
                });

            action.SetPerformFunction((agent) =>
            {
                // Find escape direction (opposite of threat)
                var threat = GameObject.FindGameObjectWithTag("Enemy");
                if (threat != null)
                {
                    Vector3 fleeDirection = (agent.transform.position - threat.transform.position).normalized;
                    agent.transform.position += fleeDirection * agent.MoveSpeed * Time.deltaTime;
                }
                return false; // Keep fleeing
            });

            return action;
        }

        /// <summary>
        /// Create collect resource action
        /// </summary>
        public static GOAPDynamicAction CreateCollectAction(GameObject parent)
        {
            var action = parent.AddComponent<GOAPDynamicAction>();
            action.Configure("Collect", 1f, 2f,
                new Dictionary<string, object>
                {
                    ["resource_nearby"] = true
                },
                new Dictionary<string, object>
                {
                    ["resource_collected"] = true
                });

            return action;
        }
    }
}
