using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * O2PickupPump — the intake pump that gates O2Pickup collection behind a
     * sweet-spot timing loop. Replaces ManualBellowsPump's self-generated air:
     * with this active, O2 bubbles can ONLY be collected through a well-timed pump.
     *
     * The core loop:
     *   1. Press the pump button → the charge bar starts looping 0→1, wrapping
     *      back to zero each time it fills (unlike ManualBellowsPump's single charge).
     *   2. Press the button again to stop the pump — the outcome is graded:
     *        - Pickup within pickupRadius + sweet spot → full collect; air flows
     *          into the ManualBellowsPump tank at sweetSpotRewardMultiplier.
     *        - Pickup within pickupRadius + outside the sweet spot → weak collect
     *          at weakRewardMultiplier (the bubble is still consumed).
     *        - No pickup in range → the pump seizes with an Air Lock penalty,
     *          same as ManualBellowsPump's anti-spam lock.
     *
     * Feedback:
     *   - OnPickupAvailable / OnPickupUnavailable fire as a pickup enters/leaves
     *     range while idle — wire a "pump now!" prompt to these.
     *   - A procedural LineRenderer ring shows pickupRadius while looping, turning
     *     green and pulsing in the sweet spot. A faint breathing hint ring can also
     *     show while idle with a pickup in range (showIdleHint).
     *
     * Wiring:
     *   1. Add to the submarine root (radius is measured from this transform,
     *      unless RadiusCenter overrides it).
     *   2. Optionally assign a PumpAction InputActionReference (Spacebar fallback).
     *   3. Assign a Ring Material (URP Unlit/Particle) so the ring isn't pink in URP.
     *   4. Subscribe to events for audio/visual juice.
     *   5. Point a BellowsBar's pump reference at this component to display the loop.
     */
    public class O2PickupPump : MonoBehaviour, ISweetSpotPump
    {
        // =====================
        // Pump Cycle
        // =====================

        [FoldoutGroup("Pump Cycle")]
        [Tooltip("How fast chargeProgress (0→1) advances while looping. " +
                 "1.6 = one full loop every ~0.625 seconds.")]
        [SerializeField, Min(0.1f)] private float chargeSpeed = 1.6f;

        [FoldoutGroup("Pump Cycle")]
        [Tooltip("Lower bound of the sweet spot window (0–1 of charge progress). " +
                 "Stop the pump at or above this to collect a pickup.")]
        [SerializeField, Range(0f, 1f)] private float sweetSpotMin = 0.65f;

        [FoldoutGroup("Pump Cycle")]
        [Tooltip("Upper bound of the sweet spot window (0–1 of charge progress). " +
                 "Stop the pump above this and the attempt fails.")]
        [SerializeField, Range(0f, 1f)] private float sweetSpotMax = 0.85f;

        // =====================
        // Pump Rewards
        // =====================

        [FoldoutGroup("Pump Rewards")]
        [Tooltip("Multiplier on the pickup's air value when stopped inside the sweet spot. " +
                 "Example: 1.0 → full value; 1.5 → 50% bonus for perfect timing.")]
        [SerializeField, Min(0f)] private float sweetSpotRewardMultiplier = 1f;

        [FoldoutGroup("Pump Rewards")]
        [Tooltip("Multiplier on the pickup's air value when stopped OUTSIDE the sweet spot " +
                 "with a pickup still in range. Example: 0.35 → a 10-air bubble grants 3.5.")]
        [SerializeField, Min(0f)] private float weakRewardMultiplier = 0.35f;

        // =====================
        // Pickup Range
        // =====================

        [FoldoutGroup("Pickup Range")]
        [Tooltip("Radius (world units) around the player within which an O2Pickup " +
                 "can be grabbed by a sweet spot stop.")]
        [SerializeField, Min(0.1f)] private float pickupRadius = 2.5f;

        [FoldoutGroup("Pickup Range")]
        [Tooltip("Optional override for the centre of the pickup radius. " +
                 "Leave empty to use this component's transform.")]
        [SerializeField] private Transform radiusCenter;

        // =====================
        // Radius Ring Visual
        // =====================

        [FoldoutGroup("Radius Ring")]
        [Tooltip("Show a LineRenderer circle at pickupRadius while the pump is looping. " +
                 "Pulses when the charge is in the sweet spot.")]
        [SerializeField] private bool showRadiusRing = true;

        [FoldoutGroup("Radius Ring")]
        [Tooltip("Also show a faint, slowly breathing ring while idle when a pickup is in " +
                 "range — a visual nudge to start pumping.")]
        [SerializeField] private bool showIdleHint = true;

        [FoldoutGroup("Radius Ring")]
        [Tooltip("Material for the ring LineRenderer. Assign a URP Unlit/Particle material. " +
                 "Leave empty to use Unity default (may appear pink in URP).")]
        [SerializeField] private Material ringMaterial;

        [FoldoutGroup("Radius Ring")]
        [Tooltip("Line width of the ring in world units.")]
        [SerializeField, Min(0.01f)] private float ringWidth = 0.05f;

        [FoldoutGroup("Radius Ring")]
        [Tooltip("Sorting order for the ring. Set high enough to draw above world sprites.")]
        [SerializeField] private int ringSortingOrder = 5;

        [FoldoutGroup("Radius Ring")]
        [Tooltip("Ring color while looping outside the sweet spot.")]
        [SerializeField] private Color loopingRingColor = new Color(0.4f, 0.8f, 1f, 0.35f);

        [FoldoutGroup("Radius Ring")]
        [Tooltip("Ring color while the charge is in the sweet spot — stop now!")]
        [SerializeField] private Color sweetSpotRingColor = new Color(0.2f, 1f, 0.25f, 0.8f);

        [FoldoutGroup("Radius Ring")]
        [Tooltip("Faint hint color while idle with a pickup in range.")]
        [SerializeField] private Color hintRingColor = new Color(0.4f, 0.8f, 1f, 0.15f);

        [FoldoutGroup("Radius Ring")]
        [Tooltip("Ring color while the Air Lock penalty is active.")]
        [SerializeField] private Color airLockRingColor = new Color(1f, 0.15f, 0.15f, 0.3f);

        [FoldoutGroup("Radius Ring")]
        [Tooltip("Sweet spot pulse size as a fraction of the radius. " +
                 "Example: 0.06 → ring breathes between 94% and 106% of pickupRadius.")]
        [SerializeField, Range(0f, 0.3f)] private float pulseAmplitude = 0.06f;

        [FoldoutGroup("Radius Ring")]
        [Tooltip("Sweet spot pulse frequency in cycles per second.")]
        [SerializeField, Min(0.1f)] private float pulseSpeed = 6f;

        // =====================
        // Air Lock (Penalty)
        // =====================

        [FoldoutGroup("Air Lock")]
        [Tooltip("How long the pump is locked after stopping it outside the sweet spot " +
                 "(or in the sweet spot with no pickup in range).")]
        [SerializeField, Min(0.1f)] private float airLockDuration = 2.5f;

        // =====================
        // Input
        // =====================

        [FoldoutGroup("Input")]
        [Tooltip("Button InputAction for the pump. If unassigned, Spacebar is used as a fallback.")]
        [SerializeField] private InputActionReference pumpAction;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired when the player starts the pump looping. Wire: bellows creak SFX.")]
        public UnityEvent OnLoopStarted;

        [FoldoutGroup("Events")]
        [Tooltip("Fired each time the charge bar wraps from full back to zero. Wire: soft tick SFX.")]
        public UnityEvent OnLoopWrapped;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when a sweet spot stop collects an O2Pickup at full reward. " +
                 "Wire: +air SFX, green flash.")]
        public UnityEvent OnPickupCollected;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when a stop outside the sweet spot still collects a pickup, " +
                 "at the weak reward multiplier. Wire: weak thud SFX, dim flash.")]
        public UnityEvent OnWeakPickup;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when the pump is stopped with no pickup in range and seizes up. " +
                 "Wire: grind SFX, red screen flash.")]
        public UnityEvent OnAirLock;

        [FoldoutGroup("Events")]
        [Tooltip("Fired whenever the loop stops for ANY reason — collect, weak collect, or " +
                 "air lock. Pairs with OnLoopStarted. Wire: BellowsBar.Hide().")]
        public UnityEvent OnLoopStopped;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when an O2Pickup enters pickupRadius, regardless of pump state. " +
                 "Wire: BellowsBar.Show().")]
        public UnityEvent OnPickupEnteredRange;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when no pickup remains in range — it drifted away or was collected. " +
                 "Wire: BellowsBar.Hide().")]
        public UnityEvent OnPickupLeftRange;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when a pickup becomes available — one is in range while the pump is " +
                 "idle. Wire: show a 'pump now!' prompt or highlight.")]
        public UnityEvent OnPickupAvailable;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when the pickup prompt should turn off — the pickup left range, was " +
                 "collected, or the pump started looping / locked. Wire: hide the prompt.")]
        public UnityEvent OnPickupUnavailable;

        // =====================
        // ISweetSpotPump (read by BellowsBar)
        // =====================

        /** Current loop progress (0–1) — read by BellowsBar for the fill. */
        public float ChargeProgress => _chargeProgress;

        /** True while the Air Lock penalty is active. */
        public bool IsAirLocked => _state == PumpState.AirLocked;

        /** True while looping and the charge currently sits within the sweet spot. */
        public bool IsInSweetSpot =>
            _state == PumpState.Looping &&
            _chargeProgress >= sweetSpotMin &&
            _chargeProgress <= sweetSpotMax;

        /** Lower bound of the sweet spot window — read by BellowsBar to position markers. */
        public float SweetSpotMin => sweetSpotMin;

        /** Upper bound of the sweet spot window — read by BellowsBar to position markers. */
        public float SweetSpotMax => sweetSpotMax;

        /** True while the pump loop is running. */
        public bool IsLooping => _state == PumpState.Looping;

        /** True while a pickup is in range and the pump is idle — the prompt state. */
        public bool IsPickupAvailable => _pickupAvailable;

        /** True while a pickup sits within pickupRadius, regardless of pump state. */
        public bool IsPickupInRange => _pickupInRange;

        // =====================
        // Debug (Inspector)
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private string CurrentState => _state.ToString();

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float AirLockRemaining => Mathf.Max(0f, _airLockTimer);

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private bool PickupInRange => Application.isPlaying && FindNearestPickup() != null;

        [FoldoutGroup("Debug")]
        [Tooltip("Draw the pickup radius gizmo at all times, not just when selected.")]
        [SerializeField] private bool alwaysShowRadiusGizmo = true;

        // =====================
        // Internal State
        // =====================

        private enum PumpState { Idle, Looping, AirLocked }

        private PumpState _state = PumpState.Idle;
        private float _chargeProgress;
        private float _airLockTimer;
        private bool _pickupAvailable;
        private bool _pickupInRange;

        // Radius ring visual
        private LineRenderer _ringLine;
        private const int RingSegments = 48;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            if (showRadiusRing || showIdleHint) BuildRingRenderer();
        }

        private void OnEnable()
        {
            if (pumpAction != null) pumpAction.action.Enable();
        }

        private void OnDisable()
        {
            if (pumpAction != null) pumpAction.action.Disable();
        }

        private void Update()
        {
            UpdateStateMachine();
            ProcessInput();
            UpdatePickupTracking();
            UpdateRadiusRing();
        }

        // -------------------------------------------------------
        // State Machine
        // -------------------------------------------------------

        /**
         * Drives the pump each frame.
         *
         * Looping:   advances chargeProgress and wraps it back to zero when it
         *            hits 1.0 — the loop repeats until the player stops it.
         * AirLocked: counts down the penalty timer, then returns to Idle.
         */
        private void UpdateStateMachine()
        {
            switch (_state)
            {
                case PumpState.Looping:
                    _chargeProgress += chargeSpeed * Time.deltaTime;

                    // Wrap around: e.g. 1.07 → 0.07, and signal listeners on each lap
                    if (_chargeProgress >= 1f)
                    {
                        _chargeProgress -= 1f;
                        OnLoopWrapped?.Invoke();
                    }
                    break;

                case PumpState.AirLocked:
                    _airLockTimer -= Time.deltaTime;
                    if (_airLockTimer <= 0f) _state = PumpState.Idle;
                    break;
            }
        }

        // -------------------------------------------------------
        // Input
        // -------------------------------------------------------

        /**
         * Reads pump button state from InputAction (preferred) or
         * Keyboard.current.spaceKey (fallback for quick testing).
         * A single press both starts and stops the loop, depending on state.
         */
        private void ProcessInput()
        {
            if (!GetPumpPressed()) return;

            switch (_state)
            {
                case PumpState.Idle:    StartLoop();   break;
                case PumpState.Looping: TryCollect();  break;
                // AirLocked: presses are ignored until the penalty expires
            }
        }

        private bool GetPumpPressed() =>
            pumpAction != null
                ? pumpAction.action.WasPressedThisFrame()
                : Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;

        // -------------------------------------------------------
        // Pump Logic
        // -------------------------------------------------------

        /** Begins the looping charge cycle from zero. */
        private void StartLoop()
        {
            _chargeProgress = 0f;
            _state = PumpState.Looping;
            OnLoopStarted?.Invoke();
        }

        /**
         * Called when the pump button is pressed while looping. Outcome is graded
         * by timing, like ManualBellowsPump's perfect/weak release:
         *
         *   Pickup in range + sweet spot  → full collect (sweetSpotRewardMultiplier)
         *   Pickup in range + outside it  → weak collect (weakRewardMultiplier)
         *   No pickup in range            → Air Lock penalty, nothing gained
         *
         * Example: 10-air bubble, multipliers 1.0 / 0.35 → 10 air perfect, 3.5 weak.
         */
        private void TryCollect()
        {
            // No bubble in range — the stop wasted the pump, lock it
            O2Pickup pickup = FindNearestPickup();
            if (pickup == null)
            {
                TriggerAirLock();
                OnLoopStopped?.Invoke();
                return;
            }

            // Grade the reward by timing; Collect() routes the air into the tank
            bool inSweetSpot = _chargeProgress >= sweetSpotMin && _chargeProgress <= sweetSpotMax;
            pickup.Collect(inSweetSpot ? sweetSpotRewardMultiplier : weakRewardMultiplier);

            _chargeProgress = 0f;
            _state = PumpState.Idle;

            if (inSweetSpot) OnPickupCollected?.Invoke();
            else             OnWeakPickup?.Invoke();
            OnLoopStopped?.Invoke();
        }

        /** Locks the pump for airLockDuration seconds — same penalty feel as ManualBellowsPump. */
        private void TriggerAirLock()
        {
            _state = PumpState.AirLocked;
            _airLockTimer = airLockDuration;
            _chargeProgress = 0f;
            OnAirLock?.Invoke();
        }

        // -------------------------------------------------------
        // Pickup Detection
        // -------------------------------------------------------

        /** Centre of the pickup radius — the override transform if set, else this object. */
        private Vector2 RadiusOrigin =>
            radiusCenter != null ? (Vector2)radiusCenter.position : (Vector2)transform.position;

        /**
         * Returns the closest O2Pickup within pickupRadius, or null if none.
         * Uses a physics overlap so only pickups with active colliders count.
         */
        private O2Pickup FindNearestPickup()
        {
            Vector2 origin = RadiusOrigin;
            Collider2D[] hits = Physics2D.OverlapCircleAll(origin, pickupRadius);

            // Scan hits for the nearest O2Pickup by squared distance
            O2Pickup nearest = null;
            float nearestSqr = float.MaxValue;
            foreach (Collider2D hit in hits)
            {
                O2Pickup pickup = hit.GetComponent<O2Pickup>();
                if (pickup == null) continue;

                float sqr = ((Vector2)pickup.transform.position - origin).sqrMagnitude;
                if (sqr < nearestSqr)
                {
                    nearestSqr = sqr;
                    nearest = pickup;
                }
            }
            return nearest;
        }

        // -------------------------------------------------------
        // Pickup Tracking (Range + Prompt)
        // -------------------------------------------------------

        /**
         * Tracks two independent conditions each frame and fires events on transitions:
         *
         *   In range  — a pickup sits within pickupRadius, regardless of pump state.
         *               Drives OnPickupEnteredRange/OnPickupLeftRange (bar visibility).
         *               A collected pickup is destroyed, so collection reads as
         *               "left range" on the following frame.
         *
         *   Available — in range AND the pump is idle. Drives the "pump now!" prompt
         *               via OnPickupAvailable/OnPickupUnavailable; turns off once the
         *               player starts looping or the pump locks.
         */
        private void UpdatePickupTracking()
        {
            bool inRange = FindNearestPickup() != null;

            // Range transitions — pump-state independent
            if (inRange != _pickupInRange)
            {
                _pickupInRange = inRange;
                if (inRange) OnPickupEnteredRange?.Invoke();
                else         OnPickupLeftRange?.Invoke();
            }

            // Prompt transitions — only meaningful while idle
            bool available = _state == PumpState.Idle && inRange;
            if (available != _pickupAvailable)
            {
                _pickupAvailable = available;
                if (available) OnPickupAvailable?.Invoke();
                else           OnPickupUnavailable?.Invoke();
            }
        }

        // -------------------------------------------------------
        // Radius Ring Visual
        // -------------------------------------------------------

        /**
         * Creates the ring LineRenderer as a child of the radius centre so it
         * follows the player automatically. useWorldSpace=false keeps all
         * positions local; geometry is rebuilt each frame to animate the pulse.
         */
        private void BuildRingRenderer()
        {
            Transform parent = radiusCenter != null ? radiusCenter : transform;
            var ringGO = new GameObject("PickupRadiusRing");
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
         * Drives ring visibility, color, and pulse from the pump state:
         *
         *   Looping, sweet spot  → green, pulsing ±pulseAmplitude (stop now!)
         *   Looping, otherwise   → steady cyan
         *   Air Locked           → dim red
         *   Idle + pickup nearby → faint slow "breathing" hint (if showIdleHint)
         *   Otherwise            → hidden
         */
        private void UpdateRadiusRing()
        {
            if (_ringLine == null) return;

            Color color   = Color.clear;
            float radius  = pickupRadius;
            bool  visible = true;

            switch (_state)
            {
                case PumpState.Looping when !showRadiusRing:
                    visible = false;
                    break;

                case PumpState.Looping:
                    bool sweet = IsInSweetSpot;
                    color = sweet ? sweetSpotRingColor : loopingRingColor;
                    // Sweet spot pulse: radius breathes ±amplitude, e.g. 2.5 → 2.35–2.65
                    if (sweet) radius *= 1f + Mathf.Sin(Time.time * pulseSpeed * 2f * Mathf.PI) * pulseAmplitude;
                    break;

                case PumpState.AirLocked when showRadiusRing:
                    color = airLockRingColor;
                    break;

                case PumpState.Idle when showIdleHint && _pickupAvailable:
                    // Slow gentle breathing draws the eye without shouting
                    color = hintRingColor;
                    radius *= 1f + Mathf.Sin(Time.time * 0.8f * 2f * Mathf.PI) * pulseAmplitude * 0.5f;
                    break;

                default:
                    visible = false;
                    break;
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
            if (alwaysShowRadiusGizmo) DrawRadiusGizmo();
        }

        private void OnDrawGizmosSelected()
        {
            if (!alwaysShowRadiusGizmo) DrawRadiusGizmo();
        }

        /**
         * Visualises the pickup radius in the Scene view.
         * Green when a pickup is currently in range (Play mode), cyan otherwise.
         */
        private void DrawRadiusGizmo()
        {
            bool inRange = Application.isPlaying && FindNearestPickup() != null;
            Gizmos.color = inRange ? new Color(0.2f, 1f, 0.25f, 0.9f) : new Color(0.3f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireSphere(RadiusOrigin, pickupRadius);
        }

        // -------------------------------------------------------
        // Editor Utilities
        // -------------------------------------------------------

#if UNITY_EDITOR
        [FoldoutGroup("Debug")]
        [Button("Start Loop"), GUIColor(0.4f, 1f, 0.4f)]
        private void DebugStartLoop()
        {
            if (!Application.isPlaying) { Debug.Log("[O2PickupPump] Play mode only."); return; }
            if (_state == PumpState.Idle) StartLoop();
        }

        [FoldoutGroup("Debug")]
        [Button("Trigger Air Lock"), GUIColor(1f, 0.3f, 0.3f)]
        private void DebugTriggerAirLock()
        {
            if (!Application.isPlaying) { Debug.Log("[O2PickupPump] Play mode only."); return; }
            TriggerAirLock();
        }
#endif
    }
}
