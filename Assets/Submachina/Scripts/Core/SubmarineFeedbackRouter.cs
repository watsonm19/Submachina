using System;
using System.Collections.Generic;
using UnityEngine;
using MoreMountains.Feedbacks;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Central switchboard for all submarine feedback effects.
     *
     * Systems trigger feedbacks by semantic key (SubFeedback enum) through
     * Sub.Feedbacks.Play / Stop — they never hold direct MMF_Player references.
     * All wiring lives in this one component's Inspector, making it easy to
     * tune, reorder, and swap feedback players without touching gameplay code.
     *
     * At runtime a dictionary is built from the serialized mappings array
     * for O(1) lookup. Duplicate keys are warned about and last-wins.
     *
     * Setup:
     *   1. Add to the submarine root (or a child under it).
     *   2. In the Mappings list, add one entry per SubFeedback key.
     *   3. Drag MMF_Player GameObjects into each entry's Players array.
     */
    public class SubmarineFeedbackRouter : SubmarineComponent
    {
        // =====================
        // Mapping Definition
        // =====================

        [Serializable]
        public struct FeedbackMapping
        {
            [HorizontalGroup("Row"), LabelWidth(120)]
            public SubFeedback feedback;

            [HorizontalGroup("Row")]
            public MMF_Player[] players;
        }

        [FoldoutGroup("Mappings")]
        [ListDrawerSettings(ShowPaging = false)]
        [Tooltip("Map each SubFeedback key to one or more MMF_Players. " +
                 "Systems call Sub.Feedbacks.Play(key) — they never reference MMF_Players directly.")]
        [SerializeField] private FeedbackMapping[] mappings;

        // =====================
        // Editor Utilities
        // =====================

        /**
         * Adds an empty mapping entry for every SubFeedback enum value that
         * isn't already mapped, so the full set of feedback keys is always
         * represented in the Inspector (even before players are wired up).
         *
         * Existing entries are preserved; only missing keys are appended.
         * New entries are sorted into enum-declaration order for readability.
         */
        [FoldoutGroup("Mappings")]
        [Button(ButtonSizes.Medium, Icon = SdfIconType.PlusSquare), GUIColor(0.6f, 0.9f, 0.7f)]
        [Tooltip("Append an empty mapping for any SubFeedback key not already present.")]
        private void AddMissingMappings()
        {
            // Collect the keys we already have so we skip them.
            var existing = new HashSet<SubFeedback>();
            if (mappings != null)
                foreach (var m in mappings)
                    existing.Add(m.feedback);

            // Build a new list: keep existing entries, then append any missing keys.
            // Re-instantiate any null/empty players array so each row owns a distinct
            // instance — prevents Odin's "Reference to ..." duplicate-reference lock.
            var result = new List<FeedbackMapping>();
            if (mappings != null)
            {
                foreach (var m in mappings)
                {
                    var fixedEntry = m;
                    if (fixedEntry.players == null || fixedEntry.players.Length == 0)
                        fixedEntry.players = new MMF_Player[0];
                    result.Add(fixedEntry);
                }
            }

            int addedCount = 0;
            foreach (SubFeedback key in Enum.GetValues(typeof(SubFeedback)))
            {
                if (existing.Contains(key)) continue;
                // Each entry needs its OWN array instance — a shared cached instance
                // (e.g. Array.Empty) makes Odin treat rows as duplicate references and lock them.
                result.Add(new FeedbackMapping { feedback = key, players = new MMF_Player[0] });
                addedCount++;
            }

            // Sort everything into enum-declaration order so the list reads cleanly.
            result.Sort((a, b) => ((int)a.feedback).CompareTo((int)b.feedback));
            mappings = result.ToArray();

#if UNITY_EDITOR
            // Persist the change so it survives domain reloads / scene saves.
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"[FeedbackRouter] Added {addedCount} missing mapping(s); total now {mappings.Length}.");
#endif
        }

        // =====================
        // Runtime Lookup
        // =====================

        private Dictionary<SubFeedback, MMF_Player[]> _lookup;

        // =====================
        // Lifecycle
        // =====================

        protected override void Awake()
        {
            base.Awake();
            BuildLookup();
        }

        /** Constructs the enum → MMF_Player[] dictionary from the serialized array. */
        private void BuildLookup()
        {
            _lookup = new Dictionary<SubFeedback, MMF_Player[]>(mappings.Length);

            for (int i = 0; i < mappings.Length; i++)
            {
                if (_lookup.ContainsKey(mappings[i].feedback))
                {
                    Debug.LogWarning($"[FeedbackRouter] Duplicate key '{mappings[i].feedback}' — last entry wins.");
                }
                _lookup[mappings[i].feedback] = mappings[i].players;
            }
        }

        // =====================
        // Public API
        // =====================

        /**
         * Plays all MMF_Players mapped to the given feedback key.
         * Safe to call even if the key has no mapping — silently returns.
         */
        public void Play(SubFeedback feedback, Vector3 position, float intensity = 1f)
        {
            if (_lookup == null || !_lookup.TryGetValue(feedback, out var players)) return;

            for (int i = 0; i < players.Length; i++)
                if (players[i] != null) players[i].PlayFeedbacks(position, intensity);
        }

        /**
         * Stops all MMF_Players mapped to the given feedback key.
         * Used for looping feedbacks (e.g. mining active) that need explicit stop.
         */
        public void Stop(SubFeedback feedback)
        {
            if (_lookup == null || !_lookup.TryGetValue(feedback, out var players)) return;

            for (int i = 0; i < players.Length; i++)
                if (players[i] != null) players[i].StopFeedbacks();
        }

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private int MappingCount => mappings != null ? mappings.Length : 0;
    }
}
