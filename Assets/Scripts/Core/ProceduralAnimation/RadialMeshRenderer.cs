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
     * UV0 maps the local bounding square to 0..1 for body textures. UV1 carries the
     * shared generated-mesh edge contract (see Mesh2DLitSpecular.shader): xy = outward
     * dir, z = normalized edge distance, w = world-unit edge distance — approximate on
     * this fan topology (one interior vertex), but plenty for outlines/rim glow/forms.
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
        // Sprite Silhouette
        // =====================

        public enum SilhouetteSource
        {
            /** Silhouette authored via the radius-by-angle curve (plain color / tiled texture). */
            RadiusProfile,
            /** Silhouette + UVs baked from an authored transparent image — the artwork rides the deformation. */
            SpriteSilhouette
        }

        [FoldoutGroup("Sprite Silhouette")]
        [Tooltip("Where the blob's shape comes from. Sprite Silhouette samples an authored transparent PNG's alpha to bake both the outline and the texture mapping — squash/rim-wobble then deform the artwork itself.")]
        [SerializeField] private SilhouetteSource silhouetteSource = SilhouetteSource.RadiusProfile;

        [FoldoutGroup("Sprite Silhouette")]
        [Tooltip("Authored image whose alpha defines the body shape (e.g. a squid-mantle PNG). The sprite's pivot is the deformation center — shapes should be roughly star-convex around it (each outward ray crosses the silhouette once).")]
        [SerializeField, ShowIf(nameof(IsSpriteMode))] private Sprite silhouetteSprite;

        [FoldoutGroup("Sprite Silhouette")]
        [Tooltip("Alpha at or above this counts as inside the silhouette.")]
        [SerializeField, Range(0.01f, 1f), ShowIf(nameof(IsSpriteMode))] private float alphaThreshold = 0.5f;

        [FoldoutGroup("Sprite Silhouette")]
        [Tooltip("Push the sprite's texture into _MainTex via property block so many silhouette creatures can share one ProcCreature material.")]
        [SerializeField, ShowIf(nameof(IsSpriteMode))] private bool applySpriteTexture = true;

        // Baked silhouette data — radii normalized to the longest ray (so Base Radius stays
        // the size knob), plus absolute texture-space UVs for each rim vertex and the pivot.
        [SerializeField, HideInInspector] private float[] bakedRadii;
        [SerializeField, HideInInspector] private Vector2[] bakedRimUVs;
        [SerializeField, HideInInspector] private Vector2 bakedPivotUV = new Vector2(0.5f, 0.5f);
        [SerializeField, HideInInspector] private int bakedForSegments = -1;
        [SerializeField, HideInInspector] private Sprite bakedFromSprite;

        private bool IsSpriteMode => silhouetteSource == SilhouetteSource.SpriteSilhouette;
        private bool HasBake => bakedRadii != null && bakedForSegments == ringSegments && bakedRadii.Length == ringSegments;

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
            // Auto-(re)bake when the sprite or ring resolution changed — edit-time only,
            // so runtime never pays for texture reads (the bake is serialized data).
            if (!Application.isPlaying && IsSpriteMode && silhouetteSprite != null
                && (!HasBake || bakedFromSprite != silhouetteSprite))
                BakeSilhouette();

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

            // Self-heal: play-mode transitions with domain/scene reload disabled can leave
            // the filter pointing at a destroyed or cleared mesh while cached fields still
            // look valid. One full rebuild recovers any such state.
            if (!MeshHealthy()) { RebuildAll(); return; }

            UpdatePositions();
        }

        /** True when the mesh object, the filter binding, and the baked topology all agree. */
        private bool MeshHealthy()
        {
            return _mesh != null
                   && _filter.sharedMesh == _mesh
                   && _builtSegments == ringSegments
                   && _mesh.vertexCount == ringSegments + 1;
        }

        // -------------------------------------------------------
        // Mesh building
        // -------------------------------------------------------

        private void EnsureMesh()
        {
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "RadialBlob (generated)", hideFlags = HideFlags.DontSave };
                _mesh.MarkDynamic();
            }

            // Re-bind every time: a play-exit scene restore can reset the filter to a
            // null/stale reference (DontSave meshes aren't part of the restore snapshot).
            if (_filter != null && _filter.sharedMesh != _mesh) _filter.sharedMesh = _mesh;
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

            // UV1 follows the shared generated-mesh edge contract (Mesh2DLitSpecular):
            // xy = outward dir (local; the radial direction approximates the silhouette
            // normal), z = normalized edge distance (0 rim .. 1 center), w = world units.
            for (int i = 0; i < n; i++)
            {
                _colors[i] = rimColor;
                float angle = i / (float)n * Mathf.PI * 2f;
                _uv1[i] = new Vector4(Mathf.Cos(angle), Mathf.Sin(angle), 0f, 0f); // rim sits on the silhouette
            }
            _colors[n] = centerColor;
            _uv1[n] = new Vector4(0f, 0f, 1f, avgRadius);

            // Fan triangles around the center vertex.
            int idx = 0;
            for (int i = 0; i < n; i++)
            {
                _indices[idx++] = n;
                _indices[idx++] = i;
                _indices[idx++] = (i + 1) % n;
            }

            UpdatePositions(pushStatic: true);
            ApplySpriteTextureBlock();
            _builtSegments = n;
        }

        /**
         * Per-frame path: positions, plus UVs whose behavior depends on the mode.
         * Profile mode maps UVs planar from the DEFORMED positions (texture stays
         * put while the silhouette moves through it). Sprite mode pins UVs to the
         * baked REST mapping, so the artwork itself stretches with squash/wobble —
         * the whole point of the silhouette workflow.
         */
        private void UpdatePositions(bool pushStatic = false)
        {
            int n = ringSegments;
            bool spriteUVs = IsSpriteMode && HasBake && bakedRimUVs != null && bakedRimUVs.Length == n;
            float uvScale = 1f / (2f * Mathf.Max(0.01f, baseRadius));

            for (int i = 0; i < n; i++)
            {
                Vector2 p = RimLocal(i);
                _vertices[i] = p;
                _uv0[i] = spriteUVs ? bakedRimUVs[i] : p * uvScale + new Vector2(0.5f, 0.5f);
            }
            _vertices[n] = Vector3.zero;
            _uv0[n] = spriteUVs ? bakedPivotUV : new Vector2(0.5f, 0.5f);

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
            // Sprite mode reads the baked normalized ray lengths; Base Radius stays the size knob.
            if (IsSpriteMode && HasBake)
                return Mathf.Max(0.01f, bakedRadii[i]) * baseRadius;

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

        // -------------------------------------------------------
        // Sprite silhouette baking
        // -------------------------------------------------------

        /**
         * Samples the sprite's alpha to bake the silhouette: one ray per rim vertex
         * marches outward from the sprite pivot recording the FURTHEST pixel at or
         * above the alpha threshold (spanning interior holes/concavities), giving a
         * radius-by-angle table plus the matching texture-space UV per rim vertex.
         * Radii are normalized to the longest ray so Base Radius remains the world-size
         * knob. Runs automatically when the sprite changes; also exposed as a button.
         */
        [FoldoutGroup("Sprite Silhouette")]
        [Button("Bake Silhouette"), ShowIf(nameof(IsSpriteMode))]
        public void BakeSilhouette()
        {
            if (silhouetteSprite == null)
            {
                Debug.LogWarning($"[RadialMeshRenderer] {name}: no silhouette sprite assigned — nothing to bake.");
                return;
            }

            Texture2D readable = MakeReadableCopy(silhouetteSprite.texture);
            try
            {
                Rect rect = silhouetteSprite.rect;
                Vector2 centerPx = rect.min + silhouetteSprite.pivot;
                float texW = readable.width, texH = readable.height;
                float rayMax = Mathf.Sqrt(rect.width * rect.width + rect.height * rect.height);
                int n = ringSegments;

                bakedRadii = new float[n];
                bakedRimUVs = new Vector2[n];
                float maxLen = 0f;

                // March each ray in ~1px steps, remembering the furthest opaque sample.
                for (int i = 0; i < n; i++)
                {
                    float ang = i / (float)n * Mathf.PI * 2f;
                    Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));

                    float found = 0f;
                    for (float d = 0.5f; d <= rayMax; d += 1f)
                    {
                        Vector2 px = centerPx + dir * d;
                        if (px.x < rect.xMin || px.x >= rect.xMax || px.y < rect.yMin || px.y >= rect.yMax) continue;
                        if (readable.GetPixel((int)px.x, (int)px.y).a >= alphaThreshold) found = d;
                    }

                    bakedRadii[i] = found;
                    Vector2 edgePx = centerPx + dir * found;
                    bakedRimUVs[i] = new Vector2(edgePx.x / texW, edgePx.y / texH);
                    if (found > maxLen) maxLen = found;
                }

                if (maxLen < 1f)
                {
                    Debug.LogWarning($"[RadialMeshRenderer] {name}: silhouette bake found no opaque pixels above threshold {alphaThreshold} — check the sprite's alpha and pivot.");
                    bakedForSegments = -1;
                    return;
                }

                // Normalize so the longest ray = 1 (clamped so no ray collapses to zero).
                for (int i = 0; i < n; i++) bakedRadii[i] = Mathf.Max(0.02f, bakedRadii[i] / maxLen);

                bakedPivotUV = new Vector2(centerPx.x / texW, centerPx.y / texH);
                bakedForSegments = n;
                bakedFromSprite = silhouetteSprite;
                if (_mesh != null) RebuildAll();
            }
            finally
            {
                if (Application.isPlaying) Destroy(readable);
                else DestroyImmediate(readable);
            }
        }

        /** GPU round-trip copy so the source texture never needs Read/Write enabled in its importer. */
        private static Texture2D MakeReadableCopy(Texture2D src)
        {
            RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(src, rt);

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }

        /** Pushes the silhouette sprite's texture into _MainTex via property block (shared material friendly). */
        private void ApplySpriteTextureBlock()
        {
            if (!IsSpriteMode || !applySpriteTexture || silhouetteSprite == null || _renderer == null) return;

            _texBlock ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_texBlock);
            _texBlock.SetTexture(MainTexId, silhouetteSprite.texture);
            _renderer.SetPropertyBlock(_texBlock);
        }

        private MaterialPropertyBlock _texBlock;
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    }
}
