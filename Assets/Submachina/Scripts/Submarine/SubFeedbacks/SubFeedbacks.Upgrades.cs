namespace Submachina.Core
{
    public static partial class SubFeedbacks
    {
        private const int UpgradesCat = 6;

        /** Fired when any upgrade is granted or leveled up. */
        public static readonly FeedbackId UpgradeGranted = new(UpgradesCat, 0);

        /** Fired when an upgrade reaches its maximum level. */
        public static readonly FeedbackId UpgradeMaxed   = new(UpgradesCat, 1);
    }
}
