using UnityEngine;
using UnityEngine.Events;
using MoreMountains.Feedbacks;
using Sirenix.OdinInspector;

/**
 * Manages a creature's hit points, handles damage from HitReceiver, and
 * triggers death when HP reaches zero. Wire HitReceiver.onHitReceived to
 * this component's TakeDamage(HitData) in the Inspector.
 *
 * Provides events for health changes, low-health thresholds, and death
 * so you can wire VFX, animations, AI state changes, and destruction.
 *
 * Usage:
 *   [Enemy Root]
 *     ├── HitReceiver   ← onHitReceived → Health.TakeDamage(HitData)
 *     ├── Health         ← this component
 *     └── ...
 */
public class Health : MonoBehaviour
{
    // =====================
    // Settings
    // =====================

    [FoldoutGroup("Health")]
    [Tooltip("Maximum hit points. Also used as starting HP.")]
    [SerializeField, Min(1)] private int maxHP = 5;

    [FoldoutGroup("Health")]
    [Tooltip("Normalized HP threshold (0-1) at which onLowHealth fires. " +
             "Example: 0.3 fires when HP drops to 30% or below.")]
    [SerializeField, Range(0f, 1f)] private float lowHealthThreshold = 0.3f;

    // =====================
    // Death Behavior
    // =====================

    [FoldoutGroup("Death")]
    [Tooltip("What happens to this GameObject when HP reaches zero.")]
    [SerializeField] private DeathBehavior deathBehavior = DeathBehavior.Destroy;

    [FoldoutGroup("Death")]
    [ShowIf("deathBehavior", DeathBehavior.Destroy)]
    [Tooltip("Delay before Destroy is called, giving time for death effects to play.")]
    [SerializeField, Min(0f)] private float destroyDelay = 0.5f;

    [FoldoutGroup("Death")]
    [ShowIf("deathBehavior", DeathBehavior.Deactivate)]
    [Tooltip("Delay before the GameObject is deactivated.")]
    [SerializeField, Min(0f)] private float deactivateDelay = 0.5f;

    // =====================
    // Events
    // =====================

    [FoldoutGroup("Events")]
    [Tooltip("Fired whenever HP changes. Passes (currentHP, maxHP) for health bars, etc.")]
    public UnityEvent<int, int> onHealthChanged;

    [FoldoutGroup("Events")]
    [Tooltip("Fired when HP drops to or below the low-health threshold. Fires once per crossing.")]
    public UnityEvent onLowHealth;

    [FoldoutGroup("Events")]
    [Tooltip("Fired when HP reaches zero. Wire death VFX, ragdoll, loot drops, etc.")]
    public UnityEvent onDeath;

    // =====================
    // MMF Feedbacks
    // =====================

    [FoldoutGroup("Feedbacks")]
    [Tooltip("MMF_Players to play on death. Called with PlayFeedbacks(position, 1.0). " +
             "Wire death explosions, screen shake, etc.")]
    [SerializeField] private MMF_Player[] deathFeedbacks;

    // =====================
    // Read-Only State
    // =====================

    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    public int CurrentHP { get; private set; }

    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    public bool IsDead { get; private set; }

    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    public float HealthPercent => maxHP > 0 ? (float)CurrentHP / maxHP : 0f;

    // =====================
    // Internal State
    // =====================

    private bool _lowHealthFired;

    // -------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------

    private void Awake()
    {
        CurrentHP = maxHP;
    }

    private void Start()
    {
        // Broadcast initial health so UI elements can initialize
        onHealthChanged?.Invoke(CurrentHP, maxHP);
    }

    // -------------------------------------------------------
    // Public API
    // -------------------------------------------------------

    /**
     * Applies damage from a HitData payload. Designed to be wired directly
     * to HitReceiver.onHitReceived in the Inspector.
     *
     * Flow:
     *  1. Ignore if already dead
     *  2. Subtract damage, clamp to zero
     *  3. Fire onHealthChanged
     *  4. Check low-health threshold
     *  5. If HP is zero, trigger death sequence
     */
    public void TakeDamage(HitData hitData)
    {
        if (IsDead) return;

        // Apply damage, clamped to zero
        CurrentHP = Mathf.Max(0, CurrentHP - hitData.damage);
        onHealthChanged?.Invoke(CurrentHP, maxHP);

        // Low-health threshold: fire once when crossing below
        if (!_lowHealthFired && HealthPercent <= lowHealthThreshold)
        {
            _lowHealthFired = true;
            onLowHealth?.Invoke();
        }

        // Death check
        if (CurrentHP <= 0)
        {
            Die();
        }
    }

    /**
     * Applies raw damage without a HitData payload.
     * Useful for environmental hazards, DoTs, or scripted damage.
     */
    public void TakeDamage(int amount)
    {
        TakeDamage(new HitData
        {
            damage = amount,
            hitPoint = transform.position,
            hitDirection = Vector3.zero,
            knockbackForce = 0f,
            source = null
        });
    }

    /**
     * Restores HP by the given amount, clamped to maxHP.
     * Resets the low-health flag if HP rises above the threshold.
     */
    public void Heal(int amount)
    {
        if (IsDead) return;

        CurrentHP = Mathf.Min(maxHP, CurrentHP + amount);
        onHealthChanged?.Invoke(CurrentHP, maxHP);

        // Reset low-health flag if healed above threshold
        if (_lowHealthFired && HealthPercent > lowHealthThreshold)
        {
            _lowHealthFired = false;
        }
    }

    /**
     * Fully restores HP and resets death/low-health state.
     * Useful for respawn or phase transitions.
     */
    public void ResetHealth()
    {
        IsDead = false;
        _lowHealthFired = false;
        CurrentHP = maxHP;
        onHealthChanged?.Invoke(CurrentHP, maxHP);
    }

    // -------------------------------------------------------
    // Death
    // -------------------------------------------------------

    /**
     * Triggers the death sequence: fires events, then applies the
     * configured death behavior (destroy, deactivate, or events only).
     */
    private void Die()
    {
        IsDead = true;

        // Fire death events
        onDeath?.Invoke();

        // Play death MMF feedbacks at full intensity
        if (deathFeedbacks != null)
        {
            for (int i = 0; i < deathFeedbacks.Length; i++)
            {
                if (deathFeedbacks[i] != null) deathFeedbacks[i].PlayFeedbacks(transform.position, 1f);
            }
        }

        // Apply death behavior
        switch (deathBehavior)
        {
            case DeathBehavior.Destroy:
                Destroy(gameObject, destroyDelay);
                break;

            case DeathBehavior.Deactivate:
                Invoke(nameof(DeactivateSelf), deactivateDelay);
                break;

            case DeathBehavior.EventsOnly:
                // No automatic behavior — handle entirely via events
                break;
        }
    }

    /** Deactivates the GameObject. Called via Invoke for delayed deactivation. */
    private void DeactivateSelf()
    {
        gameObject.SetActive(false);
    }

    // -------------------------------------------------------
    // Editor Utilities
    // -------------------------------------------------------

#if UNITY_EDITOR
    [FoldoutGroup("Debug")]
    [Button("Deal 1 Damage"), GUIColor(1f, 0.6f, 0.6f)]
    private void DebugDeal1Damage()
    {
        if (!Application.isPlaying)
        {
            Debug.Log("[Health] Can only be tested in Play mode.");
            return;
        }
        TakeDamage(1);
    }

    [FoldoutGroup("Debug")]
    [Button("Heal 1 HP"), GUIColor(0.6f, 1f, 0.6f)]
    private void DebugHeal1()
    {
        if (!Application.isPlaying)
        {
            Debug.Log("[Health] Can only be tested in Play mode.");
            return;
        }
        Heal(1);
    }

    [FoldoutGroup("Debug")]
    [Button("Kill"), GUIColor(1f, 0.4f, 0.4f)]
    private void DebugKill()
    {
        if (!Application.isPlaying)
        {
            Debug.Log("[Health] Can only be tested in Play mode.");
            return;
        }
        TakeDamage(CurrentHP);
    }

    [FoldoutGroup("Debug")]
    [Button("Reset"), GUIColor(0.6f, 0.8f, 1f)]
    private void DebugReset()
    {
        if (!Application.isPlaying)
        {
            Debug.Log("[Health] Can only be tested in Play mode.");
            return;
        }
        ResetHealth();
    }
#endif
}

/**
 * Defines what happens to the GameObject when HP reaches zero.
 */
public enum DeathBehavior
{
    Destroy,     // Destroy after delay (simple enemies, destructibles)
    Deactivate,  // SetActive(false) after delay (pooled objects, respawnables)
    EventsOnly   // No automatic behavior — handle entirely via UnityEvents
}
