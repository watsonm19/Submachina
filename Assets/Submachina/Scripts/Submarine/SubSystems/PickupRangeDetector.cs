using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Submarine-side proximity detector for all collectible pickups.
     *
     * Owns the shared pickup radius, the ring LineRenderer visual, and the
     * auto-collection logic for ScrapPickup. O2Pickup collection is handled
     * by O2PickupPump, which drives the ring's visual state via the override API.
     *
     * Ring visual states:
     *   Override active  → O2PickupPump has control (looping, sweet-spot, air-lock colors)
     *   Override clear + any pickup in range → faint idle hint
     *   Override clear + nothing in range    → hidden
     *
     * Place on the submarine root (or a child). O2PickupPump and other
     * subsystems access this via Sub.PickupRange.
     */
    public class PickupRangeDetector : SubmarineComponent
    {
        // =====================
        // Range
        // =====================

        [FoldoutGroup("Range")]
        [Tooltip("Radius (world units) within which pickups are detected and collected.")]
        [SerializeField, Min(0.1f)] private float pickupRadius = 2.5f;

        [FoldoutGroup("Range")]
        [Tooltip("Optional override for the centre of the pickup radius. " +
                 "Leave empty to use this component's transform.")]
        [SerializeField] private Transform radiusCenter;

        // =====================
        // Radius Ring Visual
        // =====================

        [FoldoutGroup("Radius Ring")]
        [Tooltip("Show the ring when any pickup is in range or an override is active.")]
        [SerializeField] private bool showRadiusRing = true;

        [FoldoutGroup("Radius Ring")]
        [Tooltip("Material for the ring LineRenderer. Assign a URP Unlit/Particle material.")]
        [SerializeField] private Material ringMaterial;

        [FoldoutGroup("Radius Ring")]
        [Tooltip("Line width of the ring in world units.")]
        [SerializeField, Min(0.01f)] private float ringWidth = 0.05f;

        [FoldoutGroup("Radius Ring")]
        [Tooltip("Sorting order for the ring — set high enough to draw above world sprites.")]
        [SerializeField] private int ringSortingOrder = 5;

        [FoldoutGroup("Radius Ring")]
        [Tooltip("Ring color for the idle hint shown while a pickup is in range.")]
        [SerializeField] private Color hintRingColor = new Color(0.4f, 0.8f, 1f, 0.15f);

        [FoldoutGroup("Radius Ring")]
        [Tooltip("Gentle breathing amplitude of the idle hint ring. " +
                 "Example: 0.03 → ring breathes between 97% and 103% of pickupRadius.")]
        [SerializeField, Range(0f, 0.3f)] private float hintPulseAmplitude = 0.03f;

        [FoldoutGroup("Radius Ring")]
        [Tooltip("Ring color flashed when a scrap pickup is collected. " +
                 "Warm gold reads as 'reward received'.")]
        [SerializeField] private Color scrapCollectFlashColor = new Color(1f, 0.85f, 0.2f, 0.9f);

        [FoldoutGroup("Radius Ring")]
        [Tooltip("How long the collect flicker lasts in seconds.")]
        [SerializeField, Min(0.05f)] private float scrapCollectFlashDuration = 0.25f;

        [FoldoutGroup("Radius Ring")]
        [Tooltip("Flicker frequency in cycles per second during the collect flash. " +
                 "Example: 20 → ring blinks 20 times per second.")]
        [SerializeField, Min(1f)] private float scrapCollectFlickerSpeed = 20f;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug")]
        [Tooltip("Draw the pickup radius gizmo at all times, not just when selected.")]
        [SerializeField] private bool alwaysShowGizmo = true;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private bool AnyPickupInRange => _anyInRange;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired when any pickup (O2 or Scrap) enters the radius.")]
        public UnityEvent onAnyPickupEnteredRange;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when no pickups remain within the radius.")]
        public UnityEvent onAnyPickupLeftRange;

        // =====================
        // Public API — Range
        // =====================

        /** World-space centre of the pickup radius. */
        public Vector2 RadiusOrigin =>
            radiusCenter != null ? (Vector2)radiusCenter.position : (Vector2)transform.position;

        /** The configured pickup radius in world units. */
        public float PickupRadius => pickupRadius;

        // =====================
        // Ring Override API (used by O2PickupPump)
        // =====================

        /** True while O2PickupPump has overridden the ring color. */
        public bool HasRingOverride { get; private set; }

        private Color _overrideColor;
        private float _overridePulseAmplitude;
        private float _overridePulseSpeed;

        /**
         * Gives O2PickupPump explicit control of the ring color and pulse.
         * Called while the pump is looping or air-locked.
         *
         * Example: looping outside sweet-spot → SetRingOverride(cyan, 0f)
         *          sweet-spot active          → SetRingOverride(green, 0.06f, 6f)
         *          air-locked                 → SetRingOverride(red, 0f)
         */
        public void SetRingOverride(Color color, float pulseAmplitude = 0f, float pulseSpeed = 6f)
        {
            HasRingOverride = true;
            _overrideColor = color;
            _overridePulseAmplitude = pulseAmplitude;
            _overridePulseSpeed = pulseSpeed;
        }

        /**
         * Releases ring control back to the default hint behaviour.
         * Called by O2PickupPump when it returns to idle.
         */
        public void ClearRingOverride()
        {
            HasRingOverride = false;
        }

        // =====================
        // State
        // =====================

        private LineRenderer _ringLine;
        private const int RingSegments = 48;
        private bool _anyInRange;
        private float _flashTimer;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();
            if (showRadiusRing) BuildRingRenderer();
        }

        private void Update()
        {
            UpdateRangeTracking();
            TryCollectScrap();
            UpdateRing();
        }

        // -------------------------------------------------------
        // Pickup Detection
        // -------------------------------------------------------

        /**
         * Returns the nearest O2Pickup within the radius, or null if none.
         * Used by O2PickupPump to find its collection target.
         */
        public O2Pickup FindNearestO2()
        {
            return FindNearest<O2Pickup>();
        }

        /**
         * Returns the nearest ScrapPickup within the radius, or null if none.
         */
        public ScrapPickup FindNearestScrap()
        {
            return FindNearest<ScrapPickup>();
        }

        /**
         * Scans the overlap area for the nearest component of type T.
         * Uses OverlapCircleAll so only active colliders are considered.
         *
         * Example: FindNearest<O2Pickup>() → nearest O2Pickup in radius.
         */
        private T FindNearest<T>() where T : Component
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(RadiusOrigin, pickupRadius);

            T nearest = null;
            float nearestSqr = float.MaxValue;

            foreach (Collider2D hit in hits)
            {
                T component = hit.GetComponent<T>();
                if (component == null) continue;

                float sqr = ((Vector2)component.transform.position - RadiusOrigin).sqrMagnitude;
                if (sqr < nearestSqr)
                {
                    nearestSqr = sqr;
                    nearest = component;
                }
            }

            return nearest;
        }

        // -------------------------------------------------------
        // Range Tracking
        // -------------------------------------------------------

        /**
         * Fires enter/leave events when the "any pickup in range" state transitions.
         * Checks for both O2 and Scrap so the ring reacts to either type.
         */
        private void UpdateRangeTracking()
        {
            bool inRange = FindNearestO2() != null || FindNearestScrap() != null;

            if (inRange == _anyInRange) return;

            _anyInRange = inRange;
            if (inRange) onAnyPickupEnteredRange?.Invoke();
            else         onAnyPickupLeftRange?.Invoke();
        }

        // -------------------------------------------------------
        // Scrap Auto-Collection
        // -------------------------------------------------------

        /**
         * Auto-collects the nearest ScrapPickup if one is within range and
         * the bank has room. If the bank is full the pickup stays in the world
         * until the player spends their stock and re-enters range.
         */
        private void TryCollectScrap()
        {
            if (Sub?.Scrap == null) return;
            if (Sub.Scrap.ScrapCount >= Sub.Scrap.MaxScrap) return;

            ScrapPickup scrap = FindNearestScrap();
            if (scrap == null) return;

            scrap.Collect(Sub);
            _flashTimer = scrapCollectFlashDuration;
        }

        // -------------------------------------------------------
        // Ring Visual
        // -------------------------------------------------------

        /**
         * Creates the ring LineRenderer as a child of the radius centre so it
         * follows the submarine automatically.
         */
        private void BuildRingRenderer()
        {
            Transform parent = radiusCenter != null ? radiusCenter : transform;
            GameObject ringGO = new GameObject("PickupRadiusRing");
            ringGO.transform.SetParent(parent, false);

            _ringLine = ringGO.AddComponent<LineRenderer>();
            _ringLine.useWorldSpace = false;
            _ringLine.loop          = true;
            _ringLine.positionCount = RingSegments;
            _ringLine.startWidth    = ringWidth;
            _ringLine.endWidth      = ringWidth;
            _ringLine.sortingOrder  = ringSortingOrder;
            _ringLine.enabled       = false;

            if (ringMaterial != null) _ringLine.material = ringMaterial;
        }

        /**
         * Drives ring visibility, color, and pulse each frame.
         *
         * Priority:
         *   1. Override active (O2PickupPump looping/locked) → use override color/pulse
         *   2. Any pickup in range, no override → show gentle breathing hint
         *   3. Nothing in range, no override → hide
         */
        private void UpdateRing()
        {
            if (_ringLine == null) return;

            float radius = pickupRadius;
            Color color;
            bool visible;

            // Collect flash — highest priority, overrides everything for a brief flicker.
            // Alternates between full color and transparent at flickerSpeed so it reads
            // as a distinct "pickup received" cue rather than a steady glow.
            if (_flashTimer > 0f)
            {
                _flashTimer -= Time.deltaTime;
                float flicker = Mathf.Abs(Mathf.Sin(Time.time * scrapCollectFlickerSpeed * Mathf.PI));
                color   = new Color(scrapCollectFlashColor.r, scrapCollectFlashColor.g,
                                    scrapCollectFlashColor.b, scrapCollectFlashColor.a * flicker);
                visible = true;
                _ringLine.enabled    = visible;
                _ringLine.startColor = color;
                _ringLine.endColor   = color;
                RebuildRingGeometry(radius);
                return;
            }

            if (HasRingOverride)
            {
                visible = true;
                color   = _overrideColor;
                if (_overridePulseAmplitude > 0f)
                    radius *= 1f + Mathf.Sin(Time.time * _overridePulseSpeed * 2f * Mathf.PI) * _overridePulseAmplitude;
            }
            else if (_anyInRange)
            {
                // Slow idle hint: gentle breath draws the eye without demanding attention
                visible = true;
                color   = hintRingColor;
                radius *= 1f + Mathf.Sin(Time.time * 0.8f * 2f * Mathf.PI) * hintPulseAmplitude;
            }
            else
            {
                visible = false;
                color   = Color.clear;
            }

            _ringLine.enabled = visible;
            if (!visible) return;

            _ringLine.startColor = color;
            _ringLine.endColor   = color;
            RebuildRingGeometry(radius);
        }

        /** Lays out RingSegments points evenly around a circle of the given radius. */
        private void RebuildRingGeometry(float radius)
        {
            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i / (float)RingSegments * 2f * Mathf.PI;
                _ringLine.SetPosition(i, new Vector3(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius,
                    0f));
            }
        }

        // -------------------------------------------------------
        // Gizmos
        // -------------------------------------------------------

        private void OnDrawGizmos()
        {
            if (alwaysShowGizmo) DrawGizmo();
        }

        private void OnDrawGizmosSelected()
        {
            if (!alwaysShowGizmo) DrawGizmo();
        }

        private void DrawGizmo()
        {
            bool inRange = Application.isPlaying && _anyInRange;
            Gizmos.color = inRange
                ? new Color(0.2f, 1f, 0.25f, 0.9f)
                : new Color(0.3f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireSphere(RadiusOrigin, pickupRadius);
        }
    }
}
