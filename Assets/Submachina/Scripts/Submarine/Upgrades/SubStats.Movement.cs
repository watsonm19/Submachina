namespace Submachina.Core
{
    public static partial class SubStats
    {
        private const int MovementCat = 5;

        public static readonly StatId LateralThrustForce  = new(MovementCat, 0);
        public static readonly StatId CounterThrustForce  = new(MovementCat, 1);
    }
}
