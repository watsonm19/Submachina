using UnityEngine;
using UnityEngine.Events;
using MoreMountains.Feedbacks;
using Sirenix.OdinInspector;

/**
 * Rigidbody2D-based knockback. Applies an impulse and then asserts a short
 * "control window" during which the owner's AI is expected to stand down, so
 * the shove actually reads instead of being erased on the next physics tick.
 *
 * Why the control window matters:
 *   Most AI in this project steers by assigning Rb.linearVelocity directly every
 *   FixedUpdate. A raw AddForce would be overwritten before it ever moved the body.
 *   So this component doesn't just push — it owns the body for knockbackDuration
 *   seconds and publishes IsBeingKnockedBack. EnemyBase checks that flag and skips
 *   its steering, which also gives the hit a natural stun beat.
 *
 * Nothing needs to call this directly: HitReceiver auto-forwards accepted hits
 * (see its autoApplyKnockback option), and HitData.knockbackForce is already
 * populated by PlayerAttack and DashRam.
 *
 * Usage:
 *   [Creature Root]
 *     ├── Rigidbody2D    ← required
 *     ├── HitReceiver    ← auto-forwards hits here
 *     ├── Health
 *     └── Knockback2D    ← this component
 */
[RequireComponent(typeof(Rigidbody2D))]
public class Knockback2D : MonoBehaviour
{
    // =====================
    // Impulse
    // =====================

    [FoldoutGroup("Knockback")]
    [Tooltip("Treat knockbackForce as a target speed in units/sec rather than a true physics " +
             "impulse. ON (recommended): a force of 6 shoves every creature at 6 u/s regardless " +
             "of its Rigidbody mass, so one tuned number reads the same on a tiny fish and a " +
             "heavy squid. OFF: uses real AddForce(Impulse), so heavier bodies resist more — " +
             "physically honest but needs per-creature retuning.")]
    [SerializeField] private bool massIndependent = true;

    [FoldoutGroup("Knockback")]
    [Tooltip("Zero the body's existing velocity before applying the impulse. " +
             "ON (recommended): knockback always reads clearly — an eel striking toward you at " +
             "8 u/s still gets visibly thrown back. OFF: the impulse adds to current motion, so " +
             "a fast approach can swallow the shove entirely (8 forward + 6 back = 2 forward).")]
    [SerializeField] private bool overrideVelocity = true;

    [FoldoutGroup("Knockback")]
    [Tooltip("How much of an incoming impulse this body shrugs off. 0 = full knockback, " +
             "0.5 = half strength, 1 = immune. Use for heavy or armored creatures. " +
             "Drivable at runtime via SetResistance() for upgrades or armored phases.")]
    [SerializeField, Range(0f, 1f)] private float knockbackResistance = 0f;

    [FoldoutGroup("Knockback")]
    [Tooltip("Impulses weaker than this (after resistance) are ignored entirely. " +
             "Prevents chip damage from producing twitchy sub-pixel nudges.")]
    [SerializeField, Min(0f)] private float minimumImpulse = 0.1f;

    [FoldoutGroup("Knockback")]
    [Tooltip("Upper bound on resulting knockback speed in units/sec. Stops stacked or " +
             "over-tuned hits from launching a creature off-screen. 0 = no clamp.")]
    [SerializeField, Min(0f)] private float maximumSpeed = 20f;

    // =====================
    // Control Window
    // =====================

    [FoldoutGroup("Control Window")]
    [Tooltip("Seconds this component holds control of the body after a hit. While active, " +
             "IsBeingKnockedBack is true and EnemyBase suspends its AI steering — so this " +
             "doubles as the stun duration. Keep it short (0.2-0.4) for a snappy hit reaction.")]
    [SerializeField, Min(0f)] private float knockbackDuration = 0.3f;

    [FoldoutGroup("Control Window")]
    [Tooltip("Apply this component's own exponential decay during the window. " +
             "ON (recommended): creature Rigidbodies here mostly run zero linear damping, so " +
             "without this the body would coast at full speed until the window expires. " +
             "OFF: leave the slowdown to the Rigidbody2D's own Linear Damping setting.")]
    [SerializeField] private bool applyCustomDrag = true;

    [FoldoutGroup("Control Window")]
    [ShowIf("applyCustomDrag")]
    [Tooltip("Decay rate of knockback velocity (exponential lerp coefficient). " +
             "Higher = snappier stop. Example: 8 sheds ~95% of the speed in ~0.37s, 16 in ~0.19s.")]
    [SerializeField, Min(0f)] private float knockbackDrag = 8f;

    [FoldoutGroup("Control Window")]
    [Tooltip("End the window early once speed drops below this, instead of waiting out the " +
             "full duration. Returns control to the AI as soon as the shove has visibly settled.")]
    [SerializeField, Min(0f)] private float restingSpeedThreshold = 0.35f;

    // =====================
    // Damage Scaling
    // =====================

    [FoldoutGroup("Damage Scaling")]
    [Tooltip("Scale knockback by the damage of the hit, so heavy blows shove harder. " +
             "OFF: every hit uses HitData.knockbackForce as-is.")]
    [SerializeField] private bool scaleWithDamage = false;

    [FoldoutGroup("Damage Scaling")]
    [ShowIf("scaleWithDamage")]
    [Tooltip("Damage that maps to full knockbackForce. Damage below this scales down, above " +
             "scales up (clamped by Max Damage Multiplier). Example: reference 3 and a 6-damage " +
             "hit doubles the shove; a 1-damage hit gives a third of it.")]
    [SerializeField, Min(1)] private int referenceDamage = 3;

    [FoldoutGroup("Damage Scaling")]
    [ShowIf("scaleWithDamage")]
    [Tooltip("Ceiling on the damage-derived multiplier, so a huge crit can't launch the target.")]
    [SerializeField, Min(1f)] private float maxDamageMultiplier = 3f;

    // =====================
    // Events
    // =====================

    [FoldoutGroup("Events")]
    [Tooltip("Fired when a knockback begins. Passes the applied impulse (direction + magnitude) " +
             "for wiring directional VFX, squash/stretch, or AI reactions.")]
    public UnityEvent<Vector2> onKnockbackStart;

    [FoldoutGroup("Events")]
    [Tooltip("Fired when the control window ends and the AI regains steering. " +
             "Wire to recovery animations or 'shake it off' cues.")]
    public UnityEvent onKnockbackEnd;

    // =====================
    // MMF Feedbacks
    // =====================

    [FoldoutGroup("Feedbacks")]
    [Tooltip("MMF_Players to play when knockback starts. Called with " +
             "PlayFeedbacks(position, normalizedStrength) where strength is impulse / Maximum Speed.")]
    [SerializeField] private MMF_Player[] knockbackFeedbacks;

    // =====================
    // Read-Only State
    // =====================

    /** True while this component owns the body — AI steering should stand down. */
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    public bool IsBeingKnockedBack { get; private set; }

    /** Seconds left in the current control window, or 0 when idle. */
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    public float KnockbackTimeRemaining => IsBeingKnockedBack ? Mathf.Max(0f, _windowEndTime - Time.time) : 0f;

    /** Effective strength multiplier after resistance. 1 = full knockback, 0 = immune. */
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    public float KnockbackScale => 1f - knockbackResistance;

    // =====================
    // Internal State
    // =====================

    private Rigidbody2D _rb;

    /** Time.time at which the current control window expires. */
    private float _windowEndTime;

    // -------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
    }

    /**
     * Runs the active control window: decays the knockback velocity and hands
     * control back to the AI once the window expires or the body has settled.
     *
     * Physics-step timing note: because the owner's AI is suspended while
     * IsBeingKnockedBack is true, nothing else is writing linearVelocity this
     * step, so the order of the two FixedUpdates doesn't matter.
     */
    private void FixedUpdate()
    {
        if (!IsBeingKnockedBack) return;

        // Exponential decay toward zero — the body eases out of the shove
        if (applyCustomDrag)
        {
            _rb.linearVelocity = Vector2.Lerp(_rb.linearVelocity, Vector2.zero, knockbackDrag * Time.fixedDeltaTime);
        }

        // Release control on either condition: the window ran out, or the shove
        // has visibly settled and holding the AI any longer would just look frozen.
        bool windowExpired = Time.time >= _windowEndTime;
        bool settled = _rb.linearVelocity.sqrMagnitude <= restingSpeedThreshold * restingSpeedThreshold;

        if (windowExpired || settled) EndKnockback();
    }

    // -------------------------------------------------------
    // Public API
    // -------------------------------------------------------

    /**
     * Applies knockback from a HitData payload — the path HitReceiver uses.
     * Derives the impulse from hitData.hitDirection * hitData.knockbackForce,
     * optionally scaled by hitData.damage.
     *
     * Returns true if a knockback was actually applied. Returns false when the
     * hit carries no knockback, no usable direction (e.g. scripted damage from
     * Health.TakeDamage(int), which leaves hitDirection zero), or the impulse
     * falls under minimumImpulse after resistance.
     */
    public bool ApplyKnockback(HitData hitData)
    {
        if (hitData.knockbackForce <= 0f) return false;

        // Directionless damage (DoTs, pressure, scripted hits) has nothing to push along
        Vector2 direction = hitData.hitDirection;
        if (direction.sqrMagnitude < 0.0001f) return false;

        // Optionally let bigger hits shove harder, clamped so a crit can't launch the target
        float strength = hitData.knockbackForce;
        if (scaleWithDamage)
        {
            float multiplier = Mathf.Min((float)hitData.damage / referenceDamage, maxDamageMultiplier);
            strength *= multiplier;
        }

        return ApplyKnockback(direction.normalized * strength);
    }

    /**
     * Applies a raw impulse and opens the control window.
     * Multiple hits in quick succession accumulate, and the window extends to
     * whichever end time is later — so a flurry keeps the target pinned rather
     * than letting the AI snap back between hits.
     *
     * Example: ApplyKnockback(Vector2.right * 6f) shoves 6 u/s to the right.
     */
    public bool ApplyKnockback(Vector2 impulse)
    {
        // Resistance shrinks the incoming impulse; 1 = fully immune
        impulse *= KnockbackScale;
        if (impulse.magnitude < minimumImpulse) return false;

        // Clear existing motion first so the shove isn't cancelled out by a charging target
        if (overrideVelocity) _rb.linearVelocity = Vector2.zero;

        // Mass-independent treats the impulse as a direct velocity change (predictable
        // across creature sizes); otherwise hand it to the physics engine as a real impulse.
        if (massIndependent) _rb.linearVelocity += impulse;
        else _rb.AddForce(impulse, ForceMode2D.Impulse);

        // Clamp so stacked hits can't fling the body off-screen
        if (maximumSpeed > 0f && _rb.linearVelocity.magnitude > maximumSpeed)
        {
            _rb.linearVelocity = _rb.linearVelocity.normalized * maximumSpeed;
        }

        // Open (or extend) the control window — never shorten an in-flight one
        _windowEndTime = Mathf.Max(_windowEndTime, Time.time + knockbackDuration);
        IsBeingKnockedBack = true;

        // Fire events and juice, with intensity normalized against the speed clamp
        onKnockbackStart?.Invoke(impulse);
        PlayKnockbackFeedbacks(impulse);

        return true;
    }

    /**
     * Immediately ends the control window and returns steering to the AI.
     * Call on death, phase changes, teleports, or anything that should cut a
     * knockback short. Leaves current velocity alone — use Cancel(true) to stop dead.
     */
    public void Cancel(bool zeroVelocity = false)
    {
        if (zeroVelocity && _rb != null) _rb.linearVelocity = Vector2.zero;
        if (IsBeingKnockedBack) EndKnockback();
    }

    /**
     * Sets knockback resistance at runtime (0 = full knockback, 1 = immune).
     * Hook for upgrades, armored phases, or status effects.
     */
    public void SetResistance(float value)
    {
        knockbackResistance = Mathf.Clamp01(value);
    }

    // -------------------------------------------------------
    // Internal Helpers
    // -------------------------------------------------------

    /** Closes the control window and notifies listeners that steering is back. */
    private void EndKnockback()
    {
        IsBeingKnockedBack = false;
        _windowEndTime = 0f;
        onKnockbackEnd?.Invoke();
    }

    /**
     * Plays knockback MMF feedbacks at this body's position, with intensity
     * normalized 0-1 against maximumSpeed so a light tap and a heavy slam
     * can drive different amounts of shake, squash, or particle emission.
     */
    private void PlayKnockbackFeedbacks(Vector2 impulse)
    {
        if (knockbackFeedbacks == null) return;

        float normalized = maximumSpeed > 0f ? Mathf.Clamp01(impulse.magnitude / maximumSpeed) : 1f;
        for (int i = 0; i < knockbackFeedbacks.Length; i++)
        {
            if (knockbackFeedbacks[i] != null) knockbackFeedbacks[i].PlayFeedbacks(transform.position, normalized);
        }
    }

    // -------------------------------------------------------
    // Editor Utilities
    // -------------------------------------------------------

#if UNITY_EDITOR
    [FoldoutGroup("Debug")]
    [Tooltip("Impulse strength used by the directional test buttons below.")]
    [SerializeField, Min(0f)] private float debugTestForce = 6f;

    [FoldoutGroup("Debug")]
    [Button("Test Knock Left"), GUIColor(1f, 0.8f, 0.6f)]
    private void DebugKnockLeft() => DebugKnock(Vector2.left);

    [FoldoutGroup("Debug")]
    [Button("Test Knock Right"), GUIColor(1f, 0.8f, 0.6f)]
    private void DebugKnockRight() => DebugKnock(Vector2.right);

    [FoldoutGroup("Debug")]
    [Button("Test Knock Up"), GUIColor(1f, 0.8f, 0.6f)]
    private void DebugKnockUp() => DebugKnock(Vector2.up);

    /** Shared body for the directional debug buttons — Play mode only, since it drives physics. */
    private void DebugKnock(Vector2 direction)
    {
        if (!Application.isPlaying)
        {
            Debug.Log("[Knockback2D] Can only be tested in Play mode.");
            return;
        }

        bool applied = ApplyKnockback(direction * debugTestForce);
        Debug.Log($"[Knockback2D] Test knock on '{name}' — {(applied ? "applied" : "rejected (resistance / below minimum)")}");
    }
#endif
}
