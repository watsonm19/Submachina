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
    public class O2System : MonoBehaviour
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

        private void Awake()
        {
            _currentMaxAir      = maxAirPressure;
            _currentAirPressure = maxAirPressure;
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
