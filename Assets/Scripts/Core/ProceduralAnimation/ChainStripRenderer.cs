using Sirenix.OdinInspector;
using UnityEngine;

namespace Core.ProceduralAnimation
{
    /**
     * Renders a ChainSimulator as a tapered ribbon mesh — fish/eel bodies, tentacles,
     * trailing fins. Built for per-frame deformation, unlike SplineFillMeshBuilder:
     * fixed topology, persistent buffers, zero per-frame allocations.
     *
     * Mesh layout: three vertices per chain point (left edge / spine / right edge)
     * so the shader can interpolate a proper interior distance for outlines, plus
     * optional rounded caps at both ends. Static data (UVs, colors, edge distances,
     * indices) is baked once; only positions move each frame.
     *
     * UV0: u = 0..1 head→tail, v = 0..1 across — a texture maps naturally along the body.
     * UV1: the shared generated-mesh edge contract (see Mesh2DLitSpecular.shader):
     *      xy = outward direction (local space, refreshed per frame as the body bends),
     *      z  = normalized edge distance (0 silhouette .. 1 spine — edge band / Form Shape),
     *      w  = world-unit edge distance (constant-width outlines + rim emission).
     */
    [ExecuteAlways]
    [DefaultExecutionOrder(60)]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class ChainStripRenderer : MonoBehaviour
    {
        // =====================
        // Source
        // =====================

        [FoldoutGroup("Source")]
        [Tooltip("Chain to render. Auto-resolves from this object or its parents if left empty — including any other IProcPointSource (e.g. an IKLeg) when no ChainSimulator is assigned.")]
        [SerializeField] private ChainSimulator chain;

        // Resolved point source — the chain when assigned, else any IProcPointSource on
        // this object or a parent (IK legs use this path).
        private IProcPointSource _source;

        // =====================
        // Shape
        // =====================

        [FoldoutGroup("Shape")]
        [Tooltip("Body width in world units at the widest point of the profile (at transform scale 1 — hierarchy scale multiplies this).")]
        [SerializeField, Min(0.01f)] private float maxWidth = 0.5f;

        [FoldoutGroup("Shape")]
        [Tooltip("Width along the body (x: 0 = head, 1 = tail; y: fraction of Max Width). Default is a fish-like bulge that tapers to the tail.")]
        [SerializeField] private AnimationCurve widthProfile = new AnimationCurve(
            new Keyframe(0f, 0.6f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.08f));

        [FoldoutGroup("Shape")]
        [Tooltip("Arc vertices per rounded end cap. 0 = flat ends (open tube look).")]
        [SerializeField, Range(0, 12)] private int capSegments = 5;

        // =====================
        // Color
        // =====================

        [FoldoutGroup("Color")]
        [Tooltip("Vertex color along the body (multiplied with the material). Fade the alpha near 1 for tentacles that dissolve at the tip.")]
        [SerializeField] private Gradient colorAlongLength = DefaultGradient();

        // =====================
        // Sorting
        // =====================

        [FoldoutGroup("Sorting")]
        [Tooltip("Sorting layer for the MeshRenderer — MeshRenderers don't expose this in the inspector, so it's applied from here.")]
        [SerializeField] private string sortingLayerName = "Default";

        [FoldoutGroup("Sorting")]
        [Tooltip("Order within the sorting layer.")]
        [SerializeField] private int sortingOrder = 0;

        // =====================
        // Performance
        // =====================

        [FoldoutGroup("Performance")]
        [Tooltip("When the renderer is off-screen, the mesh only refreshes at this interval (seconds) instead of every frame. 0 = always update.")]
        [SerializeField, Min(0f)] private float offscreenUpdateInterval = 0.25f;

        // =====================
        // Runtime state
        // =====================

        /** The MeshRenderer, exposed so creatures can drive MaterialPropertyBlocks (flash, emission pulses). */
        public MeshRenderer Renderer => _renderer;

        /** The chain being rendered (null when driven by a non-chain source such as an IKLeg). */
        public ChainSimulator Chain => chain;

        /** The resolved point source actually being skinned. */
        public IProcPointSource Source => _source;

        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private Mesh _mesh;

        // Persistent buffers — sized once per topology, mutated in place every frame.
        private Vector3[] _vertices;
        private Vector2[] _uv0;
        private Vector4[] _uv1;
        private Color32[] _colors;
        private int[] _indices;
        private int _builtPointCount = -1;
        private int _builtCapSegments = -1;
        private float _nextOffscreenUpdate;

        // Hierarchy scale factor applied to the width. The chain points already carry the
        // scale (ChainSimulator scales its lengths), and the world→local round trip through
        // InverseTransformPoint cancels the transform's own scale — so without this the
        // ribbon renders at the same world size no matter how the hierarchy is scaled.
        private float _hierarchyScale = 1f;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void OnEnable()
        {
            _filter = GetComponent<MeshFilter>();
            _renderer = GetComponent<MeshRenderer>();
            ResolveSource();

            EnsureMesh();
            ApplySorting();
            RebuildAll();
        }

        /** Explicit chain wins; otherwise adopt any IProcPointSource on self/parents (ChainSimulator or IKLeg alike). */
        private void ResolveSource()
        {
            if (chain == null) chain = GetComponentInParent<ChainSimulator>();
            _source = chain != null ? chain : GetComponentInParent<IProcPointSource>();
        }

        private void OnDisable()
        {
            // Keep the mesh object alive across disable/enable (culling toggles renderers often);
            // it's destroyed for real in OnDestroy.
        }

        private void OnDestroy()
        {
            if (_mesh != null)
            {
                if (Application.isPlaying) Destroy(_mesh);
                else DestroyImmediate(_mesh);
            }
        }

        private void OnValidate()
        {
            if (isActiveAndEnabled && _mesh != null)
            {
                ApplySorting();
                RebuildAll();
            }
        }

        private void LateUpdate()
        {
            if (_source == null) ResolveSource();
            if (_source == null) return;

            // Edit mode: show the rest pose so widths/colors are tunable without playing.
            if (!Application.isPlaying)
            {
                _source.PrepareEditorPreview();
                RebuildAll();
                return;
            }

            // Off-screen throttle — keep refreshing occasionally so bounds track the
            // creature and it pops back in correctly when re-entering the view.
            if (offscreenUpdateInterval > 0f && !_renderer.isVisible)
            {
                if (Time.time < _nextOffscreenUpdate) return;
                _nextOffscreenUpdate = Time.time + offscreenUpdateInterval;
            }

            // Self-heal: play-mode transitions with domain/scene reload disabled can leave
            // the filter pointing at a destroyed or cleared mesh while our cached fields
            // still look valid (or vice versa). One full rebuild recovers any such state.
            if (!MeshHealthy()) { RebuildAll(); return; }

            UpdatePositions();
        }

        /** True when the mesh object, the filter binding, and the baked topology all agree. */
        private bool MeshHealthy()
        {
            return _mesh != null
                   && _filter.sharedMesh == _mesh
                   && _builtPointCount == _source.PointCount
                   && _builtCapSegments == capSegments
                   && _mesh.vertexCount == _source.PointCount * 3 + capSegments * 2;
        }

        // -------------------------------------------------------
        // Mesh building
        // -------------------------------------------------------

        private void EnsureMesh()
        {
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "ChainStrip (generated)", hideFlags = HideFlags.DontSave };
                _mesh.MarkDynamic();
            }

            // Re-bind every time: a play-exit scene restore can reset the filter to a
            // null/stale reference (DontSave meshes aren't part of the restore snapshot).
            if (_filter != null && _filter.sharedMesh != _mesh) _filter.sharedMesh = _mesh;
        }

        /** Full rebuild: allocates buffers for the current topology and bakes all static data. */
        private void RebuildAll()
        {
            if (_source == null) ResolveSource();
            if (_source == null) return;
            if (!Application.isPlaying) _source.PrepareEditorPreview();
            EnsureMesh();

            int n = _source.PointCount;
            int caps = capSegments;
            int vertCount = n * 3 + caps * 2;
            int triCount = (n - 1) * 4 + (caps > 0 ? 2 * (caps + 1) : 2);

            // ---- (Re)size buffers only when topology actually changed ----
            if (_vertices == null || _vertices.Length != vertCount)
            {
                _vertices = new Vector3[vertCount];
                _uv0 = new Vector2[vertCount];
                _uv1 = new Vector4[vertCount];
                _colors = new Color32[vertCount];
                _indices = new int[triCount * 3];
            }

            // ---- Static per-row data: UVs and colors (UV1 edge data is written by
            // UpdatePositions each refresh, since the outward directions bend with the body) ----
            for (int i = 0; i < n; i++)
            {
                float along = i / (float)(n - 1);
                Color32 col = colorAlongLength.Evaluate(along);
                int v = i * 3;

                _uv0[v] = new Vector2(along, 0f);
                _uv0[v + 1] = new Vector2(along, 0.5f);
                _uv0[v + 2] = new Vector2(along, 1f);
                _colors[v] = col; _colors[v + 1] = col; _colors[v + 2] = col;
            }

            // ---- Static cap data: arc verts sit on the silhouette ----
            for (int k = 0; k < caps; k++)
            {
                int head = n * 3 + k;
                int tail = n * 3 + caps + k;
                _uv0[head] = new Vector2(0f, (k + 1f) / (caps + 1f));
                _uv0[tail] = new Vector2(1f, (k + 1f) / (caps + 1f));
                _colors[head] = colorAlongLength.Evaluate(0f);
                _colors[tail] = colorAlongLength.Evaluate(1f);
            }

            BuildIndices(n, caps);
            UpdatePositions();

            // Push the static channels once.
            _mesh.Clear();
            _mesh.vertices = _vertices;
            _mesh.uv = _uv0;
            _mesh.SetUVs(1, _uv1);
            _mesh.colors32 = _colors;
            _mesh.triangles = _indices;
            _mesh.RecalculateBounds();

            _builtPointCount = n;
            _builtCapSegments = caps;
        }

        /**
         * Per-frame path: vertex positions plus the UV1 edge data — the outward
         * directions rotate with the body as the chain bends, so they can't be baked.
         * One extra Vector4 channel upload per refresh; still zero allocations.
         */
        private void UpdatePositions()
        {
            int n = _source.PointCount;
            var t = transform;

            // Refresh the width scale each pass (flip scales are size-neutral, non-uniform averages).
            Vector3 ls = t.lossyScale;
            _hierarchyScale = (Mathf.Abs(ls.x) + Mathf.Abs(ls.y)) * 0.5f;

            // Row verts: spine point ± normal × half-width, in this transform's local space.
            // UV1 rows follow the shared edge contract: edges (±dir, 0, 0), spine (0, z, w).
            for (int i = 0; i < n; i++)
            {
                float along = i / (float)(n - 1);
                float halfW = HalfWidth(along);
                Vector2 c = _source.GetPoint(i);
                Vector2 nrm = _source.GetNormal(i);

                int v = i * 3;
                _vertices[v] = t.InverseTransformPoint(c + nrm * halfW);
                _vertices[v + 1] = t.InverseTransformPoint(c);
                _vertices[v + 2] = t.InverseTransformPoint(c - nrm * halfW);

                // Outward dir in LOCAL space (mesh verts are local); the chain normal is world.
                Vector2 dir = ((Vector2)t.InverseTransformDirection(nrm)).normalized;

                // Spine distance: half-width, and near flat (capless) ends also the
                // longitudinal distance to the tip so outlines close around them.
                float spineDist = halfW;
                if (capSegments == 0)
                    spineDist = Mathf.Min(spineDist, Mathf.Min(i, n - 1 - i) * _source.SegmentLength);

                _uv1[v] = new Vector4(dir.x, dir.y, 0f, 0f);
                _uv1[v + 1] = new Vector4(0f, 0f, spineDist / Mathf.Max(halfW, 1e-4f), spineDist);
                _uv1[v + 2] = new Vector4(-dir.x, -dir.y, 0f, 0f);
            }

            // Rounded caps: semicircle fans beyond the first/last rows.
            if (capSegments > 0)
            {
                WriteCapArc(0, -_source.GetTangent(0), HalfWidth(0f), n * 3);
                WriteCapArc(n - 1, _source.GetTangent(n - 1), HalfWidth(1f), n * 3 + capSegments);
            }

            if (Application.isPlaying && _builtPointCount == n)
            {
                _mesh.vertices = _vertices;
                _mesh.SetUVs(1, _uv1);
                _mesh.RecalculateBounds();
            }
        }

        /**
         * Writes one semicircular cap arc. 'forward' points outward beyond the chain end;
         * arc sweeps from the left edge vertex around to the right edge vertex.
         */
        private void WriteCapArc(int pointIndex, Vector2 forward, float radius, int firstVert)
        {
            Vector2 c = _source.GetPoint(pointIndex);
            Vector2 nrm = _source.GetNormal(pointIndex);
            if (pointIndex == 0) nrm = -nrm; // head cap sweeps the mirrored way so winding stays consistent

            var t = transform;
            for (int k = 0; k < capSegments; k++)
            {
                // Interpolate the half-circle from +normal (left edge) to -normal (right edge) through +forward.
                float a = (k + 1f) / (capSegments + 1f) * Mathf.PI; // 0..π exclusive
                Vector2 dir = nrm * Mathf.Cos(a) + forward * Mathf.Sin(a);
                _vertices[firstVert + k] = t.InverseTransformPoint(c + dir * radius);

                // Arc verts sit ON the silhouette: outward dir radial, both distances zero.
                Vector2 local = ((Vector2)t.InverseTransformDirection(dir)).normalized;
                _uv1[firstVert + k] = new Vector4(local.x, local.y, 0f, 0f);
            }
        }

        /** Index buffer: 4 triangles per row pair (left half + right half), then the cap fans. */
        private void BuildIndices(int n, int caps)
        {
            int idx = 0;
            for (int i = 0; i < n - 1; i++)
            {
                int a = i * 3, b = (i + 1) * 3;
                // Left half quad (left, spine)
                _indices[idx++] = a; _indices[idx++] = b; _indices[idx++] = a + 1;
                _indices[idx++] = a + 1; _indices[idx++] = b; _indices[idx++] = b + 1;
                // Right half quad (spine, right)
                _indices[idx++] = a + 1; _indices[idx++] = b + 1; _indices[idx++] = a + 2;
                _indices[idx++] = a + 2; _indices[idx++] = b + 1; _indices[idx++] = b + 2;
            }

            if (caps > 0)
            {
                WriteCapFan(ref idx, centerVert: 1, edgeA: 0, edgeB: 2, firstArc: n * 3, caps);
                WriteCapFan(ref idx, centerVert: (n - 1) * 3 + 1, edgeA: (n - 1) * 3, edgeB: (n - 1) * 3 + 2, firstArc: n * 3 + caps, caps);
            }
            else
            {
                // Flat ends: one closing triangle per end keeps the silhouette sealed.
                _indices[idx++] = 0; _indices[idx++] = 1; _indices[idx++] = 2;
                int last = (n - 1) * 3;
                _indices[idx++] = last; _indices[idx++] = last + 1; _indices[idx++] = last + 2;
            }
        }

        /** Fan of triangles from the end row's spine vertex across the cap arc. */
        private void WriteCapFan(ref int idx, int centerVert, int edgeA, int edgeB, int firstArc, int caps)
        {
            int prev = edgeA;
            for (int k = 0; k < caps; k++)
            {
                _indices[idx++] = centerVert; _indices[idx++] = prev; _indices[idx++] = firstArc + k;
                prev = firstArc + k;
            }
            _indices[idx++] = centerVert; _indices[idx++] = prev; _indices[idx++] = edgeB;
        }

        // -------------------------------------------------------
        // Helpers
        // -------------------------------------------------------

        /**
         * Per-instance tint via MaterialPropertyBlock (_Color multiplies the
         * material fill) — lets many strips share one material while varying
         * color, e.g. depth-tinted fish in a school.
         */
        public void SetTint(Color tint)
        {
            if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
            _tintBlock ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_tintBlock);
            _tintBlock.SetColor(ColorId, tint);
            _renderer.SetPropertyBlock(_tintBlock);
        }

        private MaterialPropertyBlock _tintBlock;
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        /** World-space half-width at a point along the body, hierarchy scale included. */
        private float HalfWidth(float along01) =>
            Mathf.Max(0.001f, widthProfile.Evaluate(along01)) * maxWidth * 0.5f * _hierarchyScale;

        private void ApplySorting()
        {
            if (_renderer == null) return;
            _renderer.sortingLayerName = sortingLayerName;
            _renderer.sortingOrder = sortingOrder;
        }

        private static Gradient DefaultGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }
    }
}
