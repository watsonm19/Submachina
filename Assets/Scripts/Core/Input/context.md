# Input

Components for translating player input into world-space data that other systems consume.

## Pointer System (`Gameplay.Pointer` namespace)

### PointerWorldTracker
Projects the screen-space pointer position onto a configurable Z-depth plane using the new Input System. Attach to an empty GameObject — it moves itself to the pointer each frame. Publishes position data to PointerWorldBus (static event bus) and also exposes three UnityEvents (`onPositionUpdated`, `onPrimaryInput`, `onSecondaryInput`) for direct inspector wiring.

### PointerWorldBus
Static pub/sub event bus for pointer world-position data. PointerWorldTracker publishes three events (`OnPositionUpdated`, `OnPrimaryInput`, `OnSecondaryInput`) to this bus every frame and on input. Any component can subscribe without needing a scene reference — follows the same pattern as BeatHitBus, ResourceBus, etc.

### PointerWorldListener
Prefab-friendly subscriber for PointerWorldBus. Configurable boolean toggles select which events to listen to (position, primary input, secondary input). Sets its own transform position when subscribed events fire. Designed so prefabs can receive pointer data without referencing scene-specific objects. See `Grid/Movers/context.md` for the prefab grid-mover wiring pattern.

### InputTargetPlacer
Stamps this transform's position from a **position source** (any Transform, e.g. PointerWorldTracker) when an InputAction fires. Bridges input and the follower system — the follower targets this object, and it only moves on player command. Supports single-press placement or continuous-while-held. Fires a `UnityEvent onTargetPlaced` each time a position is stamped.

## Legacy Scripts (no namespace)

### MoveToMouseClick
Uses the old `UnityEngine.Input` API and Unity Atoms (`Vector3Event`) to raise a world-position event on left click. Legacy — prefer PointerWorldTracker + PointerWorldBus for new work.

### OneButtonInputEvent
Uses the old `UnityEngine.Input.anyKeyDown` and Unity Atoms (`VoidEvent`) to raise an event on any key press. Legacy — prefer the new Input System for new work.
