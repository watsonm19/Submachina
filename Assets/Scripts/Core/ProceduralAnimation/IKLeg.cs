using Sirenix.OdinInspector;
using UnityEngine;

namespace Core.ProceduralAnimation
{
    /**
     * A 2-bone leg solved analytically in 2D — the walking counterpart to
     * ChainSimulator. Something else (LegGaitController for walk cycles, or a
     * creature brain for claws) sets FootTarget in world space each tick; this
     * component eases the actual foot toward it and solves hip→knee→foot with a
     * law-of-cosines triangle, exposing the result as an IProcPointSource so a
     * ChainStripRenderer on the same object can skin it like any chain.
     *
     * Five points are published (hip, mid-upper, knee, mid-lower, foot) so the
     * ribbon gets a smooth bend at the knee instead of a hard 3-point crease.
     */
    [DefaultExecutionOrder(55)] // after LegGaitController (52), before ChainStripRenderer (60)
    public class IKLeg : MonoBehaviour, IProcPointSource
    {
        // =====================
        // Bones
        // =====================

        [FoldoutGroup("Bones")]
        [Tooltip("Length of the upper bone (hip → knee) in world units.")]
        [SerializeField, Min(0.05f)] private float upperLength = 0.5f;

        [FoldoutGroup("Bones")]
        [Tooltip("Length of the lower bone (knee → foot) in world units.")]
        [SerializeField, Min(0.05f)] private float lowerLength = 0.55f;

        [FoldoutGroup("Bones")]
        [Tooltip("Which side the knee pops toward relative to the hip→foot line: +1 = left of it, -1 = right. Mirror this between a creature's two sides.")]
        [SerializeField, Range(-1f, 1f)] private float bendSign = 1f;

        // =====================
        // Anchor
        // =====================

        [FoldoutGroup("Anchor")]
        [Tooltip("Transform the hip hangs off. Defaults to this transform's parent (the body).")]
        [SerializeField] private Transform hipAnchor;

        [FoldoutGroup("Anchor")]
        [Tooltip("Local-space offset from the anchor to the hip joint.")]
        [SerializeField] private Vector2 hipOffset = Vector2.zero;

        // =====================
        // Foot
        // =====================

        [FoldoutGroup("Foot")]
        [Tooltip("How fast the rendered foot chases FootTarget (per second). High = snappy steps; the gait's own arc does most of the shaping.")]
        [SerializeField, Range(1f, 60f)] private float footEaseSpeed = 30f;

        // =====================
        // Public state
        // =====================

        /** Where the foot is being asked to go, world space. Set by the gait controller or a brain, every tick. */
        public Vector2 FootTarget { get; set; }

        /** World position of the hip joint. */
        public Vector2 HipWorld
        {
            get
            {
                Transform a = hipAnchor != null ? hipAnchor : (transform.parent != null ? transform.parent : transform);
                return a.TransformPoint(hipOffset);
            }
        }

        /** Current (eased) world position of the foot. */
        public Vector2 FootWorld => _points[4];

        /** Current world position of the knee. */
        public Vector2 KneeWorld => _points[2];

        /** Maximum hip→foot reach. */
        public float TotalReach => upperLength + lowerLength;

        /** Set by the gait controller for debugging/effects — true while the foot is planted (not mid-swing). */
        public bool Planted { get; set; }

        private readonly Vector2[] _points = new Vector2[5];
        private Vector2 _foot;
        private bool _initialized;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void OnEnable()
        {
            // Start with a sensible dangling pose so the first frame never renders garbage.
            _foot = DefaultFootPose();
            FootTarget = _foot;
            Solve();
            _initialized = true;
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying) { PrepareEditorPreview(); return; }

            // Ease the physical foot toward its target — the gait arcs the target itself,
            // so this is just a softening filter that hides target discontinuities.
            float t = 1f - Mathf.Exp(-footEaseSpeed * Time.deltaTime);
            _foot = Vector2.Lerp(_foot, FootTarget, t);
            Solve();
        }

        // -------------------------------------------------------
        // IK solve
        // -------------------------------------------------------

        /**
         * Analytic 2-bone solve. e.g. bones 0.5/0.55 with the foot 0.9 away:
         * law of cosines gives the hip interior angle, the knee lands on the
         * bendSign side of the hip→foot line, and the five published points are
         * the two bones with midpoints inserted for a smooth ribbon bend.
         */
        private void Solve()
        {
            Vector2 hip = HipWorld;
            Vector2 to = _foot - hip;
            float reach = TotalReach;

            // Clamp the working distance inside the annulus this leg can actually reach.
            float minD = Mathf.Abs(upperLength - lowerLength) + 0.01f;
            float d = Mathf.Clamp(to.magnitude, minD, reach * 0.999f);
            Vector2 dir = to.sqrMagnitude > 1e-8f ? to.normalized : Vector2.down;

            // Interior angle at the hip via law of cosines, offset to the bend side.
            float cosHip = (upperLength * upperLength + d * d - lowerLength * lowerLength) / (2f * upperLength * d);
            float hipAngle = Mathf.Acos(Mathf.Clamp(cosHip, -1f, 1f));
            float baseAngle = Mathf.Atan2(dir.y, dir.x);
            float boneAngle = baseAngle + hipAngle * Mathf.Sign(bendSign == 0f ? 1f : bendSign);

            Vector2 knee = hip + new Vector2(Mathf.Cos(boneAngle), Mathf.Sin(boneAngle)) * upperLength;
            Vector2 foot = hip + dir * d;

            _points[0] = hip;
            _points[1] = (hip + knee) * 0.5f;
            _points[2] = knee;
            _points[3] = (knee + foot) * 0.5f;
            _points[4] = foot;
        }

        /** Rest pose: foot dangling below the hip at most of full reach. */
        private Vector2 DefaultFootPose() => HipWorld + Vector2.down * (TotalReach * 0.85f);

        // -------------------------------------------------------
        // IProcPointSource
        // -------------------------------------------------------

        public int PointCount => 5;

        public float SegmentLength => TotalReach * 0.25f;

        public Vector2 GetPoint(int i) => _points[i];

        public Vector2 GetTangent(int i)
        {
            // Tangents follow the bone; the knee gets the average so the ribbon bends smoothly.
            Vector2 upper = (_points[2] - _points[0]);
            Vector2 lower = (_points[4] - _points[2]);
            Vector2 t = i < 2 ? upper : i > 2 ? lower : upper.normalized + lower.normalized;
            return t.sqrMagnitude > 1e-8f ? t.normalized : Vector2.down;
        }

        public Vector2 GetNormal(int i)
        {
            Vector2 t = GetTangent(i);
            return new Vector2(-t.y, t.x);
        }

        public void PrepareEditorPreview()
        {
            _foot = DefaultFootPose();
            Solve();
        }

        private void OnDrawGizmosSelected()
        {
            if (!_initialized && !Application.isPlaying) PrepareEditorPreview();
            Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.9f);
            Gizmos.DrawLine(_points[0], _points[2]);
            Gizmos.DrawLine(_points[2], _points[4]);
            Gizmos.DrawWireSphere(FootTarget, 0.05f);
        }
    }
}
