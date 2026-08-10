namespace Submachina.Core
{
    // Ballast feedback keys — used by BallastTank and the pump destination toggle.
    public static partial class SubFeedbacks
    {
        private const int BallastCat = 11;

        /** Looping cue while the tank is flooding (descending). */
        public static readonly FeedbackId BallastFlood = new(BallastCat, 0);

        /** Looping cue while the tank is blowing (ascending). */
        public static readonly FeedbackId BallastBlow = new(BallastCat, 1);

        public static readonly FeedbackId BallastFull  = new(BallastCat, 2);
        public static readonly FeedbackId BallastEmpty = new(BallastCat, 3);

        /** Pump destination switched between O2 reserve and ballast. */
        public static readonly FeedbackId PumpDestinationToggled = new(BallastCat, 4);

        /** Gear shifter clicked to a new mode (Empty / Neutral / Full). */
        public static readonly FeedbackId BallastShift = new(BallastCat, 5);
    }
}
