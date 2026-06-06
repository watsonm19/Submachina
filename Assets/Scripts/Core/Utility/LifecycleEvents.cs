using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;

namespace Utility
{
    /// <summary>
    /// Invokes Unity events on common lifecycle hooks.
    /// Use the toggles to enable only the hooks you need.
    /// </summary>
    public class LifecycleEvents : MonoBehaviour
    {
        [TitleGroup("Lifecycle Event Hooks")]
        [Tooltip("Enable to invoke events when the script instance is being loaded")]
        public bool useAwake;

        [ShowIf(nameof(useAwake))]
        [FoldoutGroup("Lifecycle Event Hooks/Awake", expanded: true)]
        [Tooltip("Invoked when the script instance is being loaded")]
        public UnityEvent onAwake;

        [TitleGroup("Lifecycle Event Hooks")]
        [Tooltip("Enable to invoke events before the first frame update")]
        public bool useStart;

        [ShowIf(nameof(useStart))]
        [FoldoutGroup("Lifecycle Event Hooks/Start", expanded: true)]
        [Tooltip("Invoked before the first frame update")]
        public UnityEvent onStart;

        [TitleGroup("Lifecycle Event Hooks")]
        [Tooltip("Enable to invoke events when the object becomes enabled and active")]
        public bool useOnEnable;

        [ShowIf(nameof(useOnEnable))]
        [FoldoutGroup("Lifecycle Event Hooks/Enable", expanded: true)]
        [Tooltip("Invoked when the object becomes enabled and active")]
        public UnityEvent onEnable;

        [TitleGroup("Lifecycle Event Hooks")]
        [Tooltip("Enable to invoke events when the object becomes disabled")]
        public bool useOnDisable;

        [ShowIf(nameof(useOnDisable))]
        [FoldoutGroup("Lifecycle Event Hooks/Disable", expanded: true)]
        [Tooltip("Invoked when the object becomes disabled")]
        public UnityEvent onDisable;

        [TitleGroup("Lifecycle Event Hooks")]
        [Tooltip("Enable to invoke events when the object is being destroyed")]
        public bool useOnDestroy;

        [ShowIf(nameof(useOnDestroy))]
        [FoldoutGroup("Lifecycle Event Hooks/Destroy", expanded: true)]
        [Tooltip("Invoked when the object is being destroyed")]
        public UnityEvent onDestroy;

        private void Awake()
        {
            if (useAwake)
            {
                onAwake?.Invoke();
            }
        }

        private void Start()
        {
            if (useStart)
            {
                onStart?.Invoke();
            }
        }

        private void OnEnable()
        {
            if (useOnEnable)
            {
                onEnable?.Invoke();
            }
        }

        private void OnDisable()
        {
            if (useOnDisable)
            {
                onDisable?.Invoke();
            }
        }

        private void OnDestroy()
        {
            if (useOnDestroy)
            {
                onDestroy?.Invoke();
            }
        }
    }
}
