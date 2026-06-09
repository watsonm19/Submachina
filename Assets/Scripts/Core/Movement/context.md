# Movement

Generic, reusable movement controllers for characters and craft.

## DriftController2D
A swiss-army 2D mover for testing and experimentation, driven by the new Input System
(Vector2 move action — WASD / left stick, defaults to `InputSystem_Actions` `Player/Move`).

Requires a `Rigidbody2D`. Integrates its own velocity each `FixedUpdate` using a
**thrust + exponential drag + optional directional gravity** model, then writes back to
`Rigidbody2D.linearVelocity` (reading the live velocity first so collisions are honored).

Feel is set by three knobs:
- **acceleration** — how fast input ramps velocity toward `maxSpeed`.
- **linearDamping** — the inertia/drift knob: `0` = frictionless space coast, high = water-thick.
- **gravity** (optional) — a constant directional force the craft fights against; off = neutral
  buoyancy / zero-g, so it suits side-view or top-down equally.

High accel + high damping = sharp/arcade; low accel + low damping = floaty drift.

Extras: optional rotate-toward-movement facing, diagonal normalization, and a small public
API (`AddImpulse`, `SetVelocity`, `SetGravityEnabled`) so other systems (dashes, knockback,
buoyancy zones) can compose with it. Odin `Reset()` / "Configure Rigidbody2D For Drift" button
sets `gravityScale = 0` and `freezeRotation` so Unity gravity doesn't double up.
