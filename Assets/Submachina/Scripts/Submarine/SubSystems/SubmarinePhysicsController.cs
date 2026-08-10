using UnityEngine;
using UnityEngine.InputSystem;
using UnityAtoms.BaseAtoms;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /** Selectable rotation axis for facing/tilt — interpreted in the rotated transform's parent space. */
    public enum RotationAxis { X, Y, Z }

    /**
     * Physics-driven player controller for the submarine.
     *
     * Simulates a heavy submersible with realistic momentum and inertia by
     * applying forces to a Rigidbody2D rather than manipulating velocity directly.
     * Unity gravity is disabled — the ocean current is simulated explicitly each
     * FixedUpdate using the currentDescentSpeed Atom, so all systems share a
     * single authoritative current value.
     *
     * Player input produces two kinds of thrust:
     *   - Lateral: left/right force for trench navigation
     *   - Counter-thrust: upward-only force to fight the descent current
     *     (downward player input is intentionally ignored — the current already
     *      handles downward pull, and stacking it breaks control feel)
     *
     * Wire-up:
     *   1. Add this component to the submarine root (with Rigidbody2D).
     *   2. Assign the currentDescentSpeed FloatVariable (owned by CurrentManager).
     *   3. Assign a ThrustAction InputActionReference pointing to a Vector2 action
     *      (e.g., "Move" from your Input Action Asset — WASD/left-stick).
     */
    [RequireComponent(typeof(Rigidbody2D))]
    [UsesFeedbacks(nameof(SubFeedbacks.ThrustActive), nameof(SubFeedbacks.ThrustLateral),
                   nameof(SubFeedbacks.ThrustCounter), nameof(SubFeedbacks.ThrustDownward))]
    public class SubmarinePhysicsController : InputSubmarineComponent
    {
        // =====================
        // Thrust Settings
        // =====================

        [FoldoutGroup("Thrust")]
        [Tooltip("Allow downward thrust input (negative Y). Disable in current-driven mode where the current " +
                 "already handles downward pull; enable in forced-scroll mode where the player navigates freely.")]
        [SerializeField] private bool allowDownwardThrust = false;

        [FoldoutGroup("Thrust")]
        [Tooltip("Apply the downward current force from currentDescentSpeed each FixedUpdate. " +
                 "Disable in forced-scroll mode (AutoScrollCamera) so the camera scroll defines the pace " +
                 "without a competing physics force pushing the sub out of the viewport.")]
        [SerializeField] private bool applyCurrentForce = true;

        [FoldoutGroup("Thrust")]
        [Tooltip("Force applied laterally per physics tick. Higher = snappier side-to-side response.")]
        [SerializeField, Min(0f)] private float lateralThrustForce = 12f;

        [FoldoutGroup("Thrust")]
        [Tooltip("Force applied upward when the player counter-thrusts against the current.")]
        [SerializeField, Min(0f)] private float counterThrustForce = 18f;

        [FoldoutGroup("Thrust")]
        [Tooltip("Scales the Atom's current speed into a Rigidbody force. " +
                 "Example: speed=5, multiplier=1.5 → 7.5 N downward per FixedUpdate.")]
        [SerializeField, Min(0f)] private float currentForceMultiplier = 1.5f;

        // =====================
        // Physics Feel
        // =====================

        [FoldoutGroup("Physics Feel")]
        [Tooltip("Linear damping on the Rigidbody. Higher values kill momentum faster — tune for the 'heavy sub' feel.")]
        [SerializeField, Min(0f)] private float linearDrag = 2.5f;

        [FoldoutGroup("Physics Feel")]
        [Tooltip("Angular drag prevents unwanted spin from asymmetric force application.")]
        [SerializeField, Min(0f)] private float angularDrag = 5f;

        [FoldoutGroup("Physics Feel")]
        [Tooltip("Rigidbody mass. Higher = slower to accelerate but more momentum when moving.")]
        [SerializeField, Min(0.1f)] private float mass = 3f;

        [FoldoutGroup("Physics Feel")]
        [Tooltip("Hard cap on speed in any direction. Prevents runaway momentum from compounding forces.")]
        [SerializeField, Min(0f)] private float maxSpeed = 15f;

        // =====================
        // Facing
        // =====================

        [FoldoutGroup("Facing")]
        [Tooltip("Optional visual-only transform to rotate (e.g., the 'VIsuals' child holding the sprite). " +
                 "If left empty the root transform rotates — note that also flips colliders, UI canvases, " +
                 "and other children. The transform's starting rotation is captured as the neutral pose, " +
                 "so a pre-rotated rig (like a root at Z=90) is preserved.")]
        [SerializeField] private Transform visualRoot;

        [FoldoutGroup("Facing/Horizontal Flip")]
        [Tooltip("Flip the sub to face left/right based on horizontal travel direction. " +
                 "Uses a real 180° rotation (not sprite flipX) so child objects like a rear particle " +
                 "effect physically swing to the correct side.")]
        [SerializeField] private bool enableHorizontalFacing = true;

        [FoldoutGroup("Facing/Horizontal Flip")]
        [Tooltip("Parent-space axis the 180° flip rotates around. Y for an unrotated root; " +
                 "if rotating a child under a Z-rotated root (like VIsuals under the Z=90 Submarine), use X.")]
        [SerializeField] private RotationAxis flipAxis = RotationAxis.Y;

        [FoldoutGroup("Facing/Horizontal Flip")]
        [Tooltip("Minimum horizontal speed before the sub commits to a flip. " +
                 "Prevents rapid left/right flickering while hovering near zero.")]
        [SerializeField, Min(0f)] private float facingFlipThreshold = 0.5f;

        [FoldoutGroup("Facing/Horizontal Flip")]
        [Tooltip("Snap to the new facing immediately. Disable to rotate through the turn at Flip Speed.")]
        [SerializeField] private bool instantFlip = true;

        [FoldoutGroup("Facing/Horizontal Flip"), HideIf("instantFlip")]
        [Tooltip("Degrees per second for a smoothed flip when Instant Flip is off.")]
        [SerializeField, Min(1f)] private float flipSpeed = 720f;

        [FoldoutGroup("Facing/Vertical Tilt")]
        [Tooltip("Tilt the nose up/down toward the vertical travel direction (pitch). " +
                 "Going up tilts the nose up, descending tilts it down.")]
        [SerializeField] private bool enableVerticalTilt = true;

        [FoldoutGroup("Facing/Vertical Tilt")]
        [Tooltip("Parent-space axis the pitch rotates around. Z is standard for 2D.")]
        [SerializeField] private RotationAxis tiltAxis = RotationAxis.Z;

        [FoldoutGroup("Facing/Vertical Tilt")]
        [Tooltip("Reverse the tilt direction if the art ends up pitching the wrong way for your rig.")]
        [SerializeField] private bool invertTilt;

        [FoldoutGroup("Facing/Vertical Tilt")]
        [Tooltip("Maximum pitch in degrees when moving fully up or down. " +
                 "Example: 25 means the nose tilts up to ±25° at full vertical speed.")]
        [SerializeField, Range(0f, 90f)] private float maxTiltAngle = 25f;

        [FoldoutGroup("Facing/Vertical Tilt")]
        [Tooltip("Vertical speed at which the tilt reaches maxTiltAngle. " +
                 "Lower values make the sub pitch dramatically from small movements.")]
        [SerializeField, Min(0.1f)] private float velocityForFullTilt = 6f;

        [FoldoutGroup("Facing/Vertical Tilt")]
        [Tooltip("Degrees per second the tilt eases toward its target. Lower = slow, heavy pitching.")]
        [SerializeField, Min(1f)] private float tiltSpeed = 90f;

        // =====================
        // World Bounds
        // =====================

        [FoldoutGroup("World Bounds")]
        [Tooltip("Maximum horizontal distance from X=0 the submarine can travel. " +
                 "Set to 0 to disable. Replace with physical walls when real cave geometry exists.")]
        [SerializeField, Min(0f)] private float horizontalBoundary = 500f;

        // =====================
        // Atoms
        // =====================

        [FoldoutGroup("Atoms")]
        [Tooltip("Shared FloatVariable owned by CurrentManager. Read each FixedUpdate to drive the downward force.")]
        [SerializeField] private FloatVariable currentDescentSpeed;

        // =====================
        // Input
        // =====================

        [FoldoutGroup("Input")]
        [Tooltip("Vector2 InputAction: X = lateral thrust, Y = counter-thrust (upward only). " +
                 "Assign from your Input Action Asset (e.g., the 'Move' action).")]
        [SerializeField] private InputActionReference thrustAction;

        // =====================
        // Debug / Read-Only State
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private Vector2 CurrentVelocity => _rb != null ? _rb.linearVelocity : Vector2.zero;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float ActiveCurrentForce => currentDescentSpeed != null
            ? currentDescentSpeed.Value * currentForceMultiplier
            : 0f;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private string CurrentFacing => _facingSign >= 0f ? "Right" : "Left";

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float CurrentTilt => _pitchAngle;

        // =====================
        // Public State
        // =====================

        /** Raw thrust input this frame — read by SubmarineDash for dash direction. */
        public Vector2 ThrustInput => _thrustInput;

        /**
         * Set true by SubmarineDash during a burst to bypass the speed clamp.
         * Without this, ClampSpeed immediately cancels the impulse on the same frame.
         */
        public bool IsDashing { get; set; }

        /** +1 when facing right, -1 when facing left — useful for aiming/spawning effects. */
        public float FacingSign => _facingSign;

        /** The designer-tuned base mass, before any registered contributions. */
        public float BaseMass => mass;

        /** Current total Rigidbody mass (base + all registered contributions). */
        public float TotalMass => _rb != null ? _rb.mass : mass;

        // =====================
        // Mass Aggregation
        // =====================

        // Extra mass registered by other systems (cargo, ballast water, tank tiers),
        // keyed by contributor so each source can update or release independently.
        private readonly System.Collections.Generic.Dictionary<object, float> _massContributions = new();

        /**
         * Registers (or updates) an extra-mass contribution from another system.
         * Example: a CargoHold with 12 units × 0.05 mass calls RegisterMass(this, 0.6);
         * the Rigidbody mass becomes base + Σ contributions, so a loaded sub
         * accelerates slower under the same thrust forces — inertia for free.
         */
        public void RegisterMass(object source, float extraMass)
        {
            if (source == null) return;
            _massContributions[source] = Mathf.Max(0f, extraMass);
            RecomputeMass();
        }

        /** Removes a contributor's mass (on module destroy / cargo banked). */
        public void UnregisterMass(object source)
        {
            if (source == null || !_massContributions.Remove(source)) return;
            RecomputeMass();
        }

        /** Applies base + Σ contributions to the Rigidbody. */
        private void RecomputeMass()
        {
            if (_rb == null) return;
            float total = mass;
            foreach (var kvp in _massContributions) total += kvp.Value;
            _rb.mass = total;
        }

        // =====================
        // Upgrade Accessors
        // =====================

        private float LateralForceMod  => Sub?.Upgrades?.Stats.Resolve(SubStats.LateralThrustForce, lateralThrustForce) ?? lateralThrustForce;
        private float CounterForceMod  => Sub?.Upgrades?.Stats.Resolve(SubStats.CounterThrustForce, counterThrustForce) ?? counterThrustForce;

        // =====================
        // Internals
        // =====================

        // Below this absolute input magnitude an axis counts as "not thrusting" —
        // shared by the O2 flags and the thrust feedback toggles so they agree.
        private const float ThrustInputDeadzone = 0.1f;

        private Rigidbody2D _rb;
        private Vector2 _thrustInput;

        // Latches tracking which looping thrust feedbacks are currently playing,
        // so each cue is Played/Stopped only once on its input edge (not every frame).
        private bool _anyThrustPlaying;
        private bool _lateralThrustPlaying;
        private bool _counterThrustPlaying;
        private bool _downwardThrustPlaying;

        private float _facingSign = 1f;
        private float _flipAngle;          // current flip rotation: 0 = right, 180 = left
        private float _pitchAngle;         // current smoothed tilt in degrees (+ = nose up)
        private Quaternion _neutralRotation; // facing transform's starting pose — flip/tilt compose on top

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();
            _rb = GetComponent<Rigidbody2D>();
            RegisterAction(thrustAction);

            // Apply feel settings to the Rigidbody at startup so the Inspector
            // values stay as the single source of truth (not the Rigidbody component fields)
            _rb.mass = mass;
            _rb.linearDamping = linearDrag;
            _rb.angularDamping = angularDrag;
            _rb.gravityScale = 0f;
            _rb.constraints = RigidbodyConstraints2D.FreezeRotation;

            // Remember the facing transform's authored pose (e.g., a root pre-rotated to Z=90)
            // so flip/tilt rotations layer on top of it instead of overwriting it
            _neutralRotation = FacingTransform.localRotation;
        }

        private void Update()
        {
            // Cache input each frame; physics application deferred to FixedUpdate
            _thrustInput = PrimaryAction != null
                ? PrimaryAction.ReadValue<Vector2>()
                : Vector2.zero;

            if (Sub?.O2 != null)
            {
                Sub.O2.IsThrustingLateral  = Mathf.Abs(_thrustInput.x) > ThrustInputDeadzone;
                Sub.O2.IsThrustingVertical = _thrustInput.y > ThrustInputDeadzone;
            }

            // Toggle looping thrust feedbacks on/off to match the player's live input
            UpdateThrustFeedbacks();
        }

        private void LateUpdate()
        {
            // Facing/tilt is purely visual — run after physics & gameplay updates
            UpdateFacingAndTilt();
        }

        private void FixedUpdate()
        {
            ApplyCurrentForce();
            ApplyPlayerThrust();
            ClampSpeed();
            ClampHorizontalBounds();
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            // Release any looping thrust cues so they don't hang while disabled
            StopAllThrustFeedbacks();
        }

        // -------------------------------------------------------
        // Physics
        // -------------------------------------------------------

        /**
         * Applies the downward environmental current as a continuous force.
         * Reading from the Atom means CurrentManager can shift the speed at any
         * time and this controller responds automatically without coupling.
         *
         * Force mode is ForceMode2D.Force (mass-scaled, per-frame) so heavier
         * submarines naturally resist the current more than lighter ones would.
         */
        private void ApplyCurrentForce()
        {
            if (!applyCurrentForce) return;
            if (currentDescentSpeed == null) return;

            float magnitude = currentDescentSpeed.Value * currentForceMultiplier;
            _rb.AddForce(Vector2.down * magnitude, ForceMode2D.Force);
        }

        /**
         * Translates raw player input into Rigidbody forces.
         *
         * Lateral (X): bidirectional — full left/right thrust.
         * Vertical (Y): upward-only counter-thrust — negative Y is clamped to zero.
         *   Allowing downward player input on top of the current makes the sub
         *   feel uncontrollable; the current already handles the downward pull.
         *
         * Example: input=(1, 0.8) → lateral=(12, 0), vertical=(0, 14.4)
         */
        private void ApplyPlayerThrust()
        {
            Vector2 lateral = new Vector2(_thrustInput.x * LateralForceMod, 0f);

            // In current-driven mode only counter-thrust (up) is allowed; in scroll mode full Y range is available
            float verticalInput = allowDownwardThrust ? _thrustInput.y : Mathf.Max(0f, _thrustInput.y);
            Vector2 vertical = new Vector2(0f, verticalInput * CounterForceMod);

            _rb.AddForce(lateral + vertical, ForceMode2D.Force);
        }

        // -------------------------------------------------------
        // Thrust Feedbacks
        // -------------------------------------------------------

        /**
         * Drives the four looping thrust feedback cues from live player input.
         *
         * Each cue mirrors the thrust that ApplyPlayerThrust actually produces,
         * so a feedback only plays while a real force is being applied:
         *   - Lateral:  left/right stick beyond the deadzone.
         *   - Counter:  upward input (the current-fighting counter-thrust).
         *   - Downward: downward input, but only when allowDownwardThrust is on —
         *     with it off the vertical input is clamped away and no force exists.
         *   - Active:   the umbrella cue — true whenever any of the above is.
         *
         * SetThrustFeedback edge-triggers Play/Stop, so cues fire once when input
         * begins and stop once when it ends rather than every frame.
         */
        private void UpdateThrustFeedbacks()
        {
            // Resolve which directions are producing thrust this frame
            bool lateralActive  = Mathf.Abs(_thrustInput.x) > ThrustInputDeadzone;
            bool counterActive  = _thrustInput.y > ThrustInputDeadzone;
            bool downwardActive = allowDownwardThrust && _thrustInput.y < -ThrustInputDeadzone;
            bool anyActive      = lateralActive || counterActive || downwardActive;

            // Toggle each cue on its input edge
            SetThrustFeedback(anyActive,      ref _anyThrustPlaying,      SubFeedbacks.ThrustActive);
            SetThrustFeedback(lateralActive,  ref _lateralThrustPlaying,  SubFeedbacks.ThrustLateral);
            SetThrustFeedback(counterActive,  ref _counterThrustPlaying,  SubFeedbacks.ThrustCounter);
            SetThrustFeedback(downwardActive, ref _downwardThrustPlaying, SubFeedbacks.ThrustDownward);
        }

        /**
         * Edge-triggered Play/Stop for a single looping thrust cue.
         * No-ops while the desired state matches the latch, so the router is
         * only touched when the input crosses on or off.
         */
        private void SetThrustFeedback(bool active, ref bool playing, FeedbackId key)
        {
            if (active == playing) return;
            playing = active;

            if (active) Sub?.Feedbacks?.Play(key, transform.position);
            else        Sub?.Feedbacks?.Stop(key);
        }

        /** Stops any playing thrust cues and clears their latches. */
        private void StopAllThrustFeedbacks()
        {
            SetThrustFeedback(false, ref _anyThrustPlaying,      SubFeedbacks.ThrustActive);
            SetThrustFeedback(false, ref _lateralThrustPlaying,  SubFeedbacks.ThrustLateral);
            SetThrustFeedback(false, ref _counterThrustPlaying,  SubFeedbacks.ThrustCounter);
            SetThrustFeedback(false, ref _downwardThrustPlaying, SubFeedbacks.ThrustDownward);
        }

        /**
         * Enforces maxSpeed by clamping the velocity magnitude each FixedUpdate.
         * Comparison uses sqrMagnitude to avoid a sqrt when the cap isn't breached.
         */
        private void ClampSpeed()
        {
            // Skip during a dash — the burst needs to exceed normal max speed briefly
            if (IsDashing) return;
            if (_rb.linearVelocity.sqrMagnitude > maxSpeed * maxSpeed)
                _rb.linearVelocity = _rb.linearVelocity.normalized * maxSpeed;
        }

        /**
         * Prevents the submarine from travelling beyond horizontalBoundary units
         * from the world center (X=0). When the limit is reached, horizontal
         * velocity is zeroed so the sub doesn't press against an invisible wall.
         * Remove this once real cave wall geometry is in place.
         */
        private void ClampHorizontalBounds()
        {
            if (horizontalBoundary <= 0f) return;

            Vector2 pos = _rb.position;
            if (Mathf.Abs(pos.x) <= horizontalBoundary) return;

            pos.x = Mathf.Clamp(pos.x, -horizontalBoundary, horizontalBoundary);
            _rb.position = pos;
            _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
        }

        // -------------------------------------------------------
        // Facing & Tilt
        // -------------------------------------------------------

        /** The transform that flip/tilt rotations are applied to. */
        private Transform FacingTransform => visualRoot != null ? visualRoot : transform;

        /**
         * Rotates the sub (or its visualRoot) to face its direction of travel.
         *
         * Horizontal flip: a 180° rotation around flipAxis driven by horizontal
         * velocity. The flip only commits once speed clears facingFlipThreshold,
         * so hovering near zero doesn't flicker. Instant by default, or eased at
         * flipSpeed. Because velocity (not input) drives it, a CavitationBurst
         * impulse swings the sub around automatically.
         *
         * Vertical tilt: vertical velocity maps linearly into a pitch angle that
         * eases independently at tiltSpeed — so a snap flip never drags the tilt.
         *   Example: velocityForFullTilt=6, maxTiltAngle=25, vy=-3 → -12.5° (nose down)
         *
         * The final pose composes flip → tilt → neutral (the authored starting
         * rotation), so pre-rotated rigs like a root at Z=90 are preserved, and
         * applying the flip after the tilt keeps positive pitch meaning "nose up"
         * for both facings.
         */
        private void UpdateFacingAndTilt()
        {
            if (!enableHorizontalFacing && !enableVerticalTilt) return;

            Vector2 velocity = _rb.linearVelocity;

            // Commit to a new facing only when clearly moving sideways
            if (Mathf.Abs(velocity.x) > facingFlipThreshold)
                _facingSign = velocity.x >= 0f ? 1f : -1f;

            // Advance the flip angle toward its target — snap or ease per settings
            float targetFlip = enableHorizontalFacing && _facingSign < 0f ? 180f : 0f;
            _flipAngle = instantFlip
                ? targetFlip
                : Mathf.MoveTowards(_flipAngle, targetFlip, flipSpeed * Time.deltaTime);

            // Ease the pitch toward the velocity-mapped target, clamped to max tilt
            float targetPitch = enableVerticalTilt
                ? Mathf.Clamp(velocity.y / velocityForFullTilt, -1f, 1f) * maxTiltAngle
                : 0f;
            _pitchAngle = Mathf.MoveTowards(_pitchAngle, targetPitch, tiltSpeed * Time.deltaTime);

            // Compose: tilt first, then flip, layered onto the authored neutral pose
            float pitchSigned = invertTilt ? -_pitchAngle : _pitchAngle;
            Quaternion flip = Quaternion.AngleAxis(_flipAngle, AxisVector(flipAxis));
            Quaternion tilt = Quaternion.AngleAxis(pitchSigned, AxisVector(tiltAxis));
            FacingTransform.localRotation = flip * tilt * _neutralRotation;
        }

        /** Maps the axis enum to a parent-space unit vector. */
        private static Vector3 AxisVector(RotationAxis axis)
        {
            switch (axis)
            {
                case RotationAxis.X: return Vector3.right;
                case RotationAxis.Y: return Vector3.up;
                default: return Vector3.forward;
            }
        }

        // -------------------------------------------------------
        // Editor Utilities
        // -------------------------------------------------------

#if UNITY_EDITOR
        [FoldoutGroup("Debug")]
        [Button("Kill Velocity"), GUIColor(1f, 0.5f, 0.2f)]
        private void DebugKillVelocity()
        {
            if (!Application.isPlaying) { Debug.Log("[Submarine] Only available in Play mode."); return; }
            _rb.linearVelocity = Vector2.zero;
            _rb.angularVelocity = 0f;
            Debug.Log("[Submarine] Velocity zeroed.");
        }

        [FoldoutGroup("Debug")]
        [Button("Burst Upward"), GUIColor(0.4f, 0.8f, 1f)]
        private void DebugBurstUp()
        {
            if (!Application.isPlaying) { Debug.Log("[Submarine] Only available in Play mode."); return; }
            _rb.AddForce(Vector2.up * counterThrustForce * 5f, ForceMode2D.Impulse);
        }
#endif
    }
}
