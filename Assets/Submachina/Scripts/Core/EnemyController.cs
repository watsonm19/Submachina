using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Sea creature enemy with patrol, chase, and an intentional attack pattern.
     *
     * State machine flow:
     *   Patrol → Chase (player enters detection range)
     *   Chase  → WindUp (player enters attack range + cooldown expired)
     *   WindUp → Attacking (telegraph complete; enemy lunges)
     *   Attacking → AttackCooldown (lunge window over)
     *   AttackCooldown → Chase (cooldown expired)
     *   Chase → Patrol (player escapes deaggro radius)
     *
     * The attack is telegraphed by an orange sprite tint during WindUp,
     * then a red tint + lunge during Attacking. An OverlapBox checks for
     * the player in the strike zone — players who read the telegraph can dodge.
     *
     * On death, O2 bubbles are scattered for the player to collect.
     *
     * Setup:
     *   - Add to an enemy prefab with Rigidbody2D + Collider2D + Health.
     *   - Assign the O2Pickup prefab. O2System is injected by WorldChunk at spawn.
     *   - Set the prefab to the "Enemy" layer so PlayerAttack can target it.
     *   - Tag the player "Player" so contact detection works.
     */
    [RequireComponent(typeof(Rigidbody2D))]
    public class EnemyController : MonoBehaviour
    {
        // =====================
        // Movement
        // =====================

        [FoldoutGroup("Movement")]
        [Tooltip("Speed while patrolling.")]
        [SerializeField, Min(0f)] private float patrolSpeed = 2f;

        [FoldoutGroup("Movement")]
        [Tooltip("Speed when chasing the player.")]
        [SerializeField, Min(0f)] private float chaseSpeed = 4f;

        [FoldoutGroup("Movement")]
        [Tooltip("Horizontal distance from spawn the enemy patrols.")]
        [SerializeField, Min(0f)] private float patrolRange = 5f;

        // =====================
        // Detection
        // =====================

        [FoldoutGroup("Detection")]
        [Tooltip("Range at which the player triggers chase mode.")]
        [SerializeField, Min(0f)] private float detectionRadius = 6f;

        [FoldoutGroup("Detection")]
        [Tooltip("Range the player must exceed to break chase. Larger than detectionRadius " +
                 "creates a hysteresis band that prevents rapid state flickering.")]
        [SerializeField, Min(0f)] private float deaggroRadius = 10f;

        // =====================
        // Attack
        // =====================

        [FoldoutGroup("Attack")]
        [Tooltip("Distance from the enemy at which an attack attempt begins.")]
        [SerializeField, Min(0f)] private float attackRange = 2.5f;

        [FoldoutGroup("Attack")]
        [Tooltip("Seconds of orange telegraph before the lunge fires. " +
                 "Long enough for a skilled player to react, short enough to feel dangerous.")]
        [SerializeField, Min(0.05f)] private float windUpDuration = 0.35f;

        [FoldoutGroup("Attack")]
        [Tooltip("Seconds the lunge and hitbox are active.")]
        [SerializeField, Min(0.05f)] private float attackDuration = 0.2f;

        [FoldoutGroup("Attack")]
        [Tooltip("Seconds before the enemy can attack again after a lunge.")]
        [SerializeField, Min(0.1f)] private float attackCooldown = 1.8f;

        [FoldoutGroup("Attack")]
        [Tooltip("Damage dealt if the player is in the hitbox during the lunge.")]
        [SerializeField, Min(0)] private int attackDamage = 2;

        [FoldoutGroup("Attack")]
        [Tooltip("Speed of the lunge burst during the attack phase.")]
        [SerializeField, Min(0f)] private float lungeSpeed = 8f;

        // =====================
        // Death Drops
        // =====================

        [FoldoutGroup("Death Drops")]
        [Tooltip("O2Pickup prefab spawned on death.")]
        [SerializeField] private GameObject o2BubblePrefab;

        [FoldoutGroup("Death Drops")]
        [Tooltip("Player's ManualBellowsPump — injected by WorldChunk when spawned via chunks.")]
        [SerializeField] private ManualBellowsPump pump;

        [FoldoutGroup("Death Drops")]
        [Tooltip("Number of O2 bubbles dropped on death.")]
        [SerializeField, Min(0)] private int bubbleDropCount = 1;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private string CurrentState => _state.ToString();

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float StateTimer => _stateTimer;

        // =====================
        // State
        // =====================

        private enum AiState { Patrol, Chase, WindUp, Attacking, AttackCooldown }

        private AiState _state = AiState.Patrol;
        private Rigidbody2D _rb;
        private SpriteRenderer _spriteRenderer;
        private Color _baseColor;
        private Transform _player;
        private Health _playerHealth;
        private Vector3 _spawnPosition;
        private float _patrolDirection = 1f;
        private float _stateTimer;
        private Vector2 _attackDirection;
        private bool _hasHitThisAttack;
        private float _attackCooldownRemaining;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 0f;
            _rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            _spawnPosition = transform.position;

            _spriteRenderer = GetComponent<SpriteRenderer>();
            if (_spriteRenderer != null) _baseColor = _spriteRenderer.color;
        }

        private void Start()
        {
            // Cache player references
            GameObject playerGO = GameObject.FindWithTag("Player");
            if (playerGO != null)
            {
                _player = playerGO.transform;
                _playerHealth = playerGO.GetComponent<Health>();
            }

            // Auto-wire death event
            Health health = GetComponent<Health>();
            if (health != null) health.onDeath.AddListener(OnDeath);
        }

        private void FixedUpdate()
        {
            UpdateState();
            HandleState();
        }

        // -------------------------------------------------------
        // State Machine
        // -------------------------------------------------------

        /**
         * Evaluates transitions between states each physics tick.
         * Transitions are one-way except Chase ↔ Patrol and the
         * AttackCooldown → Chase return.
         */
        private void UpdateState()
        {
            if (_player == null) return;
            float dist = Vector2.Distance(transform.position, _player.position);

            switch (_state)
            {
                case AiState.Patrol:
                    if (dist <= detectionRadius) EnterChase();
                    break;

                case AiState.Chase:
                    if (dist > deaggroRadius) EnterPatrol();
                    else if (dist <= attackRange && _attackCooldownRemaining <= 0f) EnterWindUp();
                    break;

                case AiState.WindUp:
                    _stateTimer += Time.fixedDeltaTime;
                    if (_stateTimer >= windUpDuration) EnterAttacking();
                    break;

                case AiState.Attacking:
                    _stateTimer += Time.fixedDeltaTime;
                    if (_stateTimer >= attackDuration) EnterCooldown();
                    break;

                case AiState.AttackCooldown:
                    _attackCooldownRemaining -= Time.fixedDeltaTime;
                    if (_attackCooldownRemaining <= 0f) EnterChase();
                    break;
            }
        }

        private void HandleState()
        {
            switch (_state)
            {
                case AiState.Patrol: Patrol(); break;
                case AiState.Chase: Chase(); break;
                case AiState.Attacking: Lunge(); break;
                case AiState.WindUp:
                case AiState.AttackCooldown:
                    _rb.linearVelocity = Vector2.zero; break;
            }

            // Flip sprite to face movement direction
            if (_spriteRenderer != null && Mathf.Abs(_rb.linearVelocity.x) > 0.1f)
                _spriteRenderer.flipX = _rb.linearVelocity.x < 0f;
        }

        // -------------------------------------------------------
        // State Transitions
        // -------------------------------------------------------

        private void EnterPatrol()
        {
            _state = AiState.Patrol;
            SetSpriteColor(_baseColor);
        }

        private void EnterChase()
        {
            _state = AiState.Chase;
            SetSpriteColor(_baseColor);
        }

        /**
         * Captures the attack direction at wind-up entry (toward the player).
         * Storing it now means the lunge fires in a fixed direction even if the
         * player moves during the telegraph — rewards reading the warning.
         */
        private void EnterWindUp()
        {
            _state = AiState.WindUp;
            _stateTimer = 0f;
            _hasHitThisAttack = false;
            _attackDirection = (_player != null)
                ? ((Vector2)_player.position - (Vector2)transform.position).normalized
                : Vector2.right;

            // Orange tint = "about to attack" telegraph
            SetSpriteColor(new Color(1f, 0.5f, 0.1f));
        }

        private void EnterAttacking()
        {
            _state = AiState.Attacking;
            _stateTimer = 0f;

            // Red tint = active strike
            SetSpriteColor(new Color(1f, 0.15f, 0.15f));
        }

        private void EnterCooldown()
        {
            _state = AiState.AttackCooldown;
            _stateTimer = 0f;
            _attackCooldownRemaining = attackCooldown;
            _rb.linearVelocity = Vector2.zero;
            SetSpriteColor(_baseColor);
        }

        // -------------------------------------------------------
        // Movement
        // -------------------------------------------------------

        /**
         * Horizontal patrol within ±patrolRange of spawn. Reverses at boundaries.
         */
        private void Patrol()
        {
            float offsetX = transform.position.x - _spawnPosition.x;
            if (offsetX >= patrolRange) _patrolDirection = -1f;
            else if (offsetX <= -patrolRange) _patrolDirection = 1f;

            _rb.linearVelocity = new Vector2(_patrolDirection * patrolSpeed, 0f);
        }

        /** Moves directly toward the player at chaseSpeed. */
        private void Chase()
        {
            if (_player == null) return;
            Vector2 dir = ((Vector2)_player.position - _rb.position).normalized;
            _rb.linearVelocity = dir * chaseSpeed;
        }

        /** Applies a velocity burst in the stored attack direction during the lunge. */
        private void Lunge()
        {
            _rb.linearVelocity = _attackDirection * lungeSpeed;
        }

        // -------------------------------------------------------
        // Attack Hit Detection
        // -------------------------------------------------------

        /**
         * Deals damage on physical contact with the player during the lunge.
         * Only fires once per attack cycle (guarded by _hasHitThisAttack) so
         * sustained contact during the slide doesn't stack damage.
         */
        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (_state != AiState.Attacking) return;
            if (_hasHitThisAttack) return;
            if (!collision.collider.CompareTag("Player")) return;

            _playerHealth?.TakeDamage(attackDamage);
            _hasHitThisAttack = true;
        }

        // -------------------------------------------------------
        // Death
        // -------------------------------------------------------

        /**
         * Called by Health.onDeath. Scatters O2 bubbles with a small random
         * offset so they don't all stack on the same position.
         */
        private void OnDeath()
        {
            for (int i = 0; i < bubbleDropCount; i++)
            {
                if (o2BubblePrefab == null) break;

                Vector2 offset = Random.insideUnitCircle * 0.8f;
                GameObject bubble = Instantiate(o2BubblePrefab,
                    transform.position + (Vector3)offset,
                    Quaternion.identity);

                O2Pickup pickup = bubble.GetComponent<O2Pickup>();
                if (pickup != null) pickup.SetPump(pump);
            }
        }

        // -------------------------------------------------------
        // Public API
        // -------------------------------------------------------

        /** Injected by WorldChunk when spawned procedurally via chunks. */
        public void SetPump(ManualBellowsPump bellowsPump)
        {
            pump = bellowsPump;
        }

        // -------------------------------------------------------
        // Helpers
        // -------------------------------------------------------

        private void SetSpriteColor(Color color)
        {
            if (_spriteRenderer != null) _spriteRenderer.color = color;
        }

        // -------------------------------------------------------
        // Editor Gizmos
        // -------------------------------------------------------

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Detection radius — yellow
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, detectionRadius);

            // Deaggro radius — red
            Gizmos.color = new Color(1f, 0.3f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, deaggroRadius);

            // Attack range — orange
            Gizmos.color = new Color(1f, 0.5f, 0.1f);
            Gizmos.DrawWireSphere(transform.position, attackRange);

            // Patrol range — cyan
            Gizmos.color = Color.cyan;
            Vector3 origin = Application.isPlaying ? _spawnPosition : transform.position;
            Gizmos.DrawLine(origin + Vector3.left * patrolRange, origin + Vector3.right * patrolRange);

        }
#endif
    }
}
