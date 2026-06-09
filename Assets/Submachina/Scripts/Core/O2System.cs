using UnityEngine;
using UnityEngine.Events;
using UnityAtoms.BaseAtoms;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Manages the player's oxygen supply — the core survival pressure of Submachina.
     *
     * O2 drains at a constant rate throughout the run. When it hits zero, it
     * begins bleeding the player's health instead. Killing enemies drops O2
     * bubbles that replenish the supply, tying combat directly to survival.
     *
     * The current O2 value is written to a FloatVariable Atom each frame so
     * the HUD bar and any other interested system can read it without coupling.
     *
     * Setup:
     *   - Add to the GameManager object alongside DepthTracker.
     *   - Assign the CurrentO2 FloatVariable atom (create one in Data/).
     *   - Assign the player's Health component so health bleed works.
     */
    public class O2System : MonoBehaviour
    {
        // =====================
        // O2 Settings
        // =====================

        [FoldoutGroup("O2 Settings")]
        [Tooltip("Maximum O2 the player can carry.")]
        [SerializeField, Min(1f)] private float maxO2 = 100f;

        [FoldoutGroup("O2 Settings")]
        [Tooltip("O2 units drained per second during normal play. " +
                 "Example: 5 = empty tank in 20 seconds at full capacity.")]
        [SerializeField, Min(0f)] private float drainRate = 5f;

        // =====================
        // Health Bleed
        // =====================

        [FoldoutGroup("Health Bleed")]
        [Tooltip("Health drained per second while O2 is empty.")]
        [SerializeField, Min(0f)] private float healthBleedRate = 8f;

        [FoldoutGroup("Health Bleed")]
        [Tooltip("The player's Health component. Receives damage when O2 runs out.")]
        [SerializeField] private Health playerHealth;

        // =====================
        // Atoms
        // =====================

        [FoldoutGroup("Atoms")]
        [Tooltip("Written each frame with the current O2 value (0 to maxO2). " +
                 "Read by the HUD bar and any other system that cares about O2.")]
        [SerializeField] private FloatVariable currentO2;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired when O2 first reaches zero.")]
        public UnityEvent onO2Depleted;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when O2 is replenished from zero back to above zero.")]
        public UnityEvent onO2Restored;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float O2Percent => maxO2 > 0 ? (_currentO2 / maxO2) * 100f : 0f;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private bool IsBleeding => _isDepleted;

        // =====================
        // State
        // =====================

        private float _currentO2;
        private bool _isDepleted;
        private float _pendingHealthDamage; // accumulates fractional damage to apply as whole numbers

        // Public accessor so O2Bar and other scripts can read max without an extra atom
        public float MaxO2 => maxO2;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            // Start with a full tank
            _currentO2 = maxO2;
            WriteAtom();
        }

        private void Update()
        {
            DrainO2();
            if (_isDepleted) BleedHealth();
        }

        // -------------------------------------------------------
        // Core
        // -------------------------------------------------------

        /**
         * Drains O2 by drainRate * deltaTime each frame.
         * Fires onO2Depleted the first time it hits zero and sets the bleed flag.
         * Fires onO2Restored if O2 rises back above zero after being depleted.
         */
        private void DrainO2()
        {
            bool wasDepletedBefore = _isDepleted;

            _currentO2 = Mathf.Max(0f, _currentO2 - drainRate * Time.deltaTime);
            _isDepleted = _currentO2 <= 0f;

            WriteAtom();

            // Fire depletion event on first crossing
            if (!wasDepletedBefore && _isDepleted)
                onO2Depleted?.Invoke();
        }

        /**
         * Accumulates fractional health damage from the bleed rate and applies
         * it as whole integer values to avoid calling TakeDamage every single frame.
         *
         * Example: bleedRate=8, deltaTime=0.016 → 0.128 damage/frame.
         *   After ~8 frames (~0.125s), 1 whole point of damage is applied.
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
         * Adds O2, clamped to maxO2. Called by O2Pickup when the player
         * collects a bubble dropped by an enemy.
         *
         * Also clears the depletion state and fires onO2Restored if coming
         * back from empty, resetting the health bleed accumulator.
         */
        public void AddO2(float amount)
        {
            bool wasDepletedBefore = _isDepleted;

            _currentO2 = Mathf.Min(maxO2, _currentO2 + amount);
            _isDepleted = _currentO2 <= 0f;
            _pendingHealthDamage = 0f;

            WriteAtom();

            if (wasDepletedBefore && !_isDepleted)
                onO2Restored?.Invoke();
        }

        /** Instantly fills O2 to max. Useful for boss transitions or debug. */
        public void RefillO2()
        {
            _currentO2 = maxO2;
            _isDepleted = false;
            _pendingHealthDamage = 0f;
            WriteAtom();
        }

        // -------------------------------------------------------
        // Internal
        // -------------------------------------------------------

        private void WriteAtom()
        {
            if (currentO2 != null) currentO2.Value = _currentO2;
        }

        // -------------------------------------------------------
        // Editor Utilities
        // -------------------------------------------------------

#if UNITY_EDITOR
        [FoldoutGroup("Debug")]
        [Button("Drain All O2"), GUIColor(1f, 0.4f, 0.2f)]
        private void DebugDrainAll()
        {
            if (!Application.isPlaying) { Debug.Log("[O2System] Play mode only."); return; }
            _currentO2 = 0f;
            _isDepleted = true;
            WriteAtom();
            onO2Depleted?.Invoke();
        }

        [FoldoutGroup("Debug")]
        [Button("Refill O2"), GUIColor(0.4f, 0.8f, 1f)]
        private void DebugRefill()
        {
            if (!Application.isPlaying) { Debug.Log("[O2System] Play mode only."); return; }
            RefillO2();
        }
#endif
    }
}
