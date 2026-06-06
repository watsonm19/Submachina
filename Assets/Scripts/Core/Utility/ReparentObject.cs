using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;
using System.Collections;

namespace Utility
{
    /**
     * Reparents a target GameObject to a new parent (or the scene root) when triggered.
     * Designed to be called from UnityEvents during a parent's end-of-life sequence,
     * allowing child objects (e.g., particle effects) to outlive their parent.
     *
     * Typical usage chain:
     *   1. Parent's death event calls Reparent() -- child detaches to scene root
     *   2. Same event chain calls PlayEffect() -- particles start on the now-safe object
     *   3. Parent tree gets destroyed -- reparented child is unaffected
     */
    public class ReparentObject : MonoBehaviour
    {
        // ── Reparent Configuration ──────────────────────────────────────────

        [TitleGroup("Reparent Configuration")]
        [Tooltip("The GameObject to reparent. Leave empty to reparent this GameObject.")]
        public GameObject targetObject;

        [TitleGroup("Reparent Configuration")]
        [Tooltip("New parent Transform. Leave empty to reparent to the scene root (world).")]
        public Transform newParent;

        [TitleGroup("Reparent Configuration")]
        [Tooltip("Keep the object's world position/rotation/scale when reparenting. Usually true for particle effects.")]
        public bool preserveWorldPosition = true;

        // ── Auto Destroy ────────────────────────────────────────────────────

        [TitleGroup("Auto Destroy")]
        [Tooltip("Destroy the reparented object after a delay.")]
        public bool destroyAfterReparent = false;

        [TitleGroup("Auto Destroy")]
        [Tooltip("Seconds to wait before destroying the reparented object.")]
        [Range(0f, 60f)]
        [ShowIf(nameof(destroyAfterReparent))]
        public float destroyDelay = 5f;

        // ── Events ──────────────────────────────────────────────────────────

        [FoldoutGroup("Events")]
        [Tooltip("Fired immediately after the object has been reparented.")]
        public UnityEvent onReparentComplete;

        // ── Internal State ──────────────────────────────────────────────────

        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly]
        [Tooltip("Whether Reparent() has already been called on this instance.")]
        #pragma warning disable CS0414
        private bool _hasBeenReparented = false;
        #pragma warning restore CS0414

        private Coroutine _destroyCoroutine;

        // ── Public API ──────────────────────────────────────────────────────

        /**
         * Reparents the target object to the configured new parent (or scene root).
         * Callable from UnityEvents with no arguments. Guards against double-invocation.
         */
        [Button("Reparent Now", ButtonSizes.Large)]
        [PropertySpace(SpaceBefore = 10)]
        public void Reparent()
        {
            // Guard against double-reparenting
            if (_hasBeenReparented)
            {
                Debug.LogWarning($"[ReparentObject] '{gameObject.name}' has already been reparented. Ignoring.", this);
                return;
            }

            // Resolve the target (self if not specified)
            GameObject resolved = ResolveTarget();
            if (resolved == null)
            {
                Debug.LogWarning($"[ReparentObject] Target is null or destroyed on '{gameObject.name}'. Cannot reparent.", this);
                return;
            }

            // Reparent to new parent (null = scene root)
            resolved.transform.SetParent(newParent, preserveWorldPosition);
            _hasBeenReparented = true;

            // Notify listeners
            onReparentComplete?.Invoke();

            // Start auto-destroy countdown if configured
            if (destroyAfterReparent)
                _destroyCoroutine = StartCoroutine(DestroyAfterDelayCoroutine());
        }

        // ── Private Helpers ─────────────────────────────────────────────────

        /**
         * Returns the effective target: the assigned targetObject, or this GameObject if none is set.
         */
        private GameObject ResolveTarget()
        {
            return targetObject != null ? targetObject : gameObject;
        }

        /**
         * Waits for the configured delay, then destroys the target object.
         */
        private IEnumerator DestroyAfterDelayCoroutine()
        {
            yield return new WaitForSeconds(destroyDelay);

            // Re-resolve in case the reference changed or was destroyed during the wait
            GameObject resolved = ResolveTarget();
            if (resolved != null) Destroy(resolved);

            _destroyCoroutine = null;
        }

        // ── Lifecycle ───────────────────────────────────────────────────────

        /**
         * Cancels any pending destroy coroutine when the component is disabled.
         */
        private void OnDisable()
        {
            if (_destroyCoroutine != null)
            {
                StopCoroutine(_destroyCoroutine);
                _destroyCoroutine = null;
            }
        }

        // ── Debug ───────────────────────────────────────────────────────────

        /**
         * Resets the reparent guard so Reparent() can be called again.
         * Useful for iterative testing in play mode.
         */
        [TitleGroup("Debug")]
        [Button("Reset State", ButtonSizes.Medium)]
        private void ResetState()
        {
            if (_destroyCoroutine != null)
            {
                StopCoroutine(_destroyCoroutine);
                _destroyCoroutine = null;
            }
            _hasBeenReparented = false;
        }
    }
}
