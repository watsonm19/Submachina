using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

namespace SynapticPro
{
    /// <summary>
    /// Unity Main Thread Dispatcher
    /// Enables execution on Unity main thread from other threads
    /// </summary>
    public class UnityMainThreadDispatcher : MonoBehaviour
    {
        private static readonly Queue<Action> _executionQueue = new Queue<Action>();

        private static UnityMainThreadDispatcher _instance = null;

        public static bool Exists()
        {
            return _instance != null;
        }

        public static UnityMainThreadDispatcher Instance()
        {
            if (!Exists())
            {
                throw new Exception("UnityMainThreadDispatcher could not find the UnityMainThreadDispatcher object.");
            }
            return _instance;
        }

        void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                DontDestroyOnLoad(this.gameObject);
            }
        }

        void OnDestroy()
        {
            _instance = null;
        }

        public void Update()
        {
            lock (_executionQueue)
            {
                while (_executionQueue.Count > 0)
                {
                    _executionQueue.Dequeue().Invoke();
                }
            }
        }

        public void Enqueue(IEnumerator action)
        {
            lock (_executionQueue)
            {
                _executionQueue.Enqueue(() => {
                    StartCoroutine(action);
                });
            }
        }

        public void Enqueue(Action action)
        {
            Enqueue(ActionWrapper(action));
        }

        IEnumerator ActionWrapper(Action a)
        {
            a();
            yield return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            if (_instance == null)
            {
                GameObject dispatcher = new GameObject("UnityMainThreadDispatcher");
                _instance = dispatcher.AddComponent<UnityMainThreadDispatcher>();
            }
        }
    }
}