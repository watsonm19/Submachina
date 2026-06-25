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
     *   1. The loop starts automatically when an O2 pickup enters range
     *      (autoActivateInRange), or by pressing the pump button. The charge bar
     *      loops 0→1, wrapping back to zero each time it fills.
     *   2. Press the button again to stop the pump — the outcome is graded:
     *        - Pickup in range + sweet spot → full collect at sweetSpotRewardMultiplier.
     *        - Pickup in range + outside it → weak collect at weakRewardMultiplier.
     *        - No pickup in range           → Air Lock penalty.
     *
     * Ring visual:
     *   The ring is owned by PickupRangeDetector (Sub.PickupRange). This pump
     *   drives the ring's colour and pulse via SetRingOverride/ClearRingOverride
     *   while it is looping or locked. The detector handles the idle hint ring itself.
     *
     * Wiring:
     *   1. Add PickupRangeDetector to the sub root (owns the radius and ring).
     *   2. Add this component to the sub — it auto-registers via SubmarineComponent.
     *   3. Optionally assign a PumpAction InputActionReference (Spacebar fallback).
     */
    [UsesFeedbacks(nameof(SubFeedbacks.PumpPerfect), nameof(SubFeedbacks.PumpWeak), nameof(SubFeedbacks.AirLock))]
    public class O2PickupPump : SweetSpotPump
    {
        // =====================
        // Activation
        // =====================

        [FoldoutGroup("Activation")]
        [Tooltip("Automatically start the loop when an O2 pickup enters range, and stop it " +
                 "(no reward, no penalty) when the last pickup leaves.")]
        [SerializeField] private bool autoActivateInRange = true;

        [FoldoutGroup("Activation")]
        [Tooltip("Block manual loop starts while no O2 pickup is in range — prevents pointless " +
                 "loops that can only end in an Air Lock. Disable to allow free-running loops.")]
        [SerializeField] private bool requirePickupToStart = true;

        // =====================
        // Pump Cycle
        // =====================

        [FoldoutGroup("Pump Cycle")]
        [Tooltip("How fast chargeProgress (0→1) advances while looping. " +
                 "1.6 = one full loop every ~0.625 seconds.")]
        [SerializeField, Min(0.1f)] private float chargeSpeed = 1.6f;

        [FoldoutGroup("Pump Cycle")]
        [Tooltip("Lower bound of the sweet spot window (0–1 of charge progress).")]
        [SerializeField, Range(0f, 1f)] private float sweetSpotMin = 0.65f;

        [FoldoutGroup("Pump Cycle")]
        [Tooltip("Upper bound of the sweet spot window (0–1 of charge progress).")]
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
        // Ring Override Colors
        // (Ring and radius owned by PickupRangeDetector — this pump only drives the colors)
        // =====================

        [FoldoutGroup("Ring Colors")]
        [Tooltip("When true, this pump overrides the ring colour while looping or air-locked. " +
                 "Disable if you want the ring's idle hint behavior only (no active colors).")]
        [SerializeField] private bool driveRingColors = true;

        [FoldoutGroup("Ring Colors")]
        [Tooltip("Ring color while looping outside the sweet spot.")]
        [SerializeField] private Color loopingRingColor = new Color(0.4f, 0.8f, 1f, 0.35f);

        [FoldoutGroup("Ring Colors")]
        [Tooltip("Ring color while the charge is in the sweet spot — stop now!")]
        [SerializeField] private Color sweetSpotRingColor = new Color(0.2f, 1f, 0.25f, 0.8f);

        [FoldoutGroup("Ring Colors")]
        [Tooltip("Ring color while the Air Lock penalty is active.")]
        [SerializeField] private Color airLockRingColor = new Color(1f, 0.15f, 0.15f, 0.3f);

        [FoldoutGroup("Ring Colors")]
        [Tooltip("Sweet spot pulse size as a fraction of the radius. " +
                 "Example: 0.06 → ring breathes between 94% and 106% of pickupRadius.")]
        [SerializeField, Range(0f, 0.3f)] private float pulseAmplitude = 0.06f;

        [FoldoutGroup("Ring Colors")]
        [Tooltip("Sweet spot pulse frequency in cycles per second.")]
        [SerializeField, Min(0.1f)] private float pulseSpeed = 6f;

        // =====================
        // Air Lock (Penalty)
        // =====================

        [FoldoutGroup("Air Lock")]
        [Tooltip("How long the pump is locked after stopping outside the sweet spot.")]
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
        [Tooltip("Fired when the player starts the pump looping.")]
        public UnityEvent OnLoopStarted;

        [FoldoutGroup("Events")]
        [Tooltip("Fired each time the charge bar wraps from full back to zero.")]
        public UnityEvent OnLoopWrapped;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when a sweet spot stop collects an O2Pickup at full reward.")]
        public UnityEvent OnSweetSpotPickup;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when a stop outside the sweet spot still collects a pickup, " +
                 "at the weak reward multiplier.")]
        public UnityEvent OnWeakPickup;

        [FoldoutGroup("Events")]
        [Tooltip("Fired whenever any pickup is collected — sweet spot OR weak.")]
        public UnityEvent OnPickupCollected;

        [FoldoutGroup("Events")]
        [Tooltip("Fired with the exact air amount granted after all multipliers " +
                 "(size × timing). Wire to FloatingTextPool.Show to display collection numbers.")]
        public UnityEvent<float> OnAirCollected;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when the pump is stopped with no pickup in range and seizes up.")]
        public UnityEvent OnAirLock;

        [FoldoutGroup("Events")]
        [Tooltip("Fired whenever the loop stops for ANY reason — collect, weak collect, or air lock.")]
        public UnityEvent OnLoopStopped;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when an O2Pickup enters pickup radius, regardless of pump state.")]
        public UnityEvent OnPickupEnteredRange;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when no O2 pickup remains in range.")]
        public UnityEvent OnPickupLeftRange;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when a pickup becomes available while the pump is idle.")]
        public UnityEvent OnPickupAvailable;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when the pickup prompt should turn off.")]
        public UnityEvent OnPickupUnavailable;

        // =====================
        // Upgrade Accessors
        // =====================

        private float SweetMultMod   => Sub?.Upgrades?.Stats.Resolve(SubStats.IntakeSweetMultiplier, sweetSpotRewardMultiplier) ?? sweetSpotRewardMultiplier;
        private float WeakMultMod    => Sub?.Upgrades?.Stats.Resolve(SubStats.IntakeWeakMultiplier, weakRewardMultiplier) ?? weakRewardMultiplier;
        private float ChargeSpeedMod => Sub?.Upgrades?.Stats.Resolve(SubStats.IntakeChargeSpeed, chargeSpeed) ?? chargeSpeed;

        // =====================
        // ISweetSpotPump (read by BellowsBar)
        // =====================

        /** Current loop progress (0–1) — read by BellowsBar for the fill. */
        public override float ChargeProgress => _chargeProgress;

        /** True while the Air Lock penalty is active. */
        public override bool IsAirLocked => _state == PumpState.AirLocked;

        /** The intake pump has no cooldown mechanic. */
        public override bool IsOnCooldown => false;

        /** True while looping and the charge currently sits within the sweet spot. */
        public override bool IsInSweetSpot =>
            _state == PumpState.Looping &&
            _chargeProgress >= sweetSpotMin &&
            _chargeProgress <= sweetSpotMax;

        /** Lower bound of the sweet spot window — read by BellowsBar to position markers. */
        public override float SweetSpotMin => sweetSpotMin;

        /** Upper bound of the sweet spot window — read by BellowsBar to position markers. */
        public override float SweetSpotMax => sweetSpotMax;

        /** True while the pump loop is running. */
        public bool IsLooping => _state == PumpState.Looping;

        /** True while an O2 pickup is in range and the pump is idle — the prompt state. */
        public bool IsPickupAvailable => _pickupAvailable;

        /** True while an O2 pickup sits within pickup radius, regardless of pump state. */
        public bool IsPickupInRange => _pickupInRange;

        /** Wants control whenever it's actively looping or an O2 bubble is in range. */
        public override bool WantsControl => IsLooping || IsPickupInRange;

        /** Outranks the always-on manual pump so it takes over near bubbles. */
        public override int ControlPriority => 10;

        // =====================
        // Debug (Inspector)
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private string CurrentState => _state.ToString();

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float AirLockRemaining => Mathf.Max(0f, _airLockTimer);

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private bool O2PickupInRange => Application.isPlaying && Sub?.PickupRange?.FindNearestO2() != null;

        // =====================
        // Internal State
        // =====================

        private enum PumpState { Idle, Looping, AirLocked }

        private PumpState _state = PumpState.Idle;
        private float _chargeProgress;
        private float _airLockTimer;
        private bool _pickupAvailable;
        private bool _pickupInRange;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();
            RegisterAction(pumpAction);
        }

        protected override void OnPumpDisabled()
        {
            // Release ring override when disabled so the detector resets cleanly
            Sub?.PickupRange?.ClearRingOverride();
        }

        private void Update()
        {
            UpdateStateMachine();
            ProcessInput();
            UpdatePickupTracking();
            UpdateRingOverride();
        }

        // -------------------------------------------------------
        // State Machine
        // -------------------------------------------------------

        /**
         * Drives the pump each frame.
         *
         * Looping:   advances chargeProgress, wraps at 1.0, fires OnLoopWrapped.
         * AirLocked: counts down the penalty timer, then returns to Idle.
         */
        private void UpdateStateMachine()
        {
            switch (_state)
            {
                case PumpState.Looping:
                    _chargeProgress += ChargeSpeedMod * Time.deltaTime;

                    // Wrap around: e.g. 1.07 → 0.07, signal listeners on each lap
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
                case PumpState.Idle when !requirePickupToStart || _pickupInRange:
                    StartLoop();
                    break;

                case PumpState.Looping: TryCollect(); break;
                // AirLocked: presses are ignored until the penalty expires
            }
        }

        private bool GetPumpPressed() =>
            PrimaryAction != null
                ? PrimaryAction.WasPressedThisFrame()
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
         * Stops the loop with no reward and no penalty — used by auto-activation
         * when the last pickup drifts out of range mid-loop.
         */
        private void StopLoop()
        {
            _chargeProgress = 0f;
            _state = PumpState.Idle;
            OnLoopStopped?.Invoke();
        }

        /**
         * Called when the pump button is pressed while looping. Outcome is graded
         * by timing:
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
            O2Pickup pickup = Sub?.PickupRange?.FindNearestO2();
            if (pickup == null)
            {
                TriggerAirLock();
                OnLoopStopped?.Invoke();
                return;
            }

            // Grade the reward by timing; Collect() routes the air into the tank
            bool inSweetSpot = _chargeProgress >= sweetSpotMin && _chargeProgress <= sweetSpotMax;
            float airCollected = pickup.Collect(CollectTarget, inSweetSpot ? SweetMultMod : WeakMultMod);

            _chargeProgress = 0f;
            _state = PumpState.Idle;

            // Route the timing-graded collect cue through the central switchboard
            if (inSweetSpot)
            {
                Sub?.Feedbacks?.Play(SubFeedbacks.PumpPerfect, transform.position);
                OnSweetSpotPickup?.Invoke();
            }
            else
            {
                Sub?.Feedbacks?.Play(SubFeedbacks.PumpWeak, transform.position);
                OnWeakPickup?.Invoke();
            }
            OnAirCollected?.Invoke(airCollected);
            OnPickupCollected?.Invoke();
            OnLoopStopped?.Invoke();
        }

        /** Locks the pump for airLockDuration seconds — same penalty feel as ManualBellowsPump. */
        private void TriggerAirLock()
        {
            _state = PumpState.AirLocked;
            _airLockTimer = airLockDuration;
            _chargeProgress = 0f;
            Sub?.Feedbacks?.Play(SubFeedbacks.AirLock, transform.position);
            OnAirLock?.Invoke();
        }

        // -------------------------------------------------------
        // Pickup Tracking (Range + Prompt)
        // -------------------------------------------------------

        /**
         * Tracks two independent conditions each frame and fires events on transitions:
         *
         *   In range  — an O2 pickup sits within the detector's radius.
         *               A collected pickup is destroyed, so collection reads as
         *               "left range" on the following frame.
         *
         *   Available — in range AND the pump is idle. Drives the "pump now!" prompt.
         */
        private void UpdatePickupTracking()
        {
            bool inRange = Sub?.PickupRange?.FindNearestO2() != null;

            // Range transitions — pump-state independent
            if (inRange != _pickupInRange)
            {
                _pickupInRange = inRange;
                if (inRange) OnPickupEnteredRange?.Invoke();
                else         OnPickupLeftRange?.Invoke();
            }

            // Auto-activation: loop runs while a pickup is nearby
            if (autoActivateInRange)
            {
                if      (_state == PumpState.Idle    &&  inRange) StartLoop();
                else if (_state == PumpState.Looping && !inRange) StopLoop();
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
        // Ring Color Override
        // -------------------------------------------------------

        /**
         * Drives the shared PickupRangeDetector ring color based on pump state.
         * The detector owns the ring geometry; this pump only signals which color
         * to show while it is active (looping, air-locked). On return to Idle the
         * override is cleared and the detector reverts to its own hint behaviour.
         *
         * Ring states this pump drives:
         *   Looping, sweet spot  → green, pulsing (stop now!)
         *   Looping, otherwise   → steady cyan
         *   Air Locked           → dim red
         *   Idle                 → override cleared (detector handles idle hint)
         */
        private void UpdateRingOverride()
        {
            if (!driveRingColors || Sub?.PickupRange == null) return;

            switch (_state)
            {
                case PumpState.Looping:
                    bool sweet = IsInSweetSpot;
                    Color col = sweet ? sweetSpotRingColor : loopingRingColor;
                    float pulse = sweet ? pulseAmplitude : 0f;
                    Sub.PickupRange.SetRingOverride(col, pulse, pulseSpeed);
                    break;

                case PumpState.AirLocked:
                    Sub.PickupRange.SetRingOverride(airLockRingColor);
                    break;

                case PumpState.Idle:
                    Sub.PickupRange.ClearRingOverride();
                    break;
            }
        }

        // -------------------------------------------------------
        // Collect Target Override
        // -------------------------------------------------------

        /**
         * When set, collected O2 is routed to this submarine's O2System instead of Sub's.
         * Companion subs set this to the player sub in Start so their O2 collection
         * benefits the player rather than a tank the companion no longer owns.
         */
        public Submarine OverrideCollectTarget { get; set; }

        // Resolve to the override if present; fall back to self
        private Submarine CollectTarget => OverrideCollectTarget != null ? OverrideCollectTarget : Sub;

        // -------------------------------------------------------
        // AI Interface
        // -------------------------------------------------------

        /**
         * For AI companions: fires TryCollect() when the pump is looping, a pickup
         * is in range, and the charge is inside the sweet spot. Call each frame while
         * the companion is targeting an O2 bubble — the pump handles timing naturally.
         *
         * Does nothing in all other states to avoid triggering AirLock penalties.
         */
        public void AICollect()
        {
            if (_state != PumpState.Looping) return;
            if (!IsPickupInRange) return;
            if (IsInSweetSpot) TryCollect();
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
