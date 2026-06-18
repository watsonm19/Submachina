namespace Submachina.Core
{
    public static partial class SubStats
    {
        private const int O2Cat = 1;

        public static readonly StatId MaxAirPressure            = new(O2Cat, 0);
        public static readonly StatId BaseDecayRate             = new(O2Cat, 1);
        public static readonly StatId LateralExertionMultiplier = new(O2Cat, 2);
        public static readonly StatId VerticalExertionMultiplier = new(O2Cat, 3);
        public static readonly StatId HealthBleedRate           = new(O2Cat, 4);
    }
}
