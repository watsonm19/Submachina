namespace Submachina.Core
{
    // Resource feedback keys — used by ResourceManager.
    public static partial class SubFeedbacks
    {
        private const int ResourcesCat = 4;

        public static readonly FeedbackId ResourcesAdded = new(ResourcesCat, 0);
        public static readonly FeedbackId LevelUp        = new(ResourcesCat, 1);
    }
}
