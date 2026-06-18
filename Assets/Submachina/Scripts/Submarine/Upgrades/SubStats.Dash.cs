namespace Submachina.Core
{
    public static partial class SubStats
    {
        private const int DashCat = 4;

        public static readonly StatId DashAirCost = new(DashCat, 0);
        public static readonly StatId DashCooldown = new(DashCat, 1);
        public static readonly StatId DashImpulse  = new(DashCat, 2);
    }
}
