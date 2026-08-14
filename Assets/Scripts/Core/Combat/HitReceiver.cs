using UnityEngine;
using UnityEngine.Events;
using MoreMountains.Feedbacks;
using Sirenix.OdinInspector;

/**
 * Component placed on any GameObject that can receive melee hits.
 * MeleeHitbox looks for this on hit targets and calls ReceiveHit() with
 * a HitData payload describing the attack. Exposes UnityEvents for wiring
 * reactions (damage, VFX, sound, AI alerts) in the Inspector.
 *
 * Usage:
 *   Add to any enemy, destructible, or interactable that should react to
 *   melee attacks. Wire the UnityEvents to your response logic.
 */
public class HitReceiver : MonoBehaviour
{
    // =====================
    // Settings
    // =====================

    [FoldoutGroup("Hit Receiver")]
    [Tooltip("When true, this receiver ignores all incoming hits (e.g. during invincibility frames).")]
    [SerializeField] private bool invulnerable = false;

    [FoldoutGroup("Hit Receiver")]
    [Tooltip("Minimum seconds between accepted hits. Prevents rapid multi-hit spam. " +
             "Example: 0.1 gives a brief grace period after each hit.")]
    [SerializeField, Min(0f)] private float hitCooldown = 0f;

    [FoldoutGroup("Hit Receiver")]
    [Tooltip("When true, accepted hits are automatically forwarded to a Health component on this " +
             "same GameObject via Health.TakeDamage(hitData) — no Inspector wiring needed. " +
             "If onHitReceived is already wired to that same Health.TakeDamage, the auto-apply is " +
             "skipped so damage is never counted twice. Turn off for receivers that route damage " +
             "elsewhere (e.g. a shield, a parent hull, or purely event-driven reactions).")]
    [SerializeField] private bool autoApplyDamageToHealth = true;

    [FoldoutGroup("Hit Receiver")]
    [Tooltip("When true, accepted hits are automatically forwarded to a Knockback2D component " +
             "on this same GameObject, which shoves the body along HitData.hitDirection by " +
             "HitData.knockbackForce. Hits carrying no knockback force or no direction are " +
             "ignored, so this is safe to leave on even for purely scripted damage.")]
    [SerializeField] private bool autoApplyKnockback = true;

    // =====================
    // Events
    // =====================

    [FoldoutGroup("Events")]
    [Tooltip("Fired when a valid hit is received. Wire to damage logic, death checks, etc.")]
    public UnityEvent<HitData> onHitReceived;

    [FoldoutGroup("Events")]
    [Tooltip("Simplified event that just fires on hit with no payload. " +
             "Convenient for wiring MMF feedbacks, animations, or sounds.")]
    public UnityEvent onHit;

    [FoldoutGroup("Events")]
    [Tooltip("Fired when invulnerability is explicitly enabled via SetInvulnerable(true).")]
    public UnityEvent onInvulnerableEnabled;

    [FoldoutGroup("Events")]
    [Tooltip("Fired when invulnerability is explicitly disabled via SetInvulnerable(false).")]
    public UnityEvent onInvulnerableDisabled;

    // =====================
    // MMF Feedbacks
    // =====================

    [FoldoutGroup("Feedbacks")]
    [Tooltip("MMF_Players to play on hit. Called with PlayFeedbacks(hitPoint, intensity). " +
             "Intensity is normalized: damage / feedbackReferenceHP, clamped 0-1.")]
    [SerializeField] private MMF_Player[] hitFeedbacks;

    [FoldoutGroup("Feedbacks")]
    [Tooltip("Reference HP used to normalize feedback intensity. " +
             "Example: if set to 5 and a hit deals 2 damage, intensity = 2/5 = 0.4. " +
             "Set to 1 for intensity = raw damage.")]
    [SerializeField, Min(1)] private int feedbackReferenceHP = 5;

    // =====================
    // Read-Only State
    // =====================

    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    public int TotalHitsReceived { get; private set; }

    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    public float TimeSinceLastHit => _lastHitTime > 0f ? Time.time - _lastHitTime : -1f;

    // =====================
    // Internal State
    // =====================

    private float _lastHitTime = -999f;

    /** Health on this same GameObject, resolved once in Awake. Null if none exists. */
    private Health _health;

    /** True when onHitReceived already calls _health.TakeDamage, so auto-apply must stand down. */
    private bool _healthAlreadyWired;

    /** Knockback2D on this same GameObject, resolved once in Awake. Null if none exists. */
    private Knockback2D _knockback;

    // -------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------

    private void Awake()
    {
        // Cache the local damage/reaction handlers so the hit path stays lookup-free
        _health = GetComponent<Health>();
        _knockback = GetComponent<Knockback2D>();

        // Detect legacy Inspector wiring (onHitReceived → Health.TakeDamage) on the same Health.
        // Those prefabs would otherwise take damage twice once auto-apply defaults on.
        _healthAlreadyWired = _health != null && IsWiredToHealthTakeDamage();
    }

    /**
     * Scans onHitReceived's persistent (Inspector-authored) listeners for a call to
     * TakeDamage on our cached Health component. Runtime-added listeners are invisible
     * here by design — only Inspector wiring can be double-counted this way.
     */
    private bool IsWiredToHealthTakeDamage()
    {
        if (onHitReceived == null) return false;

        for (int i = 0; i < onHitReceived.GetPersistentEventCount(); i++)
        {
            if (ReferenceEquals(onHitReceived.GetPersistentTarget(i), _health) &&
                // Literal rather than nameof() — Health.TakeDamage is an overloaded
                // method group, and UnityEvent stores the name as a plain string anyway.
                onHitReceived.GetPersistentMethodName(i) == "TakeDamage")
            {
                return true;
            }
        }

        return false;
    }

    // -------------------------------------------------------
    // Public API
    // -------------------------------------------------------

    /**
     * Called by MeleeHitbox (or any other damage source) when this object is hit.
     * Validates against invulnerability and cooldown, then fires events.
     *
     * Returns true if the hit was accepted, false if it was rejected
     * (invulnerable, on cooldown, etc.).
     */
    public bool ReceiveHit(HitData hitData)
    {
        // Invulnerability gate
        if (invulnerable) return false;

        // Cooldown gate
        if (Time.time - _lastHitTime < hitCooldown) return false;

        // Accept the hit
        _lastHitTime = Time.time;
        TotalHitsReceived++;

        // Fire events
        onHitReceived?.Invoke(hitData);
        onHit?.Invoke();

        // Play MMF feedbacks at the hit point with damage-normalized intensity
        if (hitFeedbacks != null)
        {
            float intensity = Mathf.Clamp01((float)hitData.damage / feedbackReferenceHP);
            for (int i = 0; i < hitFeedbacks.Length; i++)
            {
                if (hitFeedbacks[i] != null) hitFeedbacks[i].PlayFeedbacks(hitData.hitPoint, intensity);
            }
        }

        // Shove before damaging, so a killing blow still starts the body moving.
        // (Whether the corpse keeps that momentum is up to the owner — EnemyBase's
        // killVelocityOnDeath zeroes it by default.)
        if (autoApplyKnockback && _knockback != null)
        {
            _knockback.ApplyKnockback(hitData);
        }

        // Apply damage last so death behavior (destroy/deactivate) can't cut the
        // events or feedbacks above short on a same-frame kill.
        if (autoApplyDamageToHealth && _health != null && !_healthAlreadyWired)
        {
            _health.TakeDamage(hitData);
        }

        return true;
    }

    /**
     * Sets the invulnerable flag. Useful for i-frames, shields, or phase transitions.
     * Can be called from animation events, timeline signals, or other scripts.
     */
    public void SetInvulnerable(bool value)
    {
        invulnerable = value;

        if (value) onInvulnerableEnabled?.Invoke();
        else onInvulnerableDisabled?.Invoke();
    }

    // -------------------------------------------------------
    // Editor Utilities
    // -------------------------------------------------------

#if UNITY_EDITOR
    [FoldoutGroup("Debug")]
    [Button("Simulate Hit"), GUIColor(1f, 0.6f, 0.6f)]
    private void SimulateHit()
    {
        var testData = new HitData
        {
            damage = 1,
            knockbackForce = 5f,
            hitDirection = transform.forward,
            hitPoint = transform.position,
            source = null
        };

        bool accepted = ReceiveHit(testData);
        Debug.Log($"[HitReceiver] Simulated hit on '{name}' — {(accepted ? "accepted" : "rejected")}");
    }
#endif
}

/**
 * Payload struct passed to HitReceiver.ReceiveHit() describing a single attack.
 * Carried through events so listeners can react based on damage, direction, etc.
 */
[System.Serializable]
public struct HitData
{
    /** Damage amount from the attack. */
    public int damage;

    /** Knockback impulse magnitude. Direction is in hitDirection. */
    public float knockbackForce;

    /** World-space direction from attacker toward this target. */
    public Vector3 hitDirection;

    /** World-space contact point (approximate — center of overlap). */
    public Vector3 hitPoint;

    /** The GameObject that dealt the hit (weapon or hitbox). Can be null for simulated hits. */
    public GameObject source;
}
