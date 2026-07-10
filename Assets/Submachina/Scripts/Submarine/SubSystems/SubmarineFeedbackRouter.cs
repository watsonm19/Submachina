using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using MoreMountains.Feedbacks;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Central switchboard for all submarine feedback effects.
     *
     * Systems trigger feedbacks by FeedbackId key through
     * Sub.Feedbacks.Play / Stop — they never hold direct MMF_Player references.
     * All wiring lives in this one component's Inspector, making it easy to
     * tune, reorder, and swap feedback players without touching gameplay code.
     *
     * At runtime a dictionary is built from the serialized mappings array
     * for O(1) lookup. Duplicate keys are warned about and last-wins.
     *
     * Multiple routers per sub are supported: drop one on any child where a
     * grouping of feedbacks lives and map only the keys that group cares about.
     * Play/Stop on any router broadcasts to all routers under the same
     * Submarine, so the same key can be mapped on several routers and each
     * fires its own players and events.
     *
     * Setup:
     *   1. Add to the submarine root (or any child under it) — repeatable.
     *   2. In the Mappings list, add one entry per feedback key.
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
            [HorizontalGroup("Row"), LabelWidth(160)]
            public FeedbackId key;

            [HorizontalGroup("Row")]
            public MMF_Player[] players;
        }

        [FoldoutGroup("Mappings")]
        [ListDrawerSettings(ShowPaging = false)]
        [Tooltip("Map each FeedbackId key to one or more MMF_Players. " +
                 "Systems call Sub.Feedbacks.Play(key) — they never reference MMF_Players directly.")]
        [SerializeField] private FeedbackMapping[] mappings;

        // =====================
        // Editor Utilities
        // =====================

        /**
         * Adds an empty mapping entry for every FeedbackId defined in
         * SubFeedbacks that isn't already mapped, so the full set of
         * feedback keys is always represented in the Inspector.
         *
         * Existing entries are preserved; only missing keys are appended.
         * Entries are sorted by category then by name for readability.
         */
        [FoldoutGroup("Mappings")]
        [Button(ButtonSizes.Medium, Icon = SdfIconType.PlusSquare), GUIColor(0.6f, 0.9f, 0.7f)]
        [Tooltip("Append an empty mapping for any feedback key not already present.")]
        private void AddMissingMappings()
        {
            // Collect the keys we already have so we skip them.
            var existing = new HashSet<FeedbackId>();
            if (mappings != null)
                foreach (var m in mappings)
                    existing.Add(m.key);

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

            // Discover all FeedbackId fields defined in the SubFeedbacks partial class.
            int addedCount = 0;
            foreach (var field in typeof(SubFeedbacks).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(FeedbackId)) continue;

                var id = (FeedbackId)field.GetValue(null);
                if (existing.Contains(id)) continue;

                result.Add(new FeedbackMapping { key = id, players = new MMF_Player[0] });
                addedCount++;
            }

            // Sort by category then by packed value within each category.
            result.Sort((a, b) => a.key.GetHashCode().CompareTo(b.key.GetHashCode()));
            mappings = result.ToArray();

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"[FeedbackRouter] Added {addedCount} missing mapping(s); total now {mappings.Length}.");
#endif
        }

        // =====================
        // Runtime Lookup
        // =====================

        private Dictionary<FeedbackId, MMF_Player[]> _lookup;

        // =====================
        // Lifecycle
        // =====================

        protected override void Awake()
        {
            base.Awake();
            BuildLookup();
        }

        /** Constructs the FeedbackId → MMF_Player[] dictionary from the serialized array. */
        private void BuildLookup()
        {
            _lookup = new Dictionary<FeedbackId, MMF_Player[]>(mappings.Length);

            for (int i = 0; i < mappings.Length; i++)
            {
                if (_lookup.ContainsKey(mappings[i].key))
                    Debug.LogWarning($"[FeedbackRouter] Duplicate key '{mappings[i].key}' — last entry wins.");

                _lookup[mappings[i].key] = mappings[i].players;
            }
        }

        // =====================
        // Broadcast (publish/subscribe)
        // =====================

        /**
         * Raised whenever a key is played, carrying the key, world position, and
         * intensity. This is the pub/sub side of the bus: components like
         * FeedbackEventListener subscribe and react via UnityEvents, so a cue can
         * drive arbitrary behaviour with no direct reference to the firing system.
         */
        public event Action<FeedbackId, Vector3, float> FeedbackPlayed;

        /** Raised whenever a key is stopped (looping feedbacks). */
        public event Action<FeedbackId> FeedbackStopped;

        // =====================
        // Public API
        // =====================

        /**
         * Plays the given feedback key on every router under this submarine.
         * Multiple routers can coexist (one per feedback grouping in the
         * hierarchy) — calling Play on any of them reaches all of them, so
         * Sub.Feedbacks.Play call sites work unchanged no matter which
         * router holds the mapping. Falls back to local-only when the router
         * isn't parented under a Submarine (standalone/prefab testing).
         */
        public void Play(FeedbackId key, Vector3 position, float intensity = 1f)
        {
            // Broadcast across all sibling routers registered to the same sub.
            var routers = Sub != null ? Sub.FeedbackRouters : null;
            if (routers != null && routers.Count > 0)
                for (int i = 0; i < routers.Count; i++)
                    routers[i].PlayLocal(key, position, intensity);
            else
                PlayLocal(key, position, intensity);
        }

        /**
         * Stops the given feedback key on every router under this submarine.
         * Used for looping feedbacks (e.g. mining active).
         */
        public void Stop(FeedbackId key)
        {
            // Broadcast across all sibling routers registered to the same sub.
            var routers = Sub != null ? Sub.FeedbackRouters : null;
            if (routers != null && routers.Count > 0)
                for (int i = 0; i < routers.Count; i++)
                    routers[i].StopLocal(key);
            else
                StopLocal(key);
        }

        /**
         * Plays only this router's own mapped MMF_Players, then raises this
         * router's FeedbackPlayed. The event fires even when the key has no MMF
         * mapping, so a key can drive pure event listeners with no player attached.
         */
        private void PlayLocal(FeedbackId key, Vector3 position, float intensity)
        {
            // Fire any MMF players mapped to this key.
            if (_lookup != null && _lookup.TryGetValue(key, out var players))
                for (int i = 0; i < players.Length; i++)
                    if (players[i] != null) players[i].PlayFeedbacks(position, intensity);

            // Notify subscribers regardless of whether a player was mapped.
            FeedbackPlayed?.Invoke(key, position, intensity);
        }

        /** Stops only this router's own mapped MMF_Players, then raises FeedbackStopped. */
        private void StopLocal(FeedbackId key)
        {
            // Stop any MMF players mapped to this key.
            if (_lookup != null && _lookup.TryGetValue(key, out var players))
                for (int i = 0; i < players.Length; i++)
                    if (players[i] != null) players[i].StopFeedbacks();

            // Notify subscribers regardless of whether a player was mapped.
            FeedbackStopped?.Invoke(key);
        }

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private int MappingCount => mappings != null ? mappings.Length : 0;
    }
}
