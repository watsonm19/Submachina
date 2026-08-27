using Core.ProceduralAnimation;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Events;

namespace Submachina.Core
{
    /**
     * Abyssal eel — a sinuous procedural chaser built on the ChainSimulator spine.
     *
     * Behavior loop:
     *   Lurk    — serpentine drift around the spawn point, slow and unbothered.
     *   Hunt    — player detected: weaving approach (direct pursuit + perpendicular
     *             sine weave), body undulation ramps up with speed automatically.
     *   WindUp  — in strike range: brief coil telegraph, body flashes, wave frequency
     *             spikes while speed drops and the head tracks onto the target.
     *   Strike  — straight-line lunge along the locked nose; deals contact damage once.
     *   Recover — coasts out of the lunge in a slow, wide bank; the vulnerability window.
     *
     * The animation IS the tell: undulation frequency and amplitude are driven by
     * the state machine through the ChainSimulator multipliers, so players read the
     * eel's intent from its body language before the lunge comes.
     *
     * Locomotion is heading-based (see Swim): the eel travels along its nose, which
     * turns at a capped rate, with speed eased separately. Nothing in the loop can
     * reverse the travel direction inside a frame — a target behind the eel is
     * answered with a banking loop. That matters because the spine's ChainSimulator
     * takes its facing from the travel direction, and a facing that inverts in one
     * frame makes the whole body re-lay-out at once: the post-attack "pose flip".
     * The chain has its own facingTurnRate cap as a second line of defence.
     */
    public class EelEnemy : EnemyBase
    {
        // =====================
        // Detection
        // =====================

        [FoldoutGroup("Detection")]
        [Tooltip("Player within this range wakes the eel into Hunt.")]
        [SerializeField, Min(1f)] private float detectionRadius = 9f;

        [FoldoutGroup("Detection")]
        [Tooltip("Player beyond this range during Hunt sends the eel back to Lurk (hysteresis band above detection).")]
        [SerializeField, Min(1f)] private float loseInterestRadius = 14f;

        // =====================
        // Movement
        // =====================

        [FoldoutGroup("Movement")]
        [Tooltip("Cruise speed while lurking near the spawn point.")]
        [SerializeField, Min(0f)] private float lurkSpeed = 1.2f;

        [FoldoutGroup("Movement")]
        [Tooltip("Radius around the spawn point the eel patrols while lurking.")]
        [SerializeField, Min(0.5f)] private float lurkRadius = 4f;

        [FoldoutGroup("Movement")]
        [Tooltip("Pursuit speed while hunting.")]
        [SerializeField, Min(0f)] private float huntSpeed = 3.5f;

        [FoldoutGroup("Movement")]
        [Tooltip("Sideways weave amplitude while hunting (world units) — makes the approach serpentine instead of beeline.")]
        [SerializeField, Min(0f)] private float weaveAmplitude = 1.5f;

        [FoldoutGroup("Movement")]
        [Tooltip("Weave oscillations per second while hunting.")]
        [SerializeField, Min(0f)] private float weaveFrequency = 0.8f;

        [FoldoutGroup("Movement")]
        [Tooltip("Acceleration responsiveness (per second): how fast the eel's SPEED tracks what the current state wants. Does NOT govern turning (see Turn Rate), so the two knobs never interfere.")]
        [SerializeField, Range(0.5f, 20f)] private float steering = 4f;

        [FoldoutGroup("Movement")]
        [Tooltip("Turn rate of the eel's heading (degrees/sec) — a real turning circle. The eel swims along its nose and can only rotate this fast, so a target that ends up behind it is answered with a banking loop instead of a reversal through zero velocity. That reversal is what used to flip the body's pose in a single frame.")]
        [SerializeField, Range(20f, 720f)] private float turnRate = 200f;

        // =====================
        // Strike
        // =====================

        [FoldoutGroup("Strike")]
        [Tooltip("Hunt range at which the eel commits to a strike.")]
        [SerializeField, Min(0.5f)] private float strikeRange = 3.5f;

        [FoldoutGroup("Strike")]
        [Tooltip("Coil telegraph duration before the lunge — the player's dodge window.")]
        [SerializeField, Min(0.05f)] private float windUpDuration = 0.4f;

        [FoldoutGroup("Strike")]
        [Tooltip("Turn rate multiplier while coiling. The eel keeps tracking the target through the wind-up and then lunges along its own nose, so the strike never yanks the head onto a new bearing. Higher = tracks harder (tougher to sidestep); 1 = normal turning; below 1 makes a circling player very hard for it to line up.")]
        [SerializeField, Range(0.25f, 3f)] private float windUpTurnScale = 1.5f;

        [FoldoutGroup("Strike")]
        [Tooltip("Creeping speed the eel holds while coiling, instead of stopping outright. Small but deliberately non-zero: the spine takes its facing from travel and ignores speeds under ~0.15, so a fully stopped eel would aim its heading at you without the BODY following — and then the lunge would have to swing the head around mid-flight. A slow creep keeps the body aimed where the strike is going.")]
        [SerializeField, Min(0f)] private float windUpDriftSpeed = 0.8f;

        [FoldoutGroup("Strike")]
        [Tooltip("Lunge speed. Direction locks at the end of WindUp, so a moving player can dodge it.")]
        [SerializeField, Min(1f)] private float strikeSpeed = 13f;

        [FoldoutGroup("Strike")]
        [Tooltip("Lunge duration before entering Recover.")]
        [SerializeField, Min(0.05f)] private float strikeDuration = 0.45f;

        [FoldoutGroup("Strike")]
        [Tooltip("Contact damage dealt while striking (before hull depth-vulnerability scaling).")]
        [SerializeField, Min(0)] private int strikeDamage = 4;

        [FoldoutGroup("Strike")]
        [Tooltip("Loose drift after a strike — the eel's vulnerable/readable recovery window.")]
        [SerializeField, Min(0.1f)] private float recoverDuration = 1.4f;

        [FoldoutGroup("Strike")]
        [Tooltip("Speed the lunge coasts down to during Recover. Deliberately NOT zero — an eel that keeps a little headway banks around in a wide loop, while one that stops dead has to pivot on the spot, which is exactly what makes the body pose snap.")]
        [SerializeField, Min(0f)] private float recoverDriftSpeed = 1f;

        [FoldoutGroup("Strike")]
        [Tooltip("How fast the lunge's speed bleeds off toward Recover Drift Speed (per second). Higher = the lunge dies quickly; lower = a long overshooting glide past the player.")]
        [SerializeField, Range(0.2f, 10f)] private float recoverDrag = 2.5f;

        [FoldoutGroup("Strike")]
        [Tooltip("Turn rate multiplier while recovering — the eel is spent, so it comes back around slowly and wide. 0 = coasts dead straight until Recover ends.")]
        [SerializeField, Range(0f, 1f)] private float recoverTurnScale = 0.5f;

        // =====================
        // Animation Coupling
        // =====================

        [FoldoutGroup("Animation Coupling")]
        [Tooltip("Body chain driven by this brain. Auto-resolves from children if empty.")]
        [SerializeField] private ChainSimulator body;

        [FoldoutGroup("Animation Coupling")]
        [Tooltip("Strip renderer used for the wind-up flash. Auto-resolves from children if empty.")]
        [SerializeField] private ChainStripRenderer bodyRenderer;

        [FoldoutGroup("Animation Coupling")]
        [Tooltip("How fast the body's wave frequency/amplitude ease toward each state's target (per second). The state machine sets targets rather than slamming the multipliers, so a transition ramps the undulation instead of changing the body's shape between one frame and the next. High = crisp state reads, low = dreamy.")]
        [SerializeField, Range(1f, 30f)] private float bodyLanguageEaseSpeed = 8f;

        [FoldoutGroup("Animation Coupling")]
        [Tooltip("Body wave frequency multiplier while lurking — the relaxed, unbothered baseline undulation.")]
        [SerializeField, Min(0f)] private float lurkWaveFrequency = 0.8f;

        [FoldoutGroup("Animation Coupling")]
        [Tooltip("Body wave amplitude multiplier while lurking.")]
        [SerializeField, Min(0f)] private float lurkWaveAmplitude = 1f;

        [FoldoutGroup("Animation Coupling")]
        [Tooltip("Body wave frequency multiplier while hunting — urgency reads through the faster undulation.")]
        [SerializeField, Min(0f)] private float huntWaveFrequency = 1.4f;

        [FoldoutGroup("Animation Coupling")]
        [Tooltip("Body wave amplitude multiplier while hunting.")]
        [SerializeField, Min(0f)] private float huntWaveAmplitude = 1.2f;

        [FoldoutGroup("Animation Coupling")]
        [Tooltip("Wave frequency multiplier at the peak of the wind-up coil.")]
        [SerializeField, Min(1f)] private float windUpWaveBoost = 3f;

        [FoldoutGroup("Animation Coupling")]
        [Tooltip("Peak _FlashAmount pushed into the body material during wind-up.")]
        [SerializeField, Range(0f, 1f)] private float windUpFlashPeak = 0.55f;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired when the wind-up telegraph begins — wire feedbacks/audio here.")]
        public UnityEvent onWindUp;

        [FoldoutGroup("Events")]
        [Tooltip("Fired at the moment of the lunge.")]
        public UnityEvent onStrike;

        [FoldoutGroup("Events")]
        [Tooltip("Fired when the strike connects with a submarine.")]
        public UnityEvent onHitPlayer;

        // =====================
        // State
        // =====================

        private enum AiState { Lurk, Hunt, WindUp, Strike, Recover, Dead }

        private AiState _state = AiState.Lurk;
        private float _stateTimer;
        private float _weavePhase;
        private float _headingAngle;    // world angle (deg) the nose points — turns at turnRate, physically cannot snap
        private float _speed;           // smoothed speed carried along the heading
        private float _waveFreqTarget = 1f;  // per-state body-language targets the live multipliers ease toward
        private float _waveAmpTarget = 1f;
        private Vector2 _strikeDirection;
        private Vector2 _lurkTarget;
        private bool _hasHitThisStrike;
        private MaterialPropertyBlock _mpb;
        private static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");

        protected override string CurrentState => _state.ToString();

        // Lock the target mid-commitment so the coil/lunge finishes on the telegraphed victim.
        protected override bool CanRetarget => _state != AiState.WindUp && _state != AiState.Strike;

        /** Unit vector of the current heading — the direction the eel swims and points. */
        private Vector2 HeadingDirection =>
            new Vector2(Mathf.Cos(_headingAngle * Mathf.Deg2Rad), Mathf.Sin(_headingAngle * Mathf.Deg2Rad));

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();
            if (body == null) body = GetComponentInChildren<ChainSimulator>();
            if (bodyRenderer == null) bodyRenderer = GetComponentInChildren<ChainStripRenderer>();
            _mpb = new MaterialPropertyBlock();
            _lurkTarget = transform.position;

            // Start pointing along +X, which is the facing a freshly snapped ChainSimulator
            // lays its body out behind — so the eel and its spine agree on frame one.
            _headingAngle = 0f;
        }

        // -------------------------------------------------------
        // AI
        // -------------------------------------------------------

        protected override void UpdateAI()
        {
            _stateTimer += Time.fixedDeltaTime;

            // Anything that moved the body behind the AI's back (a knockback shove, a terrain
            // collision) becomes the new truth before we steer, so the model never insists on
            // a stale heading and yanks the eel back onto it.
            SyncHeadingToBody();

            switch (_state)
            {
                case AiState.Lurk: TickLurk(); break;
                case AiState.Hunt: TickHunt(); break;
                case AiState.WindUp: TickWindUp(); break;
                case AiState.Strike: TickStrike(); break;
                case AiState.Recover: TickRecover(); break;
            }

            // Body language is a continuous channel, eased every tick — Enter() only moves the target.
            EaseBodyLanguage();
        }

        /** Serpentine drift between random points around the spawn anchor. */
        private void TickLurk()
        {
            // Wake up when a submarine wanders close.
            if (DistanceToPlayer() < detectionRadius) { Enter(AiState.Hunt); return; }

            // Re-pick a wander point when reached (or on a lazy timeout).
            if (((Vector2)transform.position - _lurkTarget).sqrMagnitude < 0.6f || _stateTimer > 6f)
            {
                _lurkTarget = (Vector2)SpawnPosition + Random.insideUnitCircle * lurkRadius;
                _stateTimer = 0f;
            }

            Swim((_lurkTarget - (Vector2)transform.position).normalized * lurkSpeed);
        }

        /** Weaving pursuit — direct chase plus a perpendicular sine sweep. */
        private void TickHunt()
        {
            float dist = DistanceToPlayer();
            if (dist > loseInterestRadius) { Enter(AiState.Lurk); return; }
            if (dist < strikeRange) { Enter(AiState.WindUp); return; }

            _weavePhase += Time.fixedDeltaTime * weaveFrequency * Mathf.PI * 2f;
            Vector2 toPlayer = DirectionToPlayer();
            Vector2 side = new Vector2(-toPlayer.y, toPlayer.x);

            // e.g. amplitude 1.5 at frequency 0.8: the eel sweeps ±1.5 units across
            // its approach line roughly every 1.25 seconds — snake, not torpedo.
            Vector2 desired = (toPlayer * huntSpeed + side * (Mathf.Sin(_weavePhase) * weaveAmplitude));
            Swim(Vector2.ClampMagnitude(desired, huntSpeed * 1.3f));
        }

        /**
         * Coil telegraph: speed bleeds off while the head keeps tracking the target.
         *
         * Tracking through the coil is what makes the lunge seamless — by the time the
         * direction locks, the nose is already on it, so Strike never asks the body to
         * point somewhere it wasn't already pointing.
         */
        private void TickWindUp()
        {
            // Faster than the normal acceleration ease — a coil is a brace, not a coast.
            // Settles at a creep rather than a standstill so the spine keeps tracking the aim.
            _speed = Mathf.Lerp(_speed, windUpDriftSpeed, 1f - Mathf.Exp(-steering * 2f * Time.fixedDeltaTime));
            TurnToward(DirectionToPlayer(), turnRate * windUpTurnScale);
            Rb.linearVelocity = HeadingDirection * _speed;

            float t = Mathf.Clamp01(_stateTimer / windUpDuration);
            SetFlash(t * windUpFlashPeak);

            if (_stateTimer >= windUpDuration)
            {
                // Locks along the NOSE, not straight at the player — the coil had its chance
                // to line the shot up, so a player who kept moving can still have slipped it.
                _strikeDirection = HeadingDirection;
                Enter(AiState.Strike);
                onStrike?.Invoke();
            }
        }

        /** Straight lunge along the locked direction. */
        private void TickStrike()
        {
            _speed = strikeSpeed; // keep the model in step with the velocity we're forcing
            Rb.linearVelocity = _strikeDirection * strikeSpeed;
            if (_stateTimer >= strikeDuration) Enter(AiState.Recover);
        }

        /**
         * The vulnerability window — the lunge coasts down to a slow glide while the eel
         * banks sluggishly back toward its target.
         *
         * Keeping headway is the whole trick: an eel with speed left turns in a wide arc
         * (radius ≈ speed / turnRate), whereas one that stops dead has to pivot in place,
         * which reverses its travel direction through zero and snaps the spine's pose.
         */
        private void TickRecover()
        {
            _speed = Mathf.Lerp(_speed, recoverDriftSpeed, 1f - Mathf.Exp(-recoverDrag * Time.fixedDeltaTime));
            TurnToward(DirectionToPlayer(), turnRate * recoverTurnScale);
            Rb.linearVelocity = HeadingDirection * _speed;

            if (_stateTimer >= recoverDuration)
                Enter(DistanceToPlayer() < loseInterestRadius ? AiState.Hunt : AiState.Lurk);
        }

        /**
         * Core locomotion: the eel swims along its nose, so a desired velocity is realized
         * as a TURN onto that bearing plus a separate change of speed — never as a lerp of
         * the velocity vector itself.
         *
         * That distinction is the fix for the post-attack pose flip. Lerping velocity toward
         * a target that lies behind you drags the vector through zero, and a velocity vector
         * passing through zero inverts its direction in a single frame — which the spine's
         * ChainSimulator (Velocity facing) reads as "the head is now pointing the other way"
         * and re-lays the whole body out at once. A heading capped at turnRate can't do that:
         * the eel loops around instead, and the body follows the loop.
         */
        private void Swim(Vector2 desiredVelocity)
        {
            float desiredSpeed = desiredVelocity.magnitude;

            // Turn onto the desired bearing (ignored when the state wants a standstill,
            // so the eel holds its last heading rather than spinning on noise).
            if (desiredSpeed > 0.05f) TurnToward(desiredVelocity, turnRate);

            // Speed eases independently — turn settings never change how fast it moves.
            _speed = Mathf.Lerp(_speed, desiredSpeed, 1f - Mathf.Exp(-steering * Time.fixedDeltaTime));

            Rb.linearVelocity = HeadingDirection * _speed;
        }

        /** Rotates the heading toward a direction along the shortest arc, capped at 'rate' degrees/sec. */
        private void TurnToward(Vector2 direction, float rate)
        {
            if (direction.sqrMagnitude < 1e-6f || rate <= 0f) return;
            float desiredAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            _headingAngle = Mathf.MoveTowardsAngle(_headingAngle, desiredAngle, rate * Time.fixedDeltaTime);
        }

        /**
         * Adopts the Rigidbody's real velocity as the heading/speed model whenever something
         * outside the AI has moved the eel — a knockback shove or a hard collision. Without
         * it the next steering tick would overwrite that velocity with the stale model and
         * snap the eel (and its spine) back onto the old bearing.
         *
         * The tolerance is generous because normal steering assigns the velocity itself and
         * therefore never disagrees with the model at all; only an external force can.
         */
        private void SyncHeadingToBody()
        {
            Vector2 actual = Rb.linearVelocity;
            if ((actual - HeadingDirection * _speed).sqrMagnitude < 2.25f) return; // 1.5 u/s

            _speed = actual.magnitude;
            if (_speed > 0.05f) _headingAngle = Mathf.Atan2(actual.y, actual.x) * Mathf.Rad2Deg;
        }

        // -------------------------------------------------------
        // Transitions & animation coupling
        // -------------------------------------------------------

        /** Central state switch — resets timers and retunes the body language per state. */
        private void Enter(AiState next)
        {
            _state = next;
            _stateTimer = 0f;

            switch (next)
            {
                case AiState.Lurk:
                    SetBodyLanguage(lurkWaveFrequency, lurkWaveAmplitude); SetFlash(0f);
                    _lurkTarget = (Vector2)SpawnPosition + Random.insideUnitCircle * lurkRadius;
                    break;
                case AiState.Hunt:
                    SetBodyLanguage(huntWaveFrequency, huntWaveAmplitude); SetFlash(0f);
                    break;
                case AiState.WindUp:
                    SetBodyLanguage(freq: windUpWaveBoost, amp: 1.6f);
                    SetIntentIndicator(true);
                    onWindUp?.Invoke();
                    break;
                case AiState.Strike:
                    // Pin the heading to the locked lunge (already the same bearing — the coil
                    // aimed it — so this only guarantees the model and the velocity agree).
                    _headingAngle = Mathf.Atan2(_strikeDirection.y, _strikeDirection.x) * Mathf.Rad2Deg;
                    _speed = strikeSpeed;

                    // Body goes near-rigid during the lunge — a straight loosed arrow.
                    SetBodyLanguage(freq: 0.4f, amp: 0.4f); SetFlash(0f);
                    SetIntentIndicator(false);
                    _hasHitThisStrike = false;
                    break;
                case AiState.Recover:
                    SetBodyLanguage(freq: 0.7f, amp: 1.8f); // slack, over-wobbly = spent
                    break;
            }
        }

        /**
         * Sets the state machine's body-language target. The live multipliers ease toward it
         * in EaseBodyLanguage rather than jumping — slamming amplitude (e.g. Strike's rigid
         * 0.4 straight into Recover's slack 1.8) changes the spine's shape between two frames,
         * which reads as the pose popping even when the eel's heading is behaving.
         */
        private void SetBodyLanguage(float freq, float amp)
        {
            _waveFreqTarget = freq;
            _waveAmpTarget = amp;
        }

        /** Steps the live wave multipliers toward the current state's target. Called every AI tick. */
        private void EaseBodyLanguage()
        {
            if (body == null) return;
            float t = 1f - Mathf.Exp(-bodyLanguageEaseSpeed * Time.fixedDeltaTime);
            body.WaveFrequencyMultiplier = Mathf.Lerp(body.WaveFrequencyMultiplier, _waveFreqTarget, t);
            body.WaveAmplitudeMultiplier = Mathf.Lerp(body.WaveAmplitudeMultiplier, _waveAmpTarget, t);
        }

        /** Sets the target AND applies it this instant — for moments with no more AI ticks coming (death). */
        private void SnapBodyLanguage(float freq, float amp)
        {
            SetBodyLanguage(freq, amp);
            if (body == null) return;
            body.WaveFrequencyMultiplier = freq;
            body.WaveAmplitudeMultiplier = amp;
        }

        /** Drives the material flash channel through a property block (no material instancing). */
        private void SetFlash(float amount)
        {
            if (bodyRenderer == null || bodyRenderer.Renderer == null) return;
            bodyRenderer.Renderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(FlashAmountId, amount);
            bodyRenderer.Renderer.SetPropertyBlock(_mpb);
        }

        // -------------------------------------------------------
        // Damage & death
        // -------------------------------------------------------

        private void OnCollisionEnter2D(Collision2D collision)
        {
            // Only the lunge bites, once per strike, resolved against the submarine
            // actually struck (co-op safe — same pattern as RammingEnemy).
            if (_state != AiState.Strike || _hasHitThisStrike) return;
            if (!collision.collider.CompareTag("Player")) return;

            var victim = collision.collider.GetComponentInParent<Submarine>();
            if (victim == null) return;

            _hasHitThisStrike = true;
            int damage = victim.Hull != null ? victim.Hull.EvaluateAttack(strikeDamage) : strikeDamage;
            victim.Health?.TakeDamage(damage);
            onHitPlayer?.Invoke();

            Enter(AiState.Recover);
        }

        protected override void OnDeath()
        {
            _state = AiState.Dead;
            base.OnDeath();

            // The corpse goes fully slack — no wave drive, pure trailing rope. Snapped rather
            // than eased: UpdateAI stops running the moment we die, so nothing would ease it.
            SnapBodyLanguage(freq: 0f, amp: 0f);
            SetFlash(0f);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, detectionRadius);
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, strikeRange);
        }
    }
}
