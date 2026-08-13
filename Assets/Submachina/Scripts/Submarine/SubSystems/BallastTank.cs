using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /** Where captured intake-pump air is routed: the breathing reserve, the ballast tank, or hull pressurization. */
    public enum PumpDestination { O2Reserve, Ballast, Hull }

    /** Commanded ballast state — a three-position gear shifter. */
    public enum BallastMode { Empty, Neutral, Full }

    /**
     * Ballast tank subsystem (Sub.Ballast) — thrust-free vertical traversal on a
     * SHARED O2 economy.
     *
     * AirFraction (0..1) is how much of the tank holds AIR (the rest floods with
     * water). Air is buoyant: a FULL tank lifts the sub at roughly the speed it
     * falls with average current; an EMPTY tank sinks at full speed; NEUTRAL
     * hovers (buoyancy cancels the reference current).
     *
     * The tank and the main O2 reserve are one conserved pool:
     *   - Filling ballast (shifting up) DRAWS air from the main tank — no O2,
     *     no lift. The shift stalls when the reserve runs dry.
     *   - Emptying ballast (shifting down) RETURNS air to the main tank. If the
     *     main tank is full, the overflow vents as real O2Bubble pickups at the
     *     sub, so the air can be reclaimed by returning to the spot.
     *
     * Controls click through modes like a gear shifter: Empty ↔ Neutral ↔ Full
     * (shift-up and shift-down actions, one step per press). The tank then eases
     * AirFraction toward the commanded target, exchanging O2 as it goes.
     *
     * Heavy cargo can outweigh a full tank — the lift still assists upward
     * thrust, and CargoHold's dump control is the escape valve.
     *
     * The intake pump can route captured environment bubbles straight into the
     * tank (PumpDestination toggle): direct air injections raise AirFraction and
     * auto-promote the commanded mode so the chase logic doesn't vent the gift
     * right back.
     *
     * A sub without this component behaves exactly as before — pumps null-check
     * Sub.Ballast. Ballast is a hull-feature loadout choice.
     */
    [UsesFeedbacks(nameof(SubFeedbacks.BallastFlood), nameof(SubFeedbacks.BallastBlow),
                   nameof(SubFeedbacks.BallastFull), nameof(SubFeedbacks.BallastEmpty),
                   nameof(SubFeedbacks.BallastShift), nameof(SubFeedbacks.PumpDestinationToggled))]
    public class BallastTank : InputSubmarineComponent
    {
        // =====================
        // Tank Settings
        // =====================

        [FoldoutGroup("Tank")]
        [Tooltip("O2 units the tank holds when completely full of air. This is what a full up-shift " +
                 "costs the main reserve — and what a full down-shift gives back.")]
        [SerializeField, Min(1f)] private float tankAirCapacity = 30f;

        [FoldoutGroup("Tank")]
        [Tooltip("AirFraction change per second while shifting between modes. 0.35 ≈ 3 s for a full swing. " +
                 "Upgradeable via SubStats.BallastFloodRate (down) / BallastBlowRate (up).")]
        [SerializeField, Min(0.01f)] private float shiftRate = 0.35f;

        [FoldoutGroup("Tank")]
        [Tooltip("AirFraction that hovers (Neutral's target). Buoyancy hits the reference current force here.")]
        [SerializeField, Range(0.1f, 0.9f)] private float neutralAirFraction = 0.5f;

        [FoldoutGroup("Tank")]
        [Tooltip("The downward force an AVERAGE mission current applies to the sub — buoyancy is tuned against it: " +
                 "Neutral cancels it (hover), Full doubles it upward (rise as fast as you fall). " +
                 "Default 7.5 = baseline current speed 5 × the physics controller's 1.5 force multiplier.")]
        [SerializeField, Min(0f)] private float referenceCurrentForce = 7.5f;

        [FoldoutGroup("Tank")]
        [Tooltip("Rigidbody mass added by a completely flooded (zero-air) tank.")]
        [SerializeField, Min(0f)] private float floodedMass = 1f;

        [FoldoutGroup("Tank")]
        [Tooltip("Starting mode. Neutral spawns hovering; the fraction starts at the mode's target.")]
        [SerializeField] private BallastMode initialMode = BallastMode.Neutral;

        // =====================
        // Vented Bubbles
        // =====================

        [FoldoutGroup("Vented Bubbles")]
        [Tooltip("O2Bubble pickup spawned when venting overflows a full main tank. Leave empty to just lose the air.")]
        [SerializeField] private O2Pickup o2BubblePrefab;

        [FoldoutGroup("Vented Bubbles")]
        [Tooltip("O2 units per spawned bubble. Match the bubble prefab's replenish amount so vented air is " +
                 "truly conserved when reclaimed. Example: venting 12 units with unit 5 → 2 bubbles now, " +
                 "2 units carried toward the next.")]
        [SerializeField, Min(1f)] private float o2PerVentedBubble = 5f;

        [FoldoutGroup("Vented Bubbles")]
        [Tooltip("Bubbles scatter this far around the sub so they don't stack on one point.")]
        [SerializeField, Min(0f)] private float bubbleScatterRadius = 1.2f;

        // =====================
        // Input
        // =====================

        [FoldoutGroup("Input")]
        [Tooltip("Press to shift DOWN one gear: Full → Neutral → Empty (sink).")]
        [SerializeField] private InputActionReference floodAction;

        [FoldoutGroup("Input")]
        [Tooltip("Press to shift UP one gear: Empty → Neutral → Full (rise).")]
        [SerializeField] private InputActionReference blowAction;

        [FoldoutGroup("Input")]
        [Tooltip("Cycles the pump destination: O2 reserve → ballast tank → hull pressurization.")]
        [SerializeField] private InputActionReference pumpDestinationAction;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")] public UnityEvent onModeChanged;
        [FoldoutGroup("Events")] public UnityEvent<float> onAirFractionChanged;          // 0..1
        [FoldoutGroup("Events")] public UnityEvent<float> onBubblesVented;               // O2 units released
        [FoldoutGroup("Events")] public UnityEvent<PumpDestination> onDestinationChanged;

        // =====================
        // Public State
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        public BallastMode Mode { get; private set; } = BallastMode.Neutral;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        public float AirFraction { get; private set; }

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        public PumpDestination Destination { get; private set; } = PumpDestination.O2Reserve;

        /** True while the tank is actively changing its air level. */
        public bool IsShifting => !Mathf.Approximately(AirFraction, TargetAirFraction);

        /** The commanded target for the current mode. */
        public float TargetAirFraction => TargetFor(Mode);

        // =====================
        // Upgrade Accessors
        // =====================

        private float ShiftDownRateMod => Sub?.Upgrades?.Stats.Resolve(SubStats.BallastFloodRate, shiftRate) ?? shiftRate;
        private float ShiftUpRateMod => Sub?.Upgrades?.Stats.Resolve(SubStats.BallastBlowRate, shiftRate) ?? shiftRate;

        // =====================
        // Internals
        // =====================

        private Rigidbody2D _rb;
        private float _bubbleBank;          // vented O2 accumulating toward the next bubble
        private bool _fillLoopPlaying;      // BallastBlow loop (air rising)
        private bool _ventLoopPlaying;      // BallastFlood loop (air venting / water in)

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();
            RegisterAction(floodAction);
            RegisterAction(blowAction);
            RegisterAction(pumpDestinationAction);

            // The tank applies forces to the sub root's rigidbody, like other modules
            _rb = GetComponentInParent<Rigidbody2D>();

            // Spawn settled in the initial gear — no O2 exchange on scene start
            Mode = initialMode;
            AirFraction = TargetFor(initialMode);
        }

        private void Start()
        {
            // Push the initial water mass once siblings are registered
            UpdateMassContribution();
        }

        private void Update()
        {
            HandleShiftInput();
            HandleDestinationToggle();
            ChaseTarget();
        }

        private void FixedUpdate()
        {
            ApplyBuoyancy();
        }

        // -------------------------------------------------------
        // Gear shifting
        // -------------------------------------------------------

        /** Reads the two shift actions on their press edges. Down wins a same-frame chord. */
        private void HandleShiftInput()
        {
            if (ActionPressed(floodAction)) ShiftDown();
            else if (ActionPressed(blowAction)) ShiftUp();
        }

        /** Empty → Neutral → Full. Public so UI / feedbacks can drive the shifter too. */
        public void ShiftUp()
        {
            if (Mode == BallastMode.Full) return;
            SetMode(Mode == BallastMode.Empty ? BallastMode.Neutral : BallastMode.Full);
        }

        /** Full → Neutral → Empty. */
        public void ShiftDown()
        {
            if (Mode == BallastMode.Empty) return;
            SetMode(Mode == BallastMode.Full ? BallastMode.Neutral : BallastMode.Empty);
        }

        private void SetMode(BallastMode mode)
        {
            if (mode == Mode) return;
            Mode = mode;
            onModeChanged?.Invoke();
            Sub?.Feedbacks?.Play(SubFeedbacks.BallastShift, transform.position);
        }

        /** Mode → target air fraction. Empty sinks, Neutral hovers, Full lifts. */
        private float TargetFor(BallastMode mode) => mode switch
        {
            BallastMode.Empty => 0f,
            BallastMode.Full => 1f,
            _ => neutralAirFraction,
        };

        // -------------------------------------------------------
        // O2 exchange (the conserved pool)
        // -------------------------------------------------------

        /**
         * Eases AirFraction toward the commanded target, exchanging O2 with the
         * main tank as it moves:
         *   rising  → ConsumeAir(delta × capacity); stalls when the reserve is dry
         *   falling → AddAir up to the main tank's free space; overflow becomes
         *             world O2Bubble pickups (reclaimable)
         */
        private void ChaseTarget()
        {
            float target = TargetAirFraction;
            bool filling = false, venting = false;

            if (AirFraction < target && Sub?.O2 != null)
            {
                // Draw air from the reserve, throttled by what's actually available
                float delta = Mathf.Min(ShiftUpRateMod * Time.deltaTime, target - AirFraction);
                float available = Sub.O2.CurrentAirPressure / tankAirCapacity;
                delta = Mathf.Min(delta, available);

                if (delta > 0f)
                {
                    Sub.O2.ConsumeAir(delta * tankAirCapacity);
                    SetAirFraction(AirFraction + delta);
                    filling = true;
                    if (AirFraction >= 1f) Sub?.Feedbacks?.Play(SubFeedbacks.BallastFull, transform.position);
                }
            }
            else if (AirFraction > target && Sub?.O2 != null)
            {
                // Return air to the reserve; anything past its cap vents as bubbles
                float delta = Mathf.Min(ShiftDownRateMod * Time.deltaTime, AirFraction - target);
                float o2 = delta * tankAirCapacity;
                float freeSpace = Mathf.Max(0f, Sub.O2.MaxAir - Sub.O2.CurrentAirPressure);
                float toMain = Mathf.Min(o2, freeSpace);

                if (toMain > 0f) Sub.O2.AddAir(toMain);
                if (o2 > toMain) VentAsBubbles(o2 - toMain);

                SetAirFraction(AirFraction - delta);
                venting = true;
                if (AirFraction <= 0f) Sub?.Feedbacks?.Play(SubFeedbacks.BallastEmpty, transform.position);
            }

            // Edge-triggered looping cues for the two transfer directions
            SetLoopFeedback(filling, ref _fillLoopPlaying, SubFeedbacks.BallastBlow);
            SetLoopFeedback(venting, ref _ventLoopPlaying, SubFeedbacks.BallastFlood);
        }

        /**
         * Converts overflow O2 into real bubble pickups at the sub, banked so
         * sub-bubble amounts carry over. Example: unit 5, venting 3 then 4 →
         * first call banks 3, second spawns one bubble and banks 2.
         */
        private void VentAsBubbles(float o2Amount)
        {
            _bubbleBank += o2Amount;
            onBubblesVented?.Invoke(o2Amount);
            if (o2BubblePrefab == null) return;

            while (_bubbleBank >= o2PerVentedBubble)
            {
                _bubbleBank -= o2PerVentedBubble;
                Vector2 scatter = Random.insideUnitCircle * bubbleScatterRadius;
                Instantiate(o2BubblePrefab, transform.position + (Vector3)scatter, Quaternion.identity);
            }
        }

        // -------------------------------------------------------
        // Pump integration
        // -------------------------------------------------------

        /**
         * Injects air directly into the tank (intake pump with destination
         * Ballast, or the manual pump's ballast routing). The commanded mode
         * auto-promotes past each target the injection crosses, so the chase
         * logic keeps the gift instead of venting it back. Returns the overflow
         * that didn't fit (a full tank) for the caller to bank in the main tank.
         */
        public float AddAirToBallast(float o2Amount)
        {
            if (o2Amount <= 0f) return o2Amount;

            float delta = Mathf.Min(o2Amount / tankAirCapacity, 1f - AirFraction);
            SetAirFraction(AirFraction + delta);

            // Promote the gear to hold the new level
            while (AirFraction > TargetAirFraction + 0.01f && Mode != BallastMode.Full)
                SetMode(Mode == BallastMode.Empty ? BallastMode.Neutral : BallastMode.Full);

            if (AirFraction >= 1f) Sub?.Feedbacks?.Play(SubFeedbacks.BallastFull, transform.position);
            return o2Amount - delta * tankAirCapacity;
        }

        // -------------------------------------------------------
        // Buoyancy & mass
        // -------------------------------------------------------

        /**
         * Piecewise-linear lift through three tuning points:
         *   air 0        → 0            (sink with the current, full speed)
         *   air neutral  → +reference   (cancels the average current — hover)
         *   air 1        → +2×reference (rise about as fast as you'd fall)
         * Cargo mass isn't subtracted here — a heavy hold simply outweighs the
         * lift, which still assists upward thrust exactly as intended.
         */
        private void ApplyBuoyancy()
        {
            if (_rb == null) return;

            float lift = AirFraction <= neutralAirFraction
                ? Mathf.Lerp(0f, referenceCurrentForce, AirFraction / neutralAirFraction)
                : Mathf.Lerp(referenceCurrentForce, referenceCurrentForce * 2f,
                             (AirFraction - neutralAirFraction) / (1f - neutralAirFraction));
            _rb.AddForce(Vector2.up * lift, ForceMode2D.Force);
        }

        /** Central setter — clamps, raises change events, refreshes the water mass. */
        private void SetAirFraction(float value)
        {
            float clamped = Mathf.Clamp01(value);
            if (Mathf.Approximately(clamped, AirFraction)) return;

            AirFraction = clamped;
            onAirFractionChanged?.Invoke(AirFraction);
            UpdateMassContribution();
        }

        /** Water (the non-air part) is what weighs the sub down. */
        private void UpdateMassContribution() => Sub?.Physics?.RegisterMass(this, (1f - AirFraction) * floodedMass);

        // -------------------------------------------------------
        // Destination toggle
        // -------------------------------------------------------

        /**
         * Cycles the pump destination on the toggle action's press edge:
         * O2 reserve → Ballast → Hull → back to O2. The Hull stop is skipped
         * when the sub has no HullSystem to pressurize.
         */
        private void HandleDestinationToggle()
        {
            if (!ActionPressed(pumpDestinationAction)) return;

            Destination = Destination switch
            {
                PumpDestination.O2Reserve => PumpDestination.Ballast,
                PumpDestination.Ballast when Sub?.Hull != null => PumpDestination.Hull,
                _ => PumpDestination.O2Reserve,
            };
            onDestinationChanged?.Invoke(Destination);
            Sub?.Feedbacks?.Play(SubFeedbacks.PumpDestinationToggled, transform.position);
        }

        // -------------------------------------------------------
        // Helpers
        // -------------------------------------------------------

        /** True on the frame a registered button action is pressed. */
        private bool ActionPressed(InputActionReference reference)
        {
            var action = ResolveAction(reference);
            return action != null && action.WasPressedThisFrame();
        }

        /** Edge-triggered Play/Stop for a looping transfer cue (same idiom as thrust feedbacks). */
        private void SetLoopFeedback(bool active, ref bool playing, FeedbackId key)
        {
            if (active == playing) return;
            playing = active;
            if (active) Sub?.Feedbacks?.Play(key, transform.position);
            else        Sub?.Feedbacks?.Stop(key);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            SetLoopFeedback(false, ref _fillLoopPlaying, SubFeedbacks.BallastBlow);
            SetLoopFeedback(false, ref _ventLoopPlaying, SubFeedbacks.BallastFlood);
        }

        protected override void OnDestroy()
        {
            Sub?.Physics?.UnregisterMass(this);
            base.OnDestroy();
        }
    }
}
