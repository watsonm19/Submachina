using System;
using UnityEngine;
using DG.Tweening;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Shared plumbing for all sea creature enemies.
     *
     * Handles the boilerplate that every enemy type needs: physics setup,
     * submarine targeting, health wiring, O2 death drops, sprite flipping,
     * and the intent indicator. Subclasses only implement their state machine
     * via UpdateAI().
     *
     * Subclass checklist:
     *   1. Extend EnemyBase.
     *   2. Implement UpdateAI() with your state machine.
     *   3. Override OnDeath() if you need extra death behavior — call base.OnDeath() first.
     *   4. Override CurrentState to expose your state enum as a string for the debug inspector.
     *   5. Add your own gizmo method (OnDrawGizmosSelected) for detection radii.
     */
    [RequireComponent(typeof(Rigidbody2D))]
    public abstract class EnemyBase : MonoBehaviour
    {
        // =====================
        // Death Drops
        // =====================

        [FoldoutGroup("Death Drops")]
        [Tooltip("Drop configurations spawned on death. Each entry is a drop type " +
                 "(O2, scrap, etc.) with its own prefab, count, and type-specific settings. " +
                 "Use the type picker to add entries.")]
        [SerializeReference]
        private DropConfig[] deathDrops = Array.Empty<DropConfig>();

        // =====================
        // Intent Indicator
        // =====================

        [FoldoutGroup("Intent Indicator")]
        [Tooltip("Child GameObject shown above the enemy when telegraphing intent " +
                 "(e.g., a '!' sprite). Driven by SetIntentIndicator().")]
        [SerializeField] private GameObject intentIndicator;

        [FoldoutGroup("Intent Indicator")]
        [Tooltip("Scale punch size when the indicator first appears.")]
        [SerializeField, Min(0f)] private float intentPunchScale = 0.4f;

        [FoldoutGroup("Intent Indicator")]
        [Tooltip("Duration of the indicator's pop-in punch animation.")]
        [SerializeField, Min(0.05f)] private float intentPunchDuration = 0.15f;

        // =====================
        // Death Behavior
        // =====================

        [FoldoutGroup("Death Behavior")]
        [Tooltip("Zero out the Rigidbody's velocity on death so the corpse stops dead " +
                 "instead of coasting on its last movement vector. Note that a knockback " +
                 "death launch overrides this — see Launch On Knockback Death.")]
        [SerializeField] private bool killVelocityOnDeath = true;

        [FoldoutGroup("Death Behavior")]
        [Tooltip("When the killing blow carries knockback, let the corpse fly off with that " +
                 "momentum instead of stopping dead — the payoff hit reads as a real kill. " +
                 "Takes priority over Kill Velocity On Death, which still applies to every " +
                 "other kind of death (so a corpse never coasts off on its own AI movement). " +
                 "The corpse coasts freely; give the Rigidbody2D some Linear Damping if you " +
                 "want the launch to decelerate rather than travel at a constant speed.")]
        [SerializeField] private bool launchOnKnockbackDeath = true;

        [FoldoutGroup("Death Behavior")]
        [Tooltip("Disable the enemy's collider on death so the corpse no longer blocks or " +
                 "damages anything as it drifts off.")]
        [SerializeField] private bool disableColliderOnDeath = true;

        [FoldoutGroup("Death Behavior")]
        [Tooltip("Release the upright lock so death feedbacks can spin the corpse as it drifts off.")]
        [SerializeField] private bool releaseRotationOnDeath = true;
        
        // =====================
        // Physics
        // =====================

        [FoldoutGroup("Physics")]
        [Tooltip("Lock the Rigidbody's rotation while alive so swimmers stay level. " +
                 "Released on death so feedbacks can spin the corpse. Disable for enemies " +
                 "that should rotate freely (e.g. tumbling or physics-driven types).")]
        [SerializeField] private bool freezeRotationWhileAlive = true;

        [FoldoutGroup("Physics")]
        [Tooltip("Suspend AI steering while a Knockback2D on this enemy is mid-shove. " +
                 "Required for knockback to be visible at all — UpdateAI assigns linearVelocity " +
                 "every physics tick and would otherwise erase the impulse immediately. " +
                 "Side effect (usually desirable): state transitions also pause, so a hit reads " +
                 "as a brief stun. Disable only for enemies that must keep steering through hits.")]
        [SerializeField] private bool suspendAiDuringKnockback = true;

        // =====================
        // Targeting (multiplayer-aware)
        // =====================

        [FoldoutGroup("Targeting")]
        [Tooltip("How often (seconds) the enemy re-evaluates which submarine to chase. Only matters with 2+ players — " +
                 "a single-player sub is locked on with zero ongoing cost. Higher = cheaper but slower to notice a drop-in " +
                 "or a now-closer player. Each enemy is given a random phase so they don't all recompute on the same frame.")]
        [SerializeField, Min(0.05f)] private float retargetInterval = 0.5f;

        [FoldoutGroup("Targeting")]
        [Tooltip("A rival submarine must be this fraction of the current target's distance (or closer) before the enemy " +
                 "switches to it. 1 = always chase whoever is nearest (can jitter when players are equidistant); " +
                 "0.8 = must be 20% closer to steal aggro (stable).")]
        [SerializeField, Range(0.1f, 1f)] private float retargetSwitchAdvantage = 0.8f;

        // =====================
        // Debug (shared)
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        protected abstract string CurrentState { get; }

        // =====================
        // Protected State (available to all subclasses)
        // =====================

        /** The Rigidbody2D used for all enemy movement. */
        protected Rigidbody2D Rb { get; private set; }

        /** Optional knockback handler. Null when this enemy has no Knockback2D component. */
        protected Knockback2D Knockback { get; private set; }

        /**
         * True while a knockback is shoving this enemy and AI steering is suspended.
         * Subclasses can read this to hold off on telegraphs or attack commitments.
         */
        protected bool IsBeingKnockedBack => Knockback != null && Knockback.IsBeingKnockedBack;

        /** The SpriteRenderer for tinting and sprite flipping. */
        protected SpriteRenderer Sr { get; private set; }

        /** The sprite's original color — tints are reset to this on calm. */
        protected Color BaseColor { get; private set; }

        /** World position where this enemy spawned — used as a patrol / wander anchor. */
        protected Vector3 SpawnPosition { get; private set; }

        /** The submarine this enemy targets. Nearest at Start, then re-evaluated periodically (see MaintainTarget). */
        protected Submarine TargetSub { get; private set; }

        /** Convenience shortcut to TargetSub's Transform. */
        protected Transform Player { get; private set; }

        /** The submarine's Health component, used to deal damage on contact. */
        protected Health PlayerHealth { get; private set; }

        /** True after OnDeath fires — FixedUpdate stops calling UpdateAI. */
        protected bool IsDead { get; private set; }

        /** Next time (Time.time) this enemy is allowed to re-scan for a target. Phase-staggered per instance. */
        private float _nextRetargetTime;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected virtual void Awake()
        {
            // Physics setup — all enemies float freely with no gravity
            Rb = GetComponent<Rigidbody2D>();
            Rb.gravityScale = 0f;

            // Optional — enemies without a Knockback2D simply never get shoved
            Knockback = GetComponent<Knockback2D>();

            // Optionally lock rotation so swimmers stay upright while alive
            if (freezeRotationWhileAlive)
                Rb.constraints = RigidbodyConstraints2D.FreezeRotation;

            SpawnPosition = transform.position;

            Sr = GetComponent<SpriteRenderer>();
            if (Sr != null) BaseColor = Sr.color;
        }

        protected virtual void Start()
        {
            // Resolve nearest submarine — world objects self-target via the static list
            AcquireTarget(force: true);

            // Stagger the first periodic re-scan so a wave of enemies doesn't all retarget on one frame
            _nextRetargetTime = Time.time + UnityEngine.Random.value * RetargetInterval;

            Health health = GetComponent<Health>();
            if (health != null) health.onDeath.AddListener(OnDeath);

            SetIntentIndicator(false);
        }

        /**
         * Drives targeting and the subclass state machine each physics step.
         *
         * While a knockback is in flight we skip UpdateAI entirely, leaving the body
         * under Knockback2D's control — otherwise the subclass's per-tick
         * "Rb.linearVelocity = ..." would overwrite the impulse before it moved anything.
         * Targeting still runs, so the enemy comes out of the shove aimed correctly.
         */
        private void FixedUpdate()
        {
            if (IsDead) return;

            MaintainTarget();

            // Knockback owns the body for its control window — steering stands down
            if (!(suspendAiDuringKnockback && IsBeingKnockedBack))
                UpdateAI();

            FlipSpriteToVelocity();
        }

        // -------------------------------------------------------
        // Abstract / Virtual — implemented by subclasses
        // -------------------------------------------------------

        /**
         * Called each FixedUpdate while alive.
         * Implement your state machine here — transitions AND per-state movement.
         */
        protected abstract void UpdateAI();

        /**
         * Called by Health.onDeath. Base implementation freezes the enemy,
         * disables its collider, hides the intent indicator, and spawns O2 drops.
         * Override to add extra death behavior; always call base.OnDeath() first.
         */
        protected virtual void OnDeath()
        {
            IsDead = true;

            // Death launch: the killing blow was a knockback, so the corpse keeps that
            // momentum and flies. HitReceiver applies knockback *before* damage, so the
            // shove is already on the body by the time this death hook runs.
            bool launching = launchOnKnockbackDeath && IsBeingKnockedBack;

            // Close any in-flight knockback window so the corpse isn't left under its
            // control mid-death-animation. On a launch we still close it, which stops the
            // component's drag and lets the corpse coast freely on the impulse.
            if (Knockback != null) Knockback.Cancel();

            // Stop the corpse dead rather than coasting off on its last AI movement vector.
            // A death launch overrides this — that momentum is the whole point of the hit.
            if (killVelocityOnDeath && !launching)
                Rb.linearVelocity = Vector2.zero;

            // Optionally drop the collider so the corpse stops blocking/damaging.
            if (disableColliderOnDeath)
            {
                Collider2D col = GetComponent<Collider2D>();
                if (col != null) col.enabled = false;
            }

            // Release the upright lock so death feedbacks can spin the corpse as it
            // drifts off. Only relevant if we froze rotation while alive in the first place.
            if (freezeRotationWhileAlive || releaseRotationOnDeath)
                Rb.constraints = RigidbodyConstraints2D.None;

            SetIntentIndicator(false);
            SpawnDeathDrops();
        }

        // -------------------------------------------------------
        // Targeting (multiplayer-aware)
        // -------------------------------------------------------

        /** Seconds between periodic target re-scans. Override to tune per enemy type; defaults to the serialized value. */
        protected virtual float RetargetInterval => retargetInterval;

        /**
         * Whether the enemy is currently willing to switch targets. Default true.
         * Override to return false while mid-commitment (e.g. during a lunge) so the
         * enemy finishes its attack on the current target rather than swapping away.
         * Note: an invalid target (dead / dropped out) is always re-acquired regardless.
         */
        protected virtual bool CanRetarget => true;

        /** Hook fired whenever the target actually changes (including the initial acquire). No-op by default. */
        protected virtual void OnTargetChanged(Submarine newTarget) { }

        /**
         * Keeps TargetSub current across drop-in / drop-out and shifting positions.
         *
         * Cost is deliberately bounded:
         *   • A cheap per-frame validity check re-acquires immediately if the target
         *     died, was destroyed, or dropped out (so we never chase a corpse).
         *   • Otherwise we only re-scan on the staggered interval, and only when 2+
         *     submarines exist — single-player locks on once and pays nothing after.
         */
        private void MaintainTarget()
        {
            // Target gone (destroyed, disabled by death, or dropped out of multiplayer)?
            bool valid = TargetSub != null && TargetSub.isActiveAndEnabled;
            if (!valid)
            {
                AcquireTarget(force: true);
                _nextRetargetTime = Time.time + RetargetInterval;
                return;
            }

            // Valid target: only re-evaluate on the interval, and skip entirely in single-player
            if (Time.time < _nextRetargetTime) return;
            _nextRetargetTime = Time.time + RetargetInterval;

            if (CanRetarget && Submarine.All.Count > 1)
                AcquireTarget(force: false);
        }

        /**
         * Resolves the nearest submarine and adopts it as the target.
         * When not forced, applies hysteresis: a rival is only stolen-to if it is
         * meaningfully closer than the current target (retargetSwitchAdvantage), so
         * the enemy doesn't flip-flop between two near-equidistant players.
         */
        private void AcquireTarget(bool force)
        {
            Submarine best = Submarine.FindNearest(transform.position);

            // No submarines at all (e.g. everyone dropped out) — clear only on a forced pass
            if (best == null)
            {
                if (force && TargetSub != null) SetTarget(null);
                return;
            }

            // Forced acquire or first target — take it outright
            if (force || TargetSub == null) { SetTarget(best); return; }

            // Already nearest — nothing to do
            if (best == TargetSub) return;

            // Switch only if the candidate is closer by the required margin (compare squared distances)
            Vector2 here = transform.position;
            float currentSqr = ((Vector2)TargetSub.transform.position - here).sqrMagnitude;
            float bestSqr = ((Vector2)best.transform.position - here).sqrMagnitude;
            float advantageSqr = retargetSwitchAdvantage * retargetSwitchAdvantage;

            if (bestSqr < currentSqr * advantageSqr) SetTarget(best);
        }

        /** Points the cached target fields at a submarine (or clears them) and fires OnTargetChanged. */
        private void SetTarget(Submarine sub)
        {
            TargetSub = sub;
            Player = sub != null ? sub.transform : null;
            PlayerHealth = sub != null ? sub.Health : null;
            OnTargetChanged(sub);
        }

        // -------------------------------------------------------
        // Shared Helpers
        // -------------------------------------------------------

        /** Distance from this enemy to the player sub. Returns MaxValue if no player. */
        protected float DistanceToPlayer() =>
            Player != null ? Vector2.Distance(transform.position, Player.position) : float.MaxValue;

        /** Normalized direction from this enemy toward the player. */
        protected Vector2 DirectionToPlayer() =>
            Player != null
                ? ((Vector2)Player.position - (Vector2)transform.position).normalized
                : Vector2.right;

        /** Normalized direction directly away from the player — used by fleeing enemies. */
        protected Vector2 DirectionAwayFromPlayer() => -DirectionToPlayer();

        /** Applies a color to the sprite. Pass BaseColor to reset to the default tint. */
        protected void SetSpriteColor(Color color)
        {
            if (Sr != null) Sr.color = color;
        }

        /**
         * Shows or hides the intent indicator child object.
         * On show, fires a DOTween scale punch so the indicator pops in.
         */
        protected void SetIntentIndicator(bool show)
        {
            if (intentIndicator == null) return;
            if (intentIndicator.activeSelf == show) return;

            intentIndicator.SetActive(show);
            if (show)
                intentIndicator.transform.DOPunchScale(
                    Vector3.one * intentPunchScale, intentPunchDuration, 1, 0f);
        }

        // -------------------------------------------------------
        // Private Helpers
        // -------------------------------------------------------

        /** Spawns all configured death drops (O2, scrap, etc.) at this enemy's position. */
        private void SpawnDeathDrops()
        {
            foreach (var drop in deathDrops)
                drop?.Spawn(transform.position);
        }

        /** Flips the sprite to face the direction of horizontal movement. */
        private void FlipSpriteToVelocity()
        {
            if (Sr != null && Mathf.Abs(Rb.linearVelocity.x) > 0.1f)
                Sr.flipX = Rb.linearVelocity.x < 0f;
        }
    }
}
