using System.Collections.Generic;
using UnityEngine;

namespace SynapticPro.BehaviorTree
{
    /// <summary>
    /// Selector (OR) - Returns Success if ANY child succeeds
    /// Tries children in order until one succeeds
    /// </summary>
    public class BTSelector : BTComposite
    {
        public BTSelector(string name = "Selector") : base(name) { }

        public override BTStatus Tick()
        {
            for (int i = currentChildIndex; i < children.Count; i++)
            {
                var status = children[i].Tick();

                switch (status)
                {
                    case BTStatus.Success:
                        currentChildIndex = 0;
                        IsRunning = false;
                        return BTStatus.Success;

                    case BTStatus.Running:
                        currentChildIndex = i;
                        IsRunning = true;
                        return BTStatus.Running;

                    case BTStatus.Failure:
                        // Try next child
                        continue;
                }
            }

            // All children failed
            currentChildIndex = 0;
            IsRunning = false;
            return BTStatus.Failure;
        }
    }

    /// <summary>
    /// Sequence (AND) - Returns Success only if ALL children succeed
    /// Executes children in order, stops on first failure
    /// </summary>
    public class BTSequence : BTComposite
    {
        public BTSequence(string name = "Sequence") : base(name) { }

        public override BTStatus Tick()
        {
            for (int i = currentChildIndex; i < children.Count; i++)
            {
                var status = children[i].Tick();

                switch (status)
                {
                    case BTStatus.Success:
                        // Continue to next child
                        continue;

                    case BTStatus.Running:
                        currentChildIndex = i;
                        IsRunning = true;
                        return BTStatus.Running;

                    case BTStatus.Failure:
                        currentChildIndex = 0;
                        IsRunning = false;
                        return BTStatus.Failure;
                }
            }

            // All children succeeded
            currentChildIndex = 0;
            IsRunning = false;
            return BTStatus.Success;
        }
    }

    /// <summary>
    /// Parallel - Runs all children simultaneously
    /// Returns based on policy (RequireAll or RequireOne)
    /// </summary>
    public class BTParallel : BTComposite
    {
        public enum Policy
        {
            RequireAll,  // Success only if ALL succeed
            RequireOne   // Success if ANY succeeds
        }

        public Policy SuccessPolicy { get; set; } = Policy.RequireAll;
        public Policy FailurePolicy { get; set; } = Policy.RequireOne;

        private List<BTStatus> childStatuses = new List<BTStatus>();

        public BTParallel(string name = "Parallel") : base(name) { }

        public BTParallel(Policy successPolicy, Policy failurePolicy, string name = "Parallel") : base(name)
        {
            SuccessPolicy = successPolicy;
            FailurePolicy = failurePolicy;
        }

        public override BTStatus Tick()
        {
            // Initialize status tracking
            while (childStatuses.Count < children.Count)
            {
                childStatuses.Add(BTStatus.Running);
            }

            int successCount = 0;
            int failureCount = 0;
            int runningCount = 0;

            for (int i = 0; i < children.Count; i++)
            {
                // Skip already completed children
                if (childStatuses[i] != BTStatus.Running)
                {
                    if (childStatuses[i] == BTStatus.Success) successCount++;
                    else failureCount++;
                    continue;
                }

                var status = children[i].Tick();
                childStatuses[i] = status;

                switch (status)
                {
                    case BTStatus.Success:
                        successCount++;
                        break;
                    case BTStatus.Failure:
                        failureCount++;
                        break;
                    case BTStatus.Running:
                        runningCount++;
                        break;
                }
            }

            // Check failure policy
            if (FailurePolicy == Policy.RequireOne && failureCount > 0)
            {
                Reset();
                return BTStatus.Failure;
            }
            if (FailurePolicy == Policy.RequireAll && failureCount == children.Count)
            {
                Reset();
                return BTStatus.Failure;
            }

            // Check success policy
            if (SuccessPolicy == Policy.RequireOne && successCount > 0)
            {
                Reset();
                return BTStatus.Success;
            }
            if (SuccessPolicy == Policy.RequireAll && successCount == children.Count)
            {
                Reset();
                return BTStatus.Success;
            }

            // Still running
            if (runningCount > 0)
            {
                IsRunning = true;
                return BTStatus.Running;
            }

            // Default to failure
            Reset();
            return BTStatus.Failure;
        }

        public override void Reset()
        {
            base.Reset();
            childStatuses.Clear();
        }
    }

    /// <summary>
    /// Random Selector - Randomly picks a child to execute
    /// </summary>
    public class BTRandomSelector : BTComposite
    {
        private List<int> shuffledIndices = new List<int>();
        private int currentIndex = 0;

        public BTRandomSelector(string name = "RandomSelector") : base(name) { }

        public override BTStatus Tick()
        {
            // Shuffle on first tick
            if (shuffledIndices.Count == 0)
            {
                ShuffleChildren();
            }

            for (int i = currentIndex; i < shuffledIndices.Count; i++)
            {
                var childIndex = shuffledIndices[i];
                var status = children[childIndex].Tick();

                switch (status)
                {
                    case BTStatus.Success:
                        Reset();
                        return BTStatus.Success;

                    case BTStatus.Running:
                        currentIndex = i;
                        IsRunning = true;
                        return BTStatus.Running;

                    case BTStatus.Failure:
                        continue;
                }
            }

            Reset();
            return BTStatus.Failure;
        }

        private void ShuffleChildren()
        {
            shuffledIndices.Clear();
            for (int i = 0; i < children.Count; i++)
            {
                shuffledIndices.Add(i);
            }

            // Fisher-Yates shuffle
            for (int i = shuffledIndices.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                var temp = shuffledIndices[i];
                shuffledIndices[i] = shuffledIndices[j];
                shuffledIndices[j] = temp;
            }
        }

        public override void Reset()
        {
            base.Reset();
            shuffledIndices.Clear();
            currentIndex = 0;
        }
    }

    /// <summary>
    /// Random Sequence - Executes children in random order
    /// </summary>
    public class BTRandomSequence : BTComposite
    {
        private List<int> shuffledIndices = new List<int>();
        private int currentIndex = 0;

        public BTRandomSequence(string name = "RandomSequence") : base(name) { }

        public override BTStatus Tick()
        {
            // Shuffle on first tick
            if (shuffledIndices.Count == 0)
            {
                ShuffleChildren();
            }

            for (int i = currentIndex; i < shuffledIndices.Count; i++)
            {
                var childIndex = shuffledIndices[i];
                var status = children[childIndex].Tick();

                switch (status)
                {
                    case BTStatus.Success:
                        continue;

                    case BTStatus.Running:
                        currentIndex = i;
                        IsRunning = true;
                        return BTStatus.Running;

                    case BTStatus.Failure:
                        Reset();
                        return BTStatus.Failure;
                }
            }

            Reset();
            return BTStatus.Success;
        }

        private void ShuffleChildren()
        {
            shuffledIndices.Clear();
            for (int i = 0; i < children.Count; i++)
            {
                shuffledIndices.Add(i);
            }

            for (int i = shuffledIndices.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                var temp = shuffledIndices[i];
                shuffledIndices[i] = shuffledIndices[j];
                shuffledIndices[j] = temp;
            }
        }

        public override void Reset()
        {
            base.Reset();
            shuffledIndices.Clear();
            currentIndex = 0;
        }
    }
}
