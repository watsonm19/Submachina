using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Directional melee attack — a cone swing in the direction the turret is aiming.
     *
     * A semi-transparent arc indicator always shows where the next attack will land.
     * On attack, the arc flashes and any enemies inside the cone take damage.
     *
     * The cone is detected using OverlapCircleAll + dot product filtering, which
     * gives a clean directional feel without needing a custom physics shape.
     *
     * The arc LineRenderer is created procedurally as a child of the Turret object
     * so it inherits the turret's rotation automatically — no manual wiring needed.
     *
     * Setup:
     *   1. Assign TurretAim (the "Turret" child of the submarine).
     *   2. Assign AttackAction (Button type) from your Input Asset.
     *   3. Set Enemy Layer to the "Enemy" layer.
     *   4. Optionally assign Arc Material — a URP Unlit or Particle shader works well.
     *      If left empty, Unity uses the LineRenderer default (may appear pink in URP).
     */
    [UsesFeedbacks(nameof(SubFeedbacks.AttackSwing))]
    [UsesAnchors(nameof(SubAnchors.Muzzle))]
    public class PlayerAttack : SubmarineComponent
    {
        // =====================
        // Attack Settings
        // =====================

        [FoldoutGroup("Attack")] [Tooltip("Distance the cone extends from the turret.")] [SerializeField, Min(0f)]
        private float attackRange = 3f;

        [FoldoutGroup("Attack")]
        [Tooltip("Half-angle of the attack cone in degrees. " +
                 "Example: 45 = 90° total arc — hits enemies in a wide forward sweep.")]
        [SerializeField, Range(10f, 90f)]
        private float coneHalfAngle = 45f;

        [FoldoutGroup("Attack")] [Tooltip("Damage dealt to each enemy hit per swing.")] [SerializeField, Min(0)]
        private int attackDamage = 2;

        [FoldoutGroup("Attack")] [Tooltip("Minimum seconds between swings.")] [SerializeField, Min(0f)]
        private float attackCooldown = 0.4f;

        [FoldoutGroup("Attack")]
        [Tooltip("Anchor the swing feedback spawns from. Drives passed-position feedbacks; " +
                 "feedbacks that self-bind via FeedbackAnchorBinder ignore this.")]
        [SerializeField]
        private AnchorId attackAnchor = SubAnchors.Muzzle;

        // =====================
        // Input
        // =====================

        [FoldoutGroup("Input")] [Tooltip("Button InputAction that triggers the attack.")] [SerializeField]
        private InputActionReference attackAction;

        // =====================
        // Targeting
        // =====================

        [FoldoutGroup("Targeting")] [Tooltip("LayerMask for enemies. Must include the 'Enemy' layer.")] [SerializeField]
        private LayerMask enemyLayer;

        // =====================
        // Arc Visual
        // =====================

        [FoldoutGroup("Arc")]
        [Tooltip("Resting color of the aim arc once fully recovered. Low alpha keeps it subtle. " +
                 "HDR — push values above 1 to bloom with post-processing.")]
        [SerializeField, ColorUsage(true, true)]
        private Color arcIdleColor = new Color(0.4f, 1f, 1f, 0.15f);

        [FoldoutGroup("Arc")]
        [Tooltip("Color the arc snaps to the instant an attack fires, then recovers back to the " +
                 "idle color over the cooldown. HDR — bright values (>1) bloom for a glowing pop.")]
        [SerializeField, ColorUsage(true, true)]
        private Color arcCooldownColor = new Color(2.5f, 5f, 6f, 0.85f);

        [FoldoutGroup("Arc")]
        [Tooltip("Line width of the arc when idle (fully recovered).")]
        [SerializeField, Min(0f)]
        private float arcIdleWidth = 0.06f;

        [FoldoutGroup("Arc")]
        [Tooltip("Line width the instant an attack fires, then recovers to the idle width.")]
        [SerializeField, Min(0f)]
        private float arcCooldownWidth = 0.18f;

        [FoldoutGroup("Arc")]
        [Tooltip("Color blend across the cooldown. X = recovery progress (0 = just fired, " +
                 "1 = recovered), Y = blend from cooldown color (0) to idle color (1).")]
        [SerializeField]
        private AnimationCurve colorRecoveryCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [FoldoutGroup("Arc")]
        [Tooltip("Width blend across the cooldown. X = recovery progress (0 = just fired, " +
                 "1 = recovered), Y = blend from cooldown width (0) to idle width (1).")]
        [SerializeField]
        private AnimationCurve widthRecoveryCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [FoldoutGroup("Arc")]
        [Tooltip("Material for the arc LineRenderer. Use a URP Unlit/Particle additive material so " +
                 "HDR vertex colors bloom. Leave empty to use Unity default (may appear pink in URP).")]
        [SerializeField]
        private Material arcMaterial;

        [FoldoutGroup("Arc")]
        [Tooltip("Optional material swapped in while on cooldown, reverting to the main material " +
                 "when ready. Leave empty to keep using the main material throughout.")]
        [SerializeField]
        private Material arcCooldownMaterial;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired each time an attack swing is triggered (cooldown passed).")]
        [SerializeField]
        private UnityEvent onAttack;

        [FoldoutGroup("Events")] [Tooltip("Fired when the swing hits at least one enemy.")] [SerializeField]
        private UnityEvent onDamageDealt;

        [FoldoutGroup("Events")]
        [Tooltip("Fired the instant an attack starts the cooldown (the weapon becomes unavailable).")]
        [SerializeField]
        private UnityEvent onCooldownStart;

        [FoldoutGroup("Events")]
        [Tooltip("Fired once when the cooldown elapses and the attack is ready to fire again.")]
        [SerializeField]
        private UnityEvent onCooldownReady;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float CooldownRemaining => Mathf.Max(0f, _attackCooldownEnd - Time.time);

        // =====================
        // State
        // =====================

        private float _attackCooldownEnd = -1f;
        private LineRenderer _arcLine;

        // True while a cooldown is in progress; used to fire onCooldownReady
        // exactly once on the frame the cooldown elapses (edge detection).
        private bool _cooldownActive;

        // Arc geometry: origin + arc fan + return to origin
        // 8 arc segments = 11 total line positions
        private const int ArcSegments = 8;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Start()
        {
            BuildArcRenderer();
            UpdateArcGeometry();
        }

        private void OnEnable()
        {
            if (attackAction != null) attackAction.action.Enable();
        }

        private void OnDisable()
        {
            if (attackAction != null) attackAction.action.Disable();
        }

        private void Update()
        {
            if (attackAction != null && attackAction.action.WasPressedThisFrame())
                TryAttack();

            // Announce readiness exactly once on the frame the cooldown elapses.
            if (_cooldownActive && Time.time >= _attackCooldownEnd)
            {
                _cooldownActive = false;
                ApplyArcMaterial(false);
                onCooldownReady?.Invoke();
            }

            // Drive the arc's color/width recovery every frame.
            UpdateArcRecovery();
        }

        // -------------------------------------------------------
        // Attack
        // -------------------------------------------------------

        /**
         * Fires a melee cone in the turret's aim direction.
         * Finds all enemies within attackRange via OverlapCircleAll, then
         * filters to only those within the cone using a dot product check.
         *
         * Dot product vs angle: dot(aimDir, toEnemy) >= cos(halfAngle) means
         * the enemy is within the cone. Example: halfAngle=45° → cos=0.707,
         * so any enemy whose direction dot product is >= 0.707 is in the arc.
         */
        private void TryAttack()
        {
            if (Time.time < _attackCooldownEnd) return;
            if (Sub?.Turret == null) return;

            _attackCooldownEnd = Time.time + attackCooldown;

            // Mark the cooldown active so the recovery update can fire
            // onCooldownReady once when it elapses, and announce the start.
            _cooldownActive = true;
            ApplyArcMaterial(true);
            onCooldownStart?.Invoke();

            // Route the swing cue through the central feedback switchboard.
            // Resolve the spawn point from the anchor registry so the VFX origin
            // follows the configured mount point (e.g. muzzle). Feedbacks that
            // self-bind via FeedbackAnchorBinder ignore this passed position.
            Vector3 fxPos = Sub?.Anchors != null ? Sub.Anchors.Get(attackAnchor).position : transform.position;
            Sub?.Feedbacks?.Play(SubFeedbacks.AttackSwing, fxPos);
            onAttack?.Invoke();

            Vector2 origin = Sub.Turret.transform.position;
            Vector2 aimDir = Sub.Turret.AimDirection;
            float cosHalfAngle = Mathf.Cos(coneHalfAngle * Mathf.Deg2Rad);

            // Gather all enemies in range, then filter to cone
            Collider2D[] candidates = Physics2D.OverlapCircleAll(origin, attackRange, enemyLayer);
            int hitCount = 0;

            foreach (Collider2D col in candidates)
            {
                Vector2 toEnemy = ((Vector2)col.transform.position - origin).normalized;
                if (Vector2.Dot(aimDir, toEnemy) < cosHalfAngle) continue;

                // Route through HitReceiver when present — respects invulnerability
                // and hit cooldown gating (e.g. RammingEnemy's stun-only damage window).
                // Fall back to direct Health damage for enemies without a HitReceiver.
                HitReceiver hitReceiver = col.GetComponent<HitReceiver>();
                if (hitReceiver != null)
                {
                    HitData hitData = new HitData
                    {
                        damage        = attackDamage,
                        hitPoint      = col.transform.position,
                        hitDirection  = toEnemy,
                        knockbackForce = 0f,
                        source        = gameObject
                    };
                    if (hitReceiver.ReceiveHit(hitData)) hitCount++;
                }
                else
                {
                    Health health = col.GetComponent<Health>();
                    if (health != null) { health.TakeDamage(attackDamage); hitCount++; }
                }
            }

            if (hitCount > 0) onDamageDealt?.Invoke();

            Debug.Log($"[PlayerAttack] Swing hit {hitCount} enemies.");
        }

        // -------------------------------------------------------
        // Arc Visual
        // -------------------------------------------------------

        /**
         * Creates the LineRenderer as a child of the Turret so it rotates
         * with the turret automatically. useWorldSpace=false means all
         * positions are in the turret's local space — no per-frame updates needed.
         */
        private void BuildArcRenderer()
        {
            if (Sub?.Turret == null) return;

            GameObject arcGO = new GameObject("AttackArc");
            arcGO.transform.SetParent(Sub.Turret.transform, false);
            arcGO.transform.localPosition = Vector3.zero;

            _arcLine = arcGO.AddComponent<LineRenderer>();
            _arcLine.useWorldSpace = false;
            _arcLine.loop = false;
            _arcLine.positionCount = ArcSegments + 3;
            _arcLine.startWidth = arcIdleWidth;
            _arcLine.endWidth = arcIdleWidth;
            _arcLine.startColor = arcIdleColor;
            _arcLine.endColor = arcIdleColor;
            _arcLine.sortingOrder = 5; // render above sprites

            // Seed the idle material (cooldown swaps are handled by ApplyArcMaterial).
            ApplyArcMaterial(false);
        }

        /**
         * Calculates the fan-shaped line positions in the Turret's local space.
         * The fan opens along the local +X axis (which TurretAim points toward the mouse).
         *
         * Layout: origin(0) → left edge(1) → arc points(2..N) → right edge back to origin(N+1)
         * Example with halfAngle=45, range=3: arc sweeps from 45° above to 45° below +X axis.
         */
        private void UpdateArcGeometry()
        {
            if (_arcLine == null) return;

            _arcLine.SetPosition(0, Vector3.zero);

            for (int i = 0; i <= ArcSegments; i++)
            {
                float t = i / (float)ArcSegments;
                float angleDeg = Mathf.Lerp(-coneHalfAngle, coneHalfAngle, t);
                float angleRad = angleDeg * Mathf.Deg2Rad;
                _arcLine.SetPosition(i + 1, new Vector3(
                    Mathf.Cos(angleRad) * attackRange,
                    Mathf.Sin(angleRad) * attackRange,
                    0f));
            }

            _arcLine.SetPosition(ArcSegments + 2, Vector3.zero);
        }

        /**
         * Drives the arc's color and width from their "just fired" cooldown values
         * back to the resting idle values across the cooldown window.
         *
         * progress runs 0 → 1 over attackCooldown seconds (0 = the instant of the
         * swing, 1 = fully recovered). Each recovery curve remaps that progress so
         * color and width can ease, snap, or overshoot independently. LerpUnclamped
         * lets curves that go below 0 / above 1 push HDR colors brighter for a pop.
         */
        private void UpdateArcRecovery()
        {
            if (_arcLine == null) return;

            // Normalized recovery progress since the last swing.
            // Before any attack _attackCooldownEnd is in the past, so progress clamps to 1 (idle).
            float attackStart = _attackCooldownEnd - attackCooldown;
            float progress = attackCooldown > 0f
                ? Mathf.Clamp01((Time.time - attackStart) / attackCooldown)
                : 1f;

            // Blend color: curve Y=0 → cooldown color (bright HDR), Y=1 → idle color.
            Color color = Color.LerpUnclamped(arcCooldownColor, arcIdleColor, colorRecoveryCurve.Evaluate(progress));
            _arcLine.startColor = color;
            _arcLine.endColor = color;

            // Blend width against its own curve the same way.
            float width = Mathf.LerpUnclamped(arcCooldownWidth, arcIdleWidth, widthRecoveryCurve.Evaluate(progress));
            _arcLine.startWidth = width;
            _arcLine.endWidth = width;
        }

        /**
         * Swaps the arc's material between cooldown and idle states.
         * Uses sharedMaterial so we toggle between the assigned assets without
         * leaking per-swap material instances. When no cooldown material is
         * assigned we fall back to the main material, so the look is unchanged.
         */
        private void ApplyArcMaterial(bool onCooldown)
        {
            if (_arcLine == null) return;

            Material target = onCooldown && arcCooldownMaterial != null ? arcCooldownMaterial : arcMaterial;
            if (target != null) _arcLine.sharedMaterial = target;
        }

        // -------------------------------------------------------
        // Editor Utilities
        // -------------------------------------------------------

#if UNITY_EDITOR
        [FoldoutGroup("Debug")]
        [Button("Test Swing"), GUIColor(1f, 0.4f, 0.2f)]
        private void DebugAttack()
        {
            if (!Application.isPlaying)
            {
                Debug.Log("[PlayerAttack] Play mode only.");
                return;
            }

            TryAttack();
        }

        [FoldoutGroup("Debug")]
        [Button("Refresh Arc"), GUIColor(0.6f, 0.8f, 1f)]
        private void DebugRefreshArc()
        {
            UpdateArcGeometry();
        }
#endif
    }
}