using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SynapticPro.GOAP
{
    /// <summary>
    /// Class for debugging and visualizing GOAP AI
    /// </summary>
    public class GOAPDebugVisualizer : MonoBehaviour
    {
        [Header("Debug Settings")]
        public bool showCurrentGoal = true;
        public bool showActionPlan = true;
        public bool showWorldState = true;
        public bool showDecisionGraph = false;
        public bool showPerformanceMetrics = true;
        
        [Header("Visual Settings")]
        public Color goalColor = Color.green;
        public Color actionColor = Color.blue;
        public Color completedActionColor = Color.gray;
        public Color failedActionColor = Color.red;
        public float debugWindowWidth = 300f;
        
        [Header("Performance")]
        public int maxPlanDepth = 10;
        public float planningTimeout = 0.1f; // 100ms
        
        // Debug information
        private GOAPDebugInfo debugInfo = new GOAPDebugInfo();
        private Queue<PlanningMetrics> performanceHistory = new Queue<PlanningMetrics>(100);

        // For GUI
        private Vector2 scrollPosition;
        private bool showDebugWindow = true;
        
        void Start()
        {
            // Initialize demo data
            InitializeDemoData();
        }
        
        void Update()
        {
            // Update debug information
            UpdateDebugInfo();
            
            // Performance measurement
            if (showPerformanceMetrics && Time.frameCount % 60 == 0)
            {
                UpdatePerformanceMetrics();
            }
        }
        
        void OnGUI()
        {
            if (!showDebugWindow) return;
            
            // Draw debug window
            GUILayout.Window(0, new Rect(10, 10, debugWindowWidth, 600), DrawDebugWindow, "GOAP Debug");
            
            // World space visualization
            if (showDecisionGraph)
            {
                DrawDecisionGraphOverlay();
            }
        }
        
        void OnDrawGizmos()
        {
            if (!enabled) return;
            
            // Visualize agent's current state
            DrawAgentStatus();
            
            // Visualize action execution status
            if (showActionPlan)
            {
                DrawActionPlan();
            }
        }
        
        private void DrawDebugWindow(int windowID)
        {
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);
            
            // Current goal
            if (showCurrentGoal)
            {
                GUILayout.Label("=== Current Goal ===", GetBoldLabelStyle());
                GUI.color = goalColor;
                GUILayout.Label($"Goal: {debugInfo.currentGoal}");
                GUILayout.Label($"Priority: {debugInfo.goalPriority}");
                GUI.color = Color.white;
                GUILayout.Space(10);
            }
            
            // Action plan
            if (showActionPlan)
            {
                GUILayout.Label("=== Action Plan ===", GetBoldLabelStyle());
                if (debugInfo.currentPlan != null && debugInfo.currentPlan.Count > 0)
                {
                    for (int i = 0; i < debugInfo.currentPlan.Count; i++)
                    {
                        var action = debugInfo.currentPlan[i];
                        GUI.color = GetActionColor(action);
                        GUILayout.Label($"{i + 1}. {action.name} (Cost: {action.cost})");
                        
                        if (i == debugInfo.currentActionIndex)
                        {
                            GUILayout.Label($"   Status: {action.status}");
                            GUILayout.Label($"   Progress: {action.progress:P}");
                        }
                    }
                    GUI.color = Color.white;
                    GUILayout.Label($"Total Cost: {debugInfo.planCost}");
                }
                else
                {
                    GUILayout.Label("No active plan");
                }
                GUILayout.Space(10);
            }
            
            // World state
            if (showWorldState)
            {
                GUILayout.Label("=== World State ===", GetBoldLabelStyle());
                foreach (var state in debugInfo.worldState)
                {
                    GUILayout.Label($"{state.Key}: {state.Value}");
                }
                GUILayout.Space(10);
            }
            
            // Performance metrics
            if (showPerformanceMetrics)
            {
                GUILayout.Label("=== Performance ===", GetBoldLabelStyle());
                GUILayout.Label($"Planning Time: {debugInfo.lastPlanningTime:F3}s");
                GUILayout.Label($"Plan Attempts: {debugInfo.planAttempts}");
                GUILayout.Label($"Graph Nodes: {debugInfo.graphNodes}");
                GUILayout.Label($"Graph Edges: {debugInfo.graphEdges}");
                GUILayout.Label($"Memory Usage: {debugInfo.memoryUsage:F2} MB");
                
                // Performance graph
                DrawPerformanceGraph();
                GUILayout.Space(10);
            }
            
            // Control buttons
            GUILayout.Label("=== Controls ===", GetBoldLabelStyle());
            if (GUILayout.Button("Force Replan"))
            {
                ForceReplan();
            }
            if (GUILayout.Button("Clear Plan"))
            {
                ClearPlan();
            }
            if (GUILayout.Button("Export Debug Log"))
            {
                ExportDebugLog();
            }
            
            showDecisionGraph = GUILayout.Toggle(showDecisionGraph, "Show Decision Graph");
            
            GUILayout.EndScrollView();
            
            GUI.DragWindow();
        }
        
        private void DrawAgentStatus()
        {
            // Display state at agent position
            Vector3 agentPos = transform.position + Vector3.up * 2f;
            
            // Display current goal
            if (!string.IsNullOrEmpty(debugInfo.currentGoal))
            {
                Gizmos.color = goalColor;
                DrawString(agentPos, debugInfo.currentGoal, Color.green);
            }
            
            // Display current action
            if (debugInfo.currentPlan != null && debugInfo.currentActionIndex >= 0 && 
                debugInfo.currentActionIndex < debugInfo.currentPlan.Count)
            {
                var currentAction = debugInfo.currentPlan[debugInfo.currentActionIndex];
                Gizmos.color = actionColor;
                DrawString(agentPos + Vector3.down * 0.3f, $"Action: {currentAction.name}", actionColor);
            }
            
            // Display health bar etc.
            if (debugInfo.worldState.ContainsKey("health"))
            {
                float health = Convert.ToSingle(debugInfo.worldState["health"]);
                DrawHealthBar(transform.position + Vector3.up * 1.5f, health / 100f);
            }
        }
        
        private void DrawActionPlan()
        {
            if (debugInfo.currentPlan == null) return;
            
            Vector3 startPos = transform.position;
            
            for (int i = 0; i < debugInfo.currentPlan.Count; i++)
            {
                var action = debugInfo.currentPlan[i];
                Vector3 endPos = startPos + Vector3.forward * (i + 1) * 2f;
                
                // Connection lines between actions
                Gizmos.color = GetActionColor(action);
                Gizmos.DrawLine(startPos, endPos);
                
                // Action nodes
                Gizmos.DrawWireSphere(endPos, 0.3f);
                
                startPos = endPos;
            }
        }
        
        private void DrawDecisionGraphOverlay()
        {
            // Display decision graph overlay
            Rect graphRect = new Rect(Screen.width - 310, 10, 300, 300);
            GUI.Box(graphRect, "Decision Graph");
            
            // Graph drawing area
            Rect graphArea = new Rect(graphRect.x + 10, graphRect.y + 30, graphRect.width - 20, graphRect.height - 40);
            
            // Temporary graph data
            DrawGraph(graphArea, debugInfo);
        }
        
        private void DrawGraph(Rect area, GOAPDebugInfo info)
        {
            // Draw nodes and edges
            if (info.graphData != null)
            {
                foreach (var edge in info.graphData.edges)
                {
                    Vector2 start = NodeToScreenPos(edge.from, area);
                    Vector2 end = NodeToScreenPos(edge.to, area);
                    DrawLine(start, end, Color.gray);
                }
                
                foreach (var node in info.graphData.nodes)
                {
                    Vector2 pos = NodeToScreenPos(node.position, area);
                    GUI.color = node.isActive ? actionColor : Color.gray;
                    GUI.Box(new Rect(pos.x - 20, pos.y - 10, 40, 20), node.name);
                }
                GUI.color = Color.white;
            }
        }
        
        private void DrawPerformanceGraph()
        {
            if (performanceHistory.Count < 2) return;
            
            Rect graphRect = GUILayoutUtility.GetRect(280, 100);
            GUI.Box(graphRect, "");
            
            var metrics = performanceHistory.ToArray();
            float maxTime = metrics.Max(m => m.planningTime);
            
            for (int i = 1; i < metrics.Length; i++)
            {
                float x1 = graphRect.x + (i - 1) * graphRect.width / (metrics.Length - 1);
                float y1 = graphRect.y + graphRect.height - (metrics[i - 1].planningTime / maxTime) * graphRect.height;
                float x2 = graphRect.x + i * graphRect.width / (metrics.Length - 1);
                float y2 = graphRect.y + graphRect.height - (metrics[i].planningTime / maxTime) * graphRect.height;
                
                DrawLine(new Vector2(x1, y1), new Vector2(x2, y2), Color.cyan);
            }
        }
        
        // Helper methods
        private Color GetActionColor(ActionDebugInfo action)
        {
            switch (action.status)
            {
                case "completed": return completedActionColor;
                case "failed": return failedActionColor;
                case "executing": return actionColor;
                default: return Color.white;
            }
        }
        
        private void DrawString(Vector3 worldPos, string text, Color color)
        {
#if UNITY_EDITOR
            Handles.color = color;
            Handles.Label(worldPos, text);
#endif
        }
        
        private void DrawHealthBar(Vector3 position, float percentage)
        {
            float width = 1f;
            float height = 0.1f;
            
            // Background
            Gizmos.color = Color.red;
            Gizmos.DrawCube(position, new Vector3(width, height, 0.01f));
            
            // Health bar
            Gizmos.color = Color.green;
            Gizmos.DrawCube(position - Vector3.right * (width * (1f - percentage) / 2f), 
                           new Vector3(width * percentage, height, 0.01f));
        }
        
        private void DrawLine(Vector2 start, Vector2 end, Color color)
        {
            var temp = GUI.color;
            GUI.color = color;
            
            float angle = Mathf.Atan2(end.y - start.y, end.x - start.x) * Mathf.Rad2Deg;
            float dist = Vector2.Distance(start, end);
            
            GUIUtility.RotateAroundPivot(angle, start);
            GUI.DrawTexture(new Rect(start.x, start.y - 1, dist, 2), Texture2D.whiteTexture);
            GUIUtility.RotateAroundPivot(-angle, start);
            
            GUI.color = temp;
        }
        
        private Vector2 NodeToScreenPos(Vector2 nodePos, Rect area)
        {
            return new Vector2(
                area.x + nodePos.x * area.width,
                area.y + nodePos.y * area.height
            );
        }
        
        // Initialize demo data
        private void InitializeDemoData()
        {
            debugInfo.currentGoal = "PatrolArea";
            debugInfo.goalPriority = 80;
            debugInfo.planCost = 4.5f;
            
            debugInfo.currentPlan = new List<ActionDebugInfo>
            {
                new ActionDebugInfo { name = "MoveTo", cost = 1f, status = "completed", progress = 1f },
                new ActionDebugInfo { name = "LookAround", cost = 0.5f, status = "executing", progress = 0.6f },
                new ActionDebugInfo { name = "MarkWaypoint", cost = 0.2f, status = "pending", progress = 0f }
            };
            
            debugInfo.currentActionIndex = 1;
            
            debugInfo.worldState = new Dictionary<string, object>
            {
                ["health"] = 85,
                ["has_weapon"] = true,
                ["enemies_nearby"] = 0,
                ["patrol_route"] = "defined",
                ["at_waypoint"] = true
            };
            
            debugInfo.graphNodes = 15;
            debugInfo.graphEdges = 23;
            debugInfo.lastPlanningTime = 0.012f;
            debugInfo.planAttempts = 2;
            debugInfo.memoryUsage = 1.2f;
            
            // Graph data
            debugInfo.graphData = new GraphData
            {
                nodes = new List<GraphNode>
                {
                    new GraphNode { name = "Start", position = new Vector2(0.1f, 0.5f), isActive = true },
                    new GraphNode { name = "MoveTo", position = new Vector2(0.3f, 0.3f), isActive = true },
                    new GraphNode { name = "Attack", position = new Vector2(0.3f, 0.7f), isActive = false },
                    new GraphNode { name = "Goal", position = new Vector2(0.9f, 0.5f), isActive = false }
                },
                edges = new List<GraphEdge>
                {
                    new GraphEdge { from = new Vector2(0.1f, 0.5f), to = new Vector2(0.3f, 0.3f) },
                    new GraphEdge { from = new Vector2(0.1f, 0.5f), to = new Vector2(0.3f, 0.7f) },
                    new GraphEdge { from = new Vector2(0.3f, 0.3f), to = new Vector2(0.9f, 0.5f) }
                }
            };
        }
        
        private void UpdateDebugInfo()
        {
            // Get actual GOAPAgent if available
            var goapAgent = GetComponent<GOAPAgent>();
            if (goapAgent == null) return;

            // Update goal info
            var currentGoal = goapAgent.CurrentGoal;
            if (currentGoal != null)
            {
                debugInfo.currentGoal = currentGoal.GoalName;
                debugInfo.goalPriority = currentGoal.Priority;
            }

            // Update current action info
            var currentAction = goapAgent.CurrentAction;
            if (currentAction != null)
            {
                var existingAction = debugInfo.currentPlan?.FirstOrDefault(a => a.name == currentAction.ActionName);
                if (existingAction != null)
                {
                    existingAction.status = "executing";
                }
            }

            // Update world state from agent
            if (goapAgent.WorldState != null)
            {
                var states = goapAgent.WorldState.GetAllStates();
                debugInfo.worldState.Clear();
                foreach (var kvp in states)
                {
                    debugInfo.worldState[kvp.Key] = kvp.Value;
                }
            }

            // Update plan info
            var remainingPlan = goapAgent.GetRemainingPlan();
            if (remainingPlan != null && remainingPlan.Count > 0)
            {
                debugInfo.currentPlan = remainingPlan.Select((a, i) => new ActionDebugInfo
                {
                    name = a.ActionName,
                    cost = a.Cost,
                    status = i == 0 ? "executing" : "pending",
                    progress = i == 0 ? 0.5f : 0f
                }).ToList();
                debugInfo.currentActionIndex = 0;
                debugInfo.planCost = remainingPlan.Sum(a => a.Cost);
            }
        }
        
        private void UpdatePerformanceMetrics()
        {
            var metric = new PlanningMetrics
            {
                timestamp = Time.time,
                planningTime = debugInfo.lastPlanningTime,
                nodeCount = debugInfo.graphNodes,
                memoryUsage = debugInfo.memoryUsage
            };
            
            performanceHistory.Enqueue(metric);
            if (performanceHistory.Count > 100)
            {
                performanceHistory.Dequeue();
            }
        }
        
        private GUIStyle _boldLabelStyle;
        private GUIStyle GetBoldLabelStyle()
        {
            if (_boldLabelStyle == null)
            {
                _boldLabelStyle = new GUIStyle(GUI.skin.label);
                _boldLabelStyle.fontStyle = FontStyle.Bold;
            }
            return _boldLabelStyle;
        }

        private void ForceReplan()
        {
            UnityEngine.Debug.Log("[GOAP Debug] Forcing replan...");
            var goapAgent = GetComponent<GOAPAgent>();
            if (goapAgent != null)
            {
                goapAgent.ForceReplan();
            }
        }
        
        private void ClearPlan()
        {
            UnityEngine.Debug.Log("[GOAP Debug] Clearing current plan...");
            debugInfo.currentPlan?.Clear();
            debugInfo.currentActionIndex = -1;
        }
        
        private void ExportDebugLog()
        {
            string log = GenerateDebugLog();
            string path = $"Assets/GOAP_Debug_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            System.IO.File.WriteAllText(path, log);
            UnityEngine.Debug.Log($"[GOAP Debug] Exported debug log to: {path}");
#if UNITY_EDITOR
            AssetDatabase.Refresh();
#endif
        }
        
        private string GenerateDebugLog()
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine($"GOAP Debug Log - {DateTime.Now}");
            log.AppendLine("=====================================");
            log.AppendLine($"Agent: {gameObject.name}");
            log.AppendLine($"Current Goal: {debugInfo.currentGoal}");
            log.AppendLine($"Goal Priority: {debugInfo.goalPriority}");
            log.AppendLine("\nCurrent Plan:");
            
            if (debugInfo.currentPlan != null)
            {
                foreach (var action in debugInfo.currentPlan)
                {
                    log.AppendLine($"  - {action.name} (Cost: {action.cost}, Status: {action.status})");
                }
            }
            
            log.AppendLine("\nWorld State:");
            foreach (var state in debugInfo.worldState)
            {
                log.AppendLine($"  - {state.Key}: {state.Value}");
            }
            
            log.AppendLine($"\nPerformance Metrics:");
            log.AppendLine($"  - Planning Time: {debugInfo.lastPlanningTime:F3}s");
            log.AppendLine($"  - Graph Nodes: {debugInfo.graphNodes}");
            log.AppendLine($"  - Memory Usage: {debugInfo.memoryUsage:F2} MB");
            
            return log.ToString();
        }
    }
    
    // Debug information structure
    [System.Serializable]
    public class GOAPDebugInfo
    {
        public string currentGoal;
        public float goalPriority;
        public List<ActionDebugInfo> currentPlan;
        public int currentActionIndex;
        public Dictionary<string, object> worldState = new Dictionary<string, object>();
        public float planCost;
        public float lastPlanningTime;
        public int planAttempts;
        public int graphNodes;
        public int graphEdges;
        public float memoryUsage;
        public GraphData graphData;
    }
    
    [System.Serializable]
    public class ActionDebugInfo
    {
        public string name;
        public float cost;
        public string status; // pending, executing, completed, failed
        public float progress; // 0-1
    }
    
    [System.Serializable]
    public class PlanningMetrics
    {
        public float timestamp;
        public float planningTime;
        public int nodeCount;
        public float memoryUsage;
    }
    
    [System.Serializable]
    public class GraphData
    {
        public List<GraphNode> nodes = new List<GraphNode>();
        public List<GraphEdge> edges = new List<GraphEdge>();
    }
    
    [System.Serializable]
    public class GraphNode
    {
        public string name;
        public Vector2 position;
        public bool isActive;
    }
    
    [System.Serializable]
    public class GraphEdge
    {
        public Vector2 from;
        public Vector2 to;
    }
}