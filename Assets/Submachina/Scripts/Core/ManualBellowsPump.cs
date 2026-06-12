using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * ManualBellowsPump — the submarine's manual air compression mechanic.
     *
     * Handles the pump input loop only: hold to build charge, release within
     * the sweet spot for a Perfect Pump. Panic mashing triggers an Air Lock
     * penalty. Holding too long vents the charge uselessly.
     *
     * All air tank state (pressure, capacity, decay, health bleed) lives in
     * O2System. This component calls O2System.AddAir() when a pump action succeeds.
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
     *   1. Add to the submarine root alongside O2System.
     *   2. Assign the scene O2System.
     *   3. Optionally assign a PumpAction InputActionReference.
     *   4. Subscribe to events (OnPerfectPump, OnAirLock, etc.) for audio/visual juice.
     */
    public class ManualBellowsPump : MonoBehaviour, ISweetSpotPump
    {
        // =====================
        // References
        // =====================

        [FoldoutGroup("References")]
        [Tooltip("The submarine's O2System — receives air when a pump action succeeds.")]
        [SerializeField] private O2System o2System;

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
        // Input
        // =====================

        [FoldoutGroup("Input")]
        [Tooltip("Master switch for the manual pump-to-generate-air mechanic. " +
                 "With this off, the component is disabled entirely.")]
        [SerializeField] private bool enableManualPumping = true;

        [FoldoutGroup("Input")]
        [Tooltip("Optional O2PickupPump on the same sub. When assigned, manual pumping is " +
                 "suppressed while an O2 pickup is within the intake pump's range or its loop " +
                 "is running — the intake pump owns the pump input near bubbles.")]
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
        [Tooltip("Fired when a Perfect Pump starts the cooldown. Wire: pressure-release SFX, dimmed pump UI.")]
        public UnityEvent OnCooldownStarted;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when the cooldown expires and the pump is usable again. Wire: ready click SFX, UI re-light.")]
        public UnityEvent OnCooldownEnded;

        // =====================
        // Public Read-Only State (ISweetSpotPump)
        // =====================

        /** Current charge progress (0–1) — read by HUD charge meter. */
        public float ChargeProgress => _chargeProgress;

        /** True while the Air Lock penalty is active. */
        public bool IsAirLocked => _state == PumpState.AirLocked;

        /** True while the post-Perfect-Pump cooldown is active. */
        public bool IsOnCooldown => _state == PumpState.CoolingDown;

        /** Seconds left on the sweet spot cooldown — read by HUD for a radial/timer display. */
        public float CooldownRemaining => IsOnCooldown ? Mathf.Max(0f, _cooldownTimer) : 0f;

        /** Lower bound of the sweet spot window — read by BellowsBar to position markers. */
        public float SweetSpotMin => sweetSpotMin;

        /** Upper bound of the sweet spot window — read by BellowsBar to position markers. */
        public float SweetSpotMax => sweetSpotMax;

        /** True while charging and the current charge progress sits within the sweet spot. */
        public bool IsInSweetSpot =>
            _state == PumpState.Charging &&
            _chargeProgress >= sweetSpotMin &&
            _chargeProgress <= sweetSpotMax;

        /** True while the intake pump owns the pump input — manual pumping is unavailable. */
        public bool IsBlockedByIntakePump =>
            intakePump != null && (intakePump.IsLooping || intakePump.IsPickupInRange);

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

        private PumpState _state          = PumpState.Idle;
        private float     _chargeProgress;
        private float     _airLockTimer;
        private float     _cooldownTimer;
        private float     _lastPressTime  = -999f;
        private int       _rapidPressCount;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

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
         * Called on button release. Evaluates charge position and calls O2System.AddAir:
         *
         *   chargeProgress in [sweetSpotMin, sweetSpotMax] → Perfect Pump (perfectPumpAir)
         *   chargeProgress < sweetSpotMin or > sweetSpotMax → Weak Pump (weakPumpAir)
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
                o2System?.AddAir(perfectPumpAir);
                _rapidPressCount = 0;   // good timing resets spam penalty window
                OnPerfectPump?.Invoke();
                _chargeProgress = 0f;

                if (perfectPumpCooldown > 0f) StartCooldown();
                else _state = PumpState.Idle;
                return;
            }

            o2System?.AddAir(weakPumpAir);
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

        // -------------------------------------------------------
        // Debug GUI
        // -------------------------------------------------------

        /**
         * Draws a debug overlay showing air pressure (from O2System) and the charge
         * meter with sweet spot highlighted. Disable via showDebugGUI once real HUD is built.
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

            // ── Air Pressure bar (from O2System) ─────────────────
            float airPressure = o2System != null ? o2System.CurrentAirPressure : 0f;
            float originalMax = o2System != null ? o2System.OriginalMaxAir     : 1f;
            float airPct      = airPressure / originalMax;
            float decayRate   = o2System != null ? o2System.ActiveDecayRate    : 0f;

            GUI.Label(new Rect(bx, y, bw, 16f),
                $"Air:  {airPressure:F1} / {originalMax:F0}   (decay {decayRate:F1}/s)");
            y += 17f;

            // Track
            GUI.color = new Color(0.15f, 0.15f, 0.15f);
            GUI.DrawTexture(new Rect(bx, y, bw, BAR), Texture2D.whiteTexture);

            // Fill — green → yellow → red
            GUI.color = airPct > 0.5f
                ? Color.Lerp(Color.yellow, Color.green, (airPct - 0.5f) * 2f)
                : Color.Lerp(Color.red, Color.yellow, airPct * 2f);
            GUI.DrawTexture(new Rect(bx, y, bw * airPct, BAR), Texture2D.whiteTexture);
            GUI.color = Color.white;
            y += BAR + PAD;

            // ── Charge bar ───────────────────────────────────────
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

            // Charge fill
            bool charged = _state == PumpState.Charging || _state == PumpState.Overshot;
            GUI.color = !charged ? new Color(0.35f, 0.35f, 0.35f)
                : (_chargeProgress >= sweetSpotMin && _chargeProgress <= sweetSpotMax)
                    ? Color.green
                    : Color.cyan;
            GUI.DrawTexture(new Rect(bx, y, bw * _chargeProgress, BAR), Texture2D.whiteTexture);
            GUI.color = Color.white;
            y += BAR + PAD;

            // ── State label ──────────────────────────────────────
            string stateText = _state switch
            {
                PumpState.AirLocked   => $"⚠ AIR LOCK  ({_airLockTimer:F1}s remaining)",
                PumpState.CoolingDown => $"⏳ COOLDOWN  ({_cooldownTimer:F1}s remaining)",
                PumpState.Charging    =>
                    _chargeProgress >= sweetSpotMin && _chargeProgress <= sweetSpotMax
                        ? "★ SWEET SPOT — release now!"
                        : $"Charging... ({_chargeProgress:F2})",
                PumpState.Overshot    => "OVERSHOT — vented, release to reset",
                _                     => $"Idle  |  spam: {_rapidPressCount}/{spamPressLimit}  |  Space / pump button"
            };

            GUI.color = _state == PumpState.AirLocked   ? Color.red
                : _state == PumpState.CoolingDown        ? new Color(0.45f, 0.7f, 1f)
                : _state == PumpState.Overshot           ? new Color(1f, 0.65f, 0f)
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
        [Button("Perfect Pump"), GUIColor(0.4f, 1f, 0.4f)]
        private void DebugPerfectPump()
        {
            if (!Application.isPlaying) { Debug.Log("[BellowsPump] Play mode only."); return; }
            o2System?.AddAir(perfectPumpAir);
            OnPerfectPump?.Invoke();
        }
#endif
    }
}
