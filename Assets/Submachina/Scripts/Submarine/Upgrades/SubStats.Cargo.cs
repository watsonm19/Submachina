namespace Submachina.Core
{
    // Cargo stat keys — used by CargoHold.
    public static partial class SubStats
    {
        private const int CargoCat = 10;

        /** Total cargo units the hold can carry (all resource types combined). */
        public static readonly StatId CargoCapacity = new(CargoCat, 0);
    }
}
