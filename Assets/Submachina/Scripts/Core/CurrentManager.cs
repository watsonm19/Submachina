using UnityEngine;
using UnityAtoms.BaseAtoms;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Owns and drives the runtime value of the currentDescentSpeed Unity Atom.
     *
     * Acts as the single source of truth for ocean current strength. All other
     * systems (SubmarinePhysicsController, parallax layers, UI indicators) should
     * read currentDescentSpeed.Value rather than referencing this manager directly —
     * the Atom is the public interface; this class is the writer.
     *
     * Progression model:
     *   - Baseline speed applies at run start (tier 0).
     *   - Calling AdvanceTier() increases the target speed by speedPerTier.
     *   - A smooth lerp transitions the Atom value so the current feels like it
     *     builds rather than teleports. Disable smoothing for instant shifts.
     *   - AddSpeedBoost() applies temporary flat deltas on top of tier speed
     *     (e.g., for hazard zones). Call with a negative value to cancel.
     *
     * Place one CurrentManager in each gameplay scene. No singleton — the Atom
     * itself persists across scenes if marked as a ScriptableObject asset.
     */
    public class CurrentManager : MonoBehaviour
    {
        // =====================
        // Speed Settings
        // =====================

        [FoldoutGroup("Current Settings")]
        [Tooltip("Descent speed at the start of a run (tier 0). Units are force-equivalent — " +
                 "tuned against SubmarinePhysicsController.currentForceMultiplier.")]
        [SerializeField, Min(0f)] private float baselineSpeed = 3f;

        [FoldoutGroup("Current Settings")]
        [Tooltip("Speed added per progression tier. " +
                 "Example: baseline=3, perTier=1.5 → tier 2 target = 6.0")]
        [SerializeField, Min(0f)] private float speedPerTier = 1.5f;

        [FoldoutGroup("Current Settings")]
        [Tooltip("Absolute ceiling on descent speed regardless of tier or boosts.")]
        [SerializeField, Min(0f)] private float maxDescentSpeed = 20f;

        // =====================
        // Transition Settings
        // =====================

        [FoldoutGroup("Transitions")]
        [Tooltip("Lerps the Atom toward its target speed rather than snapping. " +
                 "Makes current feel like it physically builds and eases.")]
        [SerializeField] private bool smoothTransitions = true;

        [FoldoutGroup("Transitions")]
        [ShowIf("smoothTransitions")]
        [Tooltip("Lerp rate toward target speed. Higher = snappier transition. " +
                 "Example: rate=2 reaches ~86% of target in ~1 second.")]
        [SerializeField, Min(0.1f)] private float transitionRate = 2f;

        // =====================
        // Atoms
        // =====================

        [FoldoutGroup("Atoms")]
        [Tooltip("The shared FloatVariable written each frame. Assign the asset from your project.")]
        [SerializeField] private FloatVariable currentDescentSpeed;

        // =====================
        // Read-Only State
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private int _currentTier;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float _tierSpeed;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float _boostDelta;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float TargetSpeed => Mathf.Clamp(_tierSpeed + _boostDelta, 0f, maxDescentSpeed);

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            // Seed the Atom immediately so subscribers start with a valid value
            // before the first Update drives the lerp
            _tierSpeed = baselineSpeed;
            WriteAtom(TargetSpeed);
        }

        private void Update()
        {
            DriveDescentSpeed();
        }

        // -------------------------------------------------------
        // Core
        // -------------------------------------------------------

        /**
         * Each frame, drives the Atom value toward TargetSpeed.
         *
         * Lerp produces an exponential approach: each tick closes a fraction of
         * the remaining gap, so fast transitions feel punchy and slow ones feel
         * like building pressure. Snap mode is available for instant zone changes.
         *
         * Example at rate=2, gap=5: after 0.5s → ~3.16, after 1s → ~3.93
         */
        private void DriveDescentSpeed()
        {
            if (currentDescentSpeed == null) return;

            float next = smoothTransitions
                ? Mathf.Lerp(currentDescentSpeed.Value, TargetSpeed, Time.deltaTime * transitionRate)
                : TargetSpeed;

            WriteAtom(next);
        }

        /** Writes the value to the Atom. Centralised so we can add logging/events here later. */
        private void WriteAtom(float value)
        {
            if (currentDescentSpeed == null) return;
            currentDescentSpeed.Value = value;
        }

        // -------------------------------------------------------
        // Public API
        // -------------------------------------------------------

        /**
         * Advances to the next progression tier, increasing the target speed.
         * Call when the player crosses a depth threshold or triggers an
         * environmental event (e.g., entering a trench or pressure zone).
         */
        public void AdvanceTier()
        {
            _currentTier++;
            RecalculateTierSpeed();
        }

        /**
         * Jumps directly to a specific tier, bypassing intermediate steps.
         * Useful for scene-load restores, difficulty presets, or debug overrides.
         */
        public void SetTier(int tier)
        {
            _currentTier = Mathf.Max(0, tier);
            RecalculateTierSpeed();
        }

        /**
         * Applies a temporary flat delta on top of the tier-based speed.
         * The caller is responsible for reversing the delta when the condition ends.
         *
         * Example (hazard zone entry/exit):
         *   OnEnterVent()  → AddSpeedBoost(+5f)
         *   OnExitVent()   → AddSpeedBoost(-5f)
         */
        public void AddSpeedBoost(float delta)
        {
            _boostDelta += delta;
        }

        /** Clears all active boosts, reverting to the current tier's base speed. */
        public void ResetBoosts()
        {
            _boostDelta = 0f;
        }

        // -------------------------------------------------------
        // Internal
        // -------------------------------------------------------

        /**
         * Derives _tierSpeed from the current tier index.
         * Formula: baseline + (tier × speedPerTier), clamped to maxDescentSpeed.
         * Example: baseline=3, perTier=1.5, tier=2 → 3 + 3 = 6.0
         */
        private void RecalculateTierSpeed()
        {
            _tierSpeed = Mathf.Clamp(
                baselineSpeed + (_currentTier * speedPerTier),
                0f,
                maxDescentSpeed);
        }

        // -------------------------------------------------------
        // Editor Utilities
        // -------------------------------------------------------

#if UNITY_EDITOR
        [FoldoutGroup("Debug")]
        [Button("Advance Tier"), GUIColor(1f, 0.8f, 0.2f)]
        private void DebugAdvanceTier()
        {
            if (!Application.isPlaying) { Debug.Log("[CurrentManager] Play mode only."); return; }
            AdvanceTier();
            Debug.Log($"[CurrentManager] Tier → {_currentTier} | target speed = {TargetSpeed:F2}");
        }

        [FoldoutGroup("Debug")]
        [Button("Reset to Tier 0"), GUIColor(0.6f, 0.8f, 1f)]
        private void DebugResetTier()
        {
            if (!Application.isPlaying) { Debug.Log("[CurrentManager] Play mode only."); return; }
            ResetBoosts();
            SetTier(0);
            Debug.Log($"[CurrentManager] Reset → tier 0, target speed = {TargetSpeed:F2}");
        }

        [FoldoutGroup("Debug")]
        [Button("+5 Temporary Boost"), GUIColor(1f, 0.5f, 0.5f)]
        private void DebugAddBoost()
        {
            if (!Application.isPlaying) { Debug.Log("[CurrentManager] Play mode only."); return; }
            AddSpeedBoost(5f);
            Debug.Log($"[CurrentManager] +5 boost | target speed = {TargetSpeed:F2}");
        }

        [FoldoutGroup("Debug")]
        [Button("Clear Boosts"), GUIColor(0.6f, 1f, 0.6f)]
        private void DebugClearBoosts()
        {
            if (!Application.isPlaying) { Debug.Log("[CurrentManager] Play mode only."); return; }
            ResetBoosts();
            Debug.Log($"[CurrentManager] Boosts cleared | target speed = {TargetSpeed:F2}");
        }
#endif
    }
}
