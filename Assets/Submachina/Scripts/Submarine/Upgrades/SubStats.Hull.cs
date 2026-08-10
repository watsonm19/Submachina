namespace Submachina.Core
{
    // Hull stat keys — used by HullSystem and CollisionDamage.
    public static partial class SubStats
    {
        private const int HullCat = 9;

        /** Base structural strength. Hull Resistance = Strength × Integrity. */
        public static readonly StatId HullStrength = new(HullCat, 0);

        /** Multiplier on depth pressure load (< 1 = pressure reinforcement). */
        public static readonly StatId PressureLoadMult = new(HullCat, 1);

        /** Multiplier on collision impact load (< 1 = impact reinforcement). */
        public static readonly StatId ImpactLoadMult = new(HullCat, 2);
    }
}
