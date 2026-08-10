using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Applies damage to this GameObject's Health when it collides with solid
     * objects (rocks, walls) above a minimum impact speed.
     *
     * Attach to the submarine root alongside Health. Gentle grazes and
     * slow-speed contact are ignored via the minImpactSpeed threshold —
     * only real impacts register as damage.
     *
     * A short cooldown prevents repeated damage ticks while the sub is
     * sliding or pressed against a surface.
     *
     * Damage source:
     *   Sub.Hull present    → routed through HullSystem.EvaluateImpact, which
     *                          weighs the hit against current pressure load and
     *                          structural reserve. A 0 result means the hull
     *                          absorbed the impact within its margin — no damage,
     *                          no CollisionDamage feedback (HullSystem already
     *                          played its own overload cue).
     *   Sub.Hull absent     → legacy flat damagePerImpact (upgradeable via
     *                          SubStats.CollisionDamagePerImpact), unchanged from
     *                          before HullSystem existed — old scenes still work.
     *
     * Example: minImpactSpeed=3 means the sub only takes damage when hitting a
     * rock faster than 3 m/s, regardless of which damage source is active.
     */
    [RequireComponent(typeof(Health))]
    [UsesFeedbacks(nameof(SubFeedbacks.CollisionDamage))]
    public class CollisionDamage : SubmarineComponent
    {
        // =====================
        // Settings
        // =====================

        [FoldoutGroup("Collision")]
        [Tooltip("Minimum relative impact speed (m/s) to register as damage. " +
                 "Slow grazes and gentle wall contact are ignored below this.")]
        [SerializeField, Min(0f)] private float minImpactSpeed = 2.5f;

        [FoldoutGroup("Collision")]
        [Tooltip("Damage applied per qualifying impact.")]
        [SerializeField, Min(1)] private int damagePerImpact = 1;

        [FoldoutGroup("Collision")]
        [Tooltip("Seconds after an impact before another can register. " +
                 "Prevents repeated ticks while sliding along a surface.")]
        [SerializeField, Min(0f)] private float damageCooldown = 0.5f;

        [FoldoutGroup("Collision")]
        [Tooltip("Only register impacts with objects on these layers. " +
                 "Set to the Rock / Environment layer to avoid collisions with enemies, etc.")]
        [SerializeField] private LayerMask collisionLayers = ~0; // default: all layers

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired when a qualifying collision impact deals damage. " +
                 "Passes the impact speed (m/s) for intensity-driven effects.")]
        public UnityEvent<float> onCollisionDamage;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float LastImpactSpeed => _lastImpactSpeed;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float CooldownRemaining => Mathf.Max(0f, _cooldownEnd - Time.time);

        // =====================
        // Upgrade Accessors
        // =====================

        private int DamagePerImpactMod => Mathf.Max(0, Sub?.Upgrades?.Stats.ResolveInt(SubStats.CollisionDamagePerImpact, damagePerImpact) ?? damagePerImpact);

        // =====================
        // State
        // =====================

        private Health _health;
        private float _cooldownEnd = -1f;
        private float _lastImpactSpeed;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();
            _health = GetComponent<Health>();
        }

        // -------------------------------------------------------
        // Collision
        // -------------------------------------------------------

        /**
         * Fires once per physics frame when this Rigidbody2D first contacts
         * another solid collider. relativeVelocity is the velocity of this
         * object relative to the surface at the moment of contact — a clean
         * proxy for impact force without needing to read the Rigidbody directly.
         *
         * Example: sub moving at 8 m/s hits a static rock → relativeVelocity.magnitude ≈ 8.
         */
        private void OnCollisionEnter2D(Collision2D collision)
        {
            // Layer check — skip objects not in the target layer mask
            if ((collisionLayers & (1 << collision.gameObject.layer)) == 0) return;

            // Cooldown check — prevent rapid re-damage while sliding
            if (Time.time < _cooldownEnd) return;

            float impactSpeed = collision.relativeVelocity.magnitude;
            _lastImpactSpeed = impactSpeed;

            // Threshold check — ignore gentle contact
            if (impactSpeed < minImpactSpeed) return;

            // Damage source: HullSystem's pressure+impact overload model when present
            // (0 means the hull absorbed it), otherwise the legacy flat per-impact damage.
            int damage = Sub?.Hull != null ? Sub.Hull.EvaluateImpact(impactSpeed) : DamagePerImpactMod;
            if (damage <= 0) return;

            _health.TakeDamage(damage);
            _cooldownEnd = Time.time + damageCooldown;

            onCollisionDamage?.Invoke(impactSpeed);
            Sub?.Feedbacks?.Play(SubFeedbacks.CollisionDamage, transform.position, damage);
        }

        // -------------------------------------------------------
        // Editor Utilities
        // -------------------------------------------------------

#if UNITY_EDITOR
        [FoldoutGroup("Debug")]
        [Button("Simulate Impact"), GUIColor(1f, 0.5f, 0.2f)]
        private void DebugSimulateImpact()
        {
            if (!Application.isPlaying) { Debug.Log("[CollisionDamage] Play mode only."); return; }
            _health.TakeDamage(damagePerImpact);
            Debug.Log("[CollisionDamage] Simulated impact damage.");
        }
#endif
    }
}
