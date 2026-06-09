using UnityEngine;
using UnityEngine.Events;
using UnityAtoms.BaseAtoms;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Owns the act structure — the pacing spine of a full Submachina run.
     *
     * Each act is a countdown. When the timer expires the boss spawns regardless
     * of where the player is. If the player reaches the act's depth bonus threshold
     * before the timer expires, onDepthBonusEarned fires — wire this to award a
     * bonus upgrade, resource points, or any other reward.
     *
     * Flow per act:
     *   1. Timer counts down from actDuration.
     *   2. If player depth >= DepthBonusThreshold → onDepthBonusEarned (once per act).
     *   3. Timer hits zero → onBossSpawn fires. Timer pauses (boss phase).
     *   4. External caller (boss script) calls CompleteAct() on boss death.
     *   5. Act number increments, timer resets, next act begins.
     *
     * Place on GameManager. Wire onBossSpawn to your boss spawner (task 9).
     */
    public class ActManager : MonoBehaviour
    {
        // =====================
        // Act Settings
        // =====================

        [FoldoutGroup("Act Settings")]
        [Tooltip("Duration of each act in seconds. 420 = 7 minutes, matching EiC's pacing.")]
        [SerializeField, Min(10f)] private float actDuration = 420f;

        [FoldoutGroup("Act Settings")]
        [Tooltip("Total number of acts before the final boss. After this act count, onFinalBoss fires instead.")]
        [SerializeField, Min(1)] private int totalActs = 3;

        // =====================
        // Depth Bonus
        // =====================

        [FoldoutGroup("Depth Bonus")]
        [Tooltip("Depth in metres required to earn the bonus in act 1.")]
        [SerializeField, Min(0f)] private float depthBonusBaseThreshold = 50f;

        [FoldoutGroup("Depth Bonus")]
        [Tooltip("Additional depth required per subsequent act. " +
                 "Example: base=50, increment=75 → act 2 needs 125m, act 3 needs 200m.")]
        [SerializeField, Min(0f)] private float depthBonusIncrement = 75f;

        // =====================
        // Atoms
        // =====================

        [FoldoutGroup("Atoms")]
        [Tooltip("CurrentDepth atom from DepthTracker. Checked each frame against the bonus threshold.")]
        [SerializeField] private FloatVariable currentDepth;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired when the act timer expires. Wire to your boss spawner.")]
        public UnityEvent<int> onBossSpawn;

        [FoldoutGroup("Events")]
        [Tooltip("Fired once per act when the player reaches the depth bonus threshold before time runs out.")]
        public UnityEvent<int> onDepthBonusEarned;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when all acts are complete and the final boss should spawn.")]
        public UnityEvent onFinalBoss;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when an act completes (boss defeated) and the next act begins.")]
        public UnityEvent<int> onActStarted;

        // =====================
        // Debug / State
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private int CurrentAct => _currentAct;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float TimeRemaining => _timeRemaining;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private string Phase => _phase.ToString();

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float DepthBonusThreshold => depthBonusBaseThreshold + ((_currentAct - 1) * depthBonusIncrement);

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private bool DepthBonusEarned => _depthBonusEarned;

        // Public read for HUD
        public float ActDuration => actDuration;
        public float RemainingTime => _timeRemaining;
        public int Act => _currentAct;

        // =====================
        // State
        // =====================

        private enum ActPhase { Counting, BossActive, RunComplete }

        private ActPhase _phase = ActPhase.Counting;
        private int _currentAct = 1;
        private float _timeRemaining;
        private bool _depthBonusEarned;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            _timeRemaining = actDuration;
        }

        private void Start()
        {
            onActStarted?.Invoke(_currentAct);
        }

        private void Update()
        {
            if (_phase != ActPhase.Counting) return;

            CheckDepthBonus();
            TickTimer();
        }

        // -------------------------------------------------------
        // Core
        // -------------------------------------------------------

        /**
         * Checks whether the player has reached the depth bonus threshold
         * for the current act. Fires onDepthBonusEarned once per act.
         */
        private void CheckDepthBonus()
        {
            if (_depthBonusEarned || currentDepth == null) return;
            if (currentDepth.Value < DepthBonusThreshold) return;

            _depthBonusEarned = true;
            Debug.Log($"[ActManager] Depth bonus earned! Act {_currentAct}, depth {currentDepth.Value:F0}m.");
            onDepthBonusEarned?.Invoke(_currentAct);
        }

        /**
         * Counts the timer down each frame. When it reaches zero, transitions
         * to BossActive phase and fires the appropriate spawn event.
         */
        private void TickTimer()
        {
            _timeRemaining -= Time.deltaTime;
            if (_timeRemaining > 0f) return;

            _timeRemaining = 0f;
            _phase = ActPhase.BossActive;

            if (_currentAct >= totalActs)
            {
                Debug.Log("[ActManager] Final boss triggered.");
                onFinalBoss?.Invoke();
            }
            else
            {
                Debug.Log($"[ActManager] Act {_currentAct} boss spawning.");
                onBossSpawn?.Invoke(_currentAct);
            }
        }

        // -------------------------------------------------------
        // Public API
        // -------------------------------------------------------

        /**
         * Called by the boss script on death to close the current act
         * and begin the next one. Resets the timer and depth bonus flag.
         */
        public void CompleteAct()
        {
            if (_phase == ActPhase.RunComplete) return;

            _currentAct++;
            _timeRemaining = actDuration;
            _depthBonusEarned = false;
            _phase = ActPhase.Counting;

            Debug.Log($"[ActManager] Act {_currentAct} started.");
            onActStarted?.Invoke(_currentAct);
        }

        // -------------------------------------------------------
        // Editor Utilities
        // -------------------------------------------------------

#if UNITY_EDITOR
        [FoldoutGroup("Debug")]
        [Button("Skip to Boss"), GUIColor(1f, 0.5f, 0.2f)]
        private void DebugSkipToBoss()
        {
            if (!Application.isPlaying) { Debug.Log("[ActManager] Play mode only."); return; }
            _timeRemaining = 0f;
        }

        [FoldoutGroup("Debug")]
        [Button("Award Depth Bonus"), GUIColor(1f, 0.9f, 0.2f)]
        private void DebugAwardBonus()
        {
            if (!Application.isPlaying) { Debug.Log("[ActManager] Play mode only."); return; }
            if (_depthBonusEarned) { Debug.Log("[ActManager] Already earned this act."); return; }
            _depthBonusEarned = true;
            onDepthBonusEarned?.Invoke(_currentAct);
        }

        [FoldoutGroup("Debug")]
        [Button("Complete Act"), GUIColor(0.6f, 1f, 0.6f)]
        private void DebugCompleteAct()
        {
            if (!Application.isPlaying) { Debug.Log("[ActManager] Play mode only."); return; }
            CompleteAct();
        }
#endif
    }
}
