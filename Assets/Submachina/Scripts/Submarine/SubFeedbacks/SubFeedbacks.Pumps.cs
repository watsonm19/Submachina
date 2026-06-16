namespace Submachina.Core
{
    // Pump feedback keys — used by ManualBellowsPump, O2PickupPump.
    public static partial class SubFeedbacks
    {
        private const int PumpsCat = 5;

        public static readonly FeedbackId PumpPerfect = new(PumpsCat, 0);
        public static readonly FeedbackId PumpWeak    = new(PumpsCat, 1);
        public static readonly FeedbackId PumpCharge  = new(PumpsCat, 2);
        public static readonly FeedbackId AirLock     = new(PumpsCat, 3);
    }
}
