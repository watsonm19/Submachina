# Hub system

- [ ]  Hub: a place where the player starts from to initiate a new mission and acts as the center of the core game loop. The hub should allow purchase of upgrades that previous to now we have implemented a 3-choice roguelike thing.
- [ ]  One of the core progression elements here is that the player will be unable to go very deep at first, but by doing many such missions where resources are retrieved, they will be able to upgrade depth capability.
- [ ]  Each mission that the player goes on will have different properties - a rough summary of items via “long range scanners” that are detected in terms of resource, plants, and animal life. A target depth / objective.
- [ ]  Ideas for starting mission types:
- Retrieval - find something, bring it back
- Neutralize threat - kill a creature, harvest its remains
- Research - find something (wreckage, creatures, geology, etc) and use scanner or sensors

## Mission Properties

To justify equipment equipment, upgrades, etc, missions should have various properties that necessitate or at least facilitate different loadouts or capabilities. The hub could have some “long range sensor” that gives the player some idea of what they’ll encounter

**Hazards**:

- Hostile creatures
    - Strong creatures
        - That you need to fight
        - That you must be able to flee from
    - Fast creatures
        - You must be able to catch
    - Hidden creatures
        - You have time and equipment to find
- Water current
    - Strong directional pull or push requiring good thrust or maybe some equipment item (grappling hook, tow line, or anchor drop type thing)

**Environment:**

- Required depth
- Terrain obstacles
- Oxygen richness
    - How much ambient oxygen is available to extract from the water

**Resources:**

- Detected ores / minerals
- Wrecks / salvage

# Resource Collection Basic

Different upgrades should require different resource types, we currently only have one. These resource types should be found at different rarities, different depths, or different biomes.

- [ ]  Resource type system - define different resource types (iron, gold, copper, but lets be more clever and thematic than those types etc) tag objects with a resource type so that when they are collected it adds to ship storage
- [ ]  We have a Resources folder with some prefabs that can be used to start this out, but the MiningResource.cs script will need to core changes.

# Depth progression

Depth that the player can go to should be limited by their hull strength and oxygen carrying and gathering capabilities, so that these can be upgraded over time to progress further and do deeper missions, or go to depths with less risk.

**Unified Hull Strength**

The submarine has a single **Hull Strength** stat and an **Integrity %**. Current hull resistance is:

**Hull Resistance = Strength × Integrity**

Depth pressure continuously consumes part of that resistance, leaving a smaller **structural reserve** available to absorb impacts. An impact only damages the hull if:

**Pressure Load + Impact Load > Current Hull Resistance**

Any amount above the hull’s resistance becomes **overload**, which reduces integrity. As integrity falls, hull resistance falls too, so both depth pressure and future impacts become increasingly dangerous.

If ambient pressure alone exceeds current hull resistance, the hull begins taking continuous pressure damage, creating a potential failure cascade.

In short: **depth preloads the hull, impacts consume the remaining margin, and accumulated damage progressively lowers the margin further.**

# Oxygen:

A basic progression for O2 should be created so that we can upgrade capacity.

# Buoyancy

Since having to return from a long descent would be difficult and tedious if you had to hold thrusters up, a buoyancy system could allow more efficient upward traversal.

- Fill ballast tanks with water to descend without thrusters. Speed can be controlled.
    - Mass increases descent speed
    - Using the manual air pump could both increase descent speed while gaining O2. Essentially it’s reclaiming air in the balast tank.
- Empty ballast tanks (by consuming stored air) to  begin ascending without thrusters
- Two controls could manipulate this:
    - Toggle intake air pump destination: O2 reserve vs Ballast
        - O2 reserve: When selected, environment air bubbles consumed increase O2 stores as normal, buoyancy is unaffected
        - Ballast: When selected, environment bubbles consumed fill ballast tank, increasing upward buoyancy without thrust
    - Toggle manual pump destination: O2 reserve vs Ballast
        - O2 reserve: When selected, manual pump removes air from ballast and adds to the reserve. Same as now, but decreases buoyancy
        - Ballast: When selected, manual pump consumed air from O2 reserve and adds to ballast tank, which increases buoyancy.

# **Ship properties:**

There must be some kind of restrictions on loadout. Overall longer playthrough will equal stronger ship, but the player shouldn’t be able to have it all.

Could be like: choose one computerized system: sonar, weapons, spot lighting, flare
Choose one hull feature: Double O2, Ballast tank, Hull impact reinforcement, hull pressure reinforcement
Choose two tools: Salvage hook, projectile weapons, retractable harpoon, mining beam
Choose: storage capacity, O2 capacity, ammo capacity
Choose traversal ability: Dash, Turbo (slower to engage, 2x O2 burn, more sustainable), terrain ram (break through rocks)

- O2 tank size
    - Bigger = heavier, slower acceleration, longer expedition time without refilling
    - Lighter = lighter, faster acceleration, shorter time
- Ballast tank size
    - Big tank consumes more oxygen when filling, but then requires less fuel for surfacing. Ideal for deep missions that are more vertical
- Hull impact strength
    - Heavier, slower, stronger
    - Lighter, faster, weaker/more vulnerable
- Hull pressure strength
    - Heavier, slower, deeper
    - Lighter, faster, shallower
- Drones:
    - Attack drone, let you focus on evasion
    - O2 collection drone
    - Mining drone - increase mining speed or capability
    - Tow drone - haul cargo for you
    - Drones maybe require deuterium fuel which is rare-ish & valuable
- Weapons
- Dash ability, gills, thrusters, etc

# Cargo and Inertia

- The sub has various carrying capacity for fuel, storage for minerals and resources, and weapons and ammo
- All increases in weight impact inertia and therefore movement speed and possibly fuel consumption or energy use, and buoyancy