using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Aggressive sea creature — patrols, chases, and lunges at the submarine.
     *
     * State machine:
     *   Patrol → Chase (player enters detectionRadius)
     *   Chase  → WindUp (player enters attackRange + cooldown expired)
     *   WindUp → Attacking (telegraph complete; lunge fires in locked direction)
     *   Attacking → AttackCooldown (lunge window over)
     *   AttackCooldown → Chase (cooldown expired)
     *   Chase → Patrol (player exceeds deaggroRadius)
     *
     * The attack is telegraphed by an orange sprite tint + intent indicator during
     * WindUp, then a red tint + lunge during Attacking. Players who read the
     * telegraph can dodge. The lunge direction is locked at WindUp entry so
     * moving during the telegraph actually helps.
     *
     * Shared plumbing (health wiring, O2 drops, sprite flip, sub targeting)
     * is handled by EnemyBase.
     */
    public class EnemyController : EnemyBase
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
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float StateTimer => _stateTimer;

        protected override string CurrentState => _state.ToString();

        // =====================
        // State
        // =====================

        private enum AiState { Patrol, Chase, WindUp, Attacking, AttackCooldown, Dead }

        private AiState _state = AiState.Patrol;
        private float _patrolDirection = 1f;
        private float _stateTimer;
        private Vector2 _attackDirection;
        private bool _hasHitThisAttack;
        private float _attackCooldownRemaining;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Start()
        {
            base.Start();
            SetIntentIndicator(false);
        }

        // -------------------------------------------------------
        // Targeting
        // -------------------------------------------------------

        /**
         * Commit to the telegraphed victim: once winding up or lunging, don't let a
         * now-closer sub steal aggro mid-attack. Patrol / Chase / Cooldown stay free to
         * re-evaluate. (EnemyBase still re-acquires immediately if the target dies or
         * drops out, so a vanished victim is always handled.)
         */
        protected override bool CanRetarget =>
            _state != AiState.WindUp && _state != AiState.Attacking;

        // -------------------------------------------------------
        // AI (called each FixedUpdate by EnemyBase)
        // -------------------------------------------------------

        /**
         * Runs state transitions then executes per-state movement/behavior.
         * Dead state is handled by EnemyBase — IsDead guard prevents this running after death.
         */
        protected override void UpdateAI()
        {
            UpdateTransitions();
            HandleState();
        }

        /**
         * Evaluates when to move between states.
         * Transition graph: Patrol ↔ Chase → WindUp → Attacking → Cooldown → Chase.
         */
        private void UpdateTransitions()
        {
            float dist = DistanceToPlayer();

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
                case AiState.Patrol:          Patrol();               break;
                case AiState.Chase:           Chase();                break;
                case AiState.Attacking:       Lunge();                break;
                case AiState.WindUp:
                case AiState.AttackCooldown:  Rb.linearVelocity = Vector2.zero; break;
            }
        }

        // -------------------------------------------------------
        // State Transitions
        // -------------------------------------------------------

        private void EnterPatrol()
        {
            _state = AiState.Patrol;
            SetSpriteColor(BaseColor);
            SetIntentIndicator(false);
        }

        private void EnterChase()
        {
            _state = AiState.Chase;
            SetSpriteColor(BaseColor);
            SetIntentIndicator(false);
        }

        /**
         * Locks the lunge direction toward the player at telegraph entry.
         * The player can dodge by moving during the wind-up since the strike
         * fires in this fixed direction regardless of where they go.
         */
        private void EnterWindUp()
        {
            _state = AiState.WindUp;
            _stateTimer = 0f;
            _hasHitThisAttack = false;
            _attackDirection = DirectionToPlayer();

            // Orange tint + intent indicator telegraph
            SetSpriteColor(new Color(1f, 0.5f, 0.1f));
            SetIntentIndicator(true);
        }

        private void EnterAttacking()
        {
            _state = AiState.Attacking;
            _stateTimer = 0f;
            SetSpriteColor(new Color(1f, 0.15f, 0.15f));
        }

        private void EnterCooldown()
        {
            _state = AiState.AttackCooldown;
            _stateTimer = 0f;
            _attackCooldownRemaining = attackCooldown;
            Rb.linearVelocity = Vector2.zero;
            SetSpriteColor(BaseColor);
            SetIntentIndicator(false);
        }

        // -------------------------------------------------------
        // Movement
        // -------------------------------------------------------

        /** Horizontal patrol within ±patrolRange of spawn. Reverses at boundaries. */
        private void Patrol()
        {
            float offsetX = transform.position.x - SpawnPosition.x;
            if (offsetX >= patrolRange)  _patrolDirection = -1f;
            else if (offsetX <= -patrolRange) _patrolDirection = 1f;

            Rb.linearVelocity = new Vector2(_patrolDirection * patrolSpeed, 0f);
        }

        /** Moves directly toward the player at chaseSpeed. */
        private void Chase()
        {
            Rb.linearVelocity = DirectionToPlayer() * chaseSpeed;
        }

        /** Applies a velocity burst in the stored attack direction. */
        private void Lunge()
        {
            Rb.linearVelocity = _attackDirection * lungeSpeed;
        }

        // -------------------------------------------------------
        // Hit Detection
        // -------------------------------------------------------

        /**
         * Deals damage on contact during the lunge. Fires only once per attack
         * cycle (_hasHitThisAttack) so sustained contact doesn't stack damage.
         *
         * Damages the submarine actually struck — resolved from the collision, not the
         * cached target — so in co-op the enemy can't lunge at one sub and clip another
         * but credit the damage to the wrong player.
         */
        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (_state != AiState.Attacking) return;
            if (_hasHitThisAttack) return;
            if (!collision.collider.CompareTag("Player")) return;

            var victim = collision.collider.GetComponentInParent<Submarine>();
            victim?.Health?.TakeDamage(attackDamage);
            _hasHitThisAttack = true;
        }

        // -------------------------------------------------------
        // Death
        // -------------------------------------------------------

        protected override void OnDeath()
        {
            _state = AiState.Dead;
            base.OnDeath();
        }

        // -------------------------------------------------------
        // Gizmos
        // -------------------------------------------------------

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Detection — yellow
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, detectionRadius);

            // Deaggro — red
            Gizmos.color = new Color(1f, 0.3f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, deaggroRadius);

            // Attack range — orange
            Gizmos.color = new Color(1f, 0.5f, 0.1f);
            Gizmos.DrawWireSphere(transform.position, attackRange);

            // Patrol range — cyan line
            Gizmos.color = Color.cyan;
            Vector3 origin = Application.isPlaying ? SpawnPosition : transform.position;
            Gizmos.DrawLine(origin + Vector3.left * patrolRange, origin + Vector3.right * patrolRange);
        }
#endif
    }
}
