using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;
using Sirenix.OdinInspector;

/**
 * Thin trigger collider component that detects hits during melee swings.
 * Activated/deactivated by MeleeWeaponController based on the motion's
 * active window. Uses a HashSet to deduplicate hits within a single swing.
 *
 * Hierarchy setup:
 *   WeaponSprite (MeleeWeaponController)
 *     └── Hitbox (trigger collider) ← MeleeHitbox here
 */
[RequireComponent(typeof(Collider))]
public class MeleeHitbox : MonoBehaviour
{
    // =====================
    // Settings
    // =====================

    [FoldoutGroup("Detection")]
    [Tooltip("Tag of valid hit targets. Only colliders with this tag will register hits.")]
    [SerializeField] private string targetTag = "Enemy";

    [FoldoutGroup("Detection")]
    [Tooltip("Optional layer mask for additional filtering. Set to Everything to rely on tag only.")]
    [SerializeField] private LayerMask targetLayers = ~0;

    // =====================
    // References
    // =====================

    [FoldoutGroup("References")]
    [Tooltip("Parent MeleeWeaponController to notify on hit. Auto-resolved from parent if empty.")]
    [SerializeField] private MeleeWeaponController weaponController;

    // =====================
    // Events
    // =====================

    [FoldoutGroup("Events")]
    [Tooltip("Fired when a valid target is hit. Passes the hit GameObject for MMF wiring, VFX, etc.")]
    public UnityEvent<GameObject> onHit;

    // =====================
    // Internal State
    // =====================

    // Dedup set: prevents the same target from being hit twice in one swing.
    // Cleared each time the hitbox is deactivated (swing boundary).
    private HashSet<Collider> _hitThisSwing = new HashSet<Collider>();

    // Current swing's damage parameters (set by MeleeWeaponController each swing)
    private int _currentDamage;
    private float _currentKnockback;

    // Cached collider for enable/disable
    private Collider _collider;

    // -------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------

    private void Awake()
    {
        _collider = GetComponent<Collider>();
        _collider.isTrigger = true;

        // Auto-resolve weapon controller from parent hierarchy if not assigned
        if (weaponController == null)
        {
            weaponController = GetComponentInParent<MeleeWeaponController>();
        }

        // Start disabled — MeleeWeaponController activates during active window
        _collider.enabled = false;
    }

    // -------------------------------------------------------
    // Collision Detection
    // -------------------------------------------------------

    /**
     * Fires when a collider enters the hitbox trigger during a swing.
     *
     * Sequence:
     *  1. Dedup check — skip if already hit this swing
     *  2. Tag and layer validation
     *  3. Record hit for dedup
     *  4. Apply knockback if target has a Knockback component
     *  5. Fire events for game juice
     *  6. Notify parent weapon controller
     */
    private void OnTriggerEnter(Collider other)
    {
        // Dedup: skip targets already hit this swing
        if (_hitThisSwing.Contains(other)) return;

        // Tag filter
        // Check tag filter (skip if empty)
        if (!string.IsNullOrEmpty(targetTag) && !other.CompareTag(targetTag)) return;

        // Layer filter
        if ((targetLayers & (1 << other.gameObject.layer)) == 0) return;

        // Record this target as hit for the rest of the swing
        _hitThisSwing.Add(other);

        // Build hit direction from weapon toward target
        Vector3 hitDirection = (other.transform.position - transform.position).normalized;

        // Apply knockback: impulse directed from weapon toward the target
        Knockback knockback = other.GetComponent<Knockback>();
        if (knockback == null) knockback = other.GetComponentInParent<Knockback>();

        if (knockback != null && _currentKnockback > 0f)
        {
            knockback.ApplyKnockback(hitDirection * _currentKnockback);
        }

        // Notify HitReceiver on the target so it can react (damage, VFX, AI, etc.)
        HitReceiver receiver = other.GetComponent<HitReceiver>();
        if (receiver == null) receiver = other.GetComponentInParent<HitReceiver>();

        if (receiver != null)
        {
            var hitData = new HitData
            {
                damage = _currentDamage,
                knockbackForce = _currentKnockback,
                hitDirection = hitDirection,
                hitPoint = other.ClosestPoint(transform.position),
                source = gameObject
            };
            receiver.ReceiveHit(hitData);
        }

        // Fire hitbox-side events (for attacker-side juice: screen shake, hit flash, etc.)
        onHit?.Invoke(other.gameObject);

        // Notify the weapon controller for upstream event relay
        if (weaponController != null) weaponController.NotifyHit();
    }

    // -------------------------------------------------------
    // Public API
    // -------------------------------------------------------

    /**
     * Enables or disables the hitbox collider and clears the dedup set on deactivation.
     * Called by MeleeWeaponController at active window boundaries.
     */
    public void SetActive(bool active)
    {
        if (_collider != null) _collider.enabled = active;

        // Clear dedup set when deactivating (swing boundary)
        if (!active) _hitThisSwing.Clear();
    }

    /**
     * Configures damage parameters for the current swing.
     * Called by MeleeWeaponController at the start of each attack.
     */
    public void ConfigureSwing(int damage, float knockbackForce)
    {
        _currentDamage = damage;
        _currentKnockback = knockbackForce;
    }

    // -------------------------------------------------------
    // Gizmos
    // -------------------------------------------------------

    private void OnDrawGizmosSelected()
    {
        // Draw the hitbox trigger bounds in yellow for visibility
        Collider col = GetComponent<Collider>();
        if (col == null) return;

        Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
        Gizmos.matrix = transform.localToWorldMatrix;

        if (col is BoxCollider box)
        {
            Gizmos.DrawCube(box.center, box.size);
        }
        else if (col is SphereCollider sphere)
        {
            Gizmos.DrawSphere(sphere.center, sphere.radius);
        }
        else if (col is CapsuleCollider capsule)
        {
            Gizmos.DrawWireSphere(capsule.center, capsule.radius);
        }
    }
}
