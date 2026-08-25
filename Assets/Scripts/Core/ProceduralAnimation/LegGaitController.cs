using Sirenix.OdinInspector;
using UnityEngine;

namespace Core.ProceduralAnimation
{
    /**
     * Stepping-gait brain for a set of IKLegs — the walk cycle behind crabs and
     * anything else that scuttles over terrain.
     *
     * Each leg has a body-relative "home" stance position. Every frame the home is
     * projected onto the ground below (Physics2D raycast); when the planted foot
     * drifts too far from that desired spot, the leg swings there in a lifted arc.
     * Legs step in two alternating parity groups (0,2,4… vs 1,3,5…) so the body is
     * always supported — the classic crab shuffle. A velocity lead places feet
     * ahead of travel so the gait reads as intentional rather than dragged.
     *
     * When no ground is found under a leg it dangles at its home stance — the
     * creature brain decides what "airborne" means (sink, swim, tuck).
     */
    [DefaultExecutionOrder(52)] // after physics/brains, before IKLeg solve (55)
    public class LegGaitController : MonoBehaviour
    {
        // =====================
        // Legs
        // =====================

        [FoldoutGroup("Legs")]
        [Tooltip("Legs driven by this gait. Leave empty to auto-collect every IKLeg in children — order defines the alternating step groups (even indices vs odd).")]
        [SerializeField] private IKLeg[] legs;

        // =====================
        // Stance
        // =====================

        [FoldoutGroup("Stance")]
        [Tooltip("How far each home position splays outward from its hip, as a fraction of the hip's sideways offset. Wider = a braced, planted look (visible as feet planting further out to the sides).")]
        [SerializeField, Range(0f, 1.5f)] private float stanceSplay = 0.45f;

        [FoldoutGroup("Stance")]
        [Tooltip("How far below the hip the home stance sits, as a fraction of the leg's total reach. NOTE: grounded feet snap onto the terrain regardless, so this mostly shows in the airborne dangle/paddle pose and in how far below the body the ground probe searches.")]
        [SerializeField, Range(0.2f, 1f)] private float stanceDrop = 0.7f;

        // =====================
        // Ground
        // =====================

        [FoldoutGroup("Ground")]
        [Tooltip("Layers that count as walkable ground for foot placement.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [FoldoutGroup("Ground")]
        [Tooltip("Raycast starts this far above the home stance and probes downward.")]
        [SerializeField, Min(0.1f)] private float probeUp = 1.2f;

        [FoldoutGroup("Ground")]
        [Tooltip("How far below the home stance the probe reaches before the leg counts as airborne.")]
        [SerializeField, Min(0.1f)] private float probeDown = 2f;

        // =====================
        // Stepping
        // =====================

        [FoldoutGroup("Stepping")]
        [Tooltip("Planted-foot drift from its desired spot before a step triggers. Smaller = busier feet.")]
        [SerializeField, Min(0.02f)] private float stepThreshold = 0.35f;

        [FoldoutGroup("Stepping")]
        [Tooltip("Duration of one step swing.")]
        [SerializeField, Min(0.03f)] private float stepDuration = 0.16f;

        [FoldoutGroup("Stepping")]
        [Tooltip("Arc height of the foot during a swing.")]
        [SerializeField, Min(0f)] private float stepHeight = 0.22f;

        [FoldoutGroup("Stepping")]
        [Tooltip("Feet aim this many seconds ahead of body travel — placing steps where the body is going, not where it was.")]
        [SerializeField, Range(0f, 0.5f)] private float velocityLead = 0.15f;

        // =====================
        // Airborne
        // =====================

        [FoldoutGroup("Airborne")]
        [Tooltip("While a leg finds no ground it paddles a slow circle of this radius around its dangling stance — a gentle swim so a falling creature reads alive. 0 = stiff dangle.")]
        [SerializeField, Min(0f)] private float airborneWaveAmplitude = 0.14f;

        [FoldoutGroup("Airborne")]
        [Tooltip("Paddle cycles per second while airborne.")]
        [SerializeField, Min(0.05f)] private float airborneWaveFrequency = 1.1f;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        public float GroundedFraction { get; private set; }

        /**
         * Runtime multiplier on stanceSplay — creature brains push this to widen or
         * narrow the stance on the fly (e.g. a pre-pounce crouch brace). Moving the
         * home positions makes the feet visibly re-step into the new stance.
         */
        public float StanceSplayMultiplier { get; set; } = 1f;

        // Per-leg gait state, parallel to the legs array.
        private Vector2[] _planted;
        private bool[] _grounded;
        private bool[] _stepping;
        private float[] _stepT;
        private Vector2[] _stepFrom;

        private Vector2 _lastBodyPos;
        private Vector2 _bodyVelocity;
        private float _paddlePhase;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            if (legs == null || legs.Length == 0) legs = GetComponentsInChildren<IKLeg>();
            AllocateState();
        }

        private void OnEnable()
        {
            if (_planted == null) return;
            _lastBodyPos = transform.position;
            _bodyVelocity = Vector2.zero;

            // Re-plant everything at current stance so a cull-restore or teleport
            // doesn't leave feet planted across the map.
            for (int i = 0; i < legs.Length; i++)
            {
                _planted[i] = DesiredFootPos(i, out _grounded[i]);
                _stepping[i] = false;
                if (legs[i] != null) legs[i].FootTarget = _planted[i];
            }
        }

        private void LateUpdate()
        {
            if (legs == null || legs.Length == 0) return;
            if (!Application.isPlaying) { EditorPreviewStance(); return; }

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // Smoothed body velocity for the step lead.
            Vector2 bodyPos = transform.position;
            Vector2 rawVel = (bodyPos - _lastBodyPos) / dt;
            _lastBodyPos = bodyPos;
            _bodyVelocity = Vector2.Lerp(_bodyVelocity, rawVel, 1f - Mathf.Exp(-8f * dt));
            _paddlePhase += dt * airborneWaveFrequency * Mathf.PI * 2f;

            int groundedCount = 0;
            for (int i = 0; i < legs.Length; i++)
            {
                if (legs[i] == null) continue;
                TickLeg(i, dt);
                if (_grounded[i]) groundedCount++;
            }
            GroundedFraction = legs.Length > 0 ? groundedCount / (float)legs.Length : 0f;
        }

        // -------------------------------------------------------
        // Gait
        // -------------------------------------------------------

        /** Per-leg step logic: probe ground, trigger swings, arc the foot, publish the target. */
        private void TickLeg(int i, float dt)
        {
            Vector2 desired = DesiredFootPos(i, out bool grounded);
            _grounded[i] = grounded;

            // Airborne: no stepping — the foot drifts near its dangling stance while
            // paddling a slow staggered circle (the "crab swim") so a falling body reads alive.
            if (!grounded)
            {
                _stepping[i] = false;
                _planted[i] = Vector2.Lerp(_planted[i], desired + AirbornePaddle(i), 1f - Mathf.Exp(-6f * dt));
                legs[i].FootTarget = _planted[i];
                legs[i].Planted = false;
                return;
            }

            // Start a swing when drifted past threshold and our parity group has support,
            // or urgently (2× threshold) regardless of grouping so feet never stretch silly.
            float drift = (desired - _planted[i]).magnitude;
            if (!_stepping[i] && drift > stepThreshold && (GroupMayStep(i) || drift > stepThreshold * 2f))
            {
                _stepping[i] = true;
                _stepT[i] = 0f;
                _stepFrom[i] = _planted[i];
            }

            if (_stepping[i])
            {
                // Swing: eased lerp toward the (moving) desired spot with a sine lift arc.
                _stepT[i] += dt / stepDuration;
                float t = Mathf.Clamp01(_stepT[i]);
                float eased = t * t * (3f - 2f * t);
                Vector2 pos = Vector2.Lerp(_stepFrom[i], desired, eased);
                pos.y += Mathf.Sin(t * Mathf.PI) * stepHeight;

                if (t >= 1f) { _stepping[i] = false; _planted[i] = desired; }
                legs[i].FootTarget = _stepping[i] ? pos : _planted[i];
            }
            else
            {
                legs[i].FootTarget = _planted[i];
            }

            legs[i].Planted = !_stepping[i];
        }

        /** Circular paddle offset for airborne leg i — phases staggered along the body for a ripple. */
        private Vector2 AirbornePaddle(int i)
        {
            if (airborneWaveAmplitude <= 0f) return Vector2.zero;
            float ph = _paddlePhase + i * 1.9f;
            return new Vector2(Mathf.Cos(ph), Mathf.Sin(ph) * 0.6f) * airborneWaveAmplitude;
        }

        /** A leg may start a step only while no leg of the OPPOSITE parity is mid-swing — alternating support groups. */
        private bool GroupMayStep(int i)
        {
            int parity = i & 1;
            for (int k = 0; k < legs.Length; k++)
                if ((k & 1) != parity && _stepping[k]) return false;
            return true;
        }

        /**
         * Where leg i wants its foot: the body-local home stance, led by velocity,
         * projected onto the ground below. e.g. a hip at local (±0.4, 0) with splay
         * 0.45 and drop 0.7 of a 1-unit leg puts the home at (±0.58, -0.7) — feet
         * braced outward like a crab, then snapped to whatever rock is beneath.
         */
        private Vector2 DesiredFootPos(int i, out bool grounded)
        {
            Vector2 home = transform.TransformPoint(HomeLocal(i));
            home += _bodyVelocity * velocityLead;

            RaycastHit2D hit = Physics2D.Raycast(home + Vector2.up * probeUp, Vector2.down, probeUp + probeDown, groundMask);
            grounded = hit.collider != null;
            return grounded ? hit.point : home;
        }

        /**
         * Body-local home stance for leg i, derived from its hip position + splay/drop.
         * Computed fresh every call (it's just a transform + two multiplies) so the
         * stance sliders scrub live in the inspector instead of hitting a stale cache.
         */
        private Vector2 HomeLocal(int i)
        {
            Vector2 hipLocal = transform.InverseTransformPoint(legs[i].HipWorld);
            return hipLocal + new Vector2(hipLocal.x * stanceSplay * StanceSplayMultiplier, -legs[i].TotalReach * stanceDrop);
        }

        private void AllocateState()
        {
            int n = legs.Length;
            _planted = new Vector2[n];
            _grounded = new bool[n];
            _stepping = new bool[n];
            _stepT = new float[n];
            _stepFrom = new Vector2[n];
        }

        /** Edit mode: hold every foot at its home stance so prefabs preview a sensible pose. */
        private void EditorPreviewStance()
        {
            if (_planted == null || _planted.Length != legs.Length) AllocateState();
            for (int i = 0; i < legs.Length; i++)
            {
                if (legs[i] == null) continue;
                legs[i].FootTarget = transform.TransformPoint(HomeLocal(i));
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (legs == null) return;
            Gizmos.color = new Color(0.4f, 1f, 0.5f, 0.7f);
            for (int i = 0; i < legs.Length; i++)
            {
                if (legs[i] == null) continue;
                Gizmos.DrawWireSphere(transform.TransformPoint(HomeLocal(i)), 0.07f);
            }
        }
    }
}
