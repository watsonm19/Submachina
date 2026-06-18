namespace Submachina.Core
{
    // Combat feedback keys — used by PlayerAttack, CavitationBurst, CollisionDamage, DashRam.
    public static partial class SubFeedbacks
    {
        private const int CombatCat = 2;

        public static readonly FeedbackId AttackSwing     = new(CombatCat, 0);
        public static readonly FeedbackId DashStart       = new(CombatCat, 1);
        public static readonly FeedbackId DashEnd         = new(CombatCat, 2);
        public static readonly FeedbackId TakeDamage      = new(CombatCat, 3);
        public static readonly FeedbackId CollisionDamage = new(CombatCat, 4);
        public static readonly FeedbackId DashReady       = new(CombatCat, 5);
        public static readonly FeedbackId DashRam         = new(CombatCat, 6);
    }
}
