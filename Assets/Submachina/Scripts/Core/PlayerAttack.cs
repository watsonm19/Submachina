using System.Collections;
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
    public class PlayerAttack : MonoBehaviour
    {
        // =====================
        // References
        // =====================

        [FoldoutGroup("References")]
        [Tooltip("TurretAim component on the submarine's Turret child object.")]
        [SerializeField] private TurretAim turretAim;

        // =====================
        // Attack Settings
        // =====================

        [FoldoutGroup("Attack")]
        [Tooltip("Distance the cone extends from the turret.")]
        [SerializeField, Min(0f)] private float attackRange = 3f;

        [FoldoutGroup("Attack")]
        [Tooltip("Half-angle of the attack cone in degrees. " +
                 "Example: 45 = 90° total arc — hits enemies in a wide forward sweep.")]
        [SerializeField, Range(10f, 90f)] private float coneHalfAngle = 45f;

        [FoldoutGroup("Attack")]
        [Tooltip("Damage dealt to each enemy hit per swing.")]
        [SerializeField, Min(0)] private int attackDamage = 2;

        [FoldoutGroup("Attack")]
        [Tooltip("Minimum seconds between swings.")]
        [SerializeField, Min(0f)] private float attackCooldown = 0.4f;

        // =====================
        // Input
        // =====================

        [FoldoutGroup("Input")]
        [Tooltip("Button InputAction that triggers the attack.")]
        [SerializeField] private InputActionReference attackAction;

        // =====================
        // Targeting
        // =====================

        [FoldoutGroup("Targeting")]
        [Tooltip("LayerMask for enemies. Must include the 'Enemy' layer.")]
        [SerializeField] private LayerMask enemyLayer;

        // =====================
        // Arc Visual
        // =====================

        [FoldoutGroup("Arc")]
        [Tooltip("Color of the idle aim arc. Low alpha keeps it subtle.")]
        [SerializeField] private Color arcIdleColor = new Color(0.4f, 1f, 1f, 0.15f);

        [FoldoutGroup("Arc")]
        [Tooltip("Color of the arc when an attack fires. Higher alpha for the flash.")]
        [SerializeField] private Color arcFlashColor = new Color(0.6f, 1f, 1f, 0.75f);

        [FoldoutGroup("Arc")]
        [Tooltip("Duration of the flash when an attack fires, in seconds.")]
        [SerializeField, Min(0.01f)] private float flashDuration = 0.12f;

        [FoldoutGroup("Arc")]
        [Tooltip("Material for the arc LineRenderer. Assign a URP Unlit/Particle material. " +
                 "Leave empty to use Unity default (may appear pink in URP).")]
        [SerializeField] private Material arcMaterial;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired each time an attack swing is triggered (cooldown passed).")]
        [SerializeField] private UnityEvent onAttack;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when the swing hits at least one enemy.")]
        [SerializeField] private UnityEvent onDamageDealt;

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

        // Arc geometry: origin + arc fan + return to origin
        // 8 arc segments = 11 total line positions
        private const int ArcSegments = 8;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            BuildArcRenderer();
        }

        private void Start()
        {
            // Positions depend on serialized fields, so set after Awake
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
            if (turretAim == null) return;

            _attackCooldownEnd = Time.time + attackCooldown;
            onAttack?.Invoke();

            Vector2 origin = turretAim.transform.position;
            Vector2 aimDir = turretAim.AimDirection;
            float cosHalfAngle = Mathf.Cos(coneHalfAngle * Mathf.Deg2Rad);

            // Gather all enemies in range, then filter to cone
            Collider2D[] candidates = Physics2D.OverlapCircleAll(origin, attackRange, enemyLayer);
            int hitCount = 0;

            foreach (Collider2D col in candidates)
            {
                Vector2 toEnemy = ((Vector2)col.transform.position - origin).normalized;
                if (Vector2.Dot(aimDir, toEnemy) < cosHalfAngle) continue;

                Health health = col.GetComponent<Health>();
                if (health != null)
                {
                    health.TakeDamage(attackDamage);
                    hitCount++;
                }
            }

            if (hitCount > 0) onDamageDealt?.Invoke();

            Debug.Log($"[PlayerAttack] Swing hit {hitCount} enemies.");
            StartCoroutine(FlashArc());
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
            if (turretAim == null) return;

            GameObject arcGO = new GameObject("AttackArc");
            arcGO.transform.SetParent(turretAim.transform, false);
            arcGO.transform.localPosition = Vector3.zero;

            _arcLine = arcGO.AddComponent<LineRenderer>();
            _arcLine.useWorldSpace = false;
            _arcLine.loop = false;
            _arcLine.positionCount = ArcSegments + 3;
            _arcLine.startWidth = 0.06f;
            _arcLine.endWidth = 0.06f;
            _arcLine.startColor = arcIdleColor;
            _arcLine.endColor = arcIdleColor;
            _arcLine.sortingOrder = 5; // render above sprites

            if (arcMaterial != null)
                _arcLine.material = arcMaterial;
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
         * Briefly shows the attack flash by swapping to a high-alpha color,
         * then returning to the idle color after flashDuration seconds.
         */
        private IEnumerator FlashArc()
        {
            _arcLine.startColor = arcFlashColor;
            _arcLine.endColor = arcFlashColor;

            yield return new WaitForSeconds(flashDuration);

            _arcLine.startColor = arcIdleColor;
            _arcLine.endColor = arcIdleColor;
        }

        // -------------------------------------------------------
        // Editor Utilities
        // -------------------------------------------------------

#if UNITY_EDITOR
        [FoldoutGroup("Debug")]
        [Button("Test Swing"), GUIColor(1f, 0.4f, 0.2f)]
        private void DebugAttack()
        {
            if (!Application.isPlaying) { Debug.Log("[PlayerAttack] Play mode only."); return; }
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
