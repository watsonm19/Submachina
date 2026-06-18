using System.Collections.Generic;

namespace Submachina.Core
{
    /**
     * Central registry of all submarine feedback keys.
     *
     * Each category is defined in its own partial file (SubFeedbacks.Mining.cs,
     * SubFeedbacks.Combat.cs, etc.) so categories are fully independent —
     * adding or reordering keys in one category can never affect another.
     *
     * Category IDs are packed into the upper 16 bits of FeedbackId, so each
     * category has its own 65,536-value namespace with no manual range management.
     *
     * To add a new category:
     *   1. Create SubFeedbacks.YourCategory.cs with a unique category constant.
     *   2. Add the category name to CategoryNames below.
     *   3. Define static readonly FeedbackId fields using new FeedbackId(cat, local).
     */
    public static partial class SubFeedbacks
    {
        /** Category ID → display name, used by the editor drawer for grouping. */
        public static readonly Dictionary<int, string> CategoryNames = new()
        {
            { 1, "Mining" },
            { 2, "Combat" },
            { 3, "Scrap" },
            { 4, "Resources" },
            { 5, "Pumps" },
            { 6, "Upgrades" },
        };
    }
}
