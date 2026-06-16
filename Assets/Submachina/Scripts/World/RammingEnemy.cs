using UnityEngine;
using DG.Tweening;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Ramming sea creature — telegraphs a high-speed charge, then is left stunned.
     *
     * The core tension is a risk/reward loop: the long telegraph window lets a
     * skilled player dodge, and the stun afterward is a free damage window. Players
     * who fail to dodge take heavy damage; players who read it get an easy kill.
     *
     * State machine:
     *   Patrol → WindUp (player enters detectionRadius)
     *   WindUp → Charging (telegraph complete; charge direction locked)
     *   Charging → Stunned (traveled maxChargeDistance, or chargeDuration expired)
     *   Stunned → Patrol (stun recovery complete)
     *
     * The charge direction is locked at WindUp entry — moving during the telegraph
     * actually helps the player dodge.
     *
     * Shared plumbing (health wiring, O2 drops, sprite flip, sub targeting)
     * is handled by EnemyBase.
     */
    public class RammingEnemy : EnemyBase
    {
        // =====================
        // Movement
        // =====================

        [FoldoutGroup("Movement")]
        [Tooltip("Patrol speed — deliberately slow and lumbering to contrast with the charge.")]
        [SerializeField, Min(0f)] private float patrolSpeed = 1.5f;

        [FoldoutGroup("Movement")]
        [Tooltip("Horizontal distance from spawn the enemy patrols.")]
        [SerializeField, Min(0f)] private float patrolRange = 4f;

        // =====================
        // Detection
        // =====================

        [FoldoutGroup("Detection")]
        [Tooltip("Player enters this radius → enemy begins winding up its charge.")]
        [SerializeField, Min(0f)] private float detectionRadius = 7f;

        // =====================
        // Wind-Up
        // =====================

        [FoldoutGroup("Wind-Up")]
        [Tooltip("Seconds of telegraph before the charge fires. Long enough for the player " +
                 "to react — this is the whole skill expression of this enemy.")]
        [SerializeField, Min(0.1f)] private float windUpDuration = 0.8f;

        [FoldoutGroup("Wind-Up")]
        [Tooltip("Sprite tint during wind-up — orange signals imminent danger.")]
        [SerializeField] private Color windUpColor = new Color(1f, 0.5f, 0.1f);

        // =====================
        // Charge
        // =====================

        [FoldoutGroup("Charge")]
        [Tooltip("Speed during the charge. Very fast — this is what makes it dangerous.")]
        [SerializeField, Min(0f)] private float chargeSpeed = 14f;

        [FoldoutGroup("Charge")]
        [Tooltip("Maximum world-unit distance traveled before transitioning to Stunned. " +
                 "Example: 10 → charges roughly 10 units before running out of steam.")]
        [SerializeField, Min(0f)] private float maxChargeDistance = 10f;

        [FoldoutGroup("Charge")]
        [Tooltip("Hard time cap on the charge in case distance isn't reached " +
                 "(e.g. bouncing around obstacles). chargeSpeed / maxChargeDistance " +
                 "gives the expected duration — add a small buffer.")]
        [SerializeField, Min(0.05f)] private float maxChargeDuration = 1.2f;

        [FoldoutGroup("Charge")]
        [Tooltip("Sprite tint during the charge — bright red = active threat.")]
        [SerializeField] private Color chargeColor = new Color(1f, 0.15f, 0.15f);

        [FoldoutGroup("Charge")]
        [Tooltip("Damage dealt on contact with the player during the charge. " +
                 "High — players who don't dodge pay a big price.")]
        [SerializeField, Min(0)] private int chargeDamage = 5;

        // =====================
        // Stun
        // =====================

        [FoldoutGroup("Stun")]
        [Tooltip("Duration of the stunned / recovery state after the charge ends. " +
                 "This is the player's damage window — make it feel rewarding.")]
        [SerializeField, Min(0.1f)] private float stunDuration = 2f;

        [FoldoutGroup("Stun")]
        [Tooltip("Slow drift speed while stunned — the enemy isn't fully frozen, " +
                 "just dazed. Slight randomness makes it feel organic.")]
        [SerializeField, Min(0f)] private float stunDriftSpeed = 0.4f;

        [FoldoutGroup("Stun")]
        [Tooltip("Sprite tint while stunned — blue-grey reads as dazed, not dangerous.")]
        [SerializeField] private Color stunColor = new Color(0.6f, 0.7f, 1f);

        [FoldoutGroup("Stun")]
        [Tooltip("Scale punch size applied to the enemy on stun entry — sells the impact.")]
        [SerializeField, Min(0f)] private float stunPunchScale = 0.35f;

        [FoldoutGroup("Stun")]
        [Tooltip("Duration of the stun entry punch animation.")]
        [SerializeField, Min(0.05f)] private float stunPunchDuration = 0.2f;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float StateTimer => _stateTimer;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float ChargeDistanceTraveled => _chargeDistanceTraveled;

        protected override string CurrentState => _state.ToString();

        // =====================
        // State
        // =====================

        private enum AiState { Patrol, WindUp, Charging, Stunned, Dead }

        private AiState _state = AiState.Patrol;
        private float _patrolDirection = 1f;
        private float _stateTimer;
        private Vector2 _chargeDirection;
        private Vector2 _chargeStartPosition;
        private float _chargeDistanceTraveled;
        private bool _hasHitThisCharge;
        private Vector2 _stunDriftDirection;
        private HitReceiver _hitReceiver;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Start()
        {
            base.Start();

            // Start invulnerable — only the Stunned phase accepts damage
            _hitReceiver = GetComponent<HitReceiver>();
            _hitReceiver?.SetInvulnerable(true);
        }

        // -------------------------------------------------------
        // AI
        // -------------------------------------------------------

        /**
         * Evaluates state transitions, then drives movement and timers per state.
         */
        protected override void UpdateAI()
        {
            UpdateTransitions();
            HandleState();
        }

        /**
         * State transition evaluation.
         * Wind-up and stun are time-gated; charge ends on distance OR time cap.
         *
         * Example: chargeSpeed=14, maxChargeDistance=10 → charge lasts ~0.71s.
         *          maxChargeDuration=1.2 ensures stun even if obstacles slow it.
         */
        private void UpdateTransitions()
        {
            float dist = DistanceToPlayer();

            switch (_state)
            {
                case AiState.Patrol:
                    if (dist <= detectionRadius) EnterWindUp();
                    break;

                case AiState.WindUp:
                    _stateTimer += Time.fixedDeltaTime;
                    if (_stateTimer >= windUpDuration) EnterCharging();
                    break;

                case AiState.Charging:
                    _stateTimer += Time.fixedDeltaTime;
                    _chargeDistanceTraveled = Vector2.Distance(_chargeStartPosition, Rb.position);

                    bool distanceReached = _chargeDistanceTraveled >= maxChargeDistance;
                    bool timedOut        = _stateTimer >= maxChargeDuration;
                    if (distanceReached || timedOut) EnterStunned();
                    break;

                case AiState.Stunned:
                    _stateTimer += Time.fixedDeltaTime;
                    if (_stateTimer >= stunDuration) EnterPatrol();
                    break;
            }
        }

        private void HandleState()
        {
            switch (_state)
            {
                case AiState.Patrol:   Patrol();   break;
                case AiState.WindUp:   WindUp();   break;
                case AiState.Charging: Charging(); break;
                case AiState.Stunned:  Stunned();  break;
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
            _hitReceiver?.SetInvulnerable(true);
        }

        /**
         * Locks the charge direction toward the player at telegraph entry.
         * The player has the entire windUpDuration window to sidestep this vector.
         */
        private void EnterWindUp()
        {
            _state = AiState.WindUp;
            _stateTimer = 0f;
            _hasHitThisCharge = false;
            _chargeDirection = DirectionToPlayer();

            SetSpriteColor(windUpColor);
            SetIntentIndicator(true);
        }

        private void EnterCharging()
        {
            _state = AiState.Charging;
            _stateTimer = 0f;
            _chargeStartPosition = Rb.position;
            _chargeDistanceTraveled = 0f;

            SetSpriteColor(chargeColor);
            SetIntentIndicator(false);
        }

        /**
         * Transitions to dazed/stunned state after the charge completes.
         * Picks a random slow-drift direction and punches the scale to sell the impact.
         */
        private void EnterStunned()
        {
            _state = AiState.Stunned;
            _stateTimer = 0f;
            _stunDriftDirection = Random.insideUnitCircle.normalized;

            SetSpriteColor(stunColor);
            _hitReceiver?.SetInvulnerable(false);

            // Scale punch communicates impact — the enemy recoils from its own charge
            transform.DOPunchScale(Vector3.one * stunPunchScale, stunPunchDuration, 1, 0f);
        }

        // -------------------------------------------------------
        // Movement
        // -------------------------------------------------------

        /** Slow horizontal patrol — the lumbering weight before the explosive charge. */
        private void Patrol()
        {
            float offsetX = transform.position.x - SpawnPosition.x;
            if (offsetX >= patrolRange)       _patrolDirection = -1f;
            else if (offsetX <= -patrolRange) _patrolDirection = 1f;

            Rb.linearVelocity = new Vector2(_patrolDirection * patrolSpeed, 0f);
        }

        /** Holds position during telegraph — the enemy is bracing, not moving. */
        private void WindUp()
        {
            Rb.linearVelocity = Vector2.zero;
        }

        /** Full-speed burst in the locked charge direction. */
        private void Charging()
        {
            Rb.linearVelocity = _chargeDirection * chargeSpeed;
        }

        /** Slow aimless drift while dazed. */
        private void Stunned()
        {
            Rb.linearVelocity = _stunDriftDirection * stunDriftSpeed;
        }

        // -------------------------------------------------------
        // Hit Detection
        // -------------------------------------------------------

        /**
         * Deals heavy damage on contact during the charge.
         * One hit per charge cycle (_hasHitThisCharge) — sustained contact
         * during the slide doesn't stack damage.
         */
        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (_state != AiState.Charging) return;
            if (_hasHitThisCharge) return;
            if (!collision.collider.CompareTag("Player")) return;

            PlayerHealth?.TakeDamage(chargeDamage);
            _hasHitThisCharge = true;
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
            // Detection radius — yellow
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, detectionRadius);

            // Max charge distance — orange arc from current position in charge direction
            if (Application.isPlaying && _state == AiState.Charging)
            {
                Gizmos.color = new Color(1f, 0.5f, 0.1f);
                Gizmos.DrawLine(
                    (Vector3)_chargeStartPosition,
                    (Vector3)_chargeStartPosition + (Vector3)_chargeDirection * maxChargeDistance);
            }

            // Patrol range — cyan line
            Gizmos.color = Color.cyan;
            Vector3 origin = Application.isPlaying ? SpawnPosition : transform.position;
            Gizmos.DrawLine(origin + Vector3.left * patrolRange, origin + Vector3.right * patrolRange);
        }
#endif
    }
}
