using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * SubmarineAnchorRouter — central registry of named mount points on the sub.
     *
     * The third semantic router alongside SubmarineFeedbackRouter (Sub.Feedbacks)
     * and SubmarinePumpRouter (Sub.Pumps). It resolves an AnchorId key to the live
     * Transform of a SubmarineAnchor marker placed in the visual hierarchy, so
     * modules and feedback prefabs can ask "where is the muzzle / tail?" by key
     * instead of holding a cross-prefab transform reference.
     *
     * Markers self-register here in their OnEnable (like pumps), so swapping a
     * module that carries its own anchors keeps the registry current. As a
     * safety net, Awake also back-fills any anchors already nested under the sub.
     *
     * Accessed sibling-side via Sub.Anchors. Setup:
     *   1. Add to the submarine root alongside the other routers.
     *   2. Place SubmarineAnchor markers on child transforms — they self-register.
     */
    public class SubmarineAnchorRouter : SubmarineComponent
    {
        // Live key → marker map. Populated by Register/Unregister as markers
        // enable/disable, so runtime module swaps stay current.
        private readonly Dictionary<AnchorId, SubmarineAnchor> _anchors = new();

        // Keys we've already warned about missing, so a per-frame caller can't
        // spam the console with the same warning every frame.
        private readonly HashSet<AnchorId> _warnedMissing = new();

        // =====================
        // Lifecycle
        // =====================

        /**
         * Back-fill any anchors already nested under the sub at startup.
         * Self-registration in SubmarineAnchor.OnEnable is the primary path;
         * this sweep guards against enable-order races during runtime
         * instantiation, and Register dedups so double-adds are harmless.
         */
        protected override void Awake()
        {
            base.Awake();

            var found = GetComponentsInChildren<SubmarineAnchor>(true);
            for (int i = 0; i < found.Length; i++)
                Register(found[i]);
        }

        // =====================
        // Registration
        // =====================

        /**
         * Adds an anchor to the registry under its key. On a duplicate key the
         * last registrant wins (matching the feedback router), with a warning.
         */
        public void Register(SubmarineAnchor anchor)
        {
            if (anchor == null) return;

            if (_anchors.TryGetValue(anchor.Key, out var existing) && existing != anchor && existing != null)
                Debug.LogWarning($"[AnchorRouter] Duplicate anchor key '{anchor.Key}' — last entry wins.", anchor);

            _anchors[anchor.Key] = anchor;
        }

        /** Removes an anchor, but only if it's still the one registered under its key. */
        public void Unregister(SubmarineAnchor anchor)
        {
            if (anchor == null) return;
            if (_anchors.TryGetValue(anchor.Key, out var existing) && existing == anchor)
                _anchors.Remove(anchor.Key);
        }

        // =====================
        // Lookup
        // =====================

        /**
         * Resolves an anchor key to its transform. On a miss, warns once and
         * falls back to the sub root so effects still appear at the sub's center
         * rather than at the world origin.
         */
        public Transform Get(AnchorId key)
        {
            if (_anchors.TryGetValue(key, out var anchor) && anchor != null)
                return anchor.Point;

            if (_warnedMissing.Add(key))
                Debug.LogWarning($"[AnchorRouter] No anchor registered for '{key}' — falling back to sub root.", this);

            return transform;
        }

        /** Tries to resolve an anchor key without the sub-root fallback. */
        public bool TryGet(AnchorId key, out Transform point)
        {
            if (_anchors.TryGetValue(key, out var anchor) && anchor != null)
            {
                point = anchor.Point;
                return true;
            }

            point = null;
            return false;
        }

        /**
         * Live key → marker registry, read-only. Exposed for the scene-view
         * visualizer (SubmarineAnchorRouterEditor, a separate editor assembly)
         * to draw runtime markers that reflect the actual registered state,
         * including any module swaps. Also handy for debug tooling.
         */
        public IReadOnlyDictionary<AnchorId, SubmarineAnchor> Registry => _anchors;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private int RegisteredAnchors => _anchors.Count;

        /**
         * Read-only key → Transform map shown in the inspector. Each value is a
         * live Transform reference, so Odin renders it as a clickable object
         * field — click to select / ping that anchor in the hierarchy.
         *
         * Play mode reads the live registry (reflects runtime swaps); edit mode
         * sweeps children, since registration is a runtime-only path.
         */
        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        [DictionaryDrawerSettings(KeyLabel = "Anchor", ValueLabel = "Transform")]
        private Dictionary<AnchorId, Transform> AnchorTransforms
        {
            get
            {
                var map = new Dictionary<AnchorId, Transform>();

                // Live registry while playing — mirrors what callers resolve.
                if (Application.isPlaying)
                {
                    foreach (var kv in _anchors)
                        map[kv.Key] = kv.Value != null ? kv.Value.Point : null;
                    return map;
                }

                // Edit mode: nothing has registered yet, so sweep the hierarchy.
                var found = GetComponentsInChildren<SubmarineAnchor>(true);
                for (int i = 0; i < found.Length; i++)
                    map[found[i].Key] = found[i].Point;
                return map;
            }
        }
    }
}