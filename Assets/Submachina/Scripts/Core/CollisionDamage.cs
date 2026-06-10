using UnityEngine;
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
     * How impact speed maps to damage:
     *   Speed < minImpactSpeed  → no damage
     *   Speed >= minImpactSpeed → damagePerImpact HP lost
     *
     * Example: minImpactSpeed=3, damagePerImpact=1 means the sub only
     * takes damage when hitting a rock faster than 3 m/s.
     */
    [RequireComponent(typeof(Health))]
    public class CollisionDamage : MonoBehaviour
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
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float LastImpactSpeed => _lastImpactSpeed;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float CooldownRemaining => Mathf.Max(0f, _cooldownEnd - Time.time);

        // =====================
        // State
        // =====================

        private Health _health;
        private float _cooldownEnd = -1f;
        private float _lastImpactSpeed;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
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

            _health.TakeDamage(damagePerImpact);
            _cooldownEnd = Time.time + damageCooldown;
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
