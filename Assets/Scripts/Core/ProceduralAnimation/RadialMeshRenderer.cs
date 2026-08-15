using Sirenix.OdinInspector;
using UnityEngine;

namespace Core.ProceduralAnimation
{
    /**
     * Deformable closed-blob mesh for creature bodies — jellyfish bells, squid
     * mantles, puffer bodies. A ring of rim vertices fan-triangulated around a
     * center vertex, all in LOCAL space (the creature moves/rotates the transform).
     *
     * The base silhouette is authored with a radius-by-angle curve; creatures
     * deform it at runtime through three cheap channels:
     *   • Squash    — non-uniform XY scale (jelly pulse contraction)
     *   • RimOffsets — per-vertex radial push (rim wobble, tentacle bumps)
     *   • UniformScale — whole-body scale (growth, breathing)
     *
     * UV0 maps the local bounding square to 0..1 for body textures. UV1.z carries
     * world-ish edge distance for the ProcCreature2D outline, like ChainStripRenderer.
     */
    [ExecuteAlways]
    [DefaultExecutionOrder(60)]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class RadialMeshRenderer : MonoBehaviour
    {
        // =====================
        // Shape
        // =====================

        [FoldoutGroup("Shape")]
        [Tooltip("Rim vertices around the blob. 20-32 is smooth for creature-sized shapes.")]
        [SerializeField, Range(6, 64)] private int ringSegments = 24;

        [FoldoutGroup("Shape")]
        [Tooltip("Base radius in world units before the profile/squash/scale modifiers.")]
        [SerializeField, Min(0.01f)] private float baseRadius = 0.5f;

        [FoldoutGroup("Shape")]
        [Tooltip("Radius multiplier by angle (x: 0..1 = 0°..360° counter-clockwise from +X). Author domes, teardrops, mantles here. Constant 1 = circle.")]
        [SerializeField] private AnimationCurve radiusProfile = AnimationCurve.Constant(0f, 1f, 1f);

        // =====================
        // Color
        // =====================

        [FoldoutGroup("Color")]
        [Tooltip("Vertex color at the center of the blob (multiplied with the material).")]
        [SerializeField] private Color centerColor = Color.white;

        [FoldoutGroup("Color")]
        [Tooltip("Vertex color at the rim — drop alpha for translucent jellyfish edges.")]
        [SerializeField] private Color rimColor = Color.white;

        // =====================
        // Sorting
        // =====================

        [FoldoutGroup("Sorting")]
        [Tooltip("Sorting layer applied to the MeshRenderer.")]
        [SerializeField] private string sortingLayerName = "Default";

        [FoldoutGroup("Sorting")]
        [Tooltip("Order within the sorting layer.")]
        [SerializeField] private int sortingOrder = 0;

        // =====================
        // Performance
        // =====================

        [FoldoutGroup("Performance")]
        [Tooltip("When off-screen, refresh at this interval (seconds) instead of every frame. 0 = always update.")]
        [SerializeField, Min(0f)] private float offscreenUpdateInterval = 0.25f;

        // =====================
        // Runtime deform channels (driven by creature code)
        // =====================

        /** Non-uniform scale applied to the silhouette — the jelly-pulse contraction channel. */
        public Vector2 Squash { get; set; } = Vector2.one;

        /** Whole-body scale multiplier. */
        public float UniformScale { get; set; } = 1f;

        /**
         * Per-rim-vertex radial offset in world units (index = rim vertex, CCW from +X).
         * Lazily sized to RingSegments — write wobble/bumps here each frame.
         */
        public float[] RimOffsets
        {
            get
            {
                if (_rimOffsets == null || _rimOffsets.Length != ringSegments)
                    _rimOffsets = new float[ringSegments];
                return _rimOffsets;
            }
        }

        /** Number of rim vertices. */
        public int RingSegments => ringSegments;

        /** The MeshRenderer, for MaterialPropertyBlock effects (glow pulses, flashes). */
        public MeshRenderer Renderer => _renderer;

        /** Local-space position of rim vertex i with all current deformation applied. */
        public Vector2 GetRimLocalPoint(int i) => RimLocal(i);

        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private Mesh _mesh;
        private Vector3[] _vertices;
        private Vector2[] _uv0;
        private Vector4[] _uv1;
        private Color[] _colors;
        private int[] _indices;
        private float[] _rimOffsets;
        private int _builtSegments = -1;
        private float _nextOffscreenUpdate;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void OnEnable()
        {
            _filter = GetComponent<MeshFilter>();
            _renderer = GetComponent<MeshRenderer>();
            EnsureMesh();
            ApplySorting();
            RebuildAll();
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
            if (!Application.isPlaying)
            {
                RebuildAll();
                return;
            }

            // Off-screen throttle, mirroring ChainStripRenderer.
            if (offscreenUpdateInterval > 0f && !_renderer.isVisible)
            {
                if (Time.time < _nextOffscreenUpdate) return;
                _nextOffscreenUpdate = Time.time + offscreenUpdateInterval;
            }

            if (_builtSegments != ringSegments) RebuildAll();
            else UpdatePositions();
        }

        // -------------------------------------------------------
        // Mesh building
        // -------------------------------------------------------

        private void EnsureMesh()
        {
            if (_mesh != null) return;
            _mesh = new Mesh { name = "RadialBlob (generated)", hideFlags = HideFlags.DontSave };
            _mesh.MarkDynamic();
            _filter.sharedMesh = _mesh;
        }

        /** Full rebuild: buffers + static channels (UVs, colors, edge distances, indices). */
        private void RebuildAll()
        {
            EnsureMesh();
            int n = ringSegments;
            int vertCount = n + 1; // rim + center (center is index n)

            if (_vertices == null || _vertices.Length != vertCount)
            {
                _vertices = new Vector3[vertCount];
                _uv0 = new Vector2[vertCount];
                _uv1 = new Vector4[vertCount];
                _colors = new Color[vertCount];
                _indices = new int[n * 3];
            }

            // Average rim radius doubles as the center's edge-distance for the outline shader.
            float avgRadius = 0f;
            for (int i = 0; i < n; i++) avgRadius += RestRadius(i);
            avgRadius /= n;

            for (int i = 0; i < n; i++)
            {
                _colors[i] = rimColor;
                _uv1[i] = Vector4.zero; // rim sits on the silhouette
            }
            _colors[n] = centerColor;
            _uv1[n] = new Vector4(0f, 0f, avgRadius, 0f);

            // Fan triangles around the center vertex.
            int idx = 0;
            for (int i = 0; i < n; i++)
            {
                _indices[idx++] = n;
                _indices[idx++] = i;
                _indices[idx++] = (i + 1) % n;
            }

            UpdatePositions(pushStatic: true);
            _builtSegments = n;
        }

        /** Per-frame path: positions + planar UVs (the silhouette deforms, so UVs track it). */
        private void UpdatePositions(bool pushStatic = false)
        {
            int n = ringSegments;
            float uvScale = 1f / (2f * Mathf.Max(0.01f, baseRadius));

            for (int i = 0; i < n; i++)
            {
                Vector2 p = RimLocal(i);
                _vertices[i] = p;
                _uv0[i] = p * uvScale + new Vector2(0.5f, 0.5f);
            }
            _vertices[n] = Vector3.zero;
            _uv0[n] = new Vector2(0.5f, 0.5f);

            if (pushStatic)
            {
                _mesh.Clear();
                _mesh.vertices = _vertices;
                _mesh.uv = _uv0;
                _mesh.SetUVs(1, _uv1);
                _mesh.colors = _colors;
                _mesh.triangles = _indices;
            }
            else
            {
                _mesh.vertices = _vertices;
                _mesh.uv = _uv0;
            }
            _mesh.RecalculateBounds();
        }

        /** Rest-profile radius of rim vertex i (before squash/offsets/scale). */
        private float RestRadius(int i)
        {
            float angle01 = i / (float)ringSegments;
            return Mathf.Max(0.01f, radiusProfile.Evaluate(angle01)) * baseRadius;
        }

        /** Fully-deformed local position of rim vertex i. */
        private Vector2 RimLocal(int i)
        {
            float angle = i / (float)ringSegments * Mathf.PI * 2f;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            float r = RestRadius(i) * UniformScale;
            if (_rimOffsets != null && i < _rimOffsets.Length) r += _rimOffsets[i];
            return Vector2.Scale(dir * r, Squash);
        }

        private void ApplySorting()
        {
            if (_renderer == null) return;
            _renderer.sortingLayerName = sortingLayerName;
            _renderer.sortingOrder = sortingOrder;
        }
    }
}
