namespace Submachina.Core
{
    // Scrap feedback keys — used by ScrapManager.
    public static partial class SubFeedbacks
    {
        private const int ScrapCat = 3;

        public static readonly FeedbackId ScrapAdded = new(ScrapCat, 0);
        public static readonly FeedbackId ScrapFull  = new(ScrapCat, 1);
        public static readonly FeedbackId ScrapUsed  = new(ScrapCat, 2);
        public static readonly FeedbackId NoScrap    = new(ScrapCat, 3);
        public static readonly FeedbackId FullHealth = new(ScrapCat, 4);
    }
}
