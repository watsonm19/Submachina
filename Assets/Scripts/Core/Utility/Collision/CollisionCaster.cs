using UnityEngine;
using Sirenix.OdinInspector;

/**
 * Modular BoxCast collision service. Any movement script references this component
 * and calls CheckMove() before applying displacement. Knows nothing about input
 * or game state — purely a collision query utility.
 *
 * Relies on layer setup for self-exclusion: the Player layer should NOT be included
 * in solidObstacleLayer. This avoids expensive collider enable/disable toggling
 * and ordering hazards when multiple scripts cast per frame.
 *
 * Cast shape is derived from a BoxCollider — either one assigned explicitly
 * via the sourceCollider field, or auto-detected on this GameObject.
 * Half-extents are shrunk by skinWidth so the cast box is slightly smaller
 * than the physical collider, preventing flush-surface sticking.
 */
public class CollisionCaster : MonoBehaviour
{
    // =====================
    // Collision Settings
    // =====================

    [FoldoutGroup("Collision")]
    [Tooltip("BoxCollider whose size defines the cast shape. " +
             "Can live on this GameObject or anywhere in the hierarchy. " +
             "If left empty, falls back to GetComponent<BoxCollider>() on this GameObject.")]
    [SerializeField] private BoxCollider sourceCollider;

    [FoldoutGroup("Collision")]
    [Tooltip("Layer mask for solid obstacles that block movement (walls, solid objects). " +
             "Do NOT include the Player layer — the player must not block itself.")]
    [SerializeField] private LayerMask solidObstacleLayer;

    [FoldoutGroup("Collision")]
    [Tooltip("Gap maintained from surfaces to prevent clipping. " +
             "Example: 0.05 = 5 cm buffer on each face of the cast box.")]
    [SerializeField] private float skinWidth = 0.05f;

    // =====================
    // Read-Only State
    // =====================

    /** The shrunk half-extents used for BoxCast calls. */
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    public Vector3 CastHalfExtents { get; private set; }

    /** True if the most recent CheckMove call detected a hit. */
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    public bool LastCastHit { get; private set; }

    // =====================
    // Internal State
    // =====================

    private BoxCollider _boxCollider;

    // -------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------

    private void Awake()
    {
        // Resolve the source collider: use the assigned reference, or fall back to this GameObject
        _boxCollider = sourceCollider != null ? sourceCollider : GetComponent<BoxCollider>();

        if (_boxCollider == null)
        {
            Debug.LogError($"[CollisionCaster] '{name}' has no sourceCollider assigned and no " +
                           "BoxCollider on this GameObject. Assign one or add a BoxCollider.");
            return;
        }

        RefreshExtents();
    }

    // -------------------------------------------------------
    // Public API
    // -------------------------------------------------------

    /**
     * Checks whether a move from origin in the given direction for the given distance
     * is blocked by a solid obstacle. Returns the safe travel distance — clamped to
     * just before the hit surface if blocked, or the full distance if clear.
     *
     * Example: CheckMove(pos, Vector3.forward, 2f, out hit) returns 1.8 if a wall
     * is 1.85 units ahead (1.85 - skinWidth).
     */
    public float CheckMove(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit)
    {
        // Zero or negative distance means no movement — nothing to check
        if (distance <= 0f)
        {
            hit = default;
            LastCastHit = false;
            return 0f;
        }

        // Axis-aligned BoxCast in the movement direction
        if (Physics.BoxCast(origin, CastHalfExtents, direction,
                out hit, Quaternion.identity, distance, solidObstacleLayer))
        {
            LastCastHit = true;
            return Mathf.Max(0f, hit.distance - skinWidth);
        }

        // Clear path — full distance is safe
        LastCastHit = false;
        return distance;
    }

    /**
     * Convenience overload that discards the RaycastHit info.
     * Use when you only need the safe distance, not the hit details.
     */
    public float CheckMove(Vector3 origin, Vector3 direction, float distance)
    {
        return CheckMove(origin, direction, distance, out _);
    }

    // -------------------------------------------------------
    // Extents Calculation
    // -------------------------------------------------------

    /**
     * Recomputes CastHalfExtents from the current BoxCollider size.
     * Called automatically in Awake; can also be triggered at runtime
     * if the BoxCollider size changes dynamically.
     */
    public void RefreshExtents()
    {
        // Re-resolve if needed (e.g. sourceCollider changed at runtime)
        if (_boxCollider == null)
            _boxCollider = sourceCollider != null ? sourceCollider : GetComponent<BoxCollider>();

        if (_boxCollider == null)
        {
            Debug.LogWarning($"[CollisionCaster] '{name}' has no BoxCollider to read extents from.");
            CastHalfExtents = Vector3.one * 0.25f;
            return;
        }

        // Shrink by skinWidth on each axis, clamp to avoid degenerate casts
        Vector3 raw = _boxCollider.size * 0.5f - Vector3.one * skinWidth;
        CastHalfExtents = Vector3.Max(raw, Vector3.one * 0.01f);
    }

    // -------------------------------------------------------
    // Gizmos
    // -------------------------------------------------------

    private void OnDrawGizmosSelected()
    {
        // Show the cast box shape at the current position
        Gizmos.color = new Color(0f, 1f, 0.5f, 0.3f);
        Gizmos.DrawWireCube(transform.position, CastHalfExtents * 2f);

        // Show a solid indicator if the last cast hit something
        if (LastCastHit)
        {
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.5f);
            Gizmos.DrawWireCube(transform.position, CastHalfExtents * 2.1f);
        }
    }

    // -------------------------------------------------------
    // Editor Utilities
    // -------------------------------------------------------

#if UNITY_EDITOR
    [FoldoutGroup("Debug")]
    [Button("Refresh Extents"), GUIColor(0.6f, 0.8f, 1f)]
    private void EditorRefreshExtents()
    {
        RefreshExtents();
        Debug.Log($"[CollisionCaster] '{name}' refreshed CastHalfExtents = {CastHalfExtents}");
    }
#endif
}
