# Combat

Generic, reusable damage plumbing. Damage sources build a `HitData` payload and hand it to a
target's `HitReceiver`; the receiver gates it and fans it out to reactions.

## The pipeline

```
[damage source] → HitReceiver.ReceiveHit(HitData) → gates (invulnerable / cooldown)
                                                  → events + MMF feedbacks
                                                  → Knockback2D.ApplyKnockback(hitData)
                                                  → Health.TakeDamage(hitData)
```

`HitData` carries `damage`, `knockbackForce`, `hitDirection`, `hitPoint`, and `source`.
The direction is world-space, pointing from the attacker toward the target.

## HitReceiver
The single entry point for "something hit this object". Gates on `invulnerable` and
`hitCooldown`, then returns whether the hit was accepted — sources use that return value to
decide their own follow-up (e.g. `DashRam` only pays self-damage on an accepted hit).

Two auto-forward options, both **on by default**, remove the need to wire the common case
in the Inspector. Both look for their component on the *same GameObject*:
- **autoApplyDamageToHealth** → `Health.TakeDamage(hitData)`. Skipped automatically if
  `onHitReceived` is already wired to that same `Health.TakeDamage` in the Inspector, so
  legacy prefabs (`RammerEnemy`, `JellyfishCreature 2`) don't take double damage.
- **autoApplyKnockback** → `Knockback2D.ApplyKnockback(hitData)`.

Knockback is applied *before* damage so a killing blow still starts the body moving.

## Health
HP, death, and the low-health threshold. Writes normalized HP to an optional `FloatVariable`
atom (`currentHealth`) so UI can subscribe without a reference back. `DeathBehavior` picks
Destroy / Deactivate / EventsOnly.

## Knockback2D
Rigidbody2D shove. **Its real job is arbitrating control of the body, not just applying force.**

Most AI here (everything under `EnemyBase`) steers by assigning `Rb.linearVelocity` every
`FixedUpdate`, so a raw `AddForce` would be erased before it moved anything. So `Knockback2D`
opens a **control window** (`knockbackDuration`) and publishes `IsBeingKnockedBack`;
`EnemyBase` checks that flag and suspends `UpdateAI()` for the duration. Side effect: state
transitions pause too, so a hit naturally reads as a brief stun.

Key knobs:
- **massIndependent** (default on) — `knockbackForce` behaves as a speed in u/s, identical
  across creature masses. Off = true `AddForce(Impulse)`, physically honest but needs
  per-creature tuning.
- **overrideVelocity** (default on) — zeroes existing motion first, so a creature charging
  at you still visibly flies back instead of the shove being cancelled out.
- **knockbackResistance** (0-1) — per-creature damping, runtime-settable via `SetResistance()`
  for upgrades or armored phases.
- **applyCustomDrag / knockbackDrag** — creature bodies mostly run zero linear damping, so
  the component does its own exponential decay by default.
- **restingSpeedThreshold** — returns control early once the shove has settled.
- **scaleWithDamage** (off) — bigger hits shove harder, clamped by `maxDamageMultiplier`.

Repeat hits accumulate and *extend* the window rather than shortening it, so a flurry keeps
the target pinned.

### Death launches
Knockback is applied *before* damage, so on a killing blow the body already carries the shove
when `Health.Die()` fires. `EnemyBase.OnDeath` detects that (`launchOnKnockbackDeath`, default
on) and skips its `killVelocityOnDeath` wipe, so the corpse flies off instead of freezing —
the payoff hit reads as a real kill. Every other kind of death still stops the corpse dead, so
it never coasts away on its own AI movement vector. The window is closed either way, so the
corpse coasts freely; add `Rigidbody2D.linearDamping` if you want the launch to decelerate.

### Body reaction
`EnemyBase` listens to `Knockback2D.onKnockbackStart` and flinches every `ChainSimulator`
under the creature — `chainReaction` (Limp / FreezePose / None) plus its duration, in the
"Knockback Reaction" foldout. Without it, a chain in `FacingMode.Velocity` re-aims at the
knockback direction and keeps undulating, so the creature looks like it turned around and
swam off. See `Core/ProceduralAnimation/context.md`. A death launch re-fires the reaction
with `chainReactionDeathDuration` so the corpse never straightens out mid-flight.

## Setup for a knockable creature
`Rigidbody2D` + `HitReceiver` + `Health` + `Knockback2D` on the same GameObject. Nothing to
wire — both damage sources already populate `HitData.knockbackForce`.

## Legacy — 3D, unused
`Knockback.cs`, `MeleeHitbox.cs`, `MeleeWeaponController.cs`, `AttackMotionData.cs`, and
`DamageOnTouch.cs` came from an earlier 3D project. They use `Collider`/`OnTriggerEnter` and
`CollisionCaster`, none of which apply to this 2D game, and nothing in Submachina references
them. `Knockback2D` replaces `Knockback`. Safe to delete as a group once confirmed unneeded.

## Live damage sources (in Submachina, not here)
- `PlayerAttack` — turret-aimed cone via `OverlapCircleAll` + dot-product filter.
  Note: its `knockbackForce` defaults to **0** and is unset on the prefab — raise it (or the
  `SubStats.KnockbackForce` upgrade) before the swing shoves anything.
- `DashRam` — front-mounted shield trigger, gated on `Sub.Physics.IsDashing`.
  Ships with `knockbackForce: 6`.
