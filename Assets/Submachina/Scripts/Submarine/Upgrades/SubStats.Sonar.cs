namespace Submachina.Core
{
    public static partial class SubStats
    {
        private const int SonarCat = 8;

        public static readonly StatId SonarRange     = new(SonarCat, 0);
        public static readonly StatId SonarCooldown  = new(SonarCat, 1);
        public static readonly StatId SonarPingSpeed = new(SonarCat, 2);
    }
}
