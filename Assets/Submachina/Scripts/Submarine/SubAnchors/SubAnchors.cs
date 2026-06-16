using System.Collections.Generic;

namespace Submachina.Core
{
    /**
     * Central registry of all submarine anchor (mount point) keys.
     *
     * Each category is defined in its own partial file (SubAnchors.Mounts.cs, ...)
     * so categories are fully independent — adding or reordering keys in one
     * category can never affect another. Category IDs are packed into the upper
     * 16 bits of AnchorId, giving each category its own 65,536-value namespace.
     *
     * Mirrors SubFeedbacks. To add a new category:
     *   1. Create SubAnchors.YourCategory.cs with a unique category constant.
     *   2. Add the category name to CategoryNames below.
     *   3. Define static readonly AnchorId fields using new AnchorId(cat, local).
     */
    public static partial class SubAnchors
    {
        /** Category ID → display name, used by the editor drawer for grouping. */
        public static readonly Dictionary<int, string> CategoryNames = new()
        {
            { 1, "Hull" },
            { 2, "Weapon" },
        };
    }
}