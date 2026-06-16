namespace Submachina.Core
{
    /**
     * Semantic keys for submarine feedback effects.
     *
     * Each value maps to one or more MMF_Players wired in the
     * SubmarineFeedbackRouter's Inspector. Systems trigger feedbacks
     * by key — they never hold direct MMF_Player references.
     *
     * Values are explicit integers grouped into category ranges so the
     * enum can be reordered freely without breaking serialized data.
     * New entries: pick the next unused number in the category range.
     * Never reuse a retired number.
     *
     *   Mining     100–199
     *   Combat     200–299
     *   Scrap      300–399
     *   Resources  400–499
     *   Pumps      500–599
     */
    public enum SubFeedback
    {
        // -- Mining (100–199) --
        MiningActive    = 100,
        MiningCollect   = 101,

        // -- Combat (200–299) --
        AttackSwing     = 200,
        DashStart       = 201,
        DashEnd         = 202,
        TakeDamage      = 203,
        CollisionDamage = 204,

        // -- Scrap (300–399) --
        ScrapAdded      = 300,
        ScrapFull       = 301,
        ScrapUsed       = 302,
        NoScrap         = 303,
        FullHealth      = 304,

        // -- Resources (400–499) --
        ResourcesAdded  = 400,
        LevelUp         = 401,

        // -- Pumps (500–599) --
        PumpPerfect     = 500,
        PumpWeak        = 501,
        PumpCharge      = 502,   // looping charge cue — Play on charge start, Stop on release/overshoot
        AirLock         = 503,   // pump seizes from spam / a wasted stop
    }
}
