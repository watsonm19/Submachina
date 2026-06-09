using UnityEngine;
using UnityEngine.InputSystem;
using Sirenix.OdinInspector;

/**
 * Swiss-army 2D movement controller for testing and experimentation.
 *
 * Reads a Vector2 move action from the new Input System (WASD / left stick) and
 * drives a Rigidbody2D using a thrust + drag + optional-gravity model. Tuning the
 * three core knobs sweeps the whole feel range:
 *
 *   - High acceleration + high damping        → sharp, precise, arcade-snappy.
 *   - Low acceleration  + low damping         → floaty underwater / space drift.
 *   - Damping at 0                            → pure inertia, never stops on its own.
 *
 * Directional gravity is an optional constant force the craft must fight against
 * (e.g. a sinking submarine, or a side-view lander). With it off the controller is
 * orientation-agnostic, so it works equally well for side-view or top-down craft.
 *
 * Physics notes:
 *   - We integrate our own velocity and write it to Rigidbody2D.linearVelocity each
 *     FixedUpdate, reading the body's current velocity back first so collisions
 *     (walls, enemies) are respected rather than fought.
 *   - The Rigidbody2D should use gravityScale = 0 — we apply gravity ourselves so
 *     direction and strength are fully controllable. Reset() sets this up for you.
 */
[RequireComponent(typeof(Rigidbody2D))]
public class DriftController2D : MonoBehaviour
{
    // =====================
    // Input
    // =====================

    [FoldoutGroup("Input")]
    [Tooltip("Vector2 move action (WASD / left stick). Defaults to the project's " +
             "InputSystem_Actions 'Player/Move'. Assign the InputActionReference here.")]
    [SerializeField, Required] private InputActionReference moveAction;

    [FoldoutGroup("Input")]
    [Tooltip("Normalize diagonal input so keyboard diagonals aren't faster than cardinals. " +
             "Analog sticks are already normalized, so this only affects digital input.")]
    [SerializeField] private bool normalizeDiagonals = true;

    // =====================
    // Movement
    // =====================

    [FoldoutGroup("Movement")]
    [Tooltip("Maximum self-propelled speed (units/sec) the craft can reach from thrust. " +
             "Only enforced when Clamp To Max Speed is on.")]
    [SerializeField, MinValue(0f)] private float maxSpeed = 8f;

    [FoldoutGroup("Movement")]
    [Tooltip("Thrust applied by input (units/sec^2). Input adds velocity, it never pulls back " +
             "to zero, so momentum persists. Higher = snappier response. Example: 40 thrust " +
             "reaches max speed 8 in ~0.2s.")]
    [SerializeField, MinValue(0f)] private float acceleration = 40f;

    [FoldoutGroup("Movement")]
    [Tooltip("Velocity bleed-off per second (exponential). This is the inertia / drift knob: " +
             "0 = frictionless space drift (coasts forever); high = water-thick, stops quickly.")]
    [SerializeField, MinValue(0f)] private float linearDamping = 3f;

    [FoldoutGroup("Movement")]
    [Tooltip("Clamp total velocity magnitude to Max Speed. Turn off for pure emergent " +
             "terminal velocity from thrust vs. damping vs. gravity.")]
    [SerializeField] private bool clampToMaxSpeed = true;

    // =====================
    // Gravity (optional)
    // =====================

    [FoldoutGroup("Gravity")]
    [Tooltip("Apply a constant directional force the craft must fight against. " +
             "Off = neutral buoyancy / zero-g (works for top-down or weightless side-view).")]
    [SerializeField] private bool useGravity = false;

    [FoldoutGroup("Gravity")]
    [Tooltip("Direction gravity pulls. Down is the usual side-view default; any vector works " +
             "(e.g. a current pushing sideways). Auto-normalized at use.")]
    [SerializeField, ShowIf(nameof(useGravity))] private Vector2 gravityDirection = Vector2.down;

    [FoldoutGroup("Gravity")]
    [Tooltip("Gravity acceleration strength (units/sec^2). With damping on, terminal fall speed " +
             "settles around strength / damping.")]
    [SerializeField, ShowIf(nameof(useGravity)), MinValue(0f)] private float gravityStrength = 5f;

    // =====================
    // Facing (optional)
    // =====================

    [FoldoutGroup("Facing")]
    [Tooltip("Rotate the body to point along its movement direction. Handy for a craft sprite; " +
             "leave off for a fixed-orientation character.")]
    [SerializeField] private bool rotateTowardMovement = false;

    [FoldoutGroup("Facing")]
    [Tooltip("Degrees the sprite's 'forward' (local +X) leads from world right. Set -90 if the " +
             "art points up by default.")]
    [SerializeField, ShowIf(nameof(rotateTowardMovement))] private float facingOffsetDegrees = 0f;

    [FoldoutGroup("Facing")]
    [Tooltip("How fast the body turns toward the movement direction (deg/sec).")]
    [SerializeField, ShowIf(nameof(rotateTowardMovement)), MinValue(0f)] private float rotationSpeed = 720f;

    [FoldoutGroup("Facing")]
    [Tooltip("Below this speed (units/sec) the craft holds its current facing instead of " +
             "snapping around from tiny drift.")]
    [SerializeField, ShowIf(nameof(rotateTowardMovement)), MinValue(0f)] private float facingSpeedThreshold = 0.1f;

    // =====================
    // Read-Only State
    // =====================

    /** Current world-space velocity of the body. */
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    public Vector2 Velocity => _rb != null ? _rb.linearVelocity : Vector2.zero;

    /** Current speed magnitude (units/sec). */
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    public float Speed => Velocity.magnitude;

    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private Vector2 _moveInput;

    // =====================
    // Internal State
    // =====================

    private Rigidbody2D _rb;
    private bool _warnedMissingAction;

    // -------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------

    /**
     * Configures sensible Rigidbody2D defaults when the component is first added in
     * the editor: no Unity gravity (we own it), no spin from collisions.
     */
    private void Reset()
    {
        var rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
    }

    private void Awake()
    {
        // Cache the body; we drive its velocity directly each physics step
        _rb = GetComponent<Rigidbody2D>();
    }

    private void OnEnable()
    {
        // Enable the shared input action so ReadValue returns live data
        if (moveAction != null && moveAction.action != null) moveAction.action.Enable();
    }

    private void OnDisable()
    {
        // Stop steering immediately so the body doesn't coast on stale input
        _moveInput = Vector2.zero;
    }

    private void Update()
    {
        // Poll input on the render tick for responsiveness, consume it in FixedUpdate
        ReadMoveInput();
    }

    private void FixedUpdate()
    {
        // All physics integration happens on the fixed step
        ApplyMovement(Time.fixedDeltaTime);

        if (rotateTowardMovement) ApplyFacing(Time.fixedDeltaTime);
    }

    // -------------------------------------------------------
    // Input
    // -------------------------------------------------------

    /**
     * Reads the Vector2 move action into _moveInput, optionally normalizing diagonals.
     * Warns once (not every frame) if no action is wired so the console stays clean.
     */
    private void ReadMoveInput()
    {
        // Guard against an unassigned reference without spamming the console
        if (moveAction == null || moveAction.action == null)
        {
            if (!_warnedMissingAction)
            {
                Debug.LogWarning($"[{nameof(DriftController2D)}] No move action assigned on '{name}'.", this);
                _warnedMissingAction = true;
            }
            _moveInput = Vector2.zero;
            return;
        }

        // Raw stick/keyboard vector, magnitude 0..1 (sqrt(2) on digital diagonals)
        Vector2 raw = moveAction.action.ReadValue<Vector2>();

        // Clamp digital diagonals to unit length so they aren't faster than cardinals
        _moveInput = normalizeDiagonals && raw.sqrMagnitude > 1f ? raw.normalized : raw;
    }

    // -------------------------------------------------------
    // Movement
    // -------------------------------------------------------

    /**
     * Core integration: accelerate toward the input-driven target velocity, layer in
     * optional gravity, then bleed off speed via exponential damping. Reads the body's
     * live velocity first so wall/enemy collisions are honored.
     */
    private void ApplyMovement(float dt)
    {
        // Start from the body's actual velocity (picks up collision responses)
        Vector2 velocity = _rb.linearVelocity;

        // Input is THRUST (an acceleration), not a target velocity. Releasing the stick
        // simply stops adding thrust — it never pulls velocity back toward zero — so
        // momentum is preserved. This is what lets zero damping coast forever instead of
        // snapping to a stop the way a MoveTowards-to-target model would.
        velocity += _moveInput * (acceleration * dt);

        // Constant directional force the craft fights against (e.g. sinking)
        if (useGravity) velocity += gravityDirection.normalized * (gravityStrength * dt);

        // Exponential drag = the inertia/drift feel; Clamp01 keeps it stable at large dt.
        // At 0 the craft never slows on its own (pure space inertia, infinite drift).
        if (linearDamping > 0f) velocity *= Mathf.Clamp01(1f - linearDamping * dt);

        // Optional hard cap on self-propelled top speed
        if (clampToMaxSpeed) velocity = Vector2.ClampMagnitude(velocity, maxSpeed);

        _rb.linearVelocity = velocity;
    }

    /**
     * Rotates the body to point along its travel direction, but only once moving fast
     * enough to avoid jittery snapping from residual drift.
     */
    private void ApplyFacing(float dt)
    {
        if (Speed < facingSpeedThreshold) return;

        // Angle of travel, offset so art that points a non-default way still aligns
        float targetAngle = Mathf.Atan2(Velocity.y, Velocity.x) * Mathf.Rad2Deg + facingOffsetDegrees;

        // Ease the current rotation toward the travel angle at a fixed turn rate
        float newAngle = Mathf.MoveTowardsAngle(_rb.rotation, targetAngle, rotationSpeed * dt);
        _rb.MoveRotation(newAngle);
    }

    // -------------------------------------------------------
    // Public API
    // -------------------------------------------------------

    /**
     * Adds an instantaneous velocity change (e.g. a boost dash or explosion shove).
     * Stacks on top of current momentum; damping will bleed it off over time.
     */
    public void AddImpulse(Vector2 impulse)
    {
        if (_rb != null) _rb.linearVelocity += impulse;
    }

    /** Overwrites the current velocity outright (e.g. teleport-and-launch, hard stop). */
    public void SetVelocity(Vector2 velocity)
    {
        if (_rb != null) _rb.linearVelocity = velocity;
    }

    /** Toggle directional gravity at runtime (e.g. entering/leaving a buoyant zone). */
    public void SetGravityEnabled(bool gravityOn) => useGravity = gravityOn;

    // -------------------------------------------------------
    // Editor Utilities
    // -------------------------------------------------------

#if UNITY_EDITOR
    [FoldoutGroup("Debug")]
    [Button("Reset Velocity"), GUIColor(0.6f, 0.8f, 1f)]
    private void EditorResetVelocity()
    {
        if (!Application.isPlaying)
        {
            Debug.Log($"[{nameof(DriftController2D)}] Reset Velocity only works in Play mode.");
            return;
        }
        SetVelocity(Vector2.zero);
    }

    [FoldoutGroup("Debug")]
    [Button("Configure Rigidbody2D For Drift"), GUIColor(0.8f, 1f, 0.6f)]
    private void EditorConfigureRigidbody()
    {
        // One-click setup: kill Unity gravity (we own it) and stop collision spin
        var rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        UnityEditor.EditorUtility.SetDirty(rb);
    }
#endif
}
