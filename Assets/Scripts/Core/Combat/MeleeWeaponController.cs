using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using Sirenix.OdinInspector;

/**
 * Runtime driver for programmatic melee weapon motions. Evaluates AnimationCurve
 * data from AttackMotionData ScriptableObjects to move a weapon sprite through
 * attack arcs, with per-swing randomness for organic feel.
 *
 * Hierarchy setup:
 *   Camera
 *     └── WeaponAnchor (empty, at weapon rest position)
 *          └── WeaponSprite ← MeleeWeaponController here
 *               └── Hitbox (trigger collider) ← MeleeHitbox here
 *
 * Features:
 *   - Curve-driven absolute positioning (no drift)
 *   - Per-swing jitter for position, rotation, duration, and mirror
 *   - Combo sequencing on rapid attacks, random selection on slow attacks
 *   - Idle sway animation when not attacking
 *   - UnityEvents for attack lifecycle (start, hit, end)
 */
public class MeleeWeaponController : MonoBehaviour
{
    // =====================
    // Motion Pool
    // =====================

    [FoldoutGroup("Motion")]
    [Tooltip("Pool of attack motions to draw from. Combo sequencing cycles through these " +
             "in order; sporadic attacks pick randomly.")]
    [SerializeField, Required] private AttackMotionData[] motionPool;

    [FoldoutGroup("Motion")]
    [Tooltip("Minimum seconds between attacks (measured from attack end to next attack start).")]
    [SerializeField] private float attackCooldown = 0.15f;

    [FoldoutGroup("Motion")]
    [Tooltip("Window after an attack ends where pressing attack again continues the combo " +
             "sequence. Beyond this window, the next attack is picked randomly.")]
    [SerializeField] private float comboWindow = 0.4f;

    // =====================
    // Idle Sway
    // =====================

    [FoldoutGroup("Idle Sway")]
    [Tooltip("Amplitude of the gentle idle bob in local units.")]
    [SerializeField] private float swayAmplitude = 0.02f;

    [FoldoutGroup("Idle Sway")]
    [Tooltip("Frequency of the idle bob in cycles per second.")]
    [SerializeField] private float swayFrequency = 1.5f;

    // =====================
    // Input
    // =====================

    [FoldoutGroup("Input")]
    [Tooltip("Reference to the Attack input action (Button: left mouse / gamepad).")]
    [SerializeField, Required] private InputActionReference attackActionReference;

    // =====================
    // References
    // =====================

    [FoldoutGroup("References")]
    [Tooltip("The MeleeHitbox component on the child hitbox collider. " +
             "Activated/deactivated during the motion's active window.")]
    [SerializeField, Required] private MeleeHitbox hitbox;

    // =====================
    // Events
    // =====================

    [FoldoutGroup("Events")]
    [Tooltip("Fired when an attack motion begins. Wire to MMF feedbacks for screen shake, sounds, etc.")]
    public UnityEvent OnAttackStart;

    [FoldoutGroup("Events")]
    [Tooltip("Fired when the hitbox connects with a target during a swing.")]
    public UnityEvent OnAttackHit;

    [FoldoutGroup("Events")]
    [Tooltip("Fired when an attack motion completes.")]
    public UnityEvent OnAttackEnd;

    // =====================
    // Read-Only State
    // =====================

    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    public bool IsAttacking { get; private set; }

    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    public int CurrentComboIndex { get; private set; }

    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    public float CooldownRemaining { get; private set; }

    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private string _currentMotionName;

    // =====================
    // Internal State
    // =====================

    private InputAction _attackAction;

    // Active swing state
    private AttackMotionData _activeMotion;
    private AttackMotionData.SwingInstance _activeSwing;
    private float _attackTimer;
    private bool _hitboxActive;

    // Combo tracking
    private int _comboIndex;
    private float _lastAttackEndTime;

    // Rest pose (captured on Awake so idle sway returns to origin)
    private Vector3 _restLocalPosition;
    private Vector3 _restLocalEulerAngles;
    private Vector3 _restLocalScale;

    // -------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------

    private void Awake()
    {
        // Capture the weapon's rest pose for absolute positioning
        _restLocalPosition = transform.localPosition;
        _restLocalEulerAngles = transform.localEulerAngles;
        _restLocalScale = transform.localScale;

        // Resolve input action (shared instance pattern — enable once, never disable)
        if (attackActionReference != null)
        {
            _attackAction = attackActionReference.action;
            _attackAction.Enable();
        }

        // Start with hitbox deactivated
        if (hitbox != null) hitbox.SetActive(false);
    }

    private void Update()
    {
        ReadInput();
        UpdateCooldown();

        if (IsAttacking)
        {
            UpdateAttack();
        }
        else
        {
            UpdateIdleSway();
        }
    }

    // -------------------------------------------------------
    // Input
    // -------------------------------------------------------

    /**
     * Polls the attack action and initiates an attack if conditions are met:
     * off cooldown, not currently attacking, and motion pool is populated.
     */
    private void ReadInput()
    {
        if (_attackAction == null) return;
        if (!_attackAction.WasPressedThisFrame()) return;
        if (IsAttacking) return;
        if (CooldownRemaining > 0f) return;
        if (motionPool == null || motionPool.Length == 0) return;

        StartAttack();
    }

    // -------------------------------------------------------
    // Attack Logic
    // -------------------------------------------------------

    /**
     * Begins a new attack swing. Determines which motion to use based on
     * combo state: if the player attacks within the combo window, cycle
     * sequentially through the motion pool. Otherwise, pick randomly.
     */
    private void StartAttack()
    {
        // Combo logic: sequential if within combo window, random otherwise
        bool inComboWindow = (Time.time - _lastAttackEndTime) <= comboWindow;

        if (inComboWindow)
        {
            // Advance combo index, wrapping around the pool
            _comboIndex = (_comboIndex + 1) % motionPool.Length;
        }
        else
        {
            // Random selection for sporadic attacks
            _comboIndex = Random.Range(0, motionPool.Length);
        }

        // Resolve the motion and roll per-swing randomness
        _activeMotion = motionPool[_comboIndex];
        _activeSwing = _activeMotion.RollSwingInstance();
        _attackTimer = 0f;
        _hitboxActive = false;
        IsAttacking = true;
        CurrentComboIndex = _comboIndex;
        _currentMotionName = _activeMotion.name;

        // Wire the current swing's damage data into the hitbox
        if (hitbox != null)
        {
            hitbox.ConfigureSwing(_activeSwing.baseDamage, _activeSwing.knockbackForce);
        }

        OnAttackStart?.Invoke();
    }

    /**
     * Advances the active attack motion each frame. Evaluates position, rotation,
     * and scale curves at normalized time, applies absolute local transform, and
     * manages hitbox activation within the motion's active window.
     */
    private void UpdateAttack()
    {
        _attackTimer += Time.deltaTime;
        float t = Mathf.Clamp01(_attackTimer / _activeSwing.swingDuration);

        // --- Evaluate curves at normalized time ---
        float posX = _activeMotion.positionXCurve.Evaluate(t) * _activeSwing.positionAmp;
        float posY = _activeMotion.positionYCurve.Evaluate(t) * _activeSwing.positionAmp;
        float rotX = _activeMotion.rotationXCurve.Evaluate(t) * _activeSwing.rotationAmp;
        float rotY = _activeMotion.rotationYCurve.Evaluate(t) * _activeSwing.rotationAmp;
        float rotZ = _activeMotion.rotationZCurve.Evaluate(t) * _activeSwing.rotationAmp;
        float scaleAdd = _activeMotion.scaleCurve.Evaluate(t) * _activeSwing.scaleAmp;

        // Apply mirror: flip X position, Y rotation, and Z rotation
        if (_activeSwing.mirrored)
        {
            posX = -posX;
            rotY = -rotY;
            rotZ = -rotZ;
        }

        // --- Set absolute local transform (no drift — weapon is exactly where curves say) ---
        transform.localPosition = _restLocalPosition + new Vector3(posX, posY, 0f);
        transform.localEulerAngles = _restLocalEulerAngles + new Vector3(rotX, rotY, rotZ);
        transform.localScale = _restLocalScale + new Vector3(scaleAdd, scaleAdd, 0f);

        // --- Hitbox activation within the active window ---
        bool shouldBeActive = t >= _activeSwing.activeWindow.x && t <= _activeSwing.activeWindow.y;

        if (shouldBeActive && !_hitboxActive)
        {
            _hitboxActive = true;
            if (hitbox != null) hitbox.SetActive(true);
        }
        else if (!shouldBeActive && _hitboxActive)
        {
            _hitboxActive = false;
            if (hitbox != null) hitbox.SetActive(false);
        }

        // --- Attack complete ---
        if (t >= 1f)
        {
            EndAttack();
        }
    }

    /**
     * Ends the current attack, resets the weapon to its rest pose,
     * deactivates the hitbox, and starts the cooldown timer.
     */
    private void EndAttack()
    {
        IsAttacking = false;
        _lastAttackEndTime = Time.time;
        CooldownRemaining = attackCooldown;
        _currentMotionName = null;

        // Reset to rest pose
        transform.localPosition = _restLocalPosition;
        transform.localEulerAngles = _restLocalEulerAngles;
        transform.localScale = _restLocalScale;

        // Ensure hitbox is off
        if (_hitboxActive)
        {
            _hitboxActive = false;
            if (hitbox != null) hitbox.SetActive(false);
        }

        OnAttackEnd?.Invoke();
    }

    // -------------------------------------------------------
    // Idle Sway
    // -------------------------------------------------------

    /**
     * Applies a gentle sine-based bob when the weapon is at rest,
     * giving it visual life even when not attacking.
     */
    private void UpdateIdleSway()
    {
        float sway = Mathf.Sin(Time.time * swayFrequency * Mathf.PI * 2f) * swayAmplitude;
        transform.localPosition = _restLocalPosition + new Vector3(0f, sway, 0f);
    }

    // -------------------------------------------------------
    // Cooldown
    // -------------------------------------------------------

    /** Ticks the attack cooldown timer toward zero. */
    private void UpdateCooldown()
    {
        if (CooldownRemaining <= 0f) return;

        CooldownRemaining -= Time.deltaTime;
        if (CooldownRemaining < 0f) CooldownRemaining = 0f;
    }

    // -------------------------------------------------------
    // Public API
    // -------------------------------------------------------

    /**
     * Called by MeleeHitbox when a target is hit during this swing.
     * Relays the event upward so external systems (MMF feedbacks, etc.) can react.
     */
    public void NotifyHit()
    {
        OnAttackHit?.Invoke();
    }

    // -------------------------------------------------------
    // Editor Utilities
    // -------------------------------------------------------

#if UNITY_EDITOR
    [FoldoutGroup("Debug")]
    [Button("Test Attack (Random)"), GUIColor(1f, 0.6f, 0.6f)]
    private void TestAttackRandom()
    {
        if (!Application.isPlaying)
        {
            Debug.Log("[MeleeWeaponController] Can only be tested in Play mode.");
            return;
        }
        if (motionPool == null || motionPool.Length == 0)
        {
            Debug.LogWarning("[MeleeWeaponController] Motion pool is empty.");
            return;
        }

        // Force a random attack regardless of cooldown
        _lastAttackEndTime = 0f;
        CooldownRemaining = 0f;
        IsAttacking = false;
        StartAttack();
    }

    [FoldoutGroup("Debug")]
    [Button("Test Attack (Index 0)"), GUIColor(1f, 0.8f, 0.6f)]
    private void TestAttackIndex0()
    {
        if (!Application.isPlaying)
        {
            Debug.Log("[MeleeWeaponController] Can only be tested in Play mode.");
            return;
        }
        if (motionPool == null || motionPool.Length == 0)
        {
            Debug.LogWarning("[MeleeWeaponController] Motion pool is empty.");
            return;
        }

        // Force index 0 attack
        CooldownRemaining = 0f;
        IsAttacking = false;
        _comboIndex = -1; // Will increment to 0 in StartAttack combo path
        _lastAttackEndTime = Time.time; // Simulate combo window active
        StartAttack();
    }
#endif
}
