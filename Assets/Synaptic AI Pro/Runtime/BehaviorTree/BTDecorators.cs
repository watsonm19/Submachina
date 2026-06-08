using System;
using UnityEngine;

namespace SynapticPro.BehaviorTree
{
    /// <summary>
    /// Inverter - Inverts the result of child
    /// Success -> Failure, Failure -> Success, Running -> Running
    /// </summary>
    public class BTInverter : BTDecorator
    {
        public BTInverter(string name = "Inverter") : base(name) { }

        public override BTStatus Tick()
        {
            if (child == null) return BTStatus.Failure;

            var status = child.Tick();

            switch (status)
            {
                case BTStatus.Success:
                    return BTStatus.Failure;
                case BTStatus.Failure:
                    return BTStatus.Success;
                default:
                    IsRunning = true;
                    return BTStatus.Running;
            }
        }
    }

    /// <summary>
    /// Succeeder - Always returns Success (ignores child result)
    /// </summary>
    public class BTSucceeder : BTDecorator
    {
        public BTSucceeder(string name = "Succeeder") : base(name) { }

        public override BTStatus Tick()
        {
            if (child == null) return BTStatus.Success;

            var status = child.Tick();

            if (status == BTStatus.Running)
            {
                IsRunning = true;
                return BTStatus.Running;
            }

            return BTStatus.Success;
        }
    }

    /// <summary>
    /// Failer - Always returns Failure (ignores child result)
    /// </summary>
    public class BTFailer : BTDecorator
    {
        public BTFailer(string name = "Failer") : base(name) { }

        public override BTStatus Tick()
        {
            if (child == null) return BTStatus.Failure;

            var status = child.Tick();

            if (status == BTStatus.Running)
            {
                IsRunning = true;
                return BTStatus.Running;
            }

            return BTStatus.Failure;
        }
    }

    /// <summary>
    /// Repeater - Repeats child execution N times or until condition
    /// </summary>
    public class BTRepeater : BTDecorator
    {
        public int RepeatCount { get; set; } = -1; // -1 = infinite
        public bool RepeatUntilFail { get; set; } = false;
        public bool RepeatUntilSuccess { get; set; } = false;

        private int currentCount = 0;

        public BTRepeater(int count = -1, string name = "Repeater") : base(name)
        {
            RepeatCount = count;
        }

        public override BTStatus Tick()
        {
            if (child == null) return BTStatus.Failure;

            var status = child.Tick();

            if (status == BTStatus.Running)
            {
                IsRunning = true;
                return BTStatus.Running;
            }

            // Check termination conditions
            if (RepeatUntilFail && status == BTStatus.Failure)
            {
                Reset();
                return BTStatus.Success;
            }

            if (RepeatUntilSuccess && status == BTStatus.Success)
            {
                Reset();
                return BTStatus.Success;
            }

            currentCount++;

            // Check count limit
            if (RepeatCount > 0 && currentCount >= RepeatCount)
            {
                Reset();
                return status;
            }

            // Reset child and continue
            child.Reset();
            IsRunning = true;
            return BTStatus.Running;
        }

        public override void Reset()
        {
            base.Reset();
            currentCount = 0;
        }
    }

    /// <summary>
    /// Cooldown - Prevents child execution for a duration after completion
    /// </summary>
    public class BTCooldown : BTDecorator
    {
        public float CooldownTime { get; set; } = 1f;

        private float lastExecutionTime = float.MinValue;
        private bool childCompleted = false;

        public BTCooldown(float cooldown, string name = "Cooldown") : base(name)
        {
            CooldownTime = cooldown;
        }

        public override BTStatus Tick()
        {
            if (child == null) return BTStatus.Failure;

            // Check cooldown
            float currentTime = Time.time;
            if (currentTime - lastExecutionTime < CooldownTime)
            {
                return BTStatus.Failure;
            }

            var status = child.Tick();

            if (status == BTStatus.Running)
            {
                IsRunning = true;
                return BTStatus.Running;
            }

            // Child completed, start cooldown
            lastExecutionTime = currentTime;
            IsRunning = false;
            return status;
        }

        public override void Reset()
        {
            base.Reset();
            // Don't reset lastExecutionTime - cooldown persists
        }

        /// <summary>
        /// Force reset cooldown
        /// </summary>
        public void ResetCooldown()
        {
            lastExecutionTime = float.MinValue;
        }
    }

    /// <summary>
    /// Timeout - Fails if child takes too long
    /// </summary>
    public class BTTimeout : BTDecorator
    {
        public float TimeoutDuration { get; set; } = 5f;

        private float startTime;
        private bool started = false;

        public BTTimeout(float timeout, string name = "Timeout") : base(name)
        {
            TimeoutDuration = timeout;
        }

        public override BTStatus Tick()
        {
            if (child == null) return BTStatus.Failure;

            if (!started)
            {
                startTime = Time.time;
                started = true;
            }

            // Check timeout
            if (Time.time - startTime >= TimeoutDuration)
            {
                child.Abort();
                Reset();
                return BTStatus.Failure;
            }

            var status = child.Tick();

            if (status != BTStatus.Running)
            {
                Reset();
            }
            else
            {
                IsRunning = true;
            }

            return status;
        }

        public override void Reset()
        {
            base.Reset();
            started = false;
        }
    }

    /// <summary>
    /// Condition Decorator - Only executes child if condition is true
    /// </summary>
    public class BTConditional : BTDecorator
    {
        private Func<BTContext, bool> condition;

        public BTConditional(Func<BTContext, bool> condition, string name = "Conditional") : base(name)
        {
            this.condition = condition;
        }

        public override BTStatus Tick()
        {
            if (condition == null || !condition(Context))
            {
                return BTStatus.Failure;
            }

            if (child == null) return BTStatus.Success;

            var status = child.Tick();
            IsRunning = status == BTStatus.Running;
            return status;
        }
    }

    /// <summary>
    /// Retry - Retries child on failure
    /// </summary>
    public class BTRetry : BTDecorator
    {
        public int MaxRetries { get; set; } = 3;

        private int currentRetries = 0;

        public BTRetry(int maxRetries = 3, string name = "Retry") : base(name)
        {
            MaxRetries = maxRetries;
        }

        public override BTStatus Tick()
        {
            if (child == null) return BTStatus.Failure;

            var status = child.Tick();

            switch (status)
            {
                case BTStatus.Success:
                    Reset();
                    return BTStatus.Success;

                case BTStatus.Running:
                    IsRunning = true;
                    return BTStatus.Running;

                case BTStatus.Failure:
                    currentRetries++;
                    if (currentRetries >= MaxRetries)
                    {
                        Reset();
                        return BTStatus.Failure;
                    }
                    child.Reset();
                    IsRunning = true;
                    return BTStatus.Running;
            }

            return BTStatus.Failure;
        }

        public override void Reset()
        {
            base.Reset();
            currentRetries = 0;
        }
    }

    /// <summary>
    /// Delay - Waits before executing child
    /// </summary>
    public class BTDelay : BTDecorator
    {
        public float DelayTime { get; set; } = 1f;

        private float startTime;
        private bool waiting = false;
        private bool delayComplete = false;

        public BTDelay(float delay, string name = "Delay") : base(name)
        {
            DelayTime = delay;
        }

        public override BTStatus Tick()
        {
            if (!waiting && !delayComplete)
            {
                startTime = Time.time;
                waiting = true;
            }

            if (waiting)
            {
                if (Time.time - startTime >= DelayTime)
                {
                    waiting = false;
                    delayComplete = true;
                }
                else
                {
                    IsRunning = true;
                    return BTStatus.Running;
                }
            }

            if (child == null)
            {
                Reset();
                return BTStatus.Success;
            }

            var status = child.Tick();

            if (status != BTStatus.Running)
            {
                Reset();
            }
            else
            {
                IsRunning = true;
            }

            return status;
        }

        public override void Reset()
        {
            base.Reset();
            waiting = false;
            delayComplete = false;
        }
    }
}
