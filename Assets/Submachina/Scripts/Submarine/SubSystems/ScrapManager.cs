using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Manages the player's banked scrap metal — a consumable resource earned
     * from mining that can be spent to repair the submarine's hull (restore health).
     *
     * Scrap is awarded via a chance roll in MiningResource each time a node is
     * collected. The player banks it and spends it manually via the UseScrap
     * input action.
     *
     * Place on the GameManager. Assign the UseScrap InputAction from the player's
     * Input Action Asset (the user wires this in the editor).
     */
    [UsesFeedbacks(nameof(SubFeedbacks.ScrapAdded), nameof(SubFeedbacks.ScrapFull),
                   nameof(SubFeedbacks.ScrapUsed), nameof(SubFeedbacks.NoScrap), nameof(SubFeedbacks.FullHealth))]
    public class ScrapManager : InputSubmarineComponent
    {
        // =====================
        // Settings
        // =====================

        [FoldoutGroup("Settings")]
        [Tooltip("HP restored when one scrap is consumed to repair the hull.")]
        [SerializeField, Min(1)] private int healPerScrap = 20;

        [FoldoutGroup("Settings")]
        [Tooltip("Maximum scrap that can be banked at once. Drops are ignored when at capacity. " +
                 "Example: 1 = player can only hold one scrap at a time.")]
        [SerializeField, Min(1)] private int maxScrap = 1;


        // =====================
        // Input
        // =====================

        [FoldoutGroup("Input")]
        [Tooltip("Button action that consumes one scrap to repair the hull. " +
                 "Assign from your Input Action Asset.")]
        [SerializeField] private InputActionReference useScrapAction;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired when one scrap is banked. Passes the new banked count.")]
        public UnityEvent<int> onScrapAdded;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when a scrap drop is ignored because the bank is already full.")]
        public UnityEvent onScrapFull;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when scrap is consumed to repair the hull. Passes the remaining banked count.")]
        public UnityEvent<int> onScrapUsed;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when the player tries to use scrap but has none banked.")]
        public UnityEvent onNoScrap;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when the player tries to use scrap but the hull is already at full integrity.")]
        public UnityEvent onFullHealth;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private int BankedScrap => _scrapCount;

        // =====================
        // State
        // =====================

        private int _scrapCount;

        /** Read by ScrapDisplay each frame to update the HUD. */
        public int ScrapCount => _scrapCount;

        /** Read by ScrapDisplay to know how many dot slots to render. */
        public int MaxScrap => maxScrap;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();
            RegisterAction(useScrapAction);
        }

        private void Update()
        {
            if (PrimaryAction != null && PrimaryAction.WasPressedThisFrame())
                UseScrap();
        }

        // -------------------------------------------------------
        // Public API
        // -------------------------------------------------------

        /**
         * Banks one scrap. Called by MiningResource when the drop chance
         * roll succeeds on collection.
         */
        public void AddScrap()
        {
            // Bank is full — signal the rejected drop so the scene can react (e.g. a "full" cue)
            if (_scrapCount >= maxScrap)
            {
                Sub?.Feedbacks?.Play(SubFeedbacks.ScrapFull, transform.position);
                onScrapFull?.Invoke();
                return;
            }

            // Bank the scrap and broadcast the new count for feedbacks and listeners
            _scrapCount++;
            Sub?.Feedbacks?.Play(SubFeedbacks.ScrapAdded, transform.position);
            onScrapAdded?.Invoke(_scrapCount);

            Debug.Log($"[ScrapManager] Scrap collected. Banked: {_scrapCount}/{maxScrap}");
        }

        // -------------------------------------------------------
        // Internal
        // -------------------------------------------------------

        /**
         * Consumes one banked scrap to repair the hull by healPerScrap HP.
         *
         * Blocked if:
         *   - No scrap is banked → plays noScrapFeedbacks
         *   - Hull is already at full integrity → plays fullHealthFeedbacks
         *
         * Example: healPerScrap=20, player at 55/100 HP → repaired to 75/100.
         */
        private void UseScrap()
        {
            // No scrap available
            if (_scrapCount <= 0)
            {
                Sub?.Feedbacks?.Play(SubFeedbacks.NoScrap, transform.position);
                onNoScrap?.Invoke();
                return;
            }

            // Hull already at full integrity — don't waste the scrap
            if (Sub?.Health != null && Sub?.Health.HealthPercent >= 1f)
            {
                Sub?.Feedbacks?.Play(SubFeedbacks.FullHealth, transform.position);
                onFullHealth?.Invoke();
                return;
            }

            // Spend the scrap, repair the hull, and broadcast the remaining count
            _scrapCount--;
            Sub?.Health?.Heal(healPerScrap);
            Sub?.Feedbacks?.Play(SubFeedbacks.ScrapUsed, transform.position);
            onScrapUsed?.Invoke(_scrapCount);

            Debug.Log($"[ScrapManager] Scrap used. Healed {healPerScrap} HP. Remaining: {_scrapCount}");
        }


        // -------------------------------------------------------
        // Editor Utilities
        // -------------------------------------------------------

#if UNITY_EDITOR
        [FoldoutGroup("Debug")]
        [Button("Add Scrap"), GUIColor(0.8f, 0.7f, 0.4f)]
        private void DebugAddScrap()
        {
            if (!Application.isPlaying) { Debug.Log("[ScrapManager] Play mode only."); return; }
            AddScrap();
        }

        [FoldoutGroup("Debug")]
        [Button("Use Scrap"), GUIColor(0.6f, 1f, 0.6f)]
        private void DebugUseScrap()
        {
            if (!Application.isPlaying) { Debug.Log("[ScrapManager] Play mode only."); return; }
            UseScrap();
        }
#endif
    }
}
