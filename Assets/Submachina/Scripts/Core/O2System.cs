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
     *   - Current air pressure and dynamic max capacity
     *   - Passive decay (scaled by exertion and depth)
     *   - Max capacity degradation (only restored by O2 bubble pickups)
     *   - Health bleed when air hits zero
     *   - The CurrentO2 atom written to the HUD each frame
     *
     * External systems interact via:
     *   - IsThrusting / IsMining flags (set by SubmarinePhysicsController and MiningLaser)
     *   - AddAir / ConsumeAir / RestoreCapacity (called by O2Pickup and abilities)
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
        [Tooltip("Rate at which max capacity shrinks per second. " +
                 "Only O2 bubble pickups can restore it. Example: 0.5 → max drops by 30 over a minute.")]
        [SerializeField, Min(0f)] private float maxCapacityDecayRate = 0.5f;

        [FoldoutGroup("Air Capacity")]
        [Tooltip("Floor for max air capacity — never decays below this value.")]
        [SerializeField, Min(1f)] private float minMaxCapacity = 20f;

        // =====================
        // Decay
        // =====================

        [FoldoutGroup("Decay")]
        [Tooltip("Air units lost per second at rest. " +
                 "Example: 3 → fully drained in ~33 seconds at full capacity.")]
        [SerializeField, Min(0f)] private float baseDecayRate = 3f;

        [FoldoutGroup("Decay")]
        [Tooltip("Multiplier on decay when IsThrusting or IsMining is true. " +
                 "Example: 3× → drains 3× faster under exertion (~11 seconds from full).")]
        [SerializeField, Min(1f)] private float exertionDecayMultiplier = 3f;

        [FoldoutGroup("Decay")]
        [Tooltip("Extra flat air drained per second while mining, on top of exertion decay. " +
                 "Example: 2 → mining drains 2 additional units/s regardless of base rate.")]
        [SerializeField, Min(0f)] private float miningExtraDecayRate = 2f;

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
        // Atoms
        // =====================

        [FoldoutGroup("Atoms")]
        [Tooltip("Written each frame with the current air pressure. " +
                 "Read by O2Bar and any other system that cares about O2.")]
        [SerializeField] private FloatVariable currentO2;

        [FoldoutGroup("Atoms")]
        [Tooltip("Written when max capacity changes. Read by O2Bar for the capacity ghost bar.")]
        [SerializeField] private FloatVariable maxAirCapacity;

        [FoldoutGroup("Atoms")]
        [Tooltip("Written once at startup with the original ceiling. Read by O2Bar for normalisation.")]
        [SerializeField] private FloatVariable originalMaxAir;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired when air first reaches zero.")]
        public UnityEvent onO2Depleted;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when air is restored from zero back above zero.")]
        public UnityEvent onO2Restored;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float AirPercent => maxAirPressure > 0 ? (_currentAirPressure / maxAirPressure) * 100f : 0f;

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

        /** Set true by SubmarinePhysicsController while thrust input is active. */
        public bool IsThrusting { get; set; }

        /** Set true by MiningLaser while the laser is actively firing. */
        public bool IsMining { get; set; }

        // =====================
        // Public Properties
        // =====================

        public float CurrentAirPressure => _currentAirPressure;
        public float MaxAir             => _currentMaxAir;
        public float OriginalMaxAir     => maxAirPressure;

        /**
         * Active decay rate accounting for exertion state — read by HUD and debug displays.
         * Example: baseDecay=3, exertionMult=3, mining → 3×3 + 2 = 11/s
         */
        public float ActiveDecayRate =>
            baseDecayRate * (IsMining || IsThrusting ? exertionDecayMultiplier : 1f)
            + (IsMining ? miningExtraDecayRate : 0f);

        // =====================
        // Internal State
        // =====================

        private float _currentAirPressure;
        private float _currentMaxAir;
        private bool  _isDepleted;
        private float _pendingHealthDamage;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();
            _currentMaxAir      = maxAirPressure;
            _currentAirPressure = maxAirPressure;
            if (originalMaxAir != null) originalMaxAir.Value = maxAirPressure;
            WriteAtom();
        }

        private void Update()
        {
            DecayMaxCapacity();
            DecayAirPressure();
            if (_isDepleted) BleedHealth();
        }

        // -------------------------------------------------------
        // Core
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

            // Pull current pressure down if it exceeds the new ceiling
            if (_currentAirPressure > _currentMaxAir)
            {
                _currentAirPressure = _currentMaxAir;
                WriteAtom();
            }
        }

        /**
         * Drains air each frame at the exertion-scaled and depth-scaled rate.
         * Fires onO2Depleted the first time air hits zero.
         *
         * Example: baseDecayRate=3, exertionMult=3, depth=100m, drainPerMetre=0.005
         *   → at rest:       3 × 1.5  = 4.5/s
         *   → while thrusting: 9 × 1.5 = 13.5/s
         *   → while mining:   11 × 1.5 = 16.5/s  (9 + 2 extra, × depth mult)
         */
        private void DecayAirPressure()
        {
            if (_currentAirPressure <= 0f) return;

            bool wasDepletedBefore = _isDepleted;

            _currentAirPressure -= ActiveDecayRate * DepthMultiplier * Time.deltaTime;
            _currentAirPressure  = Mathf.Max(0f, _currentAirPressure);
            _isDepleted          = _currentAirPressure <= 0f;

            WriteAtom();

            if (!wasDepletedBefore && _isDepleted)
                onO2Depleted?.Invoke();
        }

        /**
         * Accumulates fractional health damage and applies it as whole integers
         * to avoid calling TakeDamage every single frame.
         * Example: bleedRate=8, deltaTime=0.016 → 1 HP applied every ~8 frames.
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

        // -------------------------------------------------------
        // Public API
        // -------------------------------------------------------

        /**
         * Adds air, clamped to the current dynamic max capacity.
         * Clears the depletion state and fires onO2Restored if returning from empty.
         * Called by O2 bubble pickups and the manual pump.
         */
        public void AddAir(float amount)
        {
            bool wasDepletedBefore = _isDepleted;

            _currentAirPressure  = Mathf.Min(_currentMaxAir, _currentAirPressure + amount);
            _isDepleted          = _currentAirPressure <= 0f;
            _pendingHealthDamage = 0f;

            WriteAtom();

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

            if (!wasDepletedBefore && _isDepleted)
                onO2Depleted?.Invoke();
        }

        /**
         * Raises the max air capacity by amount, clamped to the original maxAirPressure ceiling.
         * Called by O2 bubble pickups — the only way to push the capacity back up.
         */
        public void RestoreCapacity(float amount)
        {
            _currentMaxAir = Mathf.Min(maxAirPressure, _currentMaxAir + amount);
        }

        /** Instantly fills air to max. Useful for boss transitions or debug. */
        public void RefillAir()
        {
            _currentAirPressure  = _currentMaxAir;
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
            if (maxAirCapacity != null) maxAirCapacity.Value = _currentMaxAir;
        }

        // -------------------------------------------------------
        // Debug GUI
        // -------------------------------------------------------

        /**
         * Draws the on-screen air-tank debug overlay: current pressure / live max
         * capacity, plus a full breakdown of the decay rate as it is actually
         * applied — effective = (base × exertion + mining extra) × depth.
         *
         * Reads this component's own authoritative state, so the headline number
         * always matches what drains the tank, and the live THRUST / MINING flags
         * make a "stuck" rate self-diagnosing (e.g. an unwired exertion source).
         */
        private void OnGUI()
        {
            if (!Application.isPlaying || !showDebugGUI) return;

            const float x   = 10f;
            const float y0  = 10f;
            const float w   = 290f;
            const float h   = 136f;
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

            // ── Air pressure bar (filled against the live max capacity) ──
            float pct = _currentMaxAir > 0f ? _currentAirPressure / _currentMaxAir : 0f;
            GUI.Label(new Rect(bx, y, bw, 16f),
                $"Air:  {_currentAirPressure:F1} / {_currentMaxAir:F0}   (cap ceiling {maxAirPressure:F0})");
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

            // ── Decay headline: the rate actually being applied this frame ──
            GUI.Label(new Rect(bx, y, bw, 16f),
                $"<b>Decay {EffectiveDecayRate:F1}/s</b>");
            y += 17f;

            // ── Breakdown: base × exertion (+ mining extra) × depth ──
            bool  exerting  = IsThrusting || IsMining;
            float exertMult = exerting ? exertionDecayMultiplier : 1f;
            string mineExtra = (IsMining && miningExtraDecayRate > 0f)
                ? $" +{miningExtraDecayRate:F1} mine" : "";

            GUI.Label(new Rect(bx, y, bw, 16f),
                $"base {baseDecayRate:F1} · exert ×{exertMult:F1}{mineExtra} · depth ×{DepthMultiplier:F2}");
            y += 17f;

            // ── Live exertion flags — a rate stuck at base means neither is firing ──
            GUI.color = IsThrusting ? Color.cyan : new Color(0.4f, 0.4f, 0.4f);
            GUI.Label(new Rect(bx, y, bw * 0.5f, 16f), IsThrusting ? "▶ THRUST" : "· thrust");
            GUI.color = IsMining ? Color.magenta : new Color(0.4f, 0.4f, 0.4f);
            GUI.Label(new Rect(bx + bw * 0.5f, y, bw * 0.5f, 16f), IsMining ? "▶ MINING" : "· mining");
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
