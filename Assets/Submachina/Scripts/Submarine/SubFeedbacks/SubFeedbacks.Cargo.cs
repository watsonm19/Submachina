namespace Submachina.Core
{
    // Cargo feedback keys — used by CargoHold.
    public static partial class SubFeedbacks
    {
        private const int CargoCat = 10;

        public static readonly FeedbackId CargoAdded    = new(CargoCat, 0);
        public static readonly FeedbackId CargoFull     = new(CargoCat, 1);
        public static readonly FeedbackId CargoRejected = new(CargoCat, 2);

        /** Units jettisoned overboard (the anti-stuck escape valve). */
        public static readonly FeedbackId CargoDumped   = new(CargoCat, 3);
    }
}
