namespace Submachina.Core
{
    // Mining feedback keys — used by MiningLaser.
    public static partial class SubFeedbacks
    {
        private const int MiningCat = 1;

        public static readonly FeedbackId MiningActive  = new(MiningCat, 0);
        public static readonly FeedbackId MiningCollect = new(MiningCat, 1);
    }
}
