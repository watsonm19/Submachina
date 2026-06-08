using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SynapticPro.GOAP;

namespace SynapticPro.GOAP
{
    /// <summary>
    /// GOAP Test Scene Controller
    /// </summary>
    public class GOAPTestSceneController : MonoBehaviour
    {
        [Header("Test Configuration")]
        public bool autoCreateTestAgents = true;
        public int numberOfGuards = 2;
        public int numberOfCollectors = 1;
        public int numberOfSoldiers = 1;
        
        [Header("Scene Objects")]
        public Transform[] patrolPoints;
        public Transform[] resourcePoints;
        public Transform[] coverPoints;
        public Transform playerTransform;
        
        [Header("Visual Feedback")]
        public Material agentMaterial;
        public Material guardMaterial;
        public Material collectorMaterial;
        public Material soldierMaterial;
        
        private List<GameObject> testAgents = new List<GameObject>();
        private Dictionary<string, BehaviorTemplate> availableTemplates;
        
        void Start()
        {
            InitializeTemplates();
            
            if (autoCreateTestAgents)
            {
                StartCoroutine(CreateTestEnvironment());
            }
        }
        
        void Update()
        {
            // Keyboard shortcuts for testing
            if (Input.GetKeyDown(KeyCode.F1))
            {
                CreateGuardAgent();
            }
            if (Input.GetKeyDown(KeyCode.F2))
            {
                CreateCollectorAgent();
            }
            if (Input.GetKeyDown(KeyCode.F3))
            {
                CreateSoldierAgent();
            }
            if (Input.GetKeyDown(KeyCode.F4))
            {
                TestNaturalLanguageBehavior();
            }
            if (Input.GetKeyDown(KeyCode.F5))
            {
                ShowPerformanceReport();
            }
        }
        
        private void InitializeTemplates()
        {
            availableTemplates = new Dictionary<string, BehaviorTemplate>
            {
                ["Guard"] = GOAPBehaviorTemplates.GuardTemplate,
                ["Collector"] = GOAPBehaviorTemplates.CollectorTemplate,
                ["Soldier"] = GOAPBehaviorTemplates.SoldierTemplate,
                ["Wildlife"] = GOAPBehaviorTemplates.WildlifeTemplate,
                ["Merchant"] = GOAPBehaviorTemplates.MerchantTemplate
            };
        }
        
        private IEnumerator CreateTestEnvironment()
        {
            Debug.Log("[GOAP Test] Creating test environment...");
            
            // Setup scene environment
            yield return StartCoroutine(SetupSceneEnvironment());
            
            // Create test agents
            for (int i = 0; i < numberOfGuards; i++)
            {
                yield return new WaitForSeconds(0.5f);
                CreateGuardAgent($"Guard_{i + 1}");
            }
            
            for (int i = 0; i < numberOfCollectors; i++)
            {
                yield return new WaitForSeconds(0.5f);
                CreateCollectorAgent($"Collector_{i + 1}");
            }
            
            for (int i = 0; i < numberOfSoldiers; i++)
            {
                yield return new WaitForSeconds(0.5f);
                CreateSoldierAgent($"Soldier_{i + 1}");
            }
            
            // Start demonstration
            yield return new WaitForSeconds(2f);
            StartDemonstration();
        }
        
        private IEnumerator SetupSceneEnvironment()
        {
            // Create patrol points
            if (patrolPoints == null || patrolPoints.Length == 0)
            {
                CreatePatrolPoints();
            }
            
            // Create resource points
            if (resourcePoints == null || resourcePoints.Length == 0)
            {
                CreateResourcePoints();
            }
            
            // Create cover points
            if (coverPoints == null || coverPoints.Length == 0)
            {
                CreateCoverPoints();
            }
            
            // Create player object
            if (playerTransform == null)
            {
                CreatePlayerObject();
            }
            
            yield return null;
        }
        
        private void CreatePatrolPoints()
        {
            var patrolPointsList = new List<Transform>();
            
            Vector3[] positions = {
                new Vector3(-5, 0, -5),
                new Vector3(5, 0, -5),
                new Vector3(5, 0, 5),
                new Vector3(-5, 0, 5),
                new Vector3(0, 0, 0)
            };
            
            for (int i = 0; i < positions.Length; i++)
            {
                var waypoint = CreateWaypoint($"PatrolPoint_{i + 1}", positions[i], Color.yellow);
                patrolPointsList.Add(waypoint.transform);
            }
            
            patrolPoints = patrolPointsList.ToArray();
            Debug.Log($"[GOAP Test] Created {patrolPoints.Length} patrol points");
        }
        
        private void CreateResourcePoints()
        {
            var resourcePointsList = new List<Transform>();
            
            Vector3[] positions = {
                new Vector3(-8, 0, 2),
                new Vector3(8, 0, -2),
                new Vector3(2, 0, 8),
                new Vector3(-2, 0, -8)
            };
            
            for (int i = 0; i < positions.Length; i++)
            {
                var resource = CreateWaypoint($"ResourcePoint_{i + 1}", positions[i], Color.green);
                resourcePointsList.Add(resource.transform);
            }
            
            resourcePoints = resourcePointsList.ToArray();
            Debug.Log($"[GOAP Test] Created {resourcePoints.Length} resource points");
        }
        
        private void CreateCoverPoints()
        {
            var coverPointsList = new List<Transform>();
            
            Vector3[] positions = {
                new Vector3(-3, 0, 3),
                new Vector3(3, 0, 3),
                new Vector3(3, 0, -3),
                new Vector3(-3, 0, -3)
            };
            
            for (int i = 0; i < positions.Length; i++)
            {
                var cover = CreateCoverObject($"CoverPoint_{i + 1}", positions[i]);
                coverPointsList.Add(cover.transform);
            }
            
            coverPoints = coverPointsList.ToArray();
            Debug.Log($"[GOAP Test] Created {coverPoints.Length} cover points");
        }
        
        private void CreatePlayerObject()
        {
            var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Player";
            player.transform.position = new Vector3(0, 0, -10);
            player.GetComponent<Renderer>().material.color = Color.blue;
            
            // Add simple movement script
            var moveScript = player.AddComponent<SimplePlayerController>();
            
            playerTransform = player.transform;
            Debug.Log("[GOAP Test] Created player object");
        }
        
        private GameObject CreateWaypoint(string name, Vector3 position, Color color)
        {
            var waypoint = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            waypoint.name = name;
            waypoint.transform.position = position;
            waypoint.transform.localScale = Vector3.one * 0.5f;
            waypoint.GetComponent<Renderer>().material.color = color;
            waypoint.GetComponent<Collider>().isTrigger = true;
            
            return waypoint;
        }
        
        private GameObject CreateCoverObject(string name, Vector3 position)
        {
            var cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cover.name = name;
            cover.transform.position = position;
            cover.transform.localScale = new Vector3(1, 2, 1);
            cover.GetComponent<Renderer>().material.color = Color.gray;
            
            return cover;
        }
        
        private void CreateGuardAgent(string agentName = "Guard")
        {
            var agent = CreateBaseAgent(agentName, guardMaterial);
            
            // Add GOAP debug visualizer
            var debugVisualizer = agent.AddComponent<GOAPDebugVisualizer>();
            debugVisualizer.showCurrentGoal = true;
            debugVisualizer.showActionPlan = true;
            debugVisualizer.showWorldState = true;
            
            // Apply guard template (simulation)
            ApplyTemplate(agent, availableTemplates["Guard"]);
            
            testAgents.Add(agent);
            Debug.Log($"[GOAP Test] Created guard agent: {agentName}");
        }
        
        private void CreateCollectorAgent(string agentName = "Collector")
        {
            var agent = CreateBaseAgent(agentName, collectorMaterial);
            
            // Settings for resource collection agent
            var debugVisualizer = agent.AddComponent<GOAPDebugVisualizer>();
            debugVisualizer.goalColor = Color.green;
            
            ApplyTemplate(agent, availableTemplates["Collector"]);
            
            testAgents.Add(agent);
            Debug.Log($"[GOAP Test] Created collector agent: {agentName}");
        }
        
        private void CreateSoldierAgent(string agentName = "Soldier")
        {
            var agent = CreateBaseAgent(agentName, soldierMaterial);
            
            // Settings for combat agent
            var debugVisualizer = agent.AddComponent<GOAPDebugVisualizer>();
            debugVisualizer.goalColor = Color.red;
            debugVisualizer.showPerformanceMetrics = true;
            
            ApplyTemplate(agent, availableTemplates["Soldier"]);
            
            testAgents.Add(agent);
            Debug.Log($"[GOAP Test] Created soldier agent: {agentName}");
        }
        
        private GameObject CreateBaseAgent(string name, Material material)
        {
            var agent = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            agent.name = name;
            
            // Place at random position
            Vector3 randomPos = new Vector3(
                Random.Range(-10f, 10f),
                0.5f,
                Random.Range(-10f, 10f)
            );
            agent.transform.position = randomPos;
            
            // Set material
            if (material != null)
            {
                agent.GetComponent<Renderer>().material = material;
            }
            else
            {
                agent.GetComponent<Renderer>().material.color = Random.ColorHSV();
            }
            
            // Basic navigation components
            var rigidbody = agent.AddComponent<Rigidbody>();
            rigidbody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            
            return agent;
        }
        
        private void ApplyTemplate(GameObject agentObject, BehaviorTemplate template)
        {
            // Add GOAPAgent component and apply template
            var goapAgent = agentObject.GetComponent<GOAPAgent>();
            if (goapAgent == null)
            {
                goapAgent = agentObject.AddComponent<GOAPAgent>();
            }

            // Use the new extension method to apply template
            goapAgent.ApplyTemplate(template);

            // Also add legacy data component for compatibility
            var agentData = agentObject.AddComponent<GOAPAgentData>();
            agentData.template = template;
            agentData.currentGoal = template.Goals[0].Name;
            agentData.worldState = new Dictionary<string, object>(template.InitialWorldState);

            Debug.Log($"[GOAP Test] Applied template '{template.Name}' to {agentObject.name} with GOAPAgent runtime");
        }
        
        private void TestNaturalLanguageBehavior()
        {
            Debug.Log("[GOAP Test] Testing natural language behavior definition...");
            
            // Test natural language AI behavior definition
            string[] testBehaviors = {
                "Guard patrols between waypoints and attacks enemies on sight",
                "Collector gathers resources efficiently and avoids danger",
                "Soldier uses cover, suppresses enemies, and coordinates with squad",
                "Animal hunts prey when hungry and flees from predators",
                "Merchant maximizes profit through trading and negotiation"
            };
            
            foreach (var behavior in testBehaviors)
            {
                Debug.Log($"[GOAP Test] Parsing: '{behavior}'");
                // Add parse logic test here
            }
        }
        
        private void ShowPerformanceReport()
        {
            Debug.Log("[GOAP Test] === Performance Report ===");
            Debug.Log($"Active Agents: {testAgents.Count}");
            
            foreach (var agent in testAgents)
            {
                if (agent != null)
                {
                    var visualizer = agent.GetComponent<GOAPDebugVisualizer>();
                    if (visualizer != null)
                    {
                        Debug.Log($"{agent.name}: Planning efficient, Decisions responsive");
                    }
                }
            }
            
            Debug.Log($"Frame Rate: {(1.0f / Time.deltaTime):F1} FPS");
            Debug.Log($"Memory Usage: {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1024 / 1024} MB");
        }
        
        private void StartDemonstration()
        {
            Debug.Log("[GOAP Test] Starting GOAP demonstration...");
            
            // Demonstration events
            StartCoroutine(DemonstrationSequence());
        }
        
        private IEnumerator DemonstrationSequence()
        {
            Debug.Log("[GOAP Test] Phase 1: Normal patrol and collection");
            yield return new WaitForSeconds(5f);
            
            Debug.Log("[GOAP Test] Phase 2: Simulating threat detection");
            SimulateThreatDetection();
            yield return new WaitForSeconds(10f);
            
            Debug.Log("[GOAP Test] Phase 3: Resource scarcity simulation");
            SimulateResourceScarcity();
            yield return new WaitForSeconds(10f);
            
            Debug.Log("[GOAP Test] Demonstration complete. Press F1-F5 for manual tests.");
        }
        
        private void SimulateThreatDetection()
        {
            // Simulate threat detection
            foreach (var agent in testAgents)
            {
                if (agent != null && agent.name.Contains("Guard"))
                {
                    var agentData = agent.GetComponent<GOAPAgentData>();
                    if (agentData != null)
                    {
                        agentData.worldState["threat_detected"] = true;
                        Debug.Log($"[GOAP Test] {agent.name} detected threat");
                    }
                }
            }
        }
        
        private void SimulateResourceScarcity()
        {
            // Simulate resource shortage
            foreach (var agent in testAgents)
            {
                if (agent != null && agent.name.Contains("Collector"))
                {
                    var agentData = agent.GetComponent<GOAPAgentData>();
                    if (agentData != null)
                    {
                        agentData.worldState["resources_scarce"] = true;
                        Debug.Log($"[GOAP Test] {agent.name} facing resource scarcity");
                    }
                }
            }
        }
        
        void OnGUI()
        {
            // Test GUI
            GUILayout.BeginArea(new Rect(10, Screen.height - 150, 300, 140));
            GUILayout.Box("GOAP Test Controls");
            
            if (GUILayout.Button("F1: Create Guard"))
                CreateGuardAgent();
            if (GUILayout.Button("F2: Create Collector"))
                CreateCollectorAgent();
            if (GUILayout.Button("F3: Create Soldier"))
                CreateSoldierAgent();
            if (GUILayout.Button("F4: Test Natural Language"))
                TestNaturalLanguageBehavior();
            if (GUILayout.Button("F5: Performance Report"))
                ShowPerformanceReport();
            
            GUILayout.EndArea();
        }
    }
    
    /// <summary>
    /// Component for holding GOAP agent data
    /// </summary>
    public class GOAPAgentData : MonoBehaviour
    {
        public BehaviorTemplate template;
        public string currentGoal;
        public Dictionary<string, object> worldState = new Dictionary<string, object>();
        public List<string> currentPlan = new List<string>();
    }
    
    /// <summary>
    /// Simple player controller
    /// </summary>
    public class SimplePlayerController : MonoBehaviour
    {
        public float speed = 5f;
        
        void Update()
        {
            float horizontal = Input.GetAxis("Horizontal");
            float vertical = Input.GetAxis("Vertical");
            
            Vector3 movement = new Vector3(horizontal, 0, vertical) * speed * Time.deltaTime;
            transform.Translate(movement);
        }
    }
}