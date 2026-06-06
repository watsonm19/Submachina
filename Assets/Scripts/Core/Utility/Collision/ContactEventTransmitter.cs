using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;

/**
 * Generic collision/trigger event transmitter.
 * Fires UnityEvents when qualifying objects make contact, filtered by tag, layer mask,
 * and/or a specific collider reference.
 *
 * Use cases:
 *  - Kill zones (enemy touches player → fire death event)
 *  - Checkpoints (player enters zone → fire checkpoint event)
 *  - Any scenario where you need "thing A touches thing B → stuff happens"
 *
 * Supports both trigger colliders (OnTriggerEnter/Exit) and physics colliders (OnCollisionEnter/Exit).
 */
[RequireComponent(typeof(Collider))]
public class ContactEventTransmitter : MonoBehaviour
{
    // =====================
    // Filter Settings
    // =====================

    [FoldoutGroup("Filters")]
    [Tooltip("If set, only contacts with this specific collider will fire events. " +
             "Leave empty to accept any collider that passes tag/layer checks.")]
    [SerializeField] private Collider targetCollider;

    [FoldoutGroup("Filters")]
    [Tooltip("If non-empty, only GameObjects with this tag will fire events. " +
             "Example: 'Player' to only react to the player.")]
    [SerializeField] private string requiredTag = "Player";

    [FoldoutGroup("Filters")]
    [Tooltip("Layer mask filter. Only objects on these layers will fire events. " +
             "Set to 'Everything' to skip layer filtering.")]
    [SerializeField] private LayerMask layerMask = ~0; // default: Everything

    // =====================
    // Behavior
    // =====================

    [FoldoutGroup("Behavior")]
    [Tooltip("How many times this transmitter can fire. 0 = unlimited.")]
    [SerializeField, Min(0)] private int maxFirings = 0;

    [FoldoutGroup("Behavior")]
    [Tooltip("Minimum seconds between firings. Prevents rapid re-triggering. " +
             "Example: 0.5 means the transmitter ignores contacts within half a second of the last one.")]
    [SerializeField, Min(0f)] private float cooldown = 0f;

    // =====================
    // Events
    // =====================

    [FoldoutGroup("Events")]
    [Tooltip("Fired when a qualifying object makes contact.")]
    public UnityEvent onContactEnter;

    [FoldoutGroup("Events")]
    [Tooltip("Fired when a qualifying object makes contact. Passes the contacting GameObject.")]
    public UnityEvent<GameObject> onContactEnterWithObject;

    [FoldoutGroup("Events")]
    [Tooltip("Fired when a qualifying object stops making contact (trigger exit or collision exit).")]
    public UnityEvent onContactExit;

    [FoldoutGroup("Events")]
    [Tooltip("Fired on exit. Passes the departing GameObject.")]
    public UnityEvent<GameObject> onContactExitWithObject;

    // =====================
    // State
    // =====================

    private int _fireCount = 0;
    private float _lastFireTime = -Mathf.Infinity;

    // -------------------------------------------------------
    // Trigger Callbacks (for trigger colliders)
    // -------------------------------------------------------

    private void OnTriggerEnter(Collider other)
    {
        TryFireEnter(other.gameObject, other);
    }

    private void OnTriggerExit(Collider other)
    {
        TryFireExit(other.gameObject, other);
    }

    // -------------------------------------------------------
    // Collision Callbacks (for physics colliders)
    // -------------------------------------------------------

    private void OnCollisionEnter(Collision collision)
    {
        TryFireEnter(collision.gameObject, collision.collider);
    }

    private void OnCollisionExit(Collision collision)
    {
        TryFireExit(collision.gameObject, collision.collider);
    }

    // -------------------------------------------------------
    // Core Logic
    // -------------------------------------------------------

    /**
     * Validates the incoming contact against all filters, then fires enter events.
     *
     * Filter order (cheapest checks first):
     *  1. Max firings cap
     *  2. Cooldown timer
     *  3. Specific collider match (if set)
     *  4. Tag match (if set)
     *  5. Layer mask match
     */
    private void TryFireEnter(GameObject other, Collider otherCollider)
    {
        // Check firing limit (0 = unlimited)
        if (maxFirings > 0 && _fireCount >= maxFirings) return;

        // Check cooldown
        if (Time.time - _lastFireTime < cooldown) return;

        // Check specific collider filter
        if (targetCollider != null && otherCollider != targetCollider) return;

        // Check tag filter (skip if empty)
        if (!string.IsNullOrEmpty(requiredTag) && !other.CompareTag(requiredTag)) return;

        // Check layer mask — bitwise: (mask & (1 << layer)) != 0
        if ((layerMask & (1 << other.layer)) == 0) return;

        // All filters passed — fire events
        _fireCount++;
        _lastFireTime = Time.time;

        onContactEnter?.Invoke();
        onContactEnterWithObject?.Invoke(other);
    }

    /**
     * Validates the departing contact against filters, then fires exit events.
     * Exit events don't consume firings or respect cooldown — they simply
     * mirror any valid enter that could have occurred.
     */
    private void TryFireExit(GameObject other, Collider otherCollider)
    {
        // Apply the same identity filters (but skip cooldown/max — exits should always fire if enter was valid)
        if (targetCollider != null && otherCollider != targetCollider) return;
        if (!string.IsNullOrEmpty(requiredTag) && !other.CompareTag(requiredTag)) return;
        if ((layerMask & (1 << other.layer)) == 0) return;

        onContactExit?.Invoke();
        onContactExitWithObject?.Invoke(other);
    }

    // -------------------------------------------------------
    // Public API
    // -------------------------------------------------------

    /**
     * Resets the fire count and cooldown, allowing the transmitter to fire again.
     * Useful for respawn or level-reset scenarios.
     */
    public void ResetTransmitter()
    {
        _fireCount = 0;
        _lastFireTime = -Mathf.Infinity;
    }

    // -------------------------------------------------------
    // Editor Utilities
    // -------------------------------------------------------

#if UNITY_EDITOR
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private int DebugFireCount => _fireCount;

    [FoldoutGroup("Debug")]
    [Button("Simulate Contact"), GUIColor(1f, 0.8f, 0.4f)]
    [Tooltip("Simulates a contact enter event using the first object found with the required tag.")]
    private void SimulateContact()
    {
        if (string.IsNullOrEmpty(requiredTag))
        {
            Debug.LogWarning($"[ContactEventTransmitter] '{name}' — cannot simulate without a required tag.");
            return;
        }

        GameObject target = GameObject.FindGameObjectWithTag(requiredTag);
        if (target != null)
        {
            onContactEnter?.Invoke();
            onContactEnterWithObject?.Invoke(target);
            Debug.Log($"[ContactEventTransmitter] '{name}' — simulated contact with '{target.name}'");
        }
        else
        {
            Debug.LogWarning($"[ContactEventTransmitter] '{name}' — no GameObject found with tag '{requiredTag}'");
        }
    }

    [FoldoutGroup("Debug")]
    [Button("Reset"), GUIColor(0.6f, 0.8f, 1f)]
    private void DebugReset() => ResetTransmitter();
#endif

    // -------------------------------------------------------
    // Gizmos
    // -------------------------------------------------------

    private void OnDrawGizmosSelected()
    {
        // Draw the trigger/collider bounds in orange to indicate a contact zone
        Collider col = GetComponent<Collider>();
        if (col == null) return;

        Gizmos.color = new Color(1f, 0.6f, 0f, 0.3f);
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
            Gizmos.DrawSphere(capsule.center, capsule.radius);
        }
    }
}
