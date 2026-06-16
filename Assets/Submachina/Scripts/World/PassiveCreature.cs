using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Passive sea creature — wanders freely and flees from the submarine.
     *
     * Never attacks. When the player gets close it panics and swims away,
     * potentially luring the player into denser or more dangerous territory.
     * Drops more O2 than an aggressive enemy since the player has to work to catch it.
     *
     * State machine:
     *   Wander → Flee (player enters fleeRadius)
     *   Flee   → Wander (player exceeds calmRadius)
     *
     * Wander picks a random 2D target point within wanderRadius of its spawn
     * position, swims toward it, then picks another on arrival or after a
     * direction-change interval — more organic than a simple horizontal patrol.
     *
     * Shared plumbing (health wiring, O2 drops, sprite flip, sub targeting)
     * is handled by EnemyBase.
     */
    public class PassiveCreature : EnemyBase
    {
        // =====================
        // Wander
        // =====================

        [FoldoutGroup("Wander")]
        [Tooltip("Speed while wandering. Deliberately slow — this is a gentle creature.")]
        [SerializeField, Min(0f)] private float wanderSpeed = 1.2f;

        [FoldoutGroup("Wander")]
        [Tooltip("Max distance from spawn the creature wanders. " +
                 "It picks a new target when it arrives, within this radius.")]
        [SerializeField, Min(0f)] private float wanderRadius = 4f;

        [FoldoutGroup("Wander")]
        [Tooltip("Picks a new wander target after this many seconds even if it hasn't arrived. " +
                 "Prevents getting stuck drifting toward an unreachable point.")]
        [SerializeField, Min(0.5f)] private float directionChangeInterval = 3f;

        [FoldoutGroup("Wander")]
        [Tooltip("Distance threshold to consider a wander target 'reached'.")]
        [SerializeField, Min(0.05f)] private float arrivalThreshold = 0.4f;

        // =====================
        // Flee
        // =====================

        [FoldoutGroup("Flee")]
        [Tooltip("Player enters this radius → creature panics and flees.")]
        [SerializeField, Min(0f)] private float fleeRadius = 5f;

        [FoldoutGroup("Flee")]
        [Tooltip("Player must exceed this radius before the creature calms back to wandering. " +
                 "Larger than fleeRadius prevents rapid state flickering.")]
        [SerializeField, Min(0f)] private float calmRadius = 8f;

        [FoldoutGroup("Flee")]
        [Tooltip("Speed while fleeing. Fast enough to require the player to pursue, " +
                 "but still catchable.")]
        [SerializeField, Min(0f)] private float fleeSpeed = 4.5f;

        [FoldoutGroup("Flee")]
        [Tooltip("Sprite tint applied while fleeing — a subtle blue-teal to read as 'scared'.")]
        [SerializeField] private Color fleeColor = new Color(0.4f, 0.8f, 1f);

        // =====================
        // Debug
        // =====================

        protected override string CurrentState => _state.ToString();

        // =====================
        // State
        // =====================

        private enum AiState { Wander, Flee }

        private AiState _state = AiState.Wander;
        private Vector2 _wanderTarget;
        private float _directionChangeTimer;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();
            PickNewWanderTarget();
        }

        protected override void Start()
        {
            base.Start();
        }

        // -------------------------------------------------------
        // AI
        // -------------------------------------------------------

        /**
         * Evaluates flee/calm transitions, then drives movement for the current state.
         */
        protected override void UpdateAI()
        {
            UpdateTransitions();

            switch (_state)
            {
                case AiState.Wander: Wander(); break;
                case AiState.Flee:   Flee();   break;
            }
        }

        /**
         * Flee transitions are simple: enter on proximity, leave on distance.
         * No hysteresis is needed for the wander target — the timer handles re-targeting.
         */
        private void UpdateTransitions()
        {
            float dist = DistanceToPlayer();

            switch (_state)
            {
                case AiState.Wander:
                    if (dist <= fleeRadius) EnterFlee();
                    break;

                case AiState.Flee:
                    if (dist > calmRadius) EnterWander();
                    break;
            }
        }

        // -------------------------------------------------------
        // State Transitions
        // -------------------------------------------------------

        private void EnterWander()
        {
            _state = AiState.Wander;
            SetSpriteColor(BaseColor);
            PickNewWanderTarget();
        }

        private void EnterFlee()
        {
            _state = AiState.Flee;
            SetSpriteColor(fleeColor);
        }

        // -------------------------------------------------------
        // Movement
        // -------------------------------------------------------

        /**
         * Swims toward _wanderTarget. Picks a new target on arrival or when
         * the direction-change timer expires — creates a natural drifting motion.
         */
        private void Wander()
        {
            _directionChangeTimer -= Time.fixedDeltaTime;

            bool arrived = Vector2.Distance(transform.position, _wanderTarget) < arrivalThreshold;
            bool timedOut = _directionChangeTimer <= 0f;

            if (arrived || timedOut) PickNewWanderTarget();

            Vector2 dir = (_wanderTarget - (Vector2)transform.position).normalized;
            Rb.linearVelocity = dir * wanderSpeed;
        }

        /**
         * Swims directly away from the player.
         * The flee direction updates every physics tick so the creature
         * always angles away from wherever the sub currently is.
         */
        private void Flee()
        {
            Rb.linearVelocity = DirectionAwayFromPlayer() * fleeSpeed;
        }

        /**
         * Picks a random 2D point within wanderRadius of the spawn position and
         * resets the direction-change timer.
         * Constrained to spawn-relative space so the creature stays in its territory
         * while wandering, even though it can temporarily leave it while fleeing.
         */
        private void PickNewWanderTarget()
        {
            Vector2 offset = Random.insideUnitCircle * wanderRadius;
            _wanderTarget = (Vector2)SpawnPosition + offset;
            _directionChangeTimer = directionChangeInterval;
        }

        // -------------------------------------------------------
        // Death
        // -------------------------------------------------------

        protected override void OnDeath()
        {
            _state = AiState.Wander; // stops flee movement cleanly before base freezes
            base.OnDeath();
        }

        // -------------------------------------------------------
        // Gizmos
        // -------------------------------------------------------

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Flee trigger — yellow
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, fleeRadius);

            // Calm radius — cyan
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, calmRadius);

            // Wander territory — green, centered on spawn
            Gizmos.color = new Color(0.3f, 1f, 0.3f, 0.5f);
            Vector3 origin = Application.isPlaying ? SpawnPosition : transform.position;
            Gizmos.DrawWireSphere(origin, wanderRadius);

            // Current wander target — small magenta sphere
            if (Application.isPlaying && _state == AiState.Wander)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(_wanderTarget, 0.25f);
            }
        }
#endif
    }
}
