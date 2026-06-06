using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;

namespace Utility
{
    /// <summary>
    /// Serializable wrappers required for typed UnityEvents to appear in the Unity Inspector.
    /// </summary>
    [System.Serializable] public class GameObjectEvent        : UnityEvent<GameObject> { }
    [System.Serializable] public class Collision2DUnityEvent  : UnityEvent<Collision2D> { }
    [System.Serializable] public class Collider2DUnityEvent   : UnityEvent<Collider2D>  { }
    [System.Serializable] public class CollisionUnityEvent    : UnityEvent<Collision>   { }
    [System.Serializable] public class ColliderUnityEvent     : UnityEvent<Collider>    { }

    /// <summary>
    /// Fires Unity events for 2D and 3D physics collisions and triggers, with optional
    /// filtering by layer mask, tag, or specific GameObjects. Covers enter, exit, and stay
    /// callbacks for both collision and trigger variants in one unified component.
    ///
    /// Filters are AND-combined: each configured filter (non-Everything layer mask, non-empty
    /// tag list, non-empty object list) must pass for the event to fire. Unconfigured filters
    /// are ignored. Events receive the other GameObject as a parameter.
    /// </summary>
    public class CollisionEvents : MonoBehaviour
    {
        // ─── Filter ────────────────────────────────────────────────────────────────

        [TitleGroup("Collision Filter")]
        [InfoBox("Filters are AND-combined. Unconfigured filters (Everything layer mask, empty lists) are ignored.")]
        [Tooltip("Only fire events for colliders on these layers. Keep at 'Everything' to skip layer filtering.")]
        public LayerMask layerFilter = ~0;

        [TitleGroup("Collision Filter")]
        [Tooltip("Only fire events when the other object has any of these tags. Leave empty to skip tag filtering.")]
        public string[] tagFilter = new string[0];

        [TitleGroup("Collision Filter")]
        [Tooltip("Only fire events for these specific GameObjects. Leave empty to skip object filtering.")]
        public GameObject[] objectFilter = new GameObject[0];

        // ─── 2D Collision Events ───────────────────────────────────────────────────

        [TabGroup("2D Physics", "Collision")]
        [LabelText("On Enter"), Tooltip("Fire event when a 2D collision begins")]
        public bool useCollisionEnter2D;

        [TabGroup("2D Physics", "Collision"), ShowIf(nameof(useCollisionEnter2D)), HideLabel]
        public GameObjectEvent onCollisionEnter2D;

        [TabGroup("2D Physics", "Collision"), ShowIf(nameof(useCollisionEnter2D)), LabelText("On Enter (Typed)")]
        [Tooltip("Raised with the full Collision2D data — use to drive typed listeners or Unity Atoms raisers")]
        public Collision2DUnityEvent onCollisionEnter2DTyped;

        [TabGroup("2D Physics", "Collision"), Space]
        [LabelText("On Exit"), Tooltip("Fire event when a 2D collision ends")]
        public bool useCollisionExit2D;

        [TabGroup("2D Physics", "Collision"), ShowIf(nameof(useCollisionExit2D)), HideLabel]
        public GameObjectEvent onCollisionExit2D;

        [TabGroup("2D Physics", "Collision"), ShowIf(nameof(useCollisionExit2D)), LabelText("On Exit (Typed)")]
        [Tooltip("Raised with the full Collision2D data")]
        public Collision2DUnityEvent onCollisionExit2DTyped;

        [TabGroup("2D Physics", "Collision"), Space]
        [LabelText("On Stay"), Tooltip("Fire event each frame a 2D collision is ongoing")]
        public bool useCollisionStay2D;

        [TabGroup("2D Physics", "Collision"), ShowIf(nameof(useCollisionStay2D)), HideLabel]
        public GameObjectEvent onCollisionStay2D;

        [TabGroup("2D Physics", "Collision"), ShowIf(nameof(useCollisionStay2D)), LabelText("On Stay (Typed)")]
        [Tooltip("Raised with the full Collision2D data")]
        public Collision2DUnityEvent onCollisionStay2DTyped;

        // ─── 2D Trigger Events ─────────────────────────────────────────────────────

        [TabGroup("2D Physics", "Trigger")]
        [LabelText("On Enter"), Tooltip("Fire event when an object enters a 2D trigger")]
        public bool useTriggerEnter2D;

        [TabGroup("2D Physics", "Trigger"), ShowIf(nameof(useTriggerEnter2D)), HideLabel]
        public GameObjectEvent onTriggerEnter2D;

        [TabGroup("2D Physics", "Trigger"), ShowIf(nameof(useTriggerEnter2D)), LabelText("On Enter (Typed)")]
        [Tooltip("Raised with the full Collider2D data — use to drive typed listeners or Unity Atoms raisers")]
        public Collider2DUnityEvent onTriggerEnter2DTyped;

        [TabGroup("2D Physics", "Trigger"), Space]
        [LabelText("On Exit"), Tooltip("Fire event when an object exits a 2D trigger")]
        public bool useTriggerExit2D;

        [TabGroup("2D Physics", "Trigger"), ShowIf(nameof(useTriggerExit2D)), HideLabel]
        public GameObjectEvent onTriggerExit2D;

        [TabGroup("2D Physics", "Trigger"), ShowIf(nameof(useTriggerExit2D)), LabelText("On Exit (Typed)")]
        [Tooltip("Raised with the full Collider2D data")]
        public Collider2DUnityEvent onTriggerExit2DTyped;

        [TabGroup("2D Physics", "Trigger"), Space]
        [LabelText("On Stay"), Tooltip("Fire event each frame an object remains inside a 2D trigger")]
        public bool useTriggerStay2D;

        [TabGroup("2D Physics", "Trigger"), ShowIf(nameof(useTriggerStay2D)), HideLabel]
        public GameObjectEvent onTriggerStay2D;

        [TabGroup("2D Physics", "Trigger"), ShowIf(nameof(useTriggerStay2D)), LabelText("On Stay (Typed)")]
        [Tooltip("Raised with the full Collider2D data")]
        public Collider2DUnityEvent onTriggerStay2DTyped;

        // ─── 3D Collision Events ───────────────────────────────────────────────────

        [TabGroup("3D Physics", "Collision")]
        [LabelText("On Enter"), Tooltip("Fire event when a 3D collision begins")]
        public bool useCollisionEnter;

        [TabGroup("3D Physics", "Collision"), ShowIf(nameof(useCollisionEnter)), HideLabel]
        public GameObjectEvent onCollisionEnter;

        [TabGroup("3D Physics", "Collision"), ShowIf(nameof(useCollisionEnter)), LabelText("On Enter (Typed)")]
        [Tooltip("Raised with the full Collision data — use to drive typed listeners or Unity Atoms raisers")]
        public CollisionUnityEvent onCollisionEnterTyped;

        [TabGroup("3D Physics", "Collision"), Space]
        [LabelText("On Exit"), Tooltip("Fire event when a 3D collision ends")]
        public bool useCollisionExit;

        [TabGroup("3D Physics", "Collision"), ShowIf(nameof(useCollisionExit)), HideLabel]
        public GameObjectEvent onCollisionExit;

        [TabGroup("3D Physics", "Collision"), ShowIf(nameof(useCollisionExit)), LabelText("On Exit (Typed)")]
        [Tooltip("Raised with the full Collision data")]
        public CollisionUnityEvent onCollisionExitTyped;

        [TabGroup("3D Physics", "Collision"), Space]
        [LabelText("On Stay"), Tooltip("Fire event each frame a 3D collision is ongoing")]
        public bool useCollisionStay;

        [TabGroup("3D Physics", "Collision"), ShowIf(nameof(useCollisionStay)), HideLabel]
        public GameObjectEvent onCollisionStay;

        [TabGroup("3D Physics", "Collision"), ShowIf(nameof(useCollisionStay)), LabelText("On Stay (Typed)")]
        [Tooltip("Raised with the full Collision data")]
        public CollisionUnityEvent onCollisionStayTyped;

        // ─── 3D Trigger Events ─────────────────────────────────────────────────────

        [TabGroup("3D Physics", "Trigger")]
        [LabelText("On Enter"), Tooltip("Fire event when an object enters a 3D trigger")]
        public bool useTriggerEnter;

        [TabGroup("3D Physics", "Trigger"), ShowIf(nameof(useTriggerEnter)), HideLabel]
        public GameObjectEvent onTriggerEnter;

        [TabGroup("3D Physics", "Trigger"), ShowIf(nameof(useTriggerEnter)), LabelText("On Enter (Typed)")]
        [Tooltip("Raised with the full Collider data — use to drive typed listeners or Unity Atoms raisers")]
        public ColliderUnityEvent onTriggerEnterTyped;

        [TabGroup("3D Physics", "Trigger"), Space]
        [LabelText("On Exit"), Tooltip("Fire event when an object exits a 3D trigger")]
        public bool useTriggerExit;

        [TabGroup("3D Physics", "Trigger"), ShowIf(nameof(useTriggerExit)), HideLabel]
        public GameObjectEvent onTriggerExit;

        [TabGroup("3D Physics", "Trigger"), ShowIf(nameof(useTriggerExit)), LabelText("On Exit (Typed)")]
        [Tooltip("Raised with the full Collider data")]
        public ColliderUnityEvent onTriggerExitTyped;

        [TabGroup("3D Physics", "Trigger"), Space]
        [LabelText("On Stay"), Tooltip("Fire event each frame an object remains inside a 3D trigger")]
        public bool useTriggerStay;

        [TabGroup("3D Physics", "Trigger"), ShowIf(nameof(useTriggerStay)), HideLabel]
        public GameObjectEvent onTriggerStay;

        [TabGroup("3D Physics", "Trigger"), ShowIf(nameof(useTriggerStay)), LabelText("On Stay (Typed)")]
        [Tooltip("Raised with the full Collider data")]
        public ColliderUnityEvent onTriggerStayTyped;

        // ─── Debug ─────────────────────────────────────────────────────────────────

        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly, LabelText("Last Collider")]
        [Tooltip("Name of the most recent GameObject that triggered any event")]
        private string _lastColliderName = "None";

        [Button("Clear")]
        [ButtonGroup("Debug/Controls")]
        private void ClearDebug() => _lastColliderName = "None";

        // ─── Filter Logic ──────────────────────────────────────────────────────────

        /**
         * Returns true if the given GameObject passes all active filters.
         * Each filter only applies when configured (non-Everything layer mask, non-empty lists).
         * All active filters must pass — they are AND-combined, not OR.
         *
         * Example: layerFilter="Enemy", tagFilter=["Hostile"] fires only when the object
         * is on the Enemy layer AND has the "Hostile" tag.
         */
        private bool PassesFilter(GameObject other)
        {
            // Layer filter: skip if set to Everything (-1); otherwise check the layer bit
            if (layerFilter.value != -1 && (layerFilter & (1 << other.layer)) == 0)
                return false;

            // Tag filter: object must have at least one of the specified tags
            if (tagFilter.Length > 0)
            {
                bool tagMatched = false;
                foreach (var tag in tagFilter)
                    if (!string.IsNullOrEmpty(tag) && other.CompareTag(tag)) { tagMatched = true; break; }
                if (!tagMatched) return false;
            }

            // Object filter: object must be one of the specified GameObjects
            if (objectFilter.Length > 0)
            {
                bool objectMatched = false;
                foreach (var obj in objectFilter)
                    if (obj != null && other == obj) { objectMatched = true; break; }
                if (!objectMatched) return false;
            }

            return true;
        }

        // ─── 2D Collision Callbacks ────────────────────────────────────────────────

        /**
         * Called by Unity when this collider first touches another 2D collider.
         */
        private void OnCollisionEnter2D(Collision2D col)
        {
            if (!useCollisionEnter2D || !PassesFilter(col.gameObject)) return;
            _lastColliderName = col.gameObject.name;
            onCollisionEnter2D?.Invoke(col.gameObject);
            onCollisionEnter2DTyped?.Invoke(col);
        }

        /**
         * Called by Unity when this collider stops touching another 2D collider.
         */
        private void OnCollisionExit2D(Collision2D col)
        {
            if (!useCollisionExit2D || !PassesFilter(col.gameObject)) return;
            _lastColliderName = col.gameObject.name;
            onCollisionExit2D?.Invoke(col.gameObject);
            onCollisionExit2DTyped?.Invoke(col);
        }

        /**
         * Called by Unity each frame this collider remains in contact with another 2D collider.
         */
        private void OnCollisionStay2D(Collision2D col)
        {
            if (!useCollisionStay2D || !PassesFilter(col.gameObject)) return;
            _lastColliderName = col.gameObject.name;
            onCollisionStay2D?.Invoke(col.gameObject);
            onCollisionStay2DTyped?.Invoke(col);
        }

        // ─── 2D Trigger Callbacks ──────────────────────────────────────────────────

        /**
         * Called by Unity when a 2D collider enters this trigger zone.
         */
        private void OnTriggerEnter2D(Collider2D col)
        {
            if (!useTriggerEnter2D || !PassesFilter(col.gameObject)) return;
            _lastColliderName = col.gameObject.name;
            onTriggerEnter2D?.Invoke(col.gameObject);
            onTriggerEnter2DTyped?.Invoke(col);
        }

        /**
         * Called by Unity when a 2D collider exits this trigger zone.
         */
        private void OnTriggerExit2D(Collider2D col)
        {
            if (!useTriggerExit2D || !PassesFilter(col.gameObject)) return;
            _lastColliderName = col.gameObject.name;
            onTriggerExit2D?.Invoke(col.gameObject);
            onTriggerExit2DTyped?.Invoke(col);
        }

        /**
         * Called by Unity each frame a 2D collider remains inside this trigger zone.
         */
        private void OnTriggerStay2D(Collider2D col)
        {
            if (!useTriggerStay2D || !PassesFilter(col.gameObject)) return;
            _lastColliderName = col.gameObject.name;
            onTriggerStay2D?.Invoke(col.gameObject);
            onTriggerStay2DTyped?.Invoke(col);
        }

        // ─── 3D Collision Callbacks ────────────────────────────────────────────────

        /**
         * Called by Unity when this collider first touches another 3D collider.
         */
        private void OnCollisionEnter(Collision col)
        {
            if (!useCollisionEnter || !PassesFilter(col.gameObject)) return;
            _lastColliderName = col.gameObject.name;
            onCollisionEnter?.Invoke(col.gameObject);
            onCollisionEnterTyped?.Invoke(col);
        }

        /**
         * Called by Unity when this collider stops touching another 3D collider.
         */
        private void OnCollisionExit(Collision col)
        {
            if (!useCollisionExit || !PassesFilter(col.gameObject)) return;
            _lastColliderName = col.gameObject.name;
            onCollisionExit?.Invoke(col.gameObject);
            onCollisionExitTyped?.Invoke(col);
        }

        /**
         * Called by Unity each frame this collider remains in contact with another 3D collider.
         */
        private void OnCollisionStay(Collision col)
        {
            if (!useCollisionStay || !PassesFilter(col.gameObject)) return;
            _lastColliderName = col.gameObject.name;
            onCollisionStay?.Invoke(col.gameObject);
            onCollisionStayTyped?.Invoke(col);
        }

        // ─── 3D Trigger Callbacks ──────────────────────────────────────────────────

        /**
         * Called by Unity when a 3D collider enters this trigger zone.
         */
        private void OnTriggerEnter(Collider col)
        {
            if (!useTriggerEnter || !PassesFilter(col.gameObject)) return;
            _lastColliderName = col.gameObject.name;
            onTriggerEnter?.Invoke(col.gameObject);
            onTriggerEnterTyped?.Invoke(col);
        }

        /**
         * Called by Unity when a 3D collider exits this trigger zone.
         */
        private void OnTriggerExit(Collider col)
        {
            if (!useTriggerExit || !PassesFilter(col.gameObject)) return;
            _lastColliderName = col.gameObject.name;
            onTriggerExit?.Invoke(col.gameObject);
            onTriggerExitTyped?.Invoke(col);
        }

        /**
         * Called by Unity each frame a 3D collider remains inside this trigger zone.
         */
        private void OnTriggerStay(Collider col)
        {
            if (!useTriggerStay || !PassesFilter(col.gameObject)) return;
            _lastColliderName = col.gameObject.name;
            onTriggerStay?.Invoke(col.gameObject);
            onTriggerStayTyped?.Invoke(col);
        }
    }
}
