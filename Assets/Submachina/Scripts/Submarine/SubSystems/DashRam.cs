using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Deals damage to enemies AND the submarine when the sub dashes into them.
     *
     * Pairs with CavitationBurst: Sub.Physics.IsDashing is true only during an
     * active dash, so ordinary movement contact does nothing — only intentional
     * rams count. This keeps the risk/reward intentional and learnable.
     *
     * Risk/reward: the enemy takes dashDamage, the sub pays selfDamage as the cost
     * of an aggressive play. Careless panic-dashing into enemies is punishing;
     * a skilled player can weaponize the dash as a combat tool.
     *
     * Enemy hits route through HitReceiver so phase invulnerability is respected
     * (e.g. RammingEnemy is immune while charging). Self-damage only fires when
     * the hit is accepted — you don't lose HP ramming a shielded enemy.
     *
     * A short cooldown prevents double-fires when composite enemy colliders
     * send multiple OnCollisionEnter2D events in the same physics step.
     */
    [UsesFeedbacks(nameof(SubFeedbacks.DashRam))]
    public class DashRam : SubmarineComponent
    {
        // =====================
        // Settings
        // =====================

        [FoldoutGroup("Damage")]
        [Tooltip("Only objects on these layers trigger a dash-ram. " +
                 "Set to the Enemy layer.")]
        [SerializeField] private LayerMask enemyLayer;

        [FoldoutGroup("Damage")]
        [Tooltip("Damage dealt to the enemy on a successful ram.")]
        [SerializeField, Min(1)] private int dashDamage = 3;

        [FoldoutGroup("Damage")]
        [Tooltip("Damage the submarine takes when the ram connects. " +
                 "Set to 0 to remove the risk side of the trade-off.")]
        [SerializeField, Min(0)] private int selfDamage = 1;

        [FoldoutGroup("Damage")]
        [Tooltip("Knockback impulse applied to the enemy in the dash direction. " +
                 "Example: 6 gives a satisfying shove without sending enemies off-screen.")]
        [SerializeField, Min(0f)] private float knockbackForce = 6f;

        [FoldoutGroup("Damage")]
        [Tooltip("Seconds between accepted rams. Prevents double-hits when multiple " +
                 "colliders on the same enemy fire separate collision events.")]
        [SerializeField, Min(0f)] private float damageCooldown = 0.3f;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired when a dash-ram lands and the hit is accepted. " +
                 "Passes the world-space contact point for spawning effects.")]
        public UnityEvent<Vector2> onDashRam;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when a ram attempt hits an invulnerable or on-cooldown target.")]
        public UnityEvent onDashRamBlocked;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float CooldownRemaining => Mathf.Max(0f, _cooldownEnd - Time.time);

        // =====================
        // State
        // =====================

        private float _cooldownEnd = -1f;

        // -------------------------------------------------------
        // Collision
        // -------------------------------------------------------

        /**
         * Fires once per physics step when a new contact begins with an object
         * on the enemy layer, but only acts during an active CavitationBurst dash.
         *
         * Flow:
         *   1. Early-out if not dashing or wrong layer.
         *   2. Check per-component cooldown (composite collider guard).
         *   3. Attempt ReceiveHit() via HitReceiver — respects invulnerability.
         *      Falls back to direct Health.TakeDamage for enemies without HitReceiver.
         *   4. Hit accepted → pay self-damage, fire feedback and events.
         *   5. Hit rejected → fire blocked event only (no self-damage).
         */
        private void OnCollisionEnter2D(Collision2D collision)
        {
            // Gate: active dash + enemy layer
            if (Sub?.Physics == null || !Sub.Physics.IsDashing) return;
            if ((enemyLayer.value & (1 << collision.gameObject.layer)) == 0) return;

            // Cooldown guard — prevents double-fires from multi-collider enemies
            if (Time.time < _cooldownEnd) return;
            _cooldownEnd = Time.time + damageCooldown;

            // Build hit payload from the collision geometry
            Vector2 contact = collision.GetContact(0).point;
            Vector2 ramDir = collision.relativeVelocity.sqrMagnitude > 0.01f
                ? collision.relativeVelocity.normalized
                : (Vector2)(collision.transform.position - transform.position).normalized;

            var hitData = new HitData
            {
                damage       = dashDamage,
                knockbackForce = knockbackForce,
                hitDirection = ramDir,
                hitPoint     = contact,
                source       = gameObject
            };

            // Route through HitReceiver to respect phase invulnerability
            bool hitAccepted = false;
            HitReceiver receiver = collision.gameObject.GetComponentInParent<HitReceiver>();
            if (receiver != null)
            {
                hitAccepted = receiver.ReceiveHit(hitData);
            }
            else
            {
                // Fallback for enemies that expose Health without HitReceiver
                Health health = collision.gameObject.GetComponentInParent<Health>();
                if (health != null)
                {
                    health.TakeDamage(dashDamage);
                    hitAccepted = true;
                }
            }

            // Hit connected — pay the self-damage cost and fire juice
            if (hitAccepted)
            {
                if (selfDamage > 0) Sub?.Health?.TakeDamage(selfDamage);
                onDashRam?.Invoke(contact);
                Sub?.Feedbacks?.Play(SubFeedbacks.DashRam, contact);
            }
            else
            {
                onDashRamBlocked?.Invoke();
            }
        }

        // -------------------------------------------------------
        // Editor Utilities
        // -------------------------------------------------------

#if UNITY_EDITOR
        [FoldoutGroup("Debug")]
        [Button("Simulate Self-Damage"), GUIColor(0.6f, 0.8f, 1f)]
        private void DebugSimulateSelf()
        {
            if (!Application.isPlaying) { Debug.Log("[DashRam] Play mode only."); return; }
            Sub?.Health?.TakeDamage(selfDamage);
            Debug.Log($"[DashRam] Simulated self-damage — sub took {selfDamage} damage.");
        }
#endif
    }
}
