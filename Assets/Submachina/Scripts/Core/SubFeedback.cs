namespace Submachina.Core
{
    /**
     * Semantic keys for submarine feedback effects.
     *
     * Each value maps to one or more MMF_Players wired in the
     * SubmarineFeedbackRouter's Inspector. Systems trigger feedbacks
     * by key — they never hold direct MMF_Player references.
     *
     * Add new entries here when new feedback-triggering systems are created.
     */
    public enum SubFeedback
    {
        // -- Mining --
        MiningActive,
        MiningCollect,

        // -- Combat --
        AttackSwing,
        DashStart,
        DashEnd,
        TakeDamage,
        CollisionDamage,

        // -- Scrap --
        ScrapAdded,
        ScrapFull,
        ScrapUsed,
        NoScrap,
        FullHealth,

        // -- Resources --
        ResourcesAdded,
        LevelUp,

        // -- Pumps --
        PumpPerfect,
        PumpWeak,
    }
}
