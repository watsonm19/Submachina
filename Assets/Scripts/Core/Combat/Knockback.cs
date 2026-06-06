using UnityEngine;
using Sirenix.OdinInspector;

/**
 * Standalone knockback component that applies decaying impulse-based displacement
 * with wall collision via CollisionCaster. Designed as a modular add-on: any script
 * can call ApplyKnockback() and this component handles decay, collision, and
 * optional input suppression signaling.
 *
 * Hierarchy setup:
 *   [Player Root]
 *     ├── CollisionCaster       ← required reference
 *     ├── Knockback             ← this component
 *     └── ...
 */
public class Knockback : MonoBehaviour
{
    // =====================
    // Settings
    // =====================

    [FoldoutGroup("Knockback")]
    [Tooltip("How fast knockback velocity decays each frame (exponential lerp coefficient). " +
             "Higher = snappier stop. Example: 8 decays ~95% in ~0.37 s; 16 in ~0.19 s.")]
    [SerializeField] private float knockbackDrag = 8f;

    [FoldoutGroup("Knockback")]
    [Tooltip("If true, signals to the owning controller that input should be ignored " +
             "while knockback is active. The controller must check SuppressesInput.")]
    [SerializeField] private bool suppressInputDuringKnockback = false;

    [FoldoutGroup("Knockback")]
    [Tooltip("CollisionCaster used for wall collision during knockback displacement.")]
    [SerializeField, Required] private CollisionCaster collisionCaster;

    [FoldoutGroup("Knockback")]
    [Tooltip("Transform to apply knockback displacement to. If left empty, uses this GameObject.")]
    [SerializeField] private Transform moveTarget;

    // =====================
    // Read-Only State
    // =====================

    /** True while knockback velocity is above the stop threshold. */
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    public bool IsBeingKnockedBack => _knockbackVelocity.sqrMagnitude > KnockbackThreshold * KnockbackThreshold;

    /** True when knockback is active AND suppressInputDuringKnockback is enabled. */
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    public bool SuppressesInput => suppressInputDuringKnockback && IsBeingKnockedBack;

    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private Vector3 _knockbackVelocity;

    // =====================
    // Internal State
    // =====================

    private Transform _moveTransform;

    /** Velocity below this magnitude is treated as zero and stops further processing. */
    private const float KnockbackThreshold = 0.01f;

    // -------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------

    private void Awake()
    {
        // Resolve the movement target: use the assigned transform, or fall back to self
        _moveTransform = moveTarget != null ? moveTarget : transform;
    }

    private void Update()
    {
        UpdateKnockback();
    }

    // -------------------------------------------------------
    // Knockback Logic
    // -------------------------------------------------------

    /**
     * Decays and applies the knockback velocity each frame.
     * Uses CollisionCaster.CheckMove() to prevent passing through solid obstacles.
     * On wall impact, velocity is zeroed immediately for a crisp stop.
     */
    private void UpdateKnockback()
    {
        if (!IsBeingKnockedBack) return;

        // Exponential decay: velocity approaches zero over time
        _knockbackVelocity = Vector3.Lerp(_knockbackVelocity, Vector3.zero, knockbackDrag * Time.deltaTime);

        // Compute this frame's knockback displacement
        Vector3 knockbackDelta = _knockbackVelocity * Time.deltaTime;
        Vector3 knockbackDir = knockbackDelta.normalized;
        float knockbackDist = knockbackDelta.magnitude;

        // Collision check via CollisionCaster; clamp if a solid obstacle is in the way
        if (knockbackDist > 0f)
        {
            float safeDist = collisionCaster.CheckMove(
                _moveTransform.position, knockbackDir, knockbackDist, out RaycastHit hit);

            if (safeDist < knockbackDist)
            {
                // Wall impact: apply clamped travel and kill velocity
                _moveTransform.position += knockbackDir * safeDist;
                _knockbackVelocity = Vector3.zero;
            }
            else
            {
                // Clear path: apply full knockback delta
                _moveTransform.position += knockbackDelta;
            }
        }
    }

    // -------------------------------------------------------
    // Public API
    // -------------------------------------------------------

    /**
     * Adds an impulse to the knockback velocity.
     * Multiple calls in the same frame accumulate (e.g. caught in an explosion and hit by an enemy).
     * Example: ApplyKnockback(transform.right * 5f) for a 5 m/s rightward shove.
     */
    public void ApplyKnockback(Vector3 impulse)
    {
        _knockbackVelocity += impulse;
    }

    // -------------------------------------------------------
    // Editor Utilities
    // -------------------------------------------------------

#if UNITY_EDITOR
    [FoldoutGroup("Debug")]
    [Button("Test Knockback Backward"), GUIColor(1f, 0.8f, 0.6f)]
    private void TestKnockbackBackward()
    {
        if (!Application.isPlaying)
        {
            Debug.Log("[Knockback] Can only be tested in Play mode.");
            return;
        }
        ApplyKnockback(_moveTransform.forward * 5f);
    }
    
    [Button("Test Knockback Forward"), GUIColor(1f, 0.8f, 0.6f)]
    private void TestKnockbackForward()
    {
        if (!Application.isPlaying)
        {
            Debug.Log("[Knockback] Can only be tested in Play mode.");
            return;
        }
        ApplyKnockback(_moveTransform.forward * -5f);
    }

    [FoldoutGroup("Debug")]
    [Button("Test Knockback Right"), GUIColor(1f, 0.8f, 0.6f)]
    private void TestKnockbackRight()
    {
        if (!Application.isPlaying)
        {
            Debug.Log("[Knockback] Can only be tested in Play mode.");
            return;
        }
        ApplyKnockback(_moveTransform.right * 5f);
    }
#endif
}
