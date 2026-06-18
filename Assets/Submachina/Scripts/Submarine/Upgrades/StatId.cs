using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Submachina.Core
{
    /**
     * Unique identifier for an upgradeable stat on a submarine subsystem.
     *
     * Packs a category ID (upper 16 bits) and a local value (lower 16 bits)
     * into a single int — the same layout as FeedbackId and AnchorId.
     * Each category partial file defines its own constant category ID,
     * so values are isolated across categories.
     *
     * Serialized as a single int for stability. Use SubStats.* static
     * fields to reference specific stats; never construct raw IDs.
     */
    [Serializable]
    public struct StatId : IEquatable<StatId>
    {
        [SerializeField] private int _packed;

        public StatId(int category, int local)
        {
            _packed = (category << 16) | (local & 0xFFFF);
        }

        /** Category portion of the packed ID (upper 16 bits). */
        public int Category => (_packed >> 16) & 0xFFFF;

        // =====================
        // Equality
        // =====================

        public bool Equals(StatId other) => _packed == other._packed;
        public override bool Equals(object obj) => obj is StatId other && Equals(other);
        public override int GetHashCode() => _packed;

        public static bool operator ==(StatId a, StatId b) => a._packed == b._packed;
        public static bool operator !=(StatId a, StatId b) => a._packed != b._packed;

        /** True when this ID has never been assigned (default struct value). */
        public bool IsEmpty => _packed == 0;

        // =====================
        // Debug Display
        // =====================

        private static Dictionary<int, string> _nameCache;

        /**
         * Resolves the packed value to the SubStats field name via
         * a lazily-built reflection cache. Falls back to the raw int
         * if the value doesn't match any declared field.
         */
        public override string ToString()
        {
            if (_nameCache == null) BuildNameCache();
            return _nameCache.TryGetValue(_packed, out var name) ? name : $"StatId({_packed})";
        }

        private static void BuildNameCache()
        {
            _nameCache = new Dictionary<int, string>();
            foreach (var field in typeof(SubStats).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(StatId)) continue;
                var id = (StatId)field.GetValue(null);
                _nameCache[id._packed] = field.Name;
            }
        }

        /** Force-rebuilds the name cache. Call after hot-reloading stat definitions. */
        internal static void InvalidateNameCache() => _nameCache = null;
    }
}
