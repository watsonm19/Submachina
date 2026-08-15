using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Events;

namespace Submachina.Core
{
    /**
     * Distance-based activity culling — the first real optimization layer for a
     * world whose chunks (and their spawned enemies) are never despawned.
     *
     * Each instance checks its distance to the nearest submarine (falling back to
     * the main camera when no subs exist) on a slow, phase-staggered interval and
     * suspends its expensive parts while far away: listed Behaviours (AI, chain
     * simulators, procedural renderers), child Renderers, physics simulation, and
     * whole child objects. No manager, no singleton — mirrors the Submarine.All
     * static-registry pattern; a thousand instances cost a few distance checks
     * per frame in aggregate.
     *
     * The GameObject itself stays active, so Health, drops, and spawn state persist
     * exactly as the world-persistence design expects.
     */
    public class DistanceCullable : MonoBehaviour
    {
        // =====================
        // Distances
        // =====================

        [FoldoutGroup("Distances")]
        [Tooltip("Suspend when the nearest submarine is farther than this (world units). A 20-unit chunk grid with spawn radius 3 fills ~70 units around the player, so 45-60 keeps everything on/near screen alive.")]
        [SerializeField, Min(5f)] private float cullDistance = 50f;

        [FoldoutGroup("Distances")]
        [Tooltip("Extra margin (fraction of cull distance) before suspending, so creatures hovering at the boundary don't rapidly toggle. Restore happens at Cull Distance; suspend at Cull Distance × (1 + this).")]
        [SerializeField, Range(0f, 0.5f)] private float hysteresis = 0.1f;

        [FoldoutGroup("Distances")]
        [Tooltip("Seconds between distance checks. Each instance gets a random phase so checks spread across frames.")]
        [SerializeField, Min(0.05f)] private float checkInterval = 0.5f;

        // =====================
        // What to suspend
        // =====================

        [FoldoutGroup("What To Suspend")]
        [Tooltip("Behaviours disabled while culled — AI brains, ChainSimulators, procedural renderers, etc. Re-enabled on restore (chains snap back to their anchor via OnEnable).")]
        [SerializeField] private Behaviour[] suspendBehaviours = System.Array.Empty<Behaviour>();

        [FoldoutGroup("What To Suspend")]
        [Tooltip("Child GameObjects deactivated entirely while culled (particle rigs, light rigs).")]
        [SerializeField] private GameObject[] suspendObjects = System.Array.Empty<GameObject>();

        [FoldoutGroup("What To Suspend")]
        [Tooltip("Also disable every Renderer under this object while culled (collected once at Awake).")]
        [SerializeField] private bool suspendRenderers = true;

        [FoldoutGroup("What To Suspend")]
        [Tooltip("Set the Rigidbody2D to not simulate while culled — the big physics saving.")]
        [SerializeField] private bool suspendPhysics = true;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired when this object suspends due to distance.")]
        public UnityEvent onCulled;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when this object wakes back up.")]
        public UnityEvent onRestored;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        public bool IsCulled { get; private set; }

        private Renderer[] _renderers;
        private Rigidbody2D _rb;
        private float _nextCheckTime;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            if (suspendRenderers) _renderers = GetComponentsInChildren<Renderer>(true);
            _rb = GetComponent<Rigidbody2D>();

            // Random phase so a chunk full of creatures doesn't all check on the same frame.
            _nextCheckTime = Time.time + Random.value * checkInterval;
        }

        private void Update()
        {
            if (Time.time < _nextCheckTime) return;
            _nextCheckTime = Time.time + checkInterval;

            // Hysteresis band: suspend beyond cull × (1 + h), restore inside cull.
            float sqrDist = SqrDistanceToNearestFocus();
            float cullAt = cullDistance * (1f + hysteresis);
            if (!IsCulled && sqrDist > cullAt * cullAt) SetCulled(true);
            else if (IsCulled && sqrDist < cullDistance * cullDistance) SetCulled(false);
        }

        private void OnDisable()
        {
            // Never leave children suspended if this component is removed/disabled mid-cull.
            if (IsCulled) SetCulled(false);
        }

        // -------------------------------------------------------
        // Culling
        // -------------------------------------------------------

        /** Applies or lifts the suspension across all configured targets. */
        private void SetCulled(bool culled)
        {
            IsCulled = culled;

            foreach (var b in suspendBehaviours)
                if (b != null) b.enabled = !culled;

            foreach (var go in suspendObjects)
                if (go != null) go.SetActive(!culled);

            if (_renderers != null)
                foreach (var r in _renderers)
                    if (r != null) r.enabled = !culled;

            if (suspendPhysics && _rb != null) _rb.simulated = !culled;

            if (culled) onCulled?.Invoke();
            else onRestored?.Invoke();
        }

        /**
         * Squared distance to the nearest submarine, or to the main camera when no
         * submarines are registered (menus, pre-join). Squared to avoid the sqrt.
         */
        private float SqrDistanceToNearestFocus()
        {
            Vector2 here = transform.position;
            float best = float.MaxValue;

            var subs = Submarine.All;
            for (int i = 0; i < subs.Count; i++)
            {
                float d = ((Vector2)subs[i].transform.position - here).sqrMagnitude;
                if (d < best) best = d;
            }
            if (best < float.MaxValue) return best;

            var cam = Camera.main;
            return cam != null ? ((Vector2)cam.transform.position - here).sqrMagnitude : 0f;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, cullDistance);
        }
    }
}
