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
                 "instead of coasting on its last movement vector.")]
        [SerializeField] private bool killVelocityOnDeath = true;

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

        /** The SpriteRenderer for tinting and sprite flipping. */
        protected SpriteRenderer Sr { get; private set; }

        /** The sprite's original color — tints are reset to this on calm. */
        protected Color BaseColor { get; private set; }

        /** World position where this enemy spawned — used as a patrol / wander anchor. */
        protected Vector3 SpawnPosition { get; private set; }

        /** The submarine this enemy targets (nearest at Start). */
        protected Submarine TargetSub { get; private set; }

        /** Convenience shortcut to TargetSub's Transform. */
        protected Transform Player { get; private set; }

        /** The submarine's Health component, used to deal damage on contact. */
        protected Health PlayerHealth { get; private set; }

        /** True after OnDeath fires — FixedUpdate stops calling UpdateAI. */
        protected bool IsDead { get; private set; }

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected virtual void Awake()
        {
            // Physics setup — all enemies float freely with no gravity
            Rb = GetComponent<Rigidbody2D>();
            Rb.gravityScale = 0f;

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
            TargetSub = Submarine.FindNearest(transform.position);
            if (TargetSub != null)
            {
                Player = TargetSub.transform;
                PlayerHealth = TargetSub.Health;
            }

            Health health = GetComponent<Health>();
            if (health != null) health.onDeath.AddListener(OnDeath);

            SetIntentIndicator(false);
        }

        private void FixedUpdate()
        {
            if (IsDead) return;
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

            // Optionally stop the corpse dead instead of letting it coast.
            if (killVelocityOnDeath)
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
