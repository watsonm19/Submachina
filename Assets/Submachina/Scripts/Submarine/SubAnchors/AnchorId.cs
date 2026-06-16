using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Submachina.Core
{
    /**
     * Unique identifier for a submarine mount point ("anchor").
     *
     * An anchor is a semantic visual location on the sub — Muzzle, Front, Tail —
     * that systems and feedbacks resolve to a live Transform via Sub.Anchors,
     * instead of holding a hard reference across prefab boundaries.
     *
     * Mirrors FeedbackId exactly: a category ID (upper 16 bits) and a local
     * value (lower 16 bits) packed into a single serialized int, so each
     * category has an isolated namespace and reorders without collisions.
     *
     * Use SubAnchors.* static fields to reference specific anchors; never
     * construct raw IDs.
     */
    [Serializable]
    public struct AnchorId : IEquatable<AnchorId>
    {
        [SerializeField] private int _packed;

        public AnchorId(int category, int local)
        {
            _packed = (category << 16) | (local & 0xFFFF);
        }

        /** Category portion of the packed ID (upper 16 bits). */
        public int Category => (_packed >> 16) & 0xFFFF;

        // =====================
        // Equality
        // =====================

        public bool Equals(AnchorId other) => _packed == other._packed;
        public override bool Equals(object obj) => obj is AnchorId other && Equals(other);
        public override int GetHashCode() => _packed;

        public static bool operator ==(AnchorId a, AnchorId b) => a._packed == b._packed;
        public static bool operator !=(AnchorId a, AnchorId b) => a._packed != b._packed;

        /** True when this ID has never been assigned (default struct value). */
        public bool IsEmpty => _packed == 0;

        // =====================
        // Debug Display
        // =====================

        private static Dictionary<int, string> _nameCache;

        /**
         * Resolves the packed value to the SubAnchors field name via a lazily
         * built reflection cache. Falls back to the raw int if the value
         * doesn't match any declared field.
         */
        public override string ToString()
        {
            if (_nameCache == null) BuildNameCache();
            return _nameCache.TryGetValue(_packed, out var name) ? name : $"AnchorId({_packed})";
        }

        private static void BuildNameCache()
        {
            _nameCache = new Dictionary<int, string>();
            foreach (var field in typeof(SubAnchors).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(AnchorId)) continue;
                var id = (AnchorId)field.GetValue(null);
                _nameCache[id._packed] = field.Name;
            }
        }

        /** Force-rebuilds the name cache. Call after hot-reloading anchor definitions. */
        internal static void InvalidateNameCache() => _nameCache = null;
    }
}