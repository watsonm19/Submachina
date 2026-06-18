namespace Submachina.Core
{
    public static partial class SubStats
    {
        private const int MiningCat = 7;

        public static readonly StatId MiningDuration = new(MiningCat, 0);
        public static readonly StatId MiningRange    = new(MiningCat, 1);
    }
}
