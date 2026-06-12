using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityAtoms.BaseAtoms;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * ManualBellowsPump — a standalone prototype of the submarine's manual air
     * compression mechanic. Drop this on the submarine root to test immediately.
     *
     * The core loop: hold the pump button to build charge, release within the
     * sweet spot for a Perfect Pump. Panic mashing the button triggers an Air Lock
     * penalty. Holding too long vents the charge uselessly.
     *
     * NOTE: This is currently decoupled from the existing O2System. Integration
     * with the rest of the HUD/game loop is a separate step once the feel is dialled in.
     *
     * Intake pump handoff: when an O2PickupPump is assigned to IntakePump, manual
     * pumping is suppressed whenever an O2 pickup is in that pump's range or its
     * loop is running — the two pumps share one input, and the intake pump wins
     * near bubbles. With no intake pump assigned, this component runs standalone.
     *
     * Input: Assign a Button InputAction to PumpAction. If left empty,
     * falls back to Spacebar via Keyboard.current (New Input System).
     *
     * Wiring:
     *   1. Add to the submarine root.
     *   2. Optionally assign a PumpAction InputActionReference.
     *   3. Subscribe to events (OnPerfectPump, OnAirLock, etc.) for audio/visual juice.
     *   4. Set IsThrusting / IsMining from other systems to drive exertion decay.
     */
    public class ManualBellowsPump : MonoBehaviour, ISweetSpotPump
    {
        // =====================
        // Air Pressure
        // =====================

        [FoldoutGroup("Air Pressure")]
        [Tooltip("Maximum air pressure the sub can hold.")]
        [SerializeField, Min(1f)] private float maxAirPressure = 100f;

        [FoldoutGroup("Air Pressure")]
        [Tooltip("Air units lost per second at rest. " +
                 "Example: 3 → fully drained in ~33 seconds.")]
        [SerializeField, Min(0f)] private float baseDecayRate = 3f;

        [FoldoutGroup("Air Pressure")]
        [Tooltip("Multiplier on decay when IsThrusting or IsMining is true. " +
                 "Example: 3× → drains 3× faster under exertion (~11 seconds from full).")]
        [SerializeField, Min(1f)] private float exertionDecayMultiplier = 3f;

        [FoldoutGroup("Air Pressure")]
        [Tooltip("Extra flat air drained per second while mining, on top of the normal decay. " +
                 "Example: 2 → mining drains 2 additional units/s regardless of base rate.")]
        [SerializeField, Min(0f)] private float miningExtraDecayRate = 2f;

        [FoldoutGroup("Air Pressure")]
        [Tooltip("Rate at which max air capacity shrinks per second. " +
                 "Only O2 bubble pickups can restore it. Example: 0.5 → max drops by 30 over a minute.")]
        [SerializeField, Min(0f)] private float maxCapacityDecayRate = 0.5f;

        [FoldoutGroup("Air Pressure")]
        [Tooltip("Floor for max air capacity — it will never decay below this value.")]
        [SerializeField, Min(1f)] private float minMaxCapacity = 20f;

        // =====================
        // Charge Cycle
        // =====================

        [FoldoutGroup("Charge Cycle")]
        [Tooltip("How fast chargeProgress (0→1) builds while the button is held. " +
                 "1.6 = fills in ~0.625 seconds. Range 1.43–2.0 maps to 0.5–0.7 second charges.")]
        [SerializeField, Min(0.1f)] private float chargeSpeed = 1.6f;

        [FoldoutGroup("Charge Cycle")]
        [Tooltip("Lower bound of the sweet spot window (0–1 of charge progress). " +
                 "Release at or above this for a Perfect Pump.")]
        [SerializeField, Range(0f, 1f)] private float sweetSpotMin = 0.65f;

        [FoldoutGroup("Charge Cycle")]
        [Tooltip("Upper bound of the sweet spot window (0–1 of charge progress). " +
                 "Release above this but below 1.0 for a Weak Pump.")]
        [SerializeField, Range(0f, 1f)] private float sweetSpotMax = 0.85f;

        // =====================
        // Pump Rewards
        // =====================

        [FoldoutGroup("Pump Rewards")]
        [Tooltip("Air restored on a Perfect Pump (release within sweet spot).")]
        [SerializeField, Min(0f)] private float perfectPumpAir = 25f;

        [FoldoutGroup("Pump Rewards")]
        [Tooltip("Air restored on a Weak Pump (release outside sweet spot, before overshoot).")]
        [SerializeField, Min(0f)] private float weakPumpAir = 5f;

        // =====================
        // Sweet Spot Cooldown
        // =====================

        [FoldoutGroup("Sweet Spot Cooldown")]
        [Tooltip("Seconds the pump is unusable after a Perfect Pump. " +
                 "Set to 0 to disable the cooldown entirely.")]
        [SerializeField, Min(0f)] private float perfectPumpCooldown = 0f;

        // =====================
        // Air Lock (Anti-Spam)
        // =====================

        [FoldoutGroup("Air Lock")]
        [Tooltip("Number of rapid consecutive presses within the spam window to trigger Air Lock. " +
                 "Example: 3 means the 3rd press within the window locks the pump.")]
        [SerializeField, Min(2)] private int spamPressLimit = 3;

        [FoldoutGroup("Air Lock")]
        [Tooltip("Rolling time window (seconds) in which rapid presses are counted. " +
                 "A press resets the counter if this much time has passed since the last press.")]
        [SerializeField, Min(0.1f)] private float spamWindowDuration = 1.5f;

        [FoldoutGroup("Air Lock")]
        [Tooltip("How long the pump is locked after triggering Air Lock.")]
        [SerializeField, Min(0.1f)] private float airLockDuration = 2.5f;

        // =====================
        // Health Bleed
        // =====================

        [FoldoutGroup("Health Bleed")]
        [Tooltip("Player Health component. Damaged while air is completely empty.")]
        [SerializeField] private Health playerHealth;

        [FoldoutGroup("Health Bleed")]
        [Tooltip("Health drained per second while air is at zero. " +
                 "Example: 8 → ~1 HP per 0.125s, similar to O2System bleed.")]
        [SerializeField, Min(0f)] private float healthBleedRate = 8f;

        // =====================
        // Atoms
        // =====================

        [FoldoutGroup("Atoms")]
        [Tooltip("Written each frame with current air pressure. " +
                 "Assign the same CurrentO2 atom used by the HUD bar — no HUD changes needed.")]
        [SerializeField] private FloatVariable currentO2Atom;

        // =====================
        // Input
        // =====================

        [FoldoutGroup("Input")]
        [Tooltip("Master switch for the manual pump-to-generate-air mechanic. " +
                 "With this off, the component still acts as the air tank " +
                 "(storage, decay, health bleed, atom write).")]
        [SerializeField] private bool enableManualPumping = true;

        [FoldoutGroup("Input")]
        [Tooltip("Optional O2PickupPump on the same sub. When assigned, manual pumping is " +
                 "suppressed while an O2 pickup is within the intake pump's range or its loop " +
                 "is running — the intake pump owns the pump input near bubbles. " +
                 "Leave empty to run the manual bellows standalone.")]
        [SerializeField] private O2PickupPump intakePump;

        [FoldoutGroup("Input")]
        [Tooltip("Button InputAction for the pump. If unassigned, Spacebar is used as a fallback.")]
        [SerializeField] private InputActionReference pumpAction;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired when the player starts holding the pump button.")]
        public UnityEvent OnPumpChargeStarted;

        [FoldoutGroup("Events")]
        [Tooltip("Fired on a successful release within the sweet spot. Wire: +25 air SFX, green flash.")]
        public UnityEvent OnPerfectPump;

        [FoldoutGroup("Events")]
        [Tooltip("Fired on a release outside the sweet spot. Wire: weak thud SFX, dim flash.")]
        public UnityEvent OnWeakPump;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when charge is held past 1.0 and vents uselessly. Wire: hiss SFX.")]
        public UnityEvent OnOvershotPump;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when spam triggers the Air Lock penalty. Wire: grind SFX, red screen flash.")]
        public UnityEvent OnAirLock;

        [FoldoutGroup("Events")]
        [Tooltip("Fired once when air pressure first hits zero. Wire: critical alarm, screen vignette.")]
        public UnityEvent OnAirExhausted;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when a Perfect Pump starts the cooldown. Wire: pressure-release SFX, dimmed pump UI.")]
        public UnityEvent OnCooldownStarted;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when the cooldown expires and the pump is usable again. Wire: ready click SFX, UI re-light.")]
        public UnityEvent OnCooldownEnded;

        // =====================
        // Public Read-Only State
        // =====================

        /** Current air pressure — read by HUD, O2 integration, etc. */
        public float CurrentAirPressure => _currentAirPressure;

        /** Current charge progress (0–1) — read by HUD charge meter. */
        public float ChargeProgress => _chargeProgress;

        /** True while the Air Lock penalty is active. */
        public bool IsAirLocked => _state == PumpState.AirLocked;

        /** True while the post-Perfect-Pump cooldown is active. */
        public bool IsOnCooldown => _state == PumpState.CoolingDown;

        /** Seconds left on the sweet spot cooldown — read by HUD for a radial/timer display. */
        public float CooldownRemaining => IsOnCooldown ? Mathf.Max(0f, _cooldownTimer) : 0f;

        /** Current active decay rate, accounting for exertion and mining bonus. Read by HUD for display. */
        public float ActiveDecayRate =>
            baseDecayRate * (IsMining || IsThrusting ? exertionDecayMultiplier : 1f)
            + (IsMining ? miningExtraDecayRate : 0f);

        /** Current max air capacity — shrinks over time, restored by O2 bubbles. */
        public float MaxAir => _currentMaxAir;

        /** The original max capacity set in the Inspector — used by O2Bar as the fixed denominator
         *  so the bar visually shrinks as capacity degrades. */
        public float OriginalMaxAir => maxAirPressure;

        /** Lower bound of the sweet spot window — read by BellowsBar to position markers. */
        public float SweetSpotMin => sweetSpotMin;

        /** Upper bound of the sweet spot window — read by BellowsBar to position markers. */
        public float SweetSpotMax => sweetSpotMax;

        /** True while charging and the current charge progress sits within the sweet spot. */
        public bool IsInSweetSpot =>
            _state == PumpState.Charging &&
            _chargeProgress >= sweetSpotMin &&
            _chargeProgress <= sweetSpotMax;

        /** True while the intake pump owns the pump input — an O2 pickup is in its range
         *  or its loop is running. Manual pumping is unavailable while this is true.
         *  Read by HUD to grey out the bellows prompt. */
        public bool IsBlockedByIntakePump =>
            intakePump != null && (intakePump.IsLooping || intakePump.IsPickupInRange);

        // =====================
        // Public Exertion Flags
        // =====================

        /**
         * Set true by SubmarinePhysicsController while thrust input is active.
         * Increases air drain to reflect the physical exertion of hard maneuvering.
         */
        public bool IsThrusting { get; set; }

        /**
         * Set true by MiningLaser while the laser is actively firing.
         * Increases air drain to reflect the effort of operating the drill.
         */
        public bool IsMining { get; set; }

        // =====================
        // Debug (Inspector)
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private string CurrentState => _state.ToString();

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private int SpamCount => _rapidPressCount;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float AirLockRemaining => Mathf.Max(0f, _airLockTimer);

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float CooldownTimeRemaining => CooldownRemaining;

        [FoldoutGroup("Debug")]
        [Tooltip("Show the debug overlay in Play mode. Disable once real HUD art is in.")]
        [SerializeField] private bool showDebugGUI = true;

        // =====================
        // Internal State
        // =====================

        private enum PumpState { Idle, Charging, Overshot, AirLocked, CoolingDown }

        private PumpState _state  = PumpState.Idle;
        private float _currentAirPressure;
        private float _currentMaxAir;
        private float _chargeProgress;
        private float _airLockTimer;
        private float _cooldownTimer;
        private float _lastPressTime   = -999f;
        private int   _rapidPressCount = 0;
        private bool  _airExhaustedFired;
        private float _pendingHealthDamage;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            _currentMaxAir      = maxAirPressure;
            _currentAirPressure = maxAirPressure;
            WriteAtom();
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
            DecayMaxCapacity();
            DecayAirPressure();
            UpdateStateMachine();
            ProcessInput();
            if (_currentAirPressure <= 0f) BleedHealth();
        }

        // -------------------------------------------------------
        // Air Pressure
        // -------------------------------------------------------

        /**
         * Slowly lowers the max air capacity each frame.
         * If current pressure exceeds the new ceiling it is clamped down with it.
         * Floored at minMaxCapacity so the sub always has some capacity remaining.
         */
        private void DecayMaxCapacity()
        {
            if (maxCapacityDecayRate <= 0f) return;

            _currentMaxAir = Mathf.Max(minMaxCapacity, _currentMaxAir - maxCapacityDecayRate * Time.deltaTime);

            // If current pressure is above the new ceiling, pull it down too
            if (_currentAirPressure > _currentMaxAir)
            {
                _currentAirPressure = _currentMaxAir;
                WriteAtom();
            }
        }

        /**
         * Drains air pressure each frame at the active decay rate.
         * Decay accelerates under exertion (thrusting or mining).
         * OnAirExhausted fires exactly once when pressure first reaches zero.
         *
         * Example: baseDecay=3, exertionMultiplier=3 → 9/s while thrusting (~11s from full).
         */
        private void DecayAirPressure()
        {
            if (_currentAirPressure <= 0f) return;

            _currentAirPressure -= ActiveDecayRate * Time.deltaTime;

            if (_currentAirPressure <= 0f)
            {
                _currentAirPressure = 0f;
                if (!_airExhaustedFired)
                {
                    _airExhaustedFired = true;
                    OnAirExhausted?.Invoke();
                }
            }

            WriteAtom();
        }

        // -------------------------------------------------------
        // State Machine
        // -------------------------------------------------------

        /**
         * Drives the pump state machine each frame.
         *
         * AirLocked:   counts down the penalty timer, then returns to Idle.
         * CoolingDown: counts down the sweet spot cooldown, fires OnCooldownEnded on expiry.
         * Charging:    advances chargeProgress; transitions to Overshot if it hits 1.0
         *              (held too long — vents without reward).
         * Idle/Overshot: passive, waiting for input events.
         */
        private void UpdateStateMachine()
        {
            switch (_state)
            {
                case PumpState.AirLocked:
                    _airLockTimer -= Time.deltaTime;
                    if (_airLockTimer <= 0f) _state = PumpState.Idle;
                    break;

                case PumpState.CoolingDown:
                    _cooldownTimer -= Time.deltaTime;
                    if (_cooldownTimer <= 0f)
                    {
                        _state = PumpState.Idle;
                        OnCooldownEnded?.Invoke();
                    }
                    break;

                case PumpState.Charging:
                    _chargeProgress += chargeSpeed * Time.deltaTime;
                    if (_chargeProgress >= 1f)
                    {
                        // Held past maximum — pump vents uselessly
                        _chargeProgress = 0f;
                        _state = PumpState.Overshot;
                        OnOvershotPump?.Invoke();
                    }
                    break;
            }
        }

        // -------------------------------------------------------
        // Input
        // -------------------------------------------------------

        /**
         * Reads pump button state from InputAction (preferred) or
         * Keyboard.current.spaceKey (fallback for quick testing).
         */
        private void ProcessInput()
        {
            // Tank-only mode — air comes exclusively from external sources (O2PickupPump)
            if (!enableManualPumping) return;

            // The intake pump owns the input near O2 bubbles — abandon any in-flight
            // charge so the manual pump can't get stuck mid-cycle, then ignore presses
            if (IsBlockedByIntakePump)
            {
                if (_state == PumpState.Charging || _state == PumpState.Overshot)
                {
                    _chargeProgress = 0f;
                    _state = PumpState.Idle;
                }
                return;
            }

            if (GetPumpPressed())  HandlePress();
            if (GetPumpReleased()) HandleRelease();
        }

        private bool GetPumpPressed() =>
            pumpAction != null
                ? pumpAction.action.WasPressedThisFrame()
                : Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;

        private bool GetPumpReleased() =>
            pumpAction != null
                ? pumpAction.action.WasReleasedThisFrame()
                : Keyboard.current != null && Keyboard.current.spaceKey.wasReleasedThisFrame;

        // -------------------------------------------------------
        // Pump Logic
        // -------------------------------------------------------

        /**
         * Called on button press. Detects spam before allowing a new charge cycle.
         *
         * Spam detection uses a rolling time window: each press that arrives within
         * spamWindowDuration of the previous press increments a counter. Once
         * spamPressLimit is reached, Air Lock triggers.
         *
         * A successful pump resets the spam counter, so deliberate play is
         * never penalized. Only mindless mashing triggers the lock.
         *
         * Example: spamPressLimit=3, spamWindowDuration=1.5s
         *   → Pressing 3 times within 1.5 seconds → Air Lock.
         *   → Pressing once, waiting 1.6s, pressing again → counter resets.
         */
        private void HandlePress()
        {
            // Pump is inoperable during penalties and the sweet spot cooldown
            if (_state == PumpState.AirLocked || _state == PumpState.CoolingDown) return;

            // Rolling spam window: increment if within window, reset if outside
            if (Time.time - _lastPressTime <= spamWindowDuration)
                _rapidPressCount++;
            else
                _rapidPressCount = 1;

            _lastPressTime = Time.time;

            if (_rapidPressCount >= spamPressLimit)
            {
                TriggerAirLock();
                return;
            }

            // Begin a fresh compression cycle
            _chargeProgress = 0f;
            _state = PumpState.Charging;
            OnPumpChargeStarted?.Invoke();
        }

        /**
         * Called on button release. Evaluates charge position and awards air:
         *
         *   chargeProgress in [sweetSpotMin, sweetSpotMax] → Perfect Pump (+perfectPumpAir)
         *   chargeProgress < sweetSpotMin or > sweetSpotMax → Weak Pump (+weakPumpAir)
         *   State was Overshot (charge already hit 1.0 while held) → no air
         *
         * Perfect Pump also resets the spam counter as a reward for good play,
         * and starts the sweet spot cooldown if perfectPumpCooldown > 0.
         */
        private void HandleRelease()
        {
            if (_state == PumpState.AirLocked || _state == PumpState.CoolingDown) return;

            // Released after overshoot — already vented, no reward
            if (_state == PumpState.Overshot)
            {
                _state = PumpState.Idle;
                return;
            }

            if (_state != PumpState.Charging) return;

            bool inSweetSpot = _chargeProgress >= sweetSpotMin && _chargeProgress <= sweetSpotMax;

            if (inSweetSpot)
            {
                AddAir(perfectPumpAir);
                _rapidPressCount = 0;   // good timing resets spam penalty window
                OnPerfectPump?.Invoke();
                _chargeProgress = 0f;

                // A successful pump optionally locks the pump out for a breather
                if (perfectPumpCooldown > 0f) StartCooldown();
                else _state = PumpState.Idle;
                return;
            }

            AddAir(weakPumpAir);
            OnWeakPump?.Invoke();

            _chargeProgress = 0f;
            _state = PumpState.Idle;
        }

        /**
         * Enters the CoolingDown state for perfectPumpCooldown seconds.
         * The pump ignores all input until the timer expires and OnCooldownEnded fires.
         */
        private void StartCooldown()
        {
            _state = PumpState.CoolingDown;
            _cooldownTimer = perfectPumpCooldown;
            OnCooldownStarted?.Invoke();
        }

        /**
         * Triggers the Air Lock state — pump is inoperable for airLockDuration seconds.
         * Resets spam counter so the player starts fresh after the penalty expires.
         */
        private void TriggerAirLock()
        {
            _state = PumpState.AirLocked;
            _airLockTimer = airLockDuration;
            _chargeProgress = 0f;
            _rapidPressCount = 0;
            OnAirLock?.Invoke();
        }

        /**
         * Instantly drains a flat amount of air (e.g. from a dash or ability cost).
         * Fires OnAirExhausted if the drain pushes pressure to zero for the first time.
         */
        public void ConsumeAir(float amount)
        {
            _currentAirPressure = Mathf.Max(0f, _currentAirPressure - amount);

            if (_currentAirPressure <= 0f && !_airExhaustedFired)
            {
                _airExhaustedFired = true;
                OnAirExhausted?.Invoke();
            }

            WriteAtom();
        }

        /**
         * Raises the max air capacity by amount, clamped to the original maxAirPressure ceiling.
         * Called by O2 bubble pickups — the only way to push the capacity back up.
         */
        public void RestoreCapacity(float amount)
        {
            _currentMaxAir = Mathf.Min(maxAirPressure, _currentMaxAir + amount);
        }

        /**
         * Restores air by amount, clamped to the current dynamic max capacity.
         * Resets the exhausted flag so OnAirExhausted can fire again next drain cycle.
         */
        public void AddAir(float amount)
        {
            _currentAirPressure = Mathf.Min(_currentMaxAir, _currentAirPressure + amount);
            _airExhaustedFired = false;
            _pendingHealthDamage = 0f;
            WriteAtom();
        }

        /**
         * Accumulates fractional health damage and applies it as whole integers
         * to avoid calling TakeDamage every frame.
         * Example: bleedRate=8, deltaTime=0.016 → 1 damage applied every ~8 frames.
         */
        private void BleedHealth()
        {
            if (playerHealth == null || playerHealth.IsDead) return;

            _pendingHealthDamage += healthBleedRate * Time.deltaTime;
            int damage = Mathf.FloorToInt(_pendingHealthDamage);
            if (damage <= 0) return;

            _pendingHealthDamage -= damage;
            playerHealth.TakeDamage(damage);
        }

        private void WriteAtom()
        {
            if (currentO2Atom != null) currentO2Atom.Value = _currentAirPressure;
        }

        // -------------------------------------------------------
        // Debug GUI
        // -------------------------------------------------------

        /**
         * Draws a debug overlay in the top-left corner during Play mode.
         * Shows air pressure, the charge meter with sweet spot highlighted,
         * and the current state. Disable via showDebugGUI once real HUD is built.
         */
        private void OnGUI()
        {
            if (!Application.isPlaying || !showDebugGUI) return;

            const float X   = 10f;
            const float Y   = 10f;
            const float W   = 290f;
            const float H   = 175f;
            const float PAD = 8f;
            const float BAR = 22f;

            // Panel background
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(X, Y, W, H), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float y = Y + PAD;

            // Title
            GUI.Label(new Rect(X + PAD, y, W - PAD * 2, 18f), "<b>[ Bellows Pump ]</b>");
            y += 22f;

            float bx = X + PAD;
            float bw = W - PAD * 2f;

            // ── Air Pressure bar ─────────────────────────────
            float airPct = _currentAirPressure / maxAirPressure;
            GUI.Label(new Rect(bx, y, bw, 16f),
                $"Air:  {_currentAirPressure:F1} / {maxAirPressure:F0}   (decay {ActiveDecayRate:F1}/s)");
            y += 17f;

            // Track
            GUI.color = new Color(0.15f, 0.15f, 0.15f);
            GUI.DrawTexture(new Rect(bx, y, bw, BAR), Texture2D.whiteTexture);

            // Fill — green → yellow → red
            GUI.color = airPct > 0.5f
                ? Color.Lerp(Color.yellow, Color.green, (airPct - 0.5f) * 2f)
                : Color.Lerp(Color.red,    Color.yellow, airPct * 2f);
            GUI.DrawTexture(new Rect(bx, y, bw * airPct, BAR), Texture2D.whiteTexture);
            GUI.color = Color.white;
            y += BAR + PAD;

            // ── Charge bar ───────────────────────────────────
            GUI.Label(new Rect(bx, y, bw, 16f),
                $"Charge: {_chargeProgress:F2}   sweet spot [{sweetSpotMin:F2} – {sweetSpotMax:F2}]");
            y += 17f;

            // Track
            GUI.color = new Color(0.15f, 0.15f, 0.15f);
            GUI.DrawTexture(new Rect(bx, y, bw, BAR), Texture2D.whiteTexture);

            // Sweet spot highlight (green band)
            GUI.color = new Color(0.15f, 0.75f, 0.15f, 0.55f);
            GUI.DrawTexture(
                new Rect(bx + bw * sweetSpotMin, y, bw * (sweetSpotMax - sweetSpotMin), BAR),
                Texture2D.whiteTexture);

            // Charge fill — colour reflects position relative to sweet spot
            bool charged = _state == PumpState.Charging || _state == PumpState.Overshot;
            GUI.color = !charged ? new Color(0.35f, 0.35f, 0.35f)
                : (_chargeProgress >= sweetSpotMin && _chargeProgress <= sweetSpotMax)
                    ? Color.green
                    : Color.cyan;
            GUI.DrawTexture(new Rect(bx, y, bw * _chargeProgress, BAR), Texture2D.whiteTexture);
            GUI.color = Color.white;
            y += BAR + PAD;

            // ── State label ──────────────────────────────────
            string stateText = _state switch
            {
                PumpState.AirLocked => $"⚠ AIR LOCK  ({_airLockTimer:F1}s remaining)",
                PumpState.CoolingDown => $"⏳ COOLDOWN  ({_cooldownTimer:F1}s remaining)",
                PumpState.Charging  =>
                    _chargeProgress >= sweetSpotMin && _chargeProgress <= sweetSpotMax
                        ? "★ SWEET SPOT — release now!"
                        : $"Charging... ({_chargeProgress:F2})",
                PumpState.Overshot  => "OVERSHOT — vented, release to reset",
                _                   => $"Idle  |  spam: {_rapidPressCount}/{spamPressLimit}  |  Space / pump button"
            };

            GUI.color = _state == PumpState.AirLocked ? Color.red
                : _state == PumpState.CoolingDown ? new Color(0.45f, 0.7f, 1f)
                : _state == PumpState.Overshot ? new Color(1f, 0.65f, 0f)
                : (_state == PumpState.Charging &&
                   _chargeProgress >= sweetSpotMin && _chargeProgress <= sweetSpotMax)
                    ? Color.green : Color.white;

            GUI.Label(new Rect(bx, y, bw, 20f), stateText);
            GUI.color = Color.white;
        }

        // -------------------------------------------------------
        // Editor Utilities
        // -------------------------------------------------------

#if UNITY_EDITOR
        [FoldoutGroup("Debug")]
        [Button("Trigger Air Lock"), GUIColor(1f, 0.3f, 0.3f)]
        private void DebugTriggerAirLock()
        {
            if (!Application.isPlaying) { Debug.Log("[BellowsPump] Play mode only."); return; }
            TriggerAirLock();
        }

        [FoldoutGroup("Debug")]
        [Button("Drain 30 Air"), GUIColor(1f, 0.6f, 0.2f)]
        private void DebugDrainAir()
        {
            if (!Application.isPlaying) { Debug.Log("[BellowsPump] Play mode only."); return; }
            _currentAirPressure = Mathf.Max(0f, _currentAirPressure - 30f);
        }

        [FoldoutGroup("Debug")]
        [Button("Fill Air"), GUIColor(0.4f, 1f, 0.4f)]
        private void DebugFillAir()
        {
            if (!Application.isPlaying) { Debug.Log("[BellowsPump] Play mode only."); return; }
            AddAir(maxAirPressure);
        }
#endif
    }
}
