using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using SynapticAIPro;
using Newtonsoft.Json;

namespace SynapticPro
{
    /// <summary>
    /// Project planning and Todo management window
    /// Displays and manages AI-generated plans
    /// </summary>
    public class NexusProjectPlannerWindow : EditorWindow
    {
        // [MenuItem("Window/Synaptic Pro/📋 Project Planner")]
        public static void ShowWindow()
        {
            var window = GetWindow<NexusProjectPlannerWindow>("📋 Project Planner");
            window.minSize = new Vector2(600, 400);
            window.Show();
        }
        
        private Vector2 scrollPosition;
        private ProjectPlan currentPlan;
        private List<ProjectTask> tasks = new List<ProjectTask>();
        private string newTaskInput = "";
        private int selectedTaskIndex = -1;

        // Styles
        private GUIStyle headerStyle;
        private GUIStyle taskStyle;
        private GUIStyle completedTaskStyle;
        private GUIStyle phaseStyle;
        
        [Serializable]
        public class ProjectPlan
        {
            public string title = "New Project";
            public string overview = "";
            public List<ProjectPhase> phases = new List<ProjectPhase>();
            public string currentPhase = "planning";
            public float progress = 0f;
        }
        
        [Serializable]
        public class ProjectPhase
        {
            public string name;
            public List<string> tasks;
            public bool isCompleted;
        }
        
        [Serializable]
        public class ProjectTask
        {
            public int id;
            public string name;
            public string description;
            public string status = "pending"; // pending, in_progress, completed
            public string priority = "medium"; // low, medium, high
            public List<ProjectTask> subtasks;
            public DateTime createdAt;
            public DateTime? completedAt;
        }
        
        private void OnEnable()
        {
            // Setup WebSocket message reception
            NexusWebSocketClient.Instance.OnMessageReceived += OnWebSocketMessage;

            // Load saved data
            LoadProjectData();
        }

        private void OnDisable()
        {
            NexusWebSocketClient.Instance.OnMessageReceived -= OnWebSocketMessage;

            // Save data
            SaveProjectData();
        }
        
        private void InitializeStyles()
        {
            headerStyle = new GUIStyle(EditorStyles.largeLabel)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            
            taskStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 14,
                padding = new RectOffset(20, 10, 5, 5),
                wordWrap = true
            };
            
            completedTaskStyle = new GUIStyle(taskStyle)
            {
                normal = { textColor = new Color(0.5f, 0.5f, 0.5f) },
                fontStyle = FontStyle.Italic
            };
            
            phaseStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                padding = new RectOffset(10, 10, 10, 5)
            };
        }
        
        private void OnGUI()
        {
            if (headerStyle == null)
                InitializeStyles();
            
            DrawHeader();
            
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            
            if (currentPlan != null)
            {
                DrawProjectOverview();
                DrawPhases();
            }
            
            DrawTaskList();
            DrawTaskInput();
            
            EditorGUILayout.EndScrollView();
        }
        
        private void DrawHeader()
        {
            EditorGUILayout.Space(10);
            GUILayout.Label("📋 Project Planner", headerStyle, GUILayout.Height(30));

            if (currentPlan != null)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                // Progress bar
                var rect = GUILayoutUtility.GetRect(300, 20);
                EditorGUI.ProgressBar(rect, currentPlan.progress, $"Progress: {currentPlan.progress * 100:F0}%");
                
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
            
            EditorGUILayout.Space(10);
        }
        
        private void DrawProjectOverview()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Project title
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Project:", GUILayout.Width(80));
            currentPlan.title = EditorGUILayout.TextField(currentPlan.title);
            EditorGUILayout.EndHorizontal();

            // Overview
            GUILayout.Label("Overview:", EditorStyles.boldLabel);
            currentPlan.overview = EditorGUILayout.TextArea(currentPlan.overview, GUILayout.Height(60));
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(10);
        }
        
        private void DrawPhases()
        {
            if (currentPlan.phases.Count == 0) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("📅 Development Phases", EditorStyles.boldLabel);

            foreach (var phase in currentPlan.phases)
            {
                EditorGUILayout.BeginHorizontal();

                // Checkbox
                bool wasCompleted = phase.isCompleted;
                phase.isCompleted = EditorGUILayout.Toggle(phase.isCompleted, GUILayout.Width(20));

                if (wasCompleted != phase.isCompleted)
                {
                    UpdateProgress();
                }

                // Phase name
                var style = phase.isCompleted ? completedTaskStyle : phaseStyle;
                GUILayout.Label(phase.name, style);

                EditorGUILayout.EndHorizontal();

                // Phase tasks
                if (phase.tasks != null && phase.tasks.Count > 0)
                {
                    EditorGUI.indentLevel++;
                    foreach (var task in phase.tasks)
                    {
                        EditorGUILayout.LabelField($"• {task}", EditorStyles.miniLabel);
                    }
                    EditorGUI.indentLevel--;
                }
                
                EditorGUILayout.Space(5);
            }
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(10);
        }
        
        private void DrawTaskList()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("✅ Task List", EditorStyles.boldLabel);

            if (tasks.Count == 0)
            {
                EditorGUILayout.HelpBox("No tasks available. Try telling AI \"I want to create something like...\"", MessageType.Info);
            }
            else
            {
                for (int i = 0; i < tasks.Count; i++)
                {
                    DrawTask(tasks[i], i, 0);
                }
            }
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(10);
        }
        
        private void DrawTask(ProjectTask task, int index, int indent)
        {
            EditorGUILayout.BeginHorizontal();

            // Indent
            GUILayout.Space(indent * 20);

            // Checkbox
            bool wasCompleted = task.status == "completed";
            bool isCompleted = EditorGUILayout.Toggle(wasCompleted, GUILayout.Width(20));

            if (wasCompleted != isCompleted)
            {
                task.status = isCompleted ? "completed" : "pending";
                task.completedAt = isCompleted ? DateTime.Now : (DateTime?)null;
                UpdateProgress();
            }

            // Priority indicator
            var priorityColor = task.priority == "high" ? Color.red :
                              task.priority == "medium" ? Color.yellow :
                              Color.green;
            var oldColor = GUI.color;
            GUI.color = priorityColor;
            GUILayout.Label("●", GUILayout.Width(15));
            GUI.color = oldColor;

            // Task name
            var style = task.status == "completed" ? completedTaskStyle : taskStyle;
            if (GUILayout.Button(task.name, style))
            {
                selectedTaskIndex = index;
            }

            // Status
            var statusIcon = task.status == "completed" ? "✅" :
                           task.status == "in_progress" ? "🔄" : "⏳";
            GUILayout.Label(statusIcon, GUILayout.Width(25));

            // Delete button
            if (GUILayout.Button("×", GUILayout.Width(20)))
            {
                tasks.Remove(task);
            }

            EditorGUILayout.EndHorizontal();

            // Selected task details
            if (selectedTaskIndex == index && !string.IsNullOrEmpty(task.description))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox(task.description, MessageType.None);
                EditorGUI.indentLevel--;
            }

            // Subtasks
            if (task.subtasks != null && task.subtasks.Count > 0)
            {
                foreach (var subtask in task.subtasks)
                {
                    DrawTask(subtask, -1, indent + 1);
                }
            }
        }
        
        private void DrawTaskInput()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("New Task:", GUILayout.Width(80));
            newTaskInput = EditorGUILayout.TextField(newTaskInput);

            if (GUILayout.Button("Add", GUILayout.Width(60)))
            {
                if (!string.IsNullOrEmpty(newTaskInput))
                {
                    AddTask(newTaskInput);
                    newTaskInput = "";
                    GUI.FocusControl(null);
                }
            }
            
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();
        }
        
        private void AddTask(string taskName, string priority = "medium")
        {
            var task = new ProjectTask
            {
                id = tasks.Count + 1,
                name = taskName,
                status = "pending",
                priority = priority,
                createdAt = DateTime.Now,
                subtasks = new List<ProjectTask>()
            };
            
            tasks.Add(task);
            SaveProjectData();
        }
        
        private void OnWebSocketMessage(string message)
        {
            try
            {
                var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(message);
                
                if (data != null && data.ContainsKey("type"))
                {
                    var type = data["type"].ToString();
                    
                    switch (type)
                    {
                        case "project_plan":
                            UpdateProjectPlan(data);
                            break;
                            
                        case "task_list":
                            UpdateTaskList(data);
                            break;
                            
                        case "task_update":
                            UpdateTask(data);
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Project Planner] Error processing message: {e.Message}");
            }
        }
        
        private void UpdateProjectPlan(Dictionary<string, object> data)
        {
            if (data.ContainsKey("plan"))
            {
                var planData = data["plan"] as Newtonsoft.Json.Linq.JObject;
                if (planData != null)
                {
                    currentPlan = planData.ToObject<ProjectPlan>();
                    Repaint();
                }
            }
        }
        
        private void UpdateTaskList(Dictionary<string, object> data)
        {
            if (data.ContainsKey("tasks"))
            {
                var tasksData = data["tasks"] as Newtonsoft.Json.Linq.JArray;
                if (tasksData != null)
                {
                    tasks = tasksData.ToObject<List<ProjectTask>>();
                    Repaint();
                }
            }
        }
        
        private void UpdateTask(Dictionary<string, object> data)
        {
            if (data.ContainsKey("taskId") && data.ContainsKey("status"))
            {
                int taskId = Convert.ToInt32(data["taskId"]);
                string status = data["status"].ToString();
                
                var task = tasks.Find(t => t.id == taskId);
                if (task != null)
                {
                    task.status = status;
                    if (status == "completed")
                    {
                        task.completedAt = DateTime.Now;
                    }
                    UpdateProgress();
                    Repaint();
                }
            }
        }
        
        private void UpdateProgress()
        {
            if (currentPlan != null)
            {
                int totalTasks = tasks.Count;
                int completedTasks = tasks.FindAll(t => t.status == "completed").Count;

                if (totalTasks > 0)
                {
                    currentPlan.progress = (float)completedTasks / totalTasks;
                }

                // Also consider phase progress
                if (currentPlan.phases.Count > 0)
                {
                    int completedPhases = currentPlan.phases.FindAll(p => p.isCompleted).Count;
                    float phaseProgress = (float)completedPhases / currentPlan.phases.Count;

                    // Overall progress is average of task progress and phase progress
                    currentPlan.progress = (currentPlan.progress + phaseProgress) / 2f;
                }
            }
        }

        private void LoadProjectData()
        {
            // Load saved data from EditorPrefs
            string planJson = EditorPrefs.GetString("NexusProjectPlan", "");
            if (!string.IsNullOrEmpty(planJson))
            {
                currentPlan = JsonConvert.DeserializeObject<ProjectPlan>(planJson);
            }
            else
            {
                currentPlan = new ProjectPlan();
            }
            
            string tasksJson = EditorPrefs.GetString("NexusProjectTasks", "");
            if (!string.IsNullOrEmpty(tasksJson))
            {
                tasks = JsonConvert.DeserializeObject<List<ProjectTask>>(tasksJson);
            }
        }

        private void SaveProjectData()
        {
            // Save to EditorPrefs
            if (currentPlan != null)
            {
                EditorPrefs.SetString("NexusProjectPlan", JsonConvert.SerializeObject(currentPlan));
            }
            
            if (tasks != null && tasks.Count > 0)
            {
                EditorPrefs.SetString("NexusProjectTasks", JsonConvert.SerializeObject(tasks));
            }
        }
    }
}
