using System;
using UnityEngine;

namespace SynapticPro.BehaviorTree
{
    /// <summary>
    /// Action node - Executes a function
    /// </summary>
    public class BTAction : BTNode
    {
        private Func<BTContext, BTStatus> action;
        private Action<BTContext> onStart;
        private Action<BTContext> onEnd;

        public BTAction(Func<BTContext, BTStatus> action, string name = "Action") : base(name)
        {
            this.action = action;
        }

        public BTAction(Action<BTContext> simpleAction, string name = "Action") : base(name)
        {
            this.action = (ctx) =>
            {
                simpleAction?.Invoke(ctx);
                return BTStatus.Success;
            };
        }

        /// <summary>
        /// Set callback for when action starts
        /// </summary>
        public BTAction OnStart(Action<BTContext> callback)
        {
            onStart = callback;
            return this;
        }

        /// <summary>
        /// Set callback for when action ends
        /// </summary>
        public BTAction OnEnd(Action<BTContext> callback)
        {
            onEnd = callback;
            return this;
        }

        public override BTStatus Tick()
        {
            if (action == null) return BTStatus.Failure;

            if (!IsRunning)
            {
                onStart?.Invoke(Context);
            }

            var status = action(Context);
            IsRunning = status == BTStatus.Running;

            if (status != BTStatus.Running)
            {
                onEnd?.Invoke(Context);
            }

            return status;
        }

        public override void Abort()
        {
            base.Abort();
            onEnd?.Invoke(Context);
        }
    }

    /// <summary>
    /// Condition node - Checks a condition
    /// </summary>
    public class BTCondition : BTNode
    {
        private Func<BTContext, bool> condition;

        public BTCondition(Func<BTContext, bool> condition, string name = "Condition") : base(name)
        {
            this.condition = condition;
        }

        public override BTStatus Tick()
        {
            if (condition == null) return BTStatus.Failure;

            return condition(Context) ? BTStatus.Success : BTStatus.Failure;
        }
    }

    /// <summary>
    /// Wait node - Waits for specified duration
    /// </summary>
    public class BTWait : BTNode
    {
        public float Duration { get; set; }

        private float startTime;
        private bool started = false;

        public BTWait(float duration, string name = "Wait") : base(name)
        {
            Duration = duration;
        }

        public override BTStatus Tick()
        {
            if (!started)
            {
                startTime = Time.time;
                started = true;
            }

            if (Time.time - startTime >= Duration)
            {
                Reset();
                return BTStatus.Success;
            }

            IsRunning = true;
            return BTStatus.Running;
        }

        public override void Reset()
        {
            base.Reset();
            started = false;
        }
    }

    /// <summary>
    /// Log node - Logs a message (for debugging)
    /// </summary>
    public class BTLog : BTNode
    {
        private string message;
        private Func<BTContext, string> dynamicMessage;

        public BTLog(string message, string name = "Log") : base(name)
        {
            this.message = message;
        }

        public BTLog(Func<BTContext, string> messageFunc, string name = "Log") : base(name)
        {
            this.dynamicMessage = messageFunc;
        }

        public override BTStatus Tick()
        {
            string msg = dynamicMessage != null ? dynamicMessage(Context) : message;
            Debug.Log($"[BT] {msg}");
            return BTStatus.Success;
        }
    }

    /// <summary>
    /// Set Value node - Sets a value in context
    /// </summary>
    public class BTSetValue<T> : BTNode
    {
        private string key;
        private T value;
        private Func<BTContext, T> valueFunc;

        public BTSetValue(string key, T value, string name = "SetValue") : base(name)
        {
            this.key = key;
            this.value = value;
        }

        public BTSetValue(string key, Func<BTContext, T> valueFunc, string name = "SetValue") : base(name)
        {
            this.key = key;
            this.valueFunc = valueFunc;
        }

        public override BTStatus Tick()
        {
            T val = valueFunc != null ? valueFunc(Context) : value;
            Context.Set(key, val);
            return BTStatus.Success;
        }
    }

    /// <summary>
    /// Check Value node - Checks a value in context
    /// </summary>
    public class BTCheckValue<T> : BTNode where T : IEquatable<T>
    {
        private string key;
        private T expectedValue;
        private Func<T, T, bool> comparer;

        public BTCheckValue(string key, T expectedValue, string name = "CheckValue") : base(name)
        {
            this.key = key;
            this.expectedValue = expectedValue;
        }

        public BTCheckValue(string key, T expectedValue, Func<T, T, bool> comparer, string name = "CheckValue") : base(name)
        {
            this.key = key;
            this.expectedValue = expectedValue;
            this.comparer = comparer;
        }

        public override BTStatus Tick()
        {
            var value = Context.Get<T>(key);

            bool match;
            if (comparer != null)
            {
                match = comparer(value, expectedValue);
            }
            else
            {
                match = value != null && value.Equals(expectedValue);
            }

            return match ? BTStatus.Success : BTStatus.Failure;
        }
    }

    /// <summary>
    /// Move To node - Moves towards a target position
    /// </summary>
    public class BTMoveTo : BTNode
    {
        public float Speed { get; set; } = 5f;
        public float StoppingDistance { get; set; } = 0.5f;

        private Vector3 targetPosition;
        private Func<BTContext, Vector3> targetFunc;

        public BTMoveTo(Vector3 target, string name = "MoveTo") : base(name)
        {
            targetPosition = target;
        }

        public BTMoveTo(Func<BTContext, Vector3> targetFunc, string name = "MoveTo") : base(name)
        {
            this.targetFunc = targetFunc;
        }

        public override BTStatus Tick()
        {
            if (Context?.Transform == null) return BTStatus.Failure;

            Vector3 target = targetFunc != null ? targetFunc(Context) : targetPosition;
            Vector3 currentPos = Context.Transform.position;

            float distance = Vector3.Distance(currentPos, target);

            if (distance <= StoppingDistance)
            {
                IsRunning = false;
                return BTStatus.Success;
            }

            // Move towards target
            Vector3 direction = (target - currentPos).normalized;
            Context.Transform.position += direction * Speed * Time.deltaTime;

            // Face direction
            if (direction != Vector3.zero)
            {
                direction.y = 0;
                if (direction.sqrMagnitude > 0.001f)
                {
                    Context.Transform.forward = direction;
                }
            }

            IsRunning = true;
            return BTStatus.Running;
        }
    }

    /// <summary>
    /// Look At node - Rotates to face a target
    /// </summary>
    public class BTLookAt : BTNode
    {
        public float RotationSpeed { get; set; } = 5f;
        public float Tolerance { get; set; } = 5f; // degrees

        private Transform target;
        private Func<BTContext, Transform> targetFunc;

        public BTLookAt(Transform target, string name = "LookAt") : base(name)
        {
            this.target = target;
        }

        public BTLookAt(Func<BTContext, Transform> targetFunc, string name = "LookAt") : base(name)
        {
            this.targetFunc = targetFunc;
        }

        public override BTStatus Tick()
        {
            if (Context?.Transform == null) return BTStatus.Failure;

            Transform lookTarget = targetFunc != null ? targetFunc(Context) : target;
            if (lookTarget == null) return BTStatus.Failure;

            Vector3 direction = lookTarget.position - Context.Transform.position;
            direction.y = 0;

            if (direction.sqrMagnitude < 0.001f)
            {
                return BTStatus.Success;
            }

            Quaternion targetRotation = Quaternion.LookRotation(direction);
            float angle = Quaternion.Angle(Context.Transform.rotation, targetRotation);

            if (angle <= Tolerance)
            {
                return BTStatus.Success;
            }

            Context.Transform.rotation = Quaternion.Slerp(
                Context.Transform.rotation,
                targetRotation,
                RotationSpeed * Time.deltaTime
            );

            IsRunning = true;
            return BTStatus.Running;
        }
    }

    /// <summary>
    /// Random Success node - Randomly returns success or failure
    /// </summary>
    public class BTRandomSuccess : BTNode
    {
        public float SuccessChance { get; set; } = 0.5f;

        public BTRandomSuccess(float chance = 0.5f, string name = "RandomSuccess") : base(name)
        {
            SuccessChance = Mathf.Clamp01(chance);
        }

        public override BTStatus Tick()
        {
            return UnityEngine.Random.value < SuccessChance ? BTStatus.Success : BTStatus.Failure;
        }
    }
}
