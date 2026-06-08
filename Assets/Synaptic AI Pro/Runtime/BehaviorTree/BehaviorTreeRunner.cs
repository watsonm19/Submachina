using System;
using UnityEngine;
using UnityEngine.Events;

namespace SynapticPro.BehaviorTree
{
    /// <summary>
    /// MonoBehaviour that runs a Behavior Tree
    /// </summary>
    public class BehaviorTreeRunner : MonoBehaviour
    {
        [Header("Execution Settings")]
        [SerializeField] private bool autoStart = true;
        [SerializeField] private float tickInterval = 0f; // 0 = every frame
        [SerializeField] private bool pauseWhenDisabled = true;

        [Header("Debug")]
        [SerializeField] private bool debugMode = false;
        [SerializeField] private string currentNodeName = "";

        [Header("Events")]
        public UnityEvent OnTreeStarted;
        public UnityEvent OnTreeCompleted;
        public UnityEvent OnTreeSucceeded;
        public UnityEvent OnTreeFailed;

        // Runtime
        private BTNode rootNode;
        private BTContext context;
        private float lastTickTime;
        private bool isRunning;
        private BTStatus lastStatus = BTStatus.Running;

        /// <summary>
        /// The root node of the tree
        /// </summary>
        public BTNode RootNode => rootNode;

        /// <summary>
        /// The shared context
        /// </summary>
        public BTContext Context => context;

        /// <summary>
        /// Whether the tree is currently running
        /// </summary>
        public bool IsRunning => isRunning;

        /// <summary>
        /// Last tick status
        /// </summary>
        public BTStatus LastStatus => lastStatus;

        private void Awake()
        {
            context = new BTContext { GameObject = gameObject };
        }

        private void Start()
        {
            if (autoStart && rootNode != null)
            {
                StartTree();
            }
        }

        private void Update()
        {
            if (!isRunning || rootNode == null) return;

            // Check tick interval
            if (tickInterval > 0 && Time.time - lastTickTime < tickInterval)
            {
                return;
            }

            lastTickTime = Time.time;

            // Tick the tree
            lastStatus = rootNode.Tick();

            if (debugMode && rootNode != null)
            {
                currentNodeName = GetCurrentNodeName(rootNode);
            }

            // Handle completion
            if (lastStatus != BTStatus.Running)
            {
                OnTreeCompleted?.Invoke();

                if (lastStatus == BTStatus.Success)
                {
                    OnTreeSucceeded?.Invoke();
                    if (debugMode) Debug.Log($"[BT] Tree completed with Success");
                }
                else
                {
                    OnTreeFailed?.Invoke();
                    if (debugMode) Debug.Log($"[BT] Tree completed with Failure");
                }

                // Reset for next run
                rootNode.Reset();
            }
        }

        private void OnEnable()
        {
            if (pauseWhenDisabled && rootNode != null)
            {
                // Resume
            }
        }

        private void OnDisable()
        {
            if (pauseWhenDisabled && rootNode != null)
            {
                // Pause (just stop ticking, state preserved)
            }
        }

        /// <summary>
        /// Set the root node of the tree
        /// </summary>
        public void SetTree(BTNode root)
        {
            rootNode = root;
            if (rootNode != null)
            {
                PropagateContext(rootNode, context);
            }
        }

        /// <summary>
        /// Start running the tree
        /// </summary>
        public void StartTree()
        {
            if (rootNode == null)
            {
                Debug.LogWarning("[BT] Cannot start tree: root node is null");
                return;
            }

            isRunning = true;
            lastTickTime = Time.time;
            lastStatus = BTStatus.Running;
            OnTreeStarted?.Invoke();

            if (debugMode) Debug.Log("[BT] Tree started");
        }

        /// <summary>
        /// Stop the tree
        /// </summary>
        public void StopTree()
        {
            if (!isRunning) return;

            isRunning = false;
            rootNode?.Abort();
            rootNode?.Reset();

            if (debugMode) Debug.Log("[BT] Tree stopped");
        }

        /// <summary>
        /// Pause the tree
        /// </summary>
        public void PauseTree()
        {
            isRunning = false;
            if (debugMode) Debug.Log("[BT] Tree paused");
        }

        /// <summary>
        /// Resume the tree
        /// </summary>
        public void ResumeTree()
        {
            isRunning = true;
            if (debugMode) Debug.Log("[BT] Tree resumed");
        }

        /// <summary>
        /// Reset and restart the tree
        /// </summary>
        public void RestartTree()
        {
            StopTree();
            StartTree();
        }

        /// <summary>
        /// Set a value in the context
        /// </summary>
        public void SetContextValue<T>(string key, T value)
        {
            context?.Set(key, value);
        }

        /// <summary>
        /// Get a value from the context
        /// </summary>
        public T GetContextValue<T>(string key, T defaultValue = default)
        {
            return context != null ? context.Get(key, defaultValue) : defaultValue;
        }

        /// <summary>
        /// Propagate context to all nodes
        /// </summary>
        private void PropagateContext(BTNode node, BTContext ctx)
        {
            if (node == null) return;

            node.Context = ctx;

            if (node is BTComposite composite)
            {
                foreach (var child in composite.Children)
                {
                    PropagateContext(child, ctx);
                }
            }
            else if (node is BTDecorator decorator)
            {
                PropagateContext(decorator.Child, ctx);
            }
        }

        /// <summary>
        /// Get the name of currently executing node
        /// </summary>
        private string GetCurrentNodeName(BTNode node)
        {
            if (node == null) return "";

            if (node.IsRunning)
            {
                if (node is BTComposite composite)
                {
                    foreach (var child in composite.Children)
                    {
                        string childName = GetCurrentNodeName(child);
                        if (!string.IsNullOrEmpty(childName))
                        {
                            return childName;
                        }
                    }
                }
                else if (node is BTDecorator decorator)
                {
                    string childName = GetCurrentNodeName(decorator.Child);
                    if (!string.IsNullOrEmpty(childName))
                    {
                        return childName;
                    }
                }

                return node.Name;
            }

            return "";
        }

        private void OnDrawGizmosSelected()
        {
            if (!debugMode || rootNode == null) return;

            // Draw current node info
            Vector3 pos = transform.position + Vector3.up * 2f;
            #if UNITY_EDITOR
            UnityEditor.Handles.Label(pos, $"BT: {currentNodeName}\nStatus: {lastStatus}");
            #endif
        }
    }

    /// <summary>
    /// Builder for creating Behavior Trees fluently
    /// </summary>
    public class BehaviorTreeBuilder
    {
        private BTNode root;
        private BTNode current;

        /// <summary>
        /// Start with a Selector
        /// </summary>
        public BehaviorTreeBuilder Selector(string name = "Selector")
        {
            var node = new BTSelector(name);
            SetNode(node);
            return this;
        }

        /// <summary>
        /// Start with a Sequence
        /// </summary>
        public BehaviorTreeBuilder Sequence(string name = "Sequence")
        {
            var node = new BTSequence(name);
            SetNode(node);
            return this;
        }

        /// <summary>
        /// Start with a Parallel
        /// </summary>
        public BehaviorTreeBuilder Parallel(string name = "Parallel")
        {
            var node = new BTParallel(name);
            SetNode(node);
            return this;
        }

        /// <summary>
        /// Add an Action
        /// </summary>
        public BehaviorTreeBuilder Action(Func<BTContext, BTStatus> action, string name = "Action")
        {
            var node = new BTAction(action, name);
            AddToCurrentComposite(node);
            return this;
        }

        /// <summary>
        /// Add a simple Action (auto-succeeds)
        /// </summary>
        public BehaviorTreeBuilder Do(Action<BTContext> action, string name = "Action")
        {
            var node = new BTAction(action, name);
            AddToCurrentComposite(node);
            return this;
        }

        /// <summary>
        /// Add a Condition
        /// </summary>
        public BehaviorTreeBuilder Condition(Func<BTContext, bool> condition, string name = "Condition")
        {
            var node = new BTCondition(condition, name);
            AddToCurrentComposite(node);
            return this;
        }

        /// <summary>
        /// Add a Wait
        /// </summary>
        public BehaviorTreeBuilder Wait(float duration, string name = "Wait")
        {
            var node = new BTWait(duration, name);
            AddToCurrentComposite(node);
            return this;
        }

        /// <summary>
        /// Add an Inverter decorator
        /// </summary>
        public BehaviorTreeBuilder Invert()
        {
            // This should wrap the next node added
            // For simplicity, we'll handle this differently
            return this;
        }

        /// <summary>
        /// Build the tree
        /// </summary>
        public BTNode Build()
        {
            return root;
        }

        /// <summary>
        /// Build and assign to runner
        /// </summary>
        public void BuildAndRun(BehaviorTreeRunner runner)
        {
            runner.SetTree(Build());
            runner.StartTree();
        }

        private void SetNode(BTNode node)
        {
            if (root == null)
            {
                root = node;
            }
            else if (current is BTComposite composite)
            {
                composite.AddChild(node);
            }

            current = node;
        }

        private void AddToCurrentComposite(BTNode node)
        {
            if (current is BTComposite composite)
            {
                composite.AddChild(node);
            }
            else if (root == null)
            {
                root = node;
                current = node;
            }
        }
    }

    /// <summary>
    /// Static helper for building trees
    /// </summary>
    public static class BT
    {
        public static BTSelector Selector(string name = "Selector") => new BTSelector(name);
        public static BTSequence Sequence(string name = "Sequence") => new BTSequence(name);
        public static BTParallel Parallel(string name = "Parallel") => new BTParallel(name);
        public static BTRandomSelector RandomSelector(string name = "RandomSelector") => new BTRandomSelector(name);
        public static BTRandomSequence RandomSequence(string name = "RandomSequence") => new BTRandomSequence(name);

        public static BTInverter Inverter(string name = "Inverter") => new BTInverter(name);
        public static BTSucceeder Succeeder(string name = "Succeeder") => new BTSucceeder(name);
        public static BTFailer Failer(string name = "Failer") => new BTFailer(name);
        public static BTRepeater Repeater(int count = -1, string name = "Repeater") => new BTRepeater(count, name);
        public static BTCooldown Cooldown(float time, string name = "Cooldown") => new BTCooldown(time, name);
        public static BTTimeout Timeout(float time, string name = "Timeout") => new BTTimeout(time, name);
        public static BTRetry Retry(int count = 3, string name = "Retry") => new BTRetry(count, name);
        public static BTDelay Delay(float time, string name = "Delay") => new BTDelay(time, name);

        public static BTAction Action(Func<BTContext, BTStatus> action, string name = "Action") => new BTAction(action, name);
        public static BTAction Action(Action<BTContext> action, string name = "Action") => new BTAction(action, name);
        public static BTCondition Condition(Func<BTContext, bool> condition, string name = "Condition") => new BTCondition(condition, name);
        public static BTWait Wait(float duration, string name = "Wait") => new BTWait(duration, name);
        public static BTLog Log(string message, string name = "Log") => new BTLog(message, name);
        public static BTMoveTo MoveTo(Vector3 target, string name = "MoveTo") => new BTMoveTo(target, name);
        public static BTMoveTo MoveTo(Func<BTContext, Vector3> targetFunc, string name = "MoveTo") => new BTMoveTo(targetFunc, name);
        public static BTRandomSuccess RandomSuccess(float chance = 0.5f, string name = "RandomSuccess") => new BTRandomSuccess(chance, name);
    }
}
