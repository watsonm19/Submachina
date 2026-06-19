using UnityEngine;
using UnityEngine.Events;
using UnityAtoms.BaseAtoms;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * O2System — the submarine's air tank and core survival pressure system.
     *
     * Single source of truth for all air state. Owns:
     *   - Current air pressure against a fixed max capacity
     *   - Passive decay (scaled by exertion and depth)
     *   - Health bleed when air hits zero
     *   - The CurrentO2 atom written to the HUD each frame
     *
     * External systems interact via:
     *   - IsThrusting / IsMining flags (set by SubmarinePhysicsController and MiningLaser)
     *   - AddAir / ConsumeAir (called by O2Pickup and abilities)
     *
     * Setup:
     *   - Add to the submarine root alongside ManualBellowsPump.
     *   - Assign the CurrentO2 FloatVariable atom (shared with O2Bar).
     *   - Assign the player's Health component for health bleed.
     *   - Optionally assign the CurrentDepth atom from DepthTracker for depth scaling.
     */
    public class O2System : SubmarineComponent
    {
        // =====================
        // Air Capacity
        // =====================

        [FoldoutGroup("Air Capacity")]
        [Tooltip("Maximum air pressure the sub can hold. Also the starting amount.")]
        [SerializeField, Min(1f)] private float maxAirPressure = 100f;

        [FoldoutGroup("Air Capacity")]
        [Tooltip("Written each frame with the current air pressure. Read by O2Bar.")]
        [SerializeField] private FloatVariable currentO2;

        // =====================
        // Decay
        // =====================

        [FoldoutGroup("Decay")]
        [Tooltip("Air units lost per second at rest. " +
                 "Example: 3 → fully drained in ~33 seconds at full capacity.")]
        [SerializeField, Min(0f)] private float baseDecayRate = 3f;

        [FoldoutGroup("Decay")]
        [Tooltip("Multiplier on decay when lateral thrust is active. " +
                 "Example: 3× → drains 3× faster during side-to-side movement.")]
        [SerializeField, Min(1f)] private float lateralExertionMultiplier = 3f;

        [FoldoutGroup("Decay")]
        [Tooltip("Multiplier on decay when vertical (counter) thrust is active. " +
                 "Example: 3× → drains 3× faster when fighting the current.")]
        [SerializeField, Min(1f)] private float verticalExertionMultiplier = 3f;

        [FoldoutGroup("Decay")]
        [Tooltip("Extra flat air drained per second while mining, on top of exertion decay. " +
                 "Example: 2 → mining drains 2 additional units/s regardless of base rate.")]
        [SerializeField, Min(0f)] private float miningExtraDecayRate = 2f;

        [FoldoutGroup("Decay")]
        [Tooltip("How often (seconds) passive air loss is totalled up and reported via onAirDecayed. " +
                 "Per-frame decay is too tiny to display, so it is summed and announced in chunks " +
                 "(e.g. every 5s) for a low-frequency 'breathing' floating-text readout.")]
        [SerializeField, Min(0.1f)] private float decayReportInterval = 5f;

        // =====================
        // Depth Scaling
        // =====================

        [FoldoutGroup("Depth Scaling")]
        [Tooltip("CurrentDepth atom written by DepthTracker. Leave unassigned to disable depth scaling.")]
        [SerializeField] private FloatVariable currentDepth;

        [FoldoutGroup("Depth Scaling")]
        [Tooltip("Extra decay added per metre of depth, as a fraction of the active decay rate. " +
                 "Example: 0.005 → at 100m the multiplier is 1.5 (50% more drain).")]
        [SerializeField, Min(0f)] private float drainPerMetre = 0.005f;

        [FoldoutGroup("Depth Scaling")]
        [Tooltip("Maximum multiplier allowed from depth scaling. " +
                 "Example: 3.0 → decay can at most triple regardless of depth.")]
        [SerializeField, Min(1f)] private float maxDepthMultiplier = 3f;

        // =====================
        // Health Bleed
        // =====================

        [FoldoutGroup("Health Bleed")]
        [Tooltip("Health drained per second while air is at zero.")]
        [SerializeField, Min(0f)] private float healthBleedRate = 8f;

        [FoldoutGroup("Health Bleed")]
        [Tooltip("Player Health component — damaged while air is empty.")]
        [SerializeField] private Health playerHealth;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired when air first reaches zero.")]
        public UnityEvent onO2Depleted;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when air is restored from zero back above zero.")]
        public UnityEvent onO2Restored;

        [FoldoutGroup("Events")]
        [Tooltip("Fired whenever air is added (O2 pickups, manual/intake pumps). " +
                 "Passes the nominal amount requested. Wire to FloatingTextPool for '+air' popups.")]
        public UnityEvent<float> onAirGained;

        [FoldoutGroup("Events")]
        [Tooltip("Fired whenever air is spent in one discrete chunk (e.g. Cavitation Burst cost). " +
                 "Passes the nominal amount spent. Wire to FloatingTextPool for '-air' popups.")]
        public UnityEvent<float> onAirSpent;

        [FoldoutGroup("Events")]
        [Tooltip("Fired every decayReportInterval seconds with the air lost to passive decay since " +
                 "the last report. Lets the HUD show a periodic, low-key 'breathing loss' readout " +
                 "distinct from the punchy one-off gain/spend popups.")]
        public UnityEvent<float> onAirDecayed;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float AirPercent => MaxAirMod > 0 ? (_currentAirPressure / MaxAirMod) * 100f : 0f;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private bool IsBleeding => _isDepleted;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float EffectiveDecayRate => ActiveDecayRate * DepthMultiplier;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float DepthMultiplier => currentDepth != null
            ? Mathf.Min(1f + currentDepth.Value * drainPerMetre, maxDepthMultiplier)
            : 1f;

        [FoldoutGroup("Debug")]
        [Tooltip("Show the on-screen O2 debug overlay in Play mode (air tank + decay-rate breakdown). " +
                 "Disable once real HUD art is in.")]
        [SerializeField] private bool showDebugGUI = true;

        // =====================
        // Exertion Flags
        // =====================

        /** Set true by SubmarinePhysicsController while lateral thrust input is active. */
        public bool IsThrustingLateral { get; set; }

        /** Set true by SubmarinePhysicsController while vertical (counter) thrust input is active. */
        public bool IsThrustingVertical { get; set; }

        /** Set true by MiningLaser while the laser is actively firing. */
        public bool IsMining { get; set; }

        // =====================
        // Public Properties
        // =====================

        public float CurrentAirPressure => _currentAirPressure;

        /** Current max capacity — always the upgrade-resolved ceiling. */
        public float MaxAir         => MaxAirMod;
        public float OriginalMaxAir => MaxAirMod;

        // =====================
        // Upgrade Accessors
        // =====================

        private float MaxAirMod           => Sub?.Upgrades?.Stats.Resolve(SubStats.MaxAirPressure, maxAirPressure) ?? maxAirPressure;
        private float BaseDecayMod        => Sub?.Upgrades?.Stats.Resolve(SubStats.BaseDecayRate, baseDecayRate) ?? baseDecayRate;
        private float LateralExertionMod  => Sub?.Upgrades?.Stats.Resolve(SubStats.LateralExertionMultiplier, lateralExertionMultiplier) ?? lateralExertionMultiplier;
        private float VerticalExertionMod => Sub?.Upgrades?.Stats.Resolve(SubStats.VerticalExertionMultiplier, verticalExertionMultiplier) ?? verticalExertionMultiplier;
        private float BleedRateMod        => Sub?.Upgrades?.Stats.Resolve(SubStats.HealthBleedRate, healthBleedRate) ?? healthBleedRate;

        /**
         * Active decay rate accounting for exertion state — read by HUD and debug displays.
         *
         * Lateral and vertical exertion use separate multipliers so upgrades
         * can reduce O2 cost of movement directions independently.
         * The highest active multiplier wins (they don't stack additively).
         *
         * Example: baseDecay=3, lateralMult=3, verticalMult=3, mining → 3×3 + 2 = 11/s
         */
        public float ActiveDecayRate
        {
            get
            {
                // Pick the highest exertion multiplier among active sources
                float exertionMult = 1f;
                if (IsThrustingLateral)  exertionMult = Mathf.Max(exertionMult, LateralExertionMod);
                if (IsThrustingVertical) exertionMult = Mathf.Max(exertionMult, VerticalExertionMod);
                if (IsMining)            exertionMult = Mathf.Max(exertionMult, LateralExertionMod);

                return BaseDecayMod * exertionMult + (IsMining ? miningExtraDecayRate : 0f);
            }
        }

        // =====================
        // Internal State
        // =====================

        private float _currentAirPressure;
        private bool  _isDepleted;
        private float _pendingHealthDamage;

        // Passive-decay reporting: air drained per frame is summed here and flushed
        // to onAirDecayed once _decayReportTimer reaches decayReportInterval.
        private float _decayAccumulator;
        private float _decayReportTimer;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();
            // Use raw base values in Awake — UpgradeManager may not be registered yet.
            // Start() re-initializes with modifier-resolved values.
            _currentAirPressure = maxAirPressure;
            WriteAtom();
        }

        private void Start()
        {
            // Re-init with upgrade-resolved values now that all SubmarineComponents
            // (including UpgradeManager) are guaranteed registered.
            _currentAirPressure = Mathf.Min(_currentAirPressure, MaxAirMod);
            WriteAtom();
        }

        private void Update()
        {
            DecayAirPressure();
            ReportPassiveDecay();
            if (_isDepleted) BleedHealth();
        }

        // -------------------------------------------------------
        // Core
        // -------------------------------------------------------

        /**
         * Drains air each frame at the exertion-scaled and depth-scaled rate.
         * Fires onO2Depleted the first time air hits zero.
         *
         * Example: baseDecayRate=3, exertionMult=3, depth=100m, drainPerMetre=0.005
         *   → at rest:         3 × 1.5  = 4.5/s
         *   → while thrusting: 9 × 1.5  = 13.5/s
         *   → while mining:   11 × 1.5  = 16.5/s  (9 + 2 extra, × depth mult)
         */
        private void DecayAirPressure()
        {
            if (_currentAirPressure <= 0f) return;

            bool wasDepletedBefore = _isDepleted;

            // Track the actual pressure lost this frame so passive decay can be
            // totalled and reported periodically (see ReportPassiveDecay).
            float before = _currentAirPressure;
            _currentAirPressure -= ActiveDecayRate * DepthMultiplier * Time.deltaTime;
            _currentAirPressure  = Mathf.Max(0f, _currentAirPressure);
            _decayAccumulator   += before - _currentAirPressure;
            _isDepleted          = _currentAirPressure <= 0f;

            WriteAtom();

            if (!wasDepletedBefore && _isDepleted)
                onO2Depleted?.Invoke();
        }

        /**
         * Periodically announces how much air passive decay has eaten since the
         * last report. Per-frame loss is far too small to show as floating text,
         * so it is summed and flushed in chunks every decayReportInterval seconds.
         *
         * Only fires when something was actually lost, so a paused/full tank
         * stays quiet instead of spamming "-0" popups.
         */
        private void ReportPassiveDecay()
        {
            _decayReportTimer += Time.deltaTime;
            if (_decayReportTimer < decayReportInterval) return;

            _decayReportTimer = 0f;
            if (_decayAccumulator <= 0f) return;

            onAirDecayed?.Invoke(_decayAccumulator);
            _decayAccumulator = 0f;
        }

        /**
         * Accumulates fractional health damage and applies it as whole integers
         * to avoid calling TakeDamage every single frame.
         * Example: bleedRate=8, deltaTime=0.016 → 1 HP applied every ~8 frames.
         */
        private void BleedHealth()
        {
            if (playerHealth == null || playerHealth.IsDead) return;

            _pendingHealthDamage += BleedRateMod * Time.deltaTime;
            int damage = Mathf.FloorToInt(_pendingHealthDamage);
            if (damage <= 0) return;

            _pendingHealthDamage -= damage;
            playerHealth.TakeDamage(damage);
        }

        // -------------------------------------------------------
        // Public API
        // -------------------------------------------------------

        /**
         * Adds air, clamped to max capacity.
         * Clears the depletion state and fires onO2Restored if returning from empty.
         * Called by O2 bubble pickups and the manual pump.
         */
        public void AddAir(float amount)
        {
            bool wasDepletedBefore = _isDepleted;

            _currentAirPressure  = Mathf.Min(MaxAirMod, _currentAirPressure + amount);
            _isDepleted          = _currentAirPressure <= 0f;
            _pendingHealthDamage = 0f;

            WriteAtom();

            // Announce the nominal gain for floating-text / juice (skip no-op calls)
            if (amount > 0f) onAirGained?.Invoke(amount);

            if (wasDepletedBefore && !_isDepleted)
                onO2Restored?.Invoke();
        }

        /**
         * Instantly drains a flat amount of air (e.g. from CavitationBurst ability cost).
         * Fires onO2Depleted if the drain pushes air to zero.
         */
        public void ConsumeAir(float amount)
        {
            bool wasDepletedBefore = _isDepleted;

            _currentAirPressure = Mathf.Max(0f, _currentAirPressure - amount);
            _isDepleted         = _currentAirPressure <= 0f;

            WriteAtom();

            // Announce the nominal spend for floating-text / juice (skip no-op calls)
            if (amount > 0f) onAirSpent?.Invoke(amount);

            if (!wasDepletedBefore && _isDepleted)
                onO2Depleted?.Invoke();
        }

        /** Instantly fills air to max. Useful for boss transitions or debug. */
        public void RefillAir()
        {
            _currentAirPressure  = MaxAirMod;
            _isDepleted          = false;
            _pendingHealthDamage = 0f;
            WriteAtom();
        }

        // -------------------------------------------------------
        // Internal
        // -------------------------------------------------------

        private void WriteAtom()
        {
            if (currentO2 != null) currentO2.Value = _currentAirPressure;
        }

        // -------------------------------------------------------
        // Debug GUI
        // -------------------------------------------------------

        /**
         * Draws the on-screen air-tank debug overlay: current pressure / max,
         * plus a full breakdown of the decay rate as it is actually applied —
         * effective = (base × exertion + mining extra) × depth.
         */
        private void OnGUI()
        {
            if (!Application.isPlaying || !showDebugGUI) return;

            const float x   = 10f;
            const float y0  = 10f;
            const float w   = 290f;
            const float h   = 118f;
            const float pad = 8f;
            const float bar = 22f;

            // Panel background
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(x, y0, w, h), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float y  = y0 + pad;
            float bx = x + pad;
            float bw = w - pad * 2f;

            // Title
            GUI.Label(new Rect(bx, y, bw, 18f), "<b>[ O2 System ]</b>");
            y += 22f;

            // ── Air pressure bar ──
            float pct = MaxAirMod > 0f ? _currentAirPressure / MaxAirMod : 0f;
            GUI.Label(new Rect(bx, y, bw, 16f),
                $"Air:  {_currentAirPressure:F1} / {MaxAirMod:F0}");
            y += 17f;

            // Track
            GUI.color = new Color(0.15f, 0.15f, 0.15f);
            GUI.DrawTexture(new Rect(bx, y, bw, bar), Texture2D.whiteTexture);

            // Fill — green → yellow → red
            GUI.color = pct > 0.5f
                ? Color.Lerp(Color.yellow, Color.green, (pct - 0.5f) * 2f)
                : Color.Lerp(Color.red, Color.yellow, pct * 2f);
            GUI.DrawTexture(new Rect(bx, y, bw * pct, bar), Texture2D.whiteTexture);
            GUI.color = Color.white;
            y += bar + pad;

            // ── Decay headline ──
            GUI.Label(new Rect(bx, y, bw, 16f),
                $"<b>Decay {EffectiveDecayRate:F1}/s</b>");
            y += 17f;

            // ── Breakdown ──
            float exertMult = 1f;
            if (IsThrustingLateral)  exertMult = Mathf.Max(exertMult, LateralExertionMod);
            if (IsThrustingVertical) exertMult = Mathf.Max(exertMult, VerticalExertionMod);
            if (IsMining)            exertMult = Mathf.Max(exertMult, LateralExertionMod);
            string mineExtra = (IsMining && miningExtraDecayRate > 0f)
                ? $" +{miningExtraDecayRate:F1} mine" : "";

            GUI.Label(new Rect(bx, y, bw, 16f),
                $"base {BaseDecayMod:F1} · exert ×{exertMult:F1}{mineExtra} · depth ×{DepthMultiplier:F2}");
            y += 17f;

            // ── Live exertion flags ──
            float thirdW = bw / 3f;
            GUI.color = IsThrustingLateral ? Color.cyan : new Color(0.4f, 0.4f, 0.4f);
            GUI.Label(new Rect(bx, y, thirdW, 16f), IsThrustingLateral ? "▶ LAT" : "· lat");
            GUI.color = IsThrustingVertical ? Color.cyan : new Color(0.4f, 0.4f, 0.4f);
            GUI.Label(new Rect(bx + thirdW, y, thirdW, 16f), IsThrustingVertical ? "▶ VERT" : "· vert");
            GUI.color = IsMining ? Color.magenta : new Color(0.4f, 0.4f, 0.4f);
            GUI.Label(new Rect(bx + thirdW * 2f, y, thirdW, 16f), IsMining ? "▶ MINE" : "· mine");
            GUI.color = Color.white;
        }

        // -------------------------------------------------------
        // Editor Utilities
        // -------------------------------------------------------

#if UNITY_EDITOR
        [FoldoutGroup("Debug")]
        [Button("Drain All Air"), GUIColor(1f, 0.4f, 0.2f)]
        private void DebugDrainAll()
        {
            if (!Application.isPlaying) { Debug.Log("[O2System] Play mode only."); return; }
            ConsumeAir(_currentAirPressure);
        }

        [FoldoutGroup("Debug")]
        [Button("Refill Air"), GUIColor(0.4f, 0.8f, 1f)]
        private void DebugRefill()
        {
            if (!Application.isPlaying) { Debug.Log("[O2System] Play mode only."); return; }
            RefillAir();
        }
#endif
    }
}
