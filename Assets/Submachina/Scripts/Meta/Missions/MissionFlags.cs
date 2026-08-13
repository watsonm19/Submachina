using System;

namespace Submachina.Meta
{
    /**
     * Trait flags describing a mission site — the "biome-ish" knobs a generated
     * (or hand-authored) MissionSpec can carry to reshape world spawning.
     *
     * Spawn rules opt in via their MissionGate (require / exclude / rate-scale
     * per flag) and ChunkSpawner can swap whole SpawnProfiles on them, so one
     * flag can quietly retune many rules at once — resources, creatures, and
     * enemies alike.
     *
     * Starter taxonomy — rename or extend freely. Assets serialize these by
     * VALUE, so renaming members is safe but reassigning bit positions is not.
     */
    [Flags]
    public enum MissionFlags
    {
        None = 0,

        /** Lifeless waters — creature and O2 rules can thin out. */
        Barren = 1 << 0,

        /** Hostile-dense site — enemy rules scale up. */
        Infested = 1 << 1,

        /** Mineral-rich site — resource rules scale up. */
        MineralRich = 1 << 2,

        /** Geothermal vents biome — vent-specific rules switch on. */
        Geothermal = 1 << 3,
    }
}
