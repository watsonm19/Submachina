namespace Submachina.Core
{
    // Sonar feedback keys — used by SonarSystem for the outgoing pulse and returning echoes.
    public static partial class SubFeedbacks
    {
        private const int SonarCat = 7;

        public static readonly FeedbackId SonarPingEmit = new(SonarCat, 0);
        public static readonly FeedbackId SonarReturn   = new(SonarCat, 1);
    }
}
