namespace Submachina.Core
{
    // Ballast stat keys — used by BallastTank.
    public static partial class SubStats
    {
        private const int BallastCat = 11;

        /** Fill-fraction per second while flooding (descend). */
        public static readonly StatId BallastFloodRate = new(BallastCat, 0);

        /** Fill-fraction per second while blowing (ascend). */
        public static readonly StatId BallastBlowRate = new(BallastCat, 1);

        /** Air units consumed to blow a full tank (lower = more efficient). */
        public static readonly StatId BallastAirPerFill = new(BallastCat, 2);
    }
}
