using UnityEngine;
using UnityEngine.Events;
using UnityAtoms.BaseAtoms;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Tracks collected mining resources and triggers level ups when the
     * threshold is reached — the roguelite progression spine of Submachina.
     *
     * Each level up fires onLevelUp for the upgrade draft UI to handle,
     * resets the resource counter, and increases the threshold for the
     * next level so runs get progressively harder to advance.
     *
     * The current resource value is written to a FloatVariable Atom so
     * the HUD bar can display progress without coupling to this manager.
     *
     * Place on the GameManager object.
     */
    public class ResourceManager : MonoBehaviour
    {
        // =====================
        // Progression Settings
        // =====================

        [FoldoutGroup("Progression")]
        [Tooltip("Resources required to reach level 1.")]
        [SerializeField, Min(1f)] private float baseThreshold = 50f;

        [FoldoutGroup("Progression")]
        [Tooltip("Additional resources required per subsequent level. " +
                 "Example: base=50, increment=25 → level 2 needs 75, level 3 needs 100.")]
        [SerializeField, Min(0f)] private float thresholdIncrement = 25f;

        // =====================
        // Atoms
        // =====================

        [FoldoutGroup("Atoms")]
        [Tooltip("Written each time resources are added. Stores current resources toward next level (0 to threshold).")]
        [SerializeField] private FloatVariable currentResources;

        [FoldoutGroup("Atoms")]
        [Tooltip("Written on level up with the new threshold value. ResourceBar uses this to normalise the fill.")]
        [SerializeField] private FloatVariable resourceThreshold;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired when the resource threshold is reached. Wire to the upgrade draft UI.")]
        public UnityEvent<int> onLevelUp;

        // =====================
        // Debug / State
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private int _currentLevel;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float _currentResources;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float CurrentThreshold => baseThreshold + (_currentLevel * thresholdIncrement);

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float ProgressPercent => CurrentThreshold > 0f ? (_currentResources / CurrentThreshold) * 100f : 0f;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            WriteAtoms();
        }

        // -------------------------------------------------------
        // Public API
        // -------------------------------------------------------

        /**
         * Adds resources to the current pool and checks if the level up
         * threshold has been crossed. Called by MiningResource pickups.
         *
         * If the threshold is crossed, excess resources carry over to the
         * next level so rapid collection doesn't waste progress.
         *
         * Example: threshold=50, current=45, pickup adds 10 → levels up,
         *   carries 5 resources into the next level.
         */
        public void AddResources(float amount)
        {
            _currentResources += amount;

            while (_currentResources >= CurrentThreshold)
            {
                _currentResources -= CurrentThreshold;
                LevelUp();
            }

            WriteAtoms();
        }

        // -------------------------------------------------------
        // Internal
        // -------------------------------------------------------

        /**
         * Increments the level, fires the level up event, and updates
         * the threshold atom so the HUD bar rescales for the new level.
         */
        private void LevelUp()
        {
            _currentLevel++;
            onLevelUp?.Invoke(_currentLevel);
            WriteAtoms();

            Debug.Log($"[ResourceManager] Level up! Now level {_currentLevel}. Next threshold: {CurrentThreshold}");
        }

        private void WriteAtoms()
        {
            if (currentResources != null) currentResources.Value = _currentResources;
            if (resourceThreshold != null) resourceThreshold.Value = CurrentThreshold;
        }

        // -------------------------------------------------------
        // Editor Utilities
        // -------------------------------------------------------

#if UNITY_EDITOR
        [FoldoutGroup("Debug")]
        [Button("Add 10 Resources"), GUIColor(1f, 0.85f, 0.2f)]
        private void DebugAdd10()
        {
            if (!Application.isPlaying) { Debug.Log("[ResourceManager] Play mode only."); return; }
            AddResources(10f);
        }

        [FoldoutGroup("Debug")]
        [Button("Fill to Level Up"), GUIColor(0.6f, 1f, 0.6f)]
        private void DebugFillToLevelUp()
        {
            if (!Application.isPlaying) { Debug.Log("[ResourceManager] Play mode only."); return; }
            AddResources(CurrentThreshold - _currentResources);
        }
#endif
    }
}
