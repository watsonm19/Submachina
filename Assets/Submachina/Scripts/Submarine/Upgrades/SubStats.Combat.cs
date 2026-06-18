namespace Submachina.Core
{
    public static partial class SubStats
    {
        private const int CombatCat = 3;

        public static readonly StatId AttackDamage    = new(CombatCat, 0);
        public static readonly StatId AttackRange     = new(CombatCat, 1);
        public static readonly StatId AttackCooldown  = new(CombatCat, 2);
        public static readonly StatId KnockbackForce  = new(CombatCat, 3);
    }
}
