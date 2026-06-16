namespace Submachina.Core
{
    // Weapon anchor keys — where weapon/attack effects originate.
    public static partial class SubAnchors
    {
        private const int WeaponCat = 2;

        public static readonly AnchorId Muzzle = new(WeaponCat, 0);
    }
}