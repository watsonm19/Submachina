using System;
using System.Collections.Generic;
using UnityEngine;

namespace SynapticPro.BehaviorTree
{
    /// <summary>
    /// Node execution status
    /// </summary>
    public enum BTStatus
    {
        Success,
        Failure,
        Running
    }

    /// <summary>
    /// Base class for all Behavior Tree nodes
    /// </summary>
    public abstract class BTNode
    {
        public string Name { get; set; }
        public BTNode Parent { get; protected set; }
        public bool IsRunning { get; protected set; }

        /// <summary>
        /// Context data shared across the tree
        /// </summary>
        public BTContext Context { get; set; }

        protected BTNode(string name = "Node")
        {
            Name = name;
        }

        /// <summary>
        /// Execute this node
        /// </summary>
        public abstract BTStatus Tick();

        /// <summary>
        /// Reset node state
        /// </summary>
        public virtual void Reset()
        {
            IsRunning = false;
        }

        /// <summary>
        /// Called when node is aborted
        /// </summary>
        public virtual void Abort()
        {
            IsRunning = false;
        }

        /// <summary>
        /// Set parent node
        /// </summary>
        internal void SetParent(BTNode parent)
        {
            Parent = parent;
        }
    }

    /// <summary>
    /// Base class for nodes with children
    /// </summary>
    public abstract class BTComposite : BTNode
    {
        protected List<BTNode> children = new List<BTNode>();
        protected int currentChildIndex = 0;

        public IReadOnlyList<BTNode> Children => children;

        protected BTComposite(string name = "Composite") : base(name) { }

        /// <summary>
        /// Add a child node
        /// </summary>
        public BTComposite AddChild(BTNode child)
        {
            if (child != null)
            {
                child.SetParent(this);
                child.Context = Context;
                children.Add(child);
            }
            return this;
        }

        /// <summary>
        /// Add multiple children
        /// </summary>
        public BTComposite AddChildren(params BTNode[] nodes)
        {
            foreach (var node in nodes)
            {
                AddChild(node);
            }
            return this;
        }

        /// <summary>
        /// Remove a child node
        /// </summary>
        public void RemoveChild(BTNode child)
        {
            if (child != null)
            {
                child.SetParent(null);
                children.Remove(child);
            }
        }

        /// <summary>
        /// Clear all children
        /// </summary>
        public void ClearChildren()
        {
            foreach (var child in children)
            {
                child.SetParent(null);
            }
            children.Clear();
            currentChildIndex = 0;
        }

        public override void Reset()
        {
            base.Reset();
            currentChildIndex = 0;
            foreach (var child in children)
            {
                child.Reset();
            }
        }

        public override void Abort()
        {
            base.Abort();
            foreach (var child in children)
            {
                child.Abort();
            }
        }
    }

    /// <summary>
    /// Base class for decorator nodes (single child)
    /// </summary>
    public abstract class BTDecorator : BTNode
    {
        protected BTNode child;

        public BTNode Child => child;

        protected BTDecorator(string name = "Decorator") : base(name) { }

        /// <summary>
        /// Set the child node
        /// </summary>
        public BTDecorator SetChild(BTNode node)
        {
            if (child != null)
            {
                child.SetParent(null);
            }
            child = node;
            if (child != null)
            {
                child.SetParent(this);
                child.Context = Context;
            }
            return this;
        }

        public override void Reset()
        {
            base.Reset();
            child?.Reset();
        }

        public override void Abort()
        {
            base.Abort();
            child?.Abort();
        }
    }

    /// <summary>
    /// Shared context data for the behavior tree
    /// </summary>
    public class BTContext
    {
        private Dictionary<string, object> data = new Dictionary<string, object>();

        /// <summary>
        /// The GameObject this tree is attached to
        /// </summary>
        public GameObject GameObject { get; set; }

        /// <summary>
        /// The Transform of the GameObject
        /// </summary>
        public Transform Transform => GameObject?.transform;

        /// <summary>
        /// Set a value in the context
        /// </summary>
        public void Set<T>(string key, T value)
        {
            data[key] = value;
        }

        /// <summary>
        /// Get a value from the context
        /// </summary>
        public T Get<T>(string key, T defaultValue = default)
        {
            if (data.TryGetValue(key, out var value) && value is T typedValue)
            {
                return typedValue;
            }
            return defaultValue;
        }

        /// <summary>
        /// Check if a key exists
        /// </summary>
        public bool Has(string key)
        {
            return data.ContainsKey(key);
        }

        /// <summary>
        /// Remove a value
        /// </summary>
        public void Remove(string key)
        {
            data.Remove(key);
        }

        /// <summary>
        /// Clear all data
        /// </summary>
        public void Clear()
        {
            data.Clear();
        }
    }
}
