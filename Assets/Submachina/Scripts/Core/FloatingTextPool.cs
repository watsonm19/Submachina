using UnityEngine;
using TMPro;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Object-pooled floating text spawner for world-space number popups.
     *
     * Extends SubmarineObserver — auto-resolves its submarine from the
     * hierarchy, finds the O2PickupPump, and subscribes to OnAirCollected.
     * No manual Unity Event wiring needed: just place this component
     * anywhere inside the submarine hierarchy (e.g. the player canvas).
     *
     * Creates a ring buffer of TextMeshPro objects at Awake. When the pump
     * collects air, the next pooled text is positioned, configured, and
     * played. If all pool slots are active, the oldest is recycled.
     *
     * The pool creates world-space TextMeshPro objects (not UI), so text
     * appears at world positions without any canvas coordinate conversion.
     */
    public class FloatingTextPool : SubmarineObserver
    {
        // =====================
        // Pool
        // =====================

        [FoldoutGroup("Pool")]
        [Tooltip("How many text instances to pre-allocate. If all are active, " +
                 "the oldest is recycled. 8–12 handles rapid collection bursts.")]
        [SerializeField, Min(1)] private int poolSize = 10;

        // =====================
        // Appearance
        // =====================

        [FoldoutGroup("Appearance")]
        [Tooltip("Text color at spawn — alpha fades to 0 over the lifetime.")]
        [SerializeField] private Color textColor = new Color(0.4f, 0.9f, 1f, 1f);

        [FoldoutGroup("Appearance")]
        [Tooltip("Font size for the TextMeshPro instances.")]
        [SerializeField, Min(0.1f)] private float fontSize = 4f;

        [FoldoutGroup("Appearance")]
        [Tooltip("C# format string for the displayed number. " +
                 "{0} is the float value.\n\n" +
                 "Examples: '+{0:F0}' → '+12',  '{0:F1}' → '12.3'")]
        [SerializeField] private string numberFormat = "+{0:F0}";

        // =====================
        // Animation
        // =====================

        [FoldoutGroup("Animation")]
        [Tooltip("How fast the text drifts upward in world units per second.")]
        [SerializeField, Min(0f)] private float floatSpeed = 1.5f;

        [FoldoutGroup("Animation")]
        [Tooltip("How long the text stays visible before fully fading out.")]
        [SerializeField, Min(0.1f)] private float lifetime = 1f;

        // =====================
        // Sorting
        // =====================

        [FoldoutGroup("Sorting")]
        [Tooltip("Sorting layer for the text renderer. Leave empty for 'Default'.")]
        [SerializeField] private string sortingLayerName = "";

        [FoldoutGroup("Sorting")]
        [Tooltip("Sorting order within the layer. Use a high value to draw on top.")]
        [SerializeField] private int sortingOrder = 100;

        // =====================
        // Spawn Position
        // =====================

        [FoldoutGroup("Spawn Position")]
        [Tooltip("World-space anchor where text spawns. If unassigned, " +
                 "defaults to the submarine's transform.")]
        [SerializeField] private Transform spawnAnchor;

        [FoldoutGroup("Spawn Position")]
        [Tooltip("Random offset radius to prevent text from stacking when " +
                 "multiple pickups are collected in rapid succession.")]
        [SerializeField, Min(0f)] private float jitterRadius = 0.3f;

        // =====================
        // State
        // =====================

        private FloatingText[] _pool;
        private int _nextIndex;
        private O2PickupPump _pump;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();

            _pool = new FloatingText[poolSize];
            for (int i = 0; i < poolSize; i++)
                _pool[i] = CreateInstance(i);
        }

        private void Start()
        {
            if (Sub == null) return;

            // Auto-discover the O2PickupPump in the submarine hierarchy
            _pump = Sub.GetComponentInChildren<O2PickupPump>();
            if (_pump != null)
                _pump.OnAirCollected.AddListener(Show);
        }

        private void OnDestroy()
        {
            if (_pump != null)
                _pump.OnAirCollected.RemoveListener(Show);
        }

        // -------------------------------------------------------
        // Public API
        // -------------------------------------------------------

        /**
         * Displays a floating number at the spawn anchor (or the submarine).
         * Called automatically by the O2PickupPump's OnAirCollected event,
         * but can also be invoked manually or from other events.
         */
        public void Show(float amount)
        {
            FloatingText ft = _pool[_nextIndex];
            _nextIndex = (_nextIndex + 1) % poolSize;

            // Position at anchor, falling back to the sub's position
            Vector3 pos = spawnAnchor != null
                ? spawnAnchor.position
                : (Sub != null ? Sub.transform.position : transform.position);
            pos += (Vector3)(Random.insideUnitCircle * jitterRadius);
            ft.transform.position = pos;

            ft.Play(string.Format(numberFormat, amount), textColor, floatSpeed, lifetime);
        }

        // -------------------------------------------------------
        // Pool Creation
        // -------------------------------------------------------

        /** Creates a single pooled TextMeshPro instance with configured defaults. */
        private FloatingText CreateInstance(int index)
        {
            var go = new GameObject($"FloatingText_{index}");
            go.transform.SetParent(transform);

            // World-space TextMeshPro — no canvas needed
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;

            // Sorting — ensure text draws on top of game sprites
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                if (!string.IsNullOrEmpty(sortingLayerName))
                    renderer.sortingLayerName = sortingLayerName;
                renderer.sortingOrder = sortingOrder;
            }

            go.AddComponent<FloatingText>();
            go.SetActive(false);

            return go.GetComponent<FloatingText>();
        }
    }
}
