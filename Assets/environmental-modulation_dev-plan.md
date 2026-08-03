# Environmental Modulation and Audio Direction System

## 1. Purpose

Build a unified runtime system for translating changing game conditions into coordinated audio, lighting, visual effects, camera feedback, and triggered events.

The system should support continuous environmental modulation from sources such as:

* Player depth
* Proximity to enemies, anomalies, structures, or environmental objects
* Current biome or region
* Player health and submarine damage
* Movement speed
* Time since the last audio stinger
* Narrative progression
* Threat level
* Safety state
* Sonar activity
* Temporary scripted influences

These inputs should be capable of driving:

* Ambience-loop volume and crossfading
* Music or tonal layers
* AudioMixer groups and exposed parameters
* Stingers and one-shot sounds
* Light intensity
* Glow and emissive intensity
* Shader properties
* Post-processing
* Camera shake and noise
* Feel feedback players
* Gameplay events and scripted triggers

The system should provide a coherent layer above individual components. Audio sources, lights, and feedback players should not independently contain duplicated logic for interpreting raw game state.

---

# 2. Core Design Principle

The system should be divided into four conceptual layers:

```text
Raw World Signals
        ↓
Semantic Parameters
        ↓
Continuous Routes and Event Rules
        ↓
Output Systems
```

Example:

```text
Player depth
Enemy proximity
Recent damage
Narrative phase
        ↓
Darkness
Threat
Tension
Isolation
Hull stress
        ↓
Audio ambience
Lights
Shaders
Camera effects
Feel feedback
```

The important architectural decision is to avoid mapping every raw gameplay value directly to every output.

Instead, raw gameplay conditions are translated into a smaller number of stable, meaningful parameters. These semantic parameters become the shared creative control surface for the game.

For example:

```text
Raw inputs:
- Depth
- Cave influence
- Supernatural zone influence
- Sonar illumination

Derived parameter:
- Darkness
```

Then several systems can respond to `Darkness`:

```text
Darkness
├── Global light intensity
├── Background exposure
├── Deep-water ambience volume
├── High-frequency audio filtering
├── Particle visibility
└── Submarine exterior glow
```

This prevents every destination from separately reimplementing the meaning of depth, biome, sonar, and other conditions.

---

# 3. Initial System Scope

The first version should remain intentionally small.

It should support:

1. Float-based raw signals
2. Float-based semantic parameters
3. Multiple contributions to one parameter
4. Continuous parameter-to-output mappings
5. Threshold-based event rules
6. Cooldowns and hysteresis
7. Basic AudioDirector functionality
8. Feel integration
9. Runtime debugging
10. ScriptableObject-based authoring

The first version should not require:

* A custom graph editor
* A fully generic reflection-based property binding system
* A replacement for Feel
* A complete replacement for FMOD or Wwise
* A complex procedural music engine
* A general-purpose gameplay ability system

Those can be added later if actual project needs justify them.

---

# 4. Runtime System Overview

Create a central service called something like:

```text
EnvironmentDirector
```

Alternative names:

* ExperienceDirector
* AtmosphereDirector
* ModulationDirector
* WorldStateDirector
* SensoryDirector

`EnvironmentDirector` is probably the clearest initial name, although the system will eventually control more than environmental effects.

Suggested runtime structure:

```text
EnvironmentDirector
├── SignalRegistry
├── ParameterRegistry
├── ModulationEvaluator
├── RuleEvaluator
├── AudioDirector
├── OutputRouter
└── DebugMonitor
```

The director should own runtime state but use ScriptableObjects for authoring definitions.

---

# 5. Raw Signals

## 5.1 Definition

A raw signal represents a measurable game fact.

Examples:

```text
DepthMeters
NormalizedDepth
NearestEnemyDistance
NearestAnomalyDistance
PlayerHealth01
HullDamage01
PlayerSpeed01
SonarCharge01
SecondsSinceLastStinger
CurrentBiomeInfluence
NarrativeProgress01
SafeZoneInfluence
```

A signal should not decide what the value means creatively. It only reports the current world state.

For example:

```text
NearestEnemyDistance = 12.4 meters
```

The signal should not itself decide that this means:

```text
Threat = 0.76
```

That interpretation belongs to the modulation layer.

---

## 5.2 Signal Interface

A minimal interface:

```csharp
public interface IFloatSignal
{
    float RawValue { get; }
}
```

A more useful version may expose:

```csharp
public interface IFloatSignal
{
    float RawValue { get; }
    bool IsValid { get; }
}
```

Normalization should usually happen in modulation definitions rather than inside the signal. This allows one raw value to be interpreted differently by several parameters.

For example, a distance of 20 meters may represent:

* High threat for a large boss
* Low threat for a small creature
* Maximum influence for a large environmental structure

---

## 5.3 Signal Update Frequencies

Not every signal needs to update every frame.

Suggested categories:

### Every frame

Use for inexpensive, rapidly changing signals:

* Player depth
* Player speed
* Health
* Sonar charge
* Scripted influence values

### Fixed interval

Use for potentially expensive queries:

* Nearest enemy distance
* Nearest anomaly distance
* Number of nearby threats
* Environmental overlap queries

Typical rates:

```text
Fast proximity:      10 Hz
Normal proximity:     5 Hz
Slow environment:     1–2 Hz
```

The resulting signal can be smoothed so lower sampling rates do not produce visible or audible stepping.

Avoid creating separate coroutines for every signal. The director can maintain grouped update schedules.

---

## 5.4 Initial Signal Components

Implement these first:

* `TransformDepthSignal`
* `TransformSpeedSignal`
* `NearestTargetDistanceSignal`
* `PlayerHealthSignal`
* `TimerSignal`
* `ManualFloatSignal`
* `TriggerVolumeInfluenceSignal`
* `ScriptedInfluenceSignal`

`ManualFloatSignal` is especially useful during development because it allows testing the rest of the system with an Inspector slider.

---

# 6. Semantic Parameters

## 6.1 Purpose

Semantic parameters represent meaningful experiential conditions rather than raw measurements.

Recommended initial parameters for the submarine game:

```text
Darkness
DepthPressure
Threat
Tension
Isolation
HullStress
SupernaturalInfluence
Safety
ExplorationIntensity
SonarIntensity
```

Do not create all of these immediately. Start with approximately four:

```text
Darkness
Threat
Tension
SupernaturalInfluence
```

Add others only when they clearly simplify downstream logic.

---

## 6.2 Parameter Definition

Each semantic parameter should have a ScriptableObject definition:

```text
FloatParameterDefinition
```

Suggested fields:

```text
Identifier
Display name
Description
Default value
Minimum value
Maximum value
Attack time
Release time
Composition mode
Debug category
```

Runtime state should be stored separately:

```text
FloatParameterState
├── CurrentValue
├── TargetValue
├── PreviousValue
├── ActiveContributions
└── LastUpdateTime
```

Do not store changing runtime values directly in the ScriptableObject asset. That risks confusing edit-time definitions with play-mode state.

---

# 7. Contributions and Modulation

## 7.1 Contribution Definition

A contribution maps a raw signal into a semantic parameter.

Example:

```text
Source signal: DepthMeters
Destination parameter: Darkness

Input range:
0 meters → 800 meters

Output range:
0.0 → 0.8

Response:
AnimationCurve

Blend mode:
Add

Weight:
1.0
```

The data flow is:

```text
Raw signal
→ normalize input
→ evaluate curve
→ remap output
→ apply weight
→ combine into parameter
```

---

## 7.2 Response Curves

Use `AnimationCurve` for authored nonlinear behavior.

For example, darkness may increase slowly through the upper ocean and much more rapidly after a threshold:

```text
Depth 0.0–0.3:
Minimal darkness change

Depth 0.3–0.7:
Gradual reduction

Depth 0.7–1.0:
Rapid descent into darkness
```

Curves are preferable to hard-coded easing functions because they expose the creative behavior in the Inspector.

---

## 7.3 Blend Modes

Support a limited set initially:

### Add

```text
result += contribution
```

Useful when several effects independently increase a parameter.

### Multiply

```text
result *= contribution
```

Useful for modifiers such as temporary suppression or amplification.

### Maximum

```text
result = max(result, contribution)
```

Useful for threat or tension, where one severe influence should dominate.

### Minimum

```text
result = min(result, contribution)
```

Useful for limiting or suppressing a value.

### Override

```text
result = contribution
```

Useful for scripted sequences and temporary forced states.

Overrides should support priority.

---

## 7.4 Recommended Evaluation Order

A parameter can evaluate contributions in this order:

```text
1. Begin with base value
2. Apply additive contributions
3. Apply maximum and minimum contributions
4. Apply multiplicative contributions
5. Apply highest-priority override
6. Clamp to parameter range
7. Apply attack/release smoothing
```

This should be clearly documented and visible in debugging tools.

---

# 8. Attack, Release, and Smoothing

Parameters should support separate attack and release behavior.

Example:

```text
Threat attack:   0.15 seconds
Threat release:  4.00 seconds
```

This means threat responds quickly when an enemy approaches but fades slowly after the enemy leaves.

That is generally more useful than one generic smoothing value.

Suggested behavior:

```csharp
float smoothingTime =
    targetValue > currentValue
        ? attackTime
        : releaseTime;
```

Consider supporting three smoothing modes:

* None
* Exponential
* SmoothDamp

Start with exponential smoothing because it behaves consistently and does not require maintaining velocity state.

---

# 9. Continuous Output Routes

## 9.1 Purpose

A continuous route maps a semantic parameter to a destination property.

Example:

```text
Darkness
→ response curve
→ output range
→ global Light2D intensity
```

Suggested route fields:

```text
Source parameter
Input range
AnimationCurve
Output range
Optional route smoothing
Update frequency
Output adapter
Enabled state
```

Usually the parameter itself should perform primary smoothing. Route-level smoothing should only be used when a destination requires different response behavior.

---

## 9.2 Initial Output Adapters

Implement explicit adapters rather than a completely generic reflection system.

Recommended initial adapters:

* `Light2DIntensityOutput`
* `UnityLightIntensityOutput`
* `AudioMixerFloatOutput`
* `AudioDirectorParameterOutput`
* `MaterialFloatOutput`
* `RendererPropertyBlockFloatOutput`
* `VolumeProfileFloatOutput`
* `AnimatorFloatOutput`
* `TransformScaleOutput`
* `ParticleEmissionOutput`

Explicit adapters provide:

* Better performance
* Type safety
* Cleaner Inspector interfaces
* Easier debugging
* Less risk of renamed property paths silently breaking bindings

A generic property adapter can be added later for experimentation.

---

# 10. Event Rules

## 10.1 Purpose

Continuous values must also be able to produce discrete events.

Example:

```text
When Tension rises above 0.75
AND SecondsSinceLastStinger exceeds 20
AND player is not in a safe zone
→ play audio stinger
→ play Feel feedback
→ begin cooldown
```

---

## 10.2 Rule Conditions

The initial rule system should support:

* Value rises above threshold
* Value falls below threshold
* Value enters range
* Value exits range
* Value remains above threshold for duration
* Value remains below threshold for duration
* Cooldown complete
* Boolean condition
* Probability roll
* One-shot-only rule

Later additions may include:

* Multiple parameter comparisons
* Sequence conditions
* Narrative flags
* Time windows
* Weighted event selection

---

## 10.3 Hysteresis

Threshold rules must support separate trigger and reset thresholds.

Example:

```text
Trigger:
Threat rises above 0.75

Re-arm:
Threat falls below 0.50
```

Without hysteresis, a value oscillating near the threshold can repeatedly trigger the same event.

Suggested fields:

```text
Trigger threshold
Reset threshold
Trigger direction
Minimum active duration
Cooldown
Initial armed state
```

---

## 10.4 Rule Actions

Implement a small action interface:

```csharp
public interface IDirectorAction
{
    void Execute(DirectorContext context);
}
```

Initial actions:

* Play an AudioDirector one-shot
* Play an AudioDirector stinger
* Start or stop an ambience layer
* Play an `MMF_Player`
* Invoke a UnityEvent
* Set a semantic parameter override
* Add a temporary parameter contribution
* Set an Animator trigger
* Send a game event
* Start a cooldown
* Activate or deactivate a GameObject

One rule should be able to execute several actions.

---

# 11. Feel Integration

Feel should be used for finite, authored feedback sequences.

Good Feel use cases:

* Light flashes
* Sonar pulses
* Camera shake
* Impact feedback
* Electrical flicker
* Temporary glow
* Chromatic distortion
* Particle bursts
* Short audio accents
* Time-scale effects
* Transform movement
* Combined audiovisual sequences

The director should decide when a feedback sequence occurs.

Example:

```text
Threat crosses critical threshold
        ↓
Director rule executes
        ↓
MMF_Player.PlayFeedbacks()
```

Feel should not be responsible for continuously calculating global environmental state.

---

## 11.1 Preventing Output Conflicts

Avoid having the director and Feel simultaneously write directly to the same property.

For example, do not let both systems independently overwrite:

```text
Light2D.intensity
```

Instead, use composited values:

```text
Final light intensity =
    environment baseline
    × temporary feedback multiplier
    + temporary flash addition
```

A component could expose channels such as:

```text
Baseline
Multiplier
Additive
Override
```

Then:

```text
EnvironmentDirector → Baseline
Feel                 → Multiplier or Additive
Component            → Final Unity property
```

This is especially useful for:

* Lights
* Glow intensity
* Camera noise
* Audio volume
* Shader distortion
* Post-processing intensity

Create a reusable component such as:

```text
ModulatedFloatTarget
```

Suggested calculation:

```text
finalValue =
    override active
        ? overrideValue
        : (baseline + additive) × multiplier
```

This will prevent different systems from fighting over destination values.

---

# 12. AudioDirector

## 12.1 Purpose

The AudioDirector provides a focused Unity-side audio layer for:

* Persistent ambience loops
* Crossfading ambience layers
* Grouping related sounds
* Mixer-bus routing
* Stingers
* One-shots
* Cooldowns
* Basic randomization
* Audio state transitions

It should not attempt to replicate every feature of FMOD or Wwise.

Its role is to give the EnvironmentDirector a clean, semantic API:

```csharp
audioDirector.SetParameter("Threat", 0.7f);
audioDirector.SetAmbienceInfluence(caveAmbience, 0.6f);
audioDirector.PlayStinger(dangerStinger);
audioDirector.PlayOneShot(hullCreak);
```

---

# 13. Audio Routing and Buses

Use Unity `AudioMixer` groups as buses.

Recommended initial hierarchy:

```text
Master
├── Music
├── Ambience
│   ├── Environmental Loops
│   ├── Localized Ambience
│   └── Interior Submarine
├── Stingers
├── SFX
│   ├── Player
│   ├── Weapons
│   ├── Enemies
│   └── Environment
├── UI
└── Voice
```

For the immediate system, the important groups are:

```text
Ambience
Stingers
SFX
Music
```

Avoid creating highly specific mixer groups for every individual sound. Groups should correspond to mixing and processing needs.

Examples:

* All deep ambience may need shared filtering.
* All stingers may need to duck ambience.
* Interior submarine loops may need common EQ and compression.
* Music may need independent user volume control.

---

## 13.1 Exposed Mixer Parameters

Expose only parameters that need runtime control.

Possible initial parameters:

```text
AmbienceVolume
MusicVolume
StingerVolume
DeepWaterLowPass
InteriorLowPass
ThreatDistortion
AmbienceDuck
```

Remember that Unity AudioMixer exposed volume parameters use decibels, not linear volume.

Use a helper conversion:

```csharp
public static float LinearToDecibels(float value)
{
    return value <= 0.0001f
        ? -80f
        : Mathf.Log10(value) * 20f;
}
```

Keep user settings separate from dynamic modulation.

For example:

```text
Final ambience volume =
    user ambience setting
    × environment ambience modulation
    × temporary ducking
```

Do not overwrite the user's configured volume with environmental values.

---

# 14. Ambience Layers

## 14.1 Ambience Layer Definition

Create a ScriptableObject:

```text
AmbienceLayerDefinition
```

Suggested fields:

```text
Name
Audio clips
Mixer group
Loop mode
Base volume
Fade-in time
Fade-out time
Spatial mode
Random start position
Pitch range
Volume range
Maximum simultaneous instances
Restart behavior
Priority
```

An ambience layer may represent:

* General underwater bed
* Deep-ocean pressure tone
* Cave ambience
* Machinery interior
* Nearby thermal vent
* Supernatural region
* Creature territory
* Calm safe-zone ambience

---

## 14.2 Persistent Global Layers

Persistent global layers should use pooled or dedicated AudioSources managed by the AudioDirector.

Examples:

```text
Base underwater ambience
Deep-water tonal layer
Submarine interior machinery
Tension drone
Supernatural ambience
```

These layers can remain playing silently rather than repeatedly starting and stopping.

Benefits:

* Reliable phase continuity
* Smooth fades
* No startup latency
* Easier crossfading
* Fewer lifecycle edge cases

Do not keep hundreds of layers permanently playing. This approach is suitable for a limited set of primary ambience layers.

---

## 14.3 Influence-Based Volume

Each ambience layer should receive one or more influences.

Example:

```text
Cave ambience influence =
    cave proximity
    × cave-region occupancy
```

The AudioDirector converts the resulting influence into volume:

```text
Influence 0 → silent
Influence 1 → configured maximum volume
```

The layer definition should support an `AnimationCurve` so the audible response does not need to be linear.

---

## 14.4 Proximity-Based Ambience

For an environmental ambience emitter:

```text
Thermal vent
Ancient structure
Creature nest
Mechanical wreckage
```

Represent it with a component such as:

```text
AmbienceInfluenceEmitter
```

Suggested fields:

```text
Ambience layer
Inner radius
Outer radius
Response curve
Priority
Optional occlusion multiplier
Optional directional behavior
```

Basic influence:

```text
distance <= inner radius:
    influence = 1

distance >= outer radius:
    influence = 0

otherwise:
    influence = curve(normalized distance)
```

The emitter reports influence to the AudioDirector rather than manually controlling its own AudioSource.

This allows several nearby emitters to contribute to the same ambience layer.

---

## 14.5 Combining Multiple Emitters

For multiple emitters using the same ambience layer, support these composition modes:

### Maximum

Use the strongest nearby emitter.

Recommended default for environmental proximity ambience.

```text
final influence = max(all emitter influences)
```

### Additive clamped

Several emitters collectively increase the layer.

```text
final influence = clamp01(sum(influences))
```

### Weighted average

Useful only in more specialized cases.

Start with `Maximum` and `AdditiveClamped`.

---

## 14.6 Ambience Crossfading

Crossfading can be implemented in two ways.

### Independent layer mixing

Each layer has its own influence.

```text
Open ocean: 0.7
Cave:       0.3
```

This allows overlap and is preferable for organic ambience.

### Exclusive ambience groups

Only one member of a group should dominate.

Example:

```text
Primary biome ambience group:
- Open ocean
- Cave
- Abyss
- Ruins
```

Normalize the group weights:

```text
Open ocean: 0.7
Cave:       0.3
Abyss:      0.0
Ruins:      0.0
```

Create an optional:

```text
AmbienceGroupDefinition
```

Suggested fields:

```text
Group name
Member layers
Exclusive or additive
Normalization behavior
Group fade time
Priority
```

Do not force all ambience into exclusive groups. Many layers should remain additive.

For example:

```text
Primary biome ambience: exclusive
Submarine machinery: additive
Threat drone: additive
Supernatural texture: additive
```

---

# 15. One-Shots

## 15.1 One-Shot Definition

Create:

```text
AudioOneShotDefinition
```

Suggested fields:

```text
Audio clips
Mixer group
Volume range
Pitch range
Spatial blend
Minimum distance
Maximum distance
Cooldown
Maximum concurrent instances
Retrigger behavior
Selection mode
Optional delay range
```

Selection modes:

* Random
* Shuffle bag
* Sequential
* Weighted random

Use a shuffle bag for repeated environmental sounds to reduce obvious repetition.

---

## 15.2 One-Shot Playback Modes

Support:

### Global non-spatial

Examples:

* UI notification
* Narrative accent
* Global horror sting

### Spatial at position

Examples:

* Hull creak
* Rockfall
* Creature sound
* Distant impact

### Attached to transform

Examples:

* Moving enemy
* Machinery component
* Sonar source

A pooled AudioSource system is appropriate for these.

---

# 16. Stingers

## 16.1 Stinger Definition

A stinger is a specialized one-shot with stronger orchestration rules.

Create:

```text
AudioStingerDefinition
```

Suggested fields:

```text
Audio clips
Mixer group
Priority
Cooldown
Global cooldown category
Volume range
Pitch range
Ambience duck amount
Duck attack
Duck release
Can interrupt lower priority
Required silence window
Selection mode
```

---

## 16.2 Stinger Categories

Suggested categories:

```text
Threat
Discovery
Supernatural
Narrative
Damage
Sonar
```

Each category can maintain its own cooldown.

Also maintain a global stinger cooldown to avoid exhausting the player with frequent dramatic events.

Example:

```text
Per-stinger cooldown:       60 seconds
Threat-category cooldown:   25 seconds
Global stinger cooldown:    12 seconds
```

All applicable cooldowns must be satisfied.

---

## 16.3 Time Since Last Stinger

The AudioDirector should expose:

```text
SecondsSinceAnyStinger
SecondsSinceThreatStinger
SecondsSinceSpecificStinger
```

These can be raw signals consumed by the EnvironmentDirector.

This creates a useful feedback loop:

```text
Time since last stinger
→ rule eligibility
→ play stinger
→ timer resets
```

Avoid using time since stinger as a direct guarantee that one must play. It should be one condition among threat, pacing, location, and randomness.

---

## 16.4 Stinger Ducking

A stinger can temporarily reduce ambience or music.

Initial implementation options:

### AudioMixer snapshot

Transition into a temporary snapshot that reduces ambience.

### Exposed mixer parameter

AudioDirector animates an `AmbienceDuck` mixer parameter.

The exposed parameter approach may be simpler for the initial system.

Suggested compositing:

```text
Final ambience bus level =
    user volume
    + environmental level
    + ducking level in decibels
```

The stinger should own the ducking envelope:

```text
Fast attack
Hold during important portion
Slow release
```

---

# 17. Random Environmental Audio

Create a lightweight system for semi-random environmental one-shots.

Examples:

* Hull creaks
* Distant groans
* Metallic ticks
* Sonar-like animal calls
* Rock movement
* Water-pressure sounds
* Unidentified distant impacts

Create:

```text
RandomAudioEmitterDefinition
```

Suggested fields:

```text
One-shot pool
Minimum interval
Maximum interval
Probability
Required parameter ranges
Forbidden parameter ranges
Spatial spawn mode
Maximum simultaneous sounds
```

Example:

```text
Hull creaks:
- HullStress must exceed 0.3
- Minimum interval 8 seconds
- Maximum interval 30 seconds
- Frequency increases with HullStress
```

Prefer event density modulation over simply increasing volume.

For example:

```text
Low HullStress:
one creak every 30–60 seconds

High HullStress:
one creak every 4–12 seconds
```

This creates a more natural sense of environmental activity.

---

# 18. Audio Voices and Pooling

Use separate pools for:

* General non-spatial one-shots
* Spatial one-shots
* Attached one-shots
* Persistent ambience layers

Each playback definition should specify a concurrency policy:

```text
Allow overlap
Reject new
Stop oldest
Stop quietest
Restart existing
```

Examples:

* Rapid impacts: allow overlap within a reasonable limit
* Long horror stinger: reject or replace lower-priority stinger
* Repeated machinery clank: stop oldest
* Unique narrative cue: reject duplicate

The debug interface should show active voice counts by group.

---

# 19. Music Scope

Keep the first version of music support minimal.

The AudioDirector should initially support:

* Starting and stopping a music layer
* Crossfading between music layers
* Modulating music bus volume
* Optional tension stem
* Playing music stingers
* Preventing conflicting music states

Do not initially build:

* Beat-synchronous transitions
* Bar-aligned transitions
* Procedural musical recombination
* Tempo-aware scheduling
* Harmonic compatibility systems

Those are worth adding only if the soundtrack design requires them.

A basic model:

```text
Music state:
- None
- Exploration
- Threat
- Narrative
- Boss

Optional layers:
- Base
- Tension
- Percussion
- Supernatural texture
```

The EnvironmentDirector can drive a `MusicTension` parameter while the AudioDirector manages the actual layer volumes.

---

# 20. Audio State Versus Audio Parameters

Use continuous parameters for gradual values:

```text
Threat = 0.64
Depth = 0.82
SupernaturalInfluence = 0.30
```

Use discrete state for categorical conditions:

```text
Biome = Cave
PlayerLocation = Interior
NarrativeMode = Scripted
CombatState = Boss
```

The AudioDirector should support both.

Do not represent every categorical condition as a float. Likewise, do not create many discrete states where continuous blending would sound better.

---

# 21. Temporary Parameter Modifiers

Systems should be able to add temporary influences without changing the underlying base state.

Example:

```text
Sonar pulse:
- Reduce Darkness for 0.8 seconds
- Increase SonarIntensity for 1.5 seconds
- Add temporary supernatural reveal influence
```

Create a runtime concept such as:

```text
ParameterModifier
```

Suggested fields:

```text
Parameter
Blend mode
Value
Attack
Hold
Release
Priority
Owner
```

The returned modifier should have a handle so it can be removed or updated.

Example:

```csharp
ParameterModifierHandle handle =
    director.AddModifier(modifierDefinition);
```

This will also help scripted sequences temporarily take control without permanently changing parameter definitions.

---

# 22. Priority and Ownership

Every temporary contribution, output override, and long-running event should have an owner.

Examples:

```text
Owner:
- Enemy instance
- Trigger volume
- Scripted sequence
- Sonar system
- Narrative controller
```

When the owner is destroyed or disabled, its contributions should be removed automatically.

This prevents stale modifiers and ambience influences from remaining active after an object disappears.

Use disposable handles or registration tokens:

```csharp
IDisposable registration =
    director.RegisterContribution(...);
```

---

# 23. ScriptableObject Asset Model

Suggested asset types:

```text
DirectorParameterDefinition
SignalMappingDefinition
ContinuousRouteDefinition
EventRuleDefinition
AmbienceLayerDefinition
AmbienceGroupDefinition
AudioOneShotDefinition
AudioStingerDefinition
RandomAudioEmitterDefinition
```

Organize them into folders:

```text
Audio/
├── Ambience/
├── OneShots/
├── Stingers/
├── Groups/
└── RandomEmitters/

Director/
├── Parameters/
├── Mappings/
├── Routes/
└── Rules/
```

Use stable IDs rather than relying entirely on asset names.

---

# 24. Profiles

Consider introducing higher-level profiles after the basic system works.

Examples:

```text
OpenOceanProfile
CaveProfile
AbyssProfile
RuinsProfile
BossEncounterProfile
SafeHubProfile
```

A profile could provide:

* Parameter modifiers
* Active ambience groups
* Light baselines
* Audio routing preferences
* Allowed random events
* Post-processing defaults

Profiles should layer onto the director rather than replacing all current state instantly.

For example:

```text
Current biome profile:
Cave = 0.7 influence

Nearby supernatural profile:
Ancient Ruins = 0.4 influence
```

This supports blended transitions between regions.

---

# 25. Spatial Region System

Create region components that contribute influences based on player position.

Possible shapes:

* Collider volume
* Distance from point
* Distance from spline
* Distance from surface
* Custom procedural region

A region may contribute to:

```text
Biome influence
Darkness
Ambience layers
Reverb
Supernatural influence
Safety
Music eligibility
```

A generic component might be:

```text
DirectorInfluenceVolume
```

Suggested fields:

```text
Collider
Blend distance
Inside value
Outside value
AnimationCurve
Parameter contributions
Ambience contributions
Priority
```

This will likely be broadly useful in the procedural underwater world.

---

# 26. Debugging and Visualization

The debug tooling is a core requirement, not optional polish.

A unified system will become difficult to reason about unless its internal state is visible.

Create a runtime debug panel showing:

```text
PARAMETERS

Darkness                    0.72
Target                      0.78
Depth contribution          0.60
Cave contribution           0.18
Sonar modifier             -0.10

Threat                      0.41
Nearest enemy               0.41
Recent damage               0.20
Composition                 Maximum
```

```text
AUDIO

Active ambience:
- Base Underwater            0.80
- Cave Interior              0.44
- Deep Pressure              0.61
- Threat Drone               0.20

Active one-shots:            3
Active stinger:              None
Global stinger cooldown:     6.2 seconds
```

```text
RULES

ThreatStinger:
Armed:                       Yes
Threshold met:               No
Cooldown ready:              Yes

SupernaturalFlash:
Armed:                       No
Reset threshold:             Pending
```

Also display:

* Current raw signals
* Parameter targets and smoothed values
* Active temporary modifiers
* Output route values
* Registered proximity emitters
* Audio voice usage
* Active exclusive groups
* Rule cooldowns
* Feel players triggered recently

---

## 26.1 Scene Gizmos

Add optional gizmos for:

* Proximity ambience inner and outer radii
* Influence volumes
* Active nearest-target queries
* Current player influence
* Region blend boundaries

Use clear labels and allow gizmos to be disabled by category.

---

## 26.2 Manual Testing Panel

Provide sliders and buttons for:

* Setting raw signal overrides
* Forcing semantic parameter values
* Playing each stinger
* Starting each ambience layer
* Triggering each rule
* Playing each Feel integration
* Simulating safe and dangerous states
* Freezing parameter evaluation

This will greatly accelerate audio and atmosphere iteration.

---

# 27. Update Loop

Suggested runtime order:

```text
1. Sample due raw signals
2. Evaluate continuous signal mappings
3. Compose semantic parameter targets
4. Apply parameter attack/release smoothing
5. Evaluate event rules
6. Update continuous outputs
7. Update AudioDirector
8. Update debugging data
```

Event rules should generally evaluate against the newly smoothed parameter values unless a specific rule needs raw target values.

Use unscaled time selectively. Most environmental modulation should probably follow scaled game time, while menu fades and certain audio transitions may need unscaled time.

Make time mode explicit in definitions where relevant.

---

# 28. Performance Guidelines

The system should be efficient, but avoid premature optimization.

Good defaults:

* Evaluate cheap parameters every frame.
* Evaluate proximity searches at fixed intervals.
* Cache component references.
* Avoid reflection in hot paths.
* Avoid per-frame allocations.
* Pool one-shot AudioSources.
* Use `MaterialPropertyBlock` rather than instantiating materials.
* Keep ScriptableObjects as definitions, not mutable runtime storage.
* Use stable arrays or lists built during initialization.
* Avoid unnecessary LINQ in update loops.
* Update outputs only when values change beyond a small epsilon.

A float modulation system with dozens or even hundreds of routes is not inherently expensive. Physics queries, audio voices, material instantiation, and uncontrolled component searches are more likely bottlenecks.

---

# 29. Suggested Class Structure

```text
EnvironmentDirector
├── DirectorRuntimeContext
├── SignalScheduler
├── ParameterRuntimeRegistry
├── ContinuousRouteEvaluator
├── EventRuleEvaluator
└── DirectorDebugState
```

```text
Signals
├── FloatSignalComponent
├── TransformDepthSignal
├── TransformSpeedSignal
├── NearestTargetDistanceSignal
├── TimerSignal
├── ManualFloatSignal
└── TriggerVolumeInfluenceSignal
```

```text
Parameters
├── DirectorParameterDefinition
├── ParameterRuntimeState
├── ParameterContribution
├── ParameterModifier
└── ParameterModifierHandle
```

```text
Outputs
├── DirectorFloatOutput
├── Light2DIntensityOutput
├── AudioMixerFloatOutput
├── MaterialPropertyBlockOutput
├── AnimatorFloatOutput
└── CompositeFloatTarget
```

```text
Rules
├── DirectorRuleDefinition
├── DirectorCondition
├── ThresholdCondition
├── CooldownCondition
├── ProbabilityCondition
├── DirectorAction
├── PlayFeelAction
├── PlayStingerAction
└── AddParameterModifierAction
```

```text
Audio
├── AudioDirector
├── AudioVoicePool
├── AmbienceLayerRuntime
├── AmbienceInfluenceHandle
├── StingerRuntime
├── AudioOneShotDefinition
├── AudioStingerDefinition
├── AmbienceLayerDefinition
└── AmbienceGroupDefinition
```

---

# 30. Implementation Phases

## Phase 1: Core Parameter Runtime

### Objectives

Build the smallest viable continuous modulation system.

### Tasks

* Create `EnvironmentDirector`
* Create parameter definitions
* Create runtime parameter states
* Implement additive, maximum, multiply, and override composition
* Implement attack/release smoothing
* Add manual parameter debugging
* Add runtime parameter display

### Validation

The system can combine depth and a manual cave influence into a stable `Darkness` parameter.

---

## Phase 2: Signal Sources

### Objectives

Connect actual gameplay values.

### Tasks

* Implement depth signal
* Implement speed signal
* Implement timer signal
* Implement proximity signal
* Implement influence-volume signal
* Add fixed-frequency signal scheduling
* Add signal debug values

### Validation

`Darkness`, `Threat`, and `Tension` respond to live gameplay conditions.

---

## Phase 3: Continuous Outputs

### Objectives

Drive visible properties from semantic parameters.

### Tasks

* Implement Light2D output
* Implement material-property output
* Implement AudioMixer parameter output
* Implement composite float targets
* Add route curves and output ranges
* Add output epsilon checks
* Add output debug display

### Validation

Depth modifies lighting and a shader property while threat modifies an AudioMixer parameter.

---

## Phase 4: Minimal AudioDirector

### Objectives

Support ambience layers, one-shots, stingers, and buses.

### Tasks

* Create AudioMixer bus hierarchy
* Create persistent ambience source management
* Implement ambience layer definitions
* Implement ambience influence registration
* Implement ambience fades
* Implement exclusive ambience groups
* Build pooled one-shot playback
* Implement stinger playback
* Implement basic stinger cooldowns
* Implement ambience ducking
* Add audio debugging

### Validation

Approaching a cave crossfades cave ambience while retaining the base underwater layer. A threat rule can play a stinger and temporarily duck ambience.

---

## Phase 5: Event Rules and Feel

### Objectives

Trigger finite audiovisual sequences from parameter state.

### Tasks

* Implement rising and falling threshold conditions
* Implement hysteresis
* Implement cooldown conditions
* Implement duration-above-threshold condition
* Implement rule actions
* Add Feel player action
* Add AudioDirector stinger action
* Add temporary parameter-modifier action
* Add rule debug state

### Validation

High threat can trigger a stinger and Feel sequence once, then waits for both cooldown and reset conditions.

---

## Phase 6: Spatial Audio Influence

### Objectives

Author environmental ambience around world objects and regions.

### Tasks

* Implement point-based ambience emitter
* Implement collider-based influence volume
* Support maximum and additive contribution modes
* Add visualization gizmos
* Add registration cleanup on disable or destroy
* Add procedural-spawn compatibility

### Validation

Multiple thermal vents contribute to one ambience layer without creating duplicate uncontrolled loops.

---

## Phase 7: Profiles and Higher-Level Authoring

### Objectives

Make biome and encounter configuration efficient.

### Tasks

* Create environment profiles
* Add weighted profile influence
* Add biome ambience groups
* Add profile-based parameter contributions
* Add scripted profile overrides
* Add authoring presets

### Validation

The player can move smoothly between open ocean, cave, and supernatural ruin profiles.

---

## Phase 8: Polish and Tooling

### Objectives

Improve usability after runtime semantics are stable.

### Tasks

* Custom Inspectors
* Better runtime debug window
* Parameter history graphs
* Rule execution history
* Audio voice visualization
* Batch validation of missing references
* Duplicate ID validation
* Asset creation menus
* Presets for common mappings
* Optional editor graph view

Do not build a node graph before reaching this phase.

---

# 31. First Vertical Slice

Build one complete use case before expanding the framework.

Recommended slice:

## Inputs

```text
Depth
Cave proximity
Enemy proximity
Time since last stinger
```

## Semantic parameters

```text
Darkness
Threat
Tension
```

## Audio

```text
Base underwater ambience
Deep-pressure ambience
Cave ambience
Threat drone
One threat stinger
One hull-creak pool
```

## Visual outputs

```text
Global Light2D intensity
Submarine exterior glow
Background shader darkness
```

## Feel output

```text
Threat spike:
- brief light flicker
- camera shake
- glow pulse
```

## Behavior

```text
As depth increases:
- Darkness rises
- Base light decreases
- Deep-pressure ambience increases
- Hull-creak frequency increases

As the player approaches a cave:
- Cave ambience fades in
- Open-water ambience fades down
- Darkness receives an additional contribution

As an enemy approaches:
- Threat rises quickly
- Threat drone fades in
- Tension rises
- At a high threshold, an eligible stinger plays
- Feel triggers a short audiovisual spike

As the enemy leaves:
- Threat and tension release slowly
- Threat drone fades gradually
- The stinger rule only re-arms below its reset threshold
```

Completing this slice will expose most of the architectural problems before the system grows.

---

# 32. Recommended Initial Parameters

Keep the first working set small:

## Darkness

Sources:

* Depth
* Cave influence
* Supernatural influence
* Temporary sonar reduction

Outputs:

* Light intensity
* Background shader
* Deep ambience parameter
* Exterior glow

## Threat

Sources:

* Nearest enemy proximity
* Enemy alert state
* Incoming projectile danger
* Scripted threats

Outputs:

* Threat ambience
* Camera noise
* Subtle warning-light behavior

## Tension

Sources:

* Threat
* Recent damage
* Supernatural influence
* Narrative escalation

Outputs:

* Tension drone
* Stinger eligibility
* Visual instability
* Feel event rules

## Supernatural Influence

Sources:

* Proximity to anomaly
* Narrative phase
* Scripted region
* Recent supernatural event

Outputs:

* Distortion
* Specialized ambience
* Light irregularity
* Event eligibility

---

# 33. Important Design Decisions

## Do not drive everything from raw depth

Depth is an input, not the final creative state.

A deep region may be brightly illuminated, while a shallow cave may be extremely dark. A semantic `Darkness` parameter handles this correctly.

## Do not let every system own its own timers

Centralize stinger cooldowns, event timing, and shared pacing constraints.

## Do not let systems overwrite the same destination property

Use composed target components with baseline, additive, multiplier, and override channels.

## Do not make all audio spatial emitters self-contained

Let emitters report influence to an AudioDirector that owns the actual layers and voice limits.

## Do not start with a graph editor

Stabilize runtime semantics and authoring data first.

## Do not over-normalize the parameter set

Five meaningful parameters are better than fifty thin wrappers around raw gameplay values.

## Do not make every response linear

Use curves, attack/release, thresholds, and density modulation.

---

# 34. Additional Recommendations

## Add parameter recording

Allow the debug system to record parameter values over time.

This will make it easier to diagnose:

* Sudden jumps
* Slow response
* Oscillation
* Repeated threshold crossings
* Incorrect contribution weights

A basic rolling graph for the last 30–60 seconds would be highly valuable.

## Add deterministic random support

For procedural levels and repeatable debugging, allow audio randomization to use an optional seeded random source.

## Separate authored intensity from user volume

Dynamic ambience intensity should never replace the player's audio settings.

## Add accessibility controls later

Potential settings:

* Reduce camera shake
* Reduce flashing
* Reduce dynamic audio range
* Reduce horror stinger frequency
* Disable sudden loud sounds

These can modify output intensity without changing gameplay state.

## Preserve headroom

Avoid allowing all layers to reach maximum volume simultaneously. Define layer-level maximum gains and bus processing with combined worst-case playback in mind.

## Build silence intentionally

The system should be able to reduce activity rather than always adding more. Silence or near-silence can be one of the most powerful responses to tension or discovery.

Consider a semantic parameter such as:

```text
AudioSparsity
```

or implement temporary reductions through parameter modifiers.

## Distinguish intensity from density

For repeated sounds, increasing the number of events is often more effective than merely increasing volume.

Examples:

```text
Hull stress:
- creaks become more frequent

Supernatural influence:
- strange details occur more often

Threat:
- hostile pulses become denser
```

## Add event budgets

Introduce optional pacing budgets:

```text
Maximum major stingers per minute
Maximum horror events per region
Minimum quiet time after narrative audio
Maximum simultaneous environmental one-shots
```

This prevents independent rules from collectively overwhelming the player.

## Support narrative locks

Narrative sequences should be able to:

* Suppress random stingers
* Lower unrelated ambience
* Reserve the stinger bus
* Override selected semantic parameters
* Restore previous state cleanly afterward

## Add context snapshots for bug reports

The debugger should be able to produce a compact text dump:

```text
Current signals
Current parameters
Active contributions
Active ambience
Current cooldowns
Recently executed rules
```

This will be valuable when diagnosing emergent atmospheric behavior.

---

# 35. Definition of Success

The system is successful when:

* Gameplay systems publish facts without knowing about presentation details.
* Audio and visual systems respond to shared semantic parameters.
* Multiple influences can combine predictably.
* Continuous transitions are smooth.
* Triggered events do not spam or oscillate.
* Feel remains useful without becoming the global state manager.
* Audio loops crossfade without scene-specific bespoke scripts.
* Proximity ambience is easy to author.
* Stingers and one-shots obey global pacing rules.
* Runtime state can be inspected and explained.
* New modulation relationships can usually be created through data rather than custom code.
* The architecture remains small enough to understand and maintain.

The desired end result is not merely a float-mapping utility. It is a compact orchestration layer that gives the game a unified, debuggable language for controlling atmosphere.
