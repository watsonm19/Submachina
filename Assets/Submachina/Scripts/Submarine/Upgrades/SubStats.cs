using System.Collections.Generic;

namespace Submachina.Core
{
    /**
     * Central registry of all upgradeable stat keys.
     *
     * Each category is defined in its own partial file (SubStats.O2.cs,
     * SubStats.Combat.cs, etc.) so categories are fully independent —
     * adding or reordering keys in one category can never affect another.
     *
     * Category IDs are packed into the upper 16 bits of StatId, so each
     * category has its own 65,536-value namespace with no manual range management.
     *
     * To add a new category:
     *   1. Create SubStats.YourCategory.cs with a unique category constant.
     *   2. Add the category name to CategoryNames below.
     *   3. Define static readonly StatId fields using new StatId(cat, local).
     */
    public static partial class SubStats
    {
        /** Category ID → display name, used by the editor drawer for grouping. */
        public static readonly Dictionary<int, string> CategoryNames = new()
        {
            { 1, "O2" },
            { 2, "Pumps" },
            { 3, "Combat" },
            { 4, "Dash" },
            { 5, "Movement" },
            { 6, "Defense" },
            { 7, "Mining" },
            { 8, "Sonar" },
        };
    }
}
