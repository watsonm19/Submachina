using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;
using Sirenix.OdinInspector;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Core.Rendering
{
    /**
     * Generates a filled 2D mesh from a SpriteShapeController's spline, replacing the
     * SpriteShapeRenderer's second-class tiling fill with a MeshRenderer whose material
     * (Submachina/2D/Mesh2DLitSpecular) treats the seamless albedo/normal/specmask
     * trio as first-class texture slots.
     *
     * Workflow: keep the SpriteShapeController purely as the SPLINE EDITOR (its renderer
     * can be disabled), put this component + MeshFilter + MeshRenderer on a child object,
     * and it rebuilds the mesh automatically whenever the spline or settings change.
     *
     * What the mesh carries:
     *   - Positions  : sampled spline outline + an INSET ring one band-width inside it,
     *                  band quads between the two, ear-clipped interior inside the inset.
     *   - UV0        : planar tiling UVs (local or world space) — the seamless fill
     *                  textures repeat across the shape at uvTilesPerUnit.
     *   - UV1 (edge) : the shared generated-mesh edge contract (Mesh2DLitSpecular):
     *                  xy = outward direction at the outline (object space, unit),
     *                  z = normalized edge distance (0 outline, 1 inset/interior),
     *                  w = world-unit edge distance (0 outline, band width inside).
     *                  Edge darken/fade/bevel + Form Shape read z; outline/rim read w.
     *                  Meshes BAKED before w existed read w = 0 — re-bake before
     *                  enabling the outline on them (everything else is unaffected).
     *   - Normals/tangents : flat -Z facing with +X tangents, so the NormalsRendering
     *                  pass feeds the 2D light buffer exactly like a sprite would.
     */
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class SplineFillMeshBuilder : MonoBehaviour
    {
        // =====================
        // Source spline
        // =====================

        [FoldoutGroup("Source")]
        [Tooltip("SpriteShapeController whose spline outlines the fill. Auto-found on this object " +
                 "or a parent when left empty. The spline must be CLOSED (not open ended).")]
        [SerializeField] private SpriteShapeController spline;

        [FoldoutGroup("Source")]
        [Tooltip("Rebuild automatically in the editor whenever the spline or these settings change.")]
        [SerializeField] private bool autoRebuild = true;

        [FoldoutGroup("Source")]
        [Tooltip("Disable the source SpriteShapeRenderer on rebuild so only this mesh draws. " +
                 "Leave off if you still want the SpriteShape's edge-sprite decoration on top.")]
        [SerializeField] private bool hideSourceRenderer = false;

        // =====================
        // Geometry
        // =====================

        [FoldoutGroup("Geometry")]
        [Tooltip("Curve samples per spline segment. Linear segments always emit a single point.")]
        [SerializeField, Range(1, 64)] private int subdivisionsPerSegment = 12;

        [FoldoutGroup("Geometry")]
        [Tooltip("World-unit width of the edge band the shader's edge effects live in. Keep it " +
                 "below the smallest feature radius of the shape or the inset ring can fold over itself.")]
        [SerializeField, Min(0.01f)] private float edgeBandWidth = 0.5f;

        [FoldoutGroup("Geometry")]
        [Tooltip("Cap on how far a sharp corner's inset vertex may travel (multiples of the band " +
                 "width) — stops razor corners from spiking the inset ring inward.")]
        [SerializeField, Range(1f, 8f)] private float miterLimit = 4f;

        // =====================
        // Fill UVs
        // =====================

        [FoldoutGroup("Fill UVs")]
        [Tooltip("Texture repeats per world unit (0.25 = one tile every 4 units).")]
        [SerializeField, Min(0.001f)] private float uvTilesPerUnit = 0.25f;

        [FoldoutGroup("Fill UVs")]
        [Tooltip("World-space UVs stay continuous across separate shapes but swim if the object " +
                 "moves; local-space UVs travel with the object.")]
        [SerializeField] private bool worldSpaceUVs = false;

        // =====================
        // Sorting (MeshRenderer sorting is script-only, so expose it here)
        // =====================

        [FoldoutGroup("Sorting")]
        [SerializeField] private string sortingLayerName = "Default";

        [FoldoutGroup("Sorting")]
        [SerializeField] private int sortingOrder = 0;

        // =====================
        // Baking (freeze the generated mesh into a .asset so prefabs/instances can use
        // a plain MeshFilter + MeshRenderer with no generation cost — the builder can
        // even be removed once baked)
        // =====================

        [FoldoutGroup("Baking")]
        [Tooltip("Folder the baked mesh asset is created in (created if missing).")]
        [SerializeField] private string bakeFolder = "Assets/Submachina/Art/Meshes";

        [FoldoutGroup("Baking")]
        [Tooltip("The baked mesh asset this object is frozen to. While the MeshFilter points at it, " +
                 "auto-rebuild is SUSPENDED (spline edits do nothing) — press Rebuild to go back to " +
                 "the live generated mesh, or Bake To Asset again to refresh the asset in place.")]
        [SerializeField] private Mesh bakedMesh;

        // =====================
        // Internals
        // =====================

        private Mesh _mesh;      // generated, never saved (rebuilt in OnEnable)
        private int _lastHash;   // spline + settings fingerprint for autoRebuild polling

        /** True while the MeshFilter is frozen to the baked asset (live generation suspended). */
        private bool IsBaked => bakedMesh != null && GetComponent<MeshFilter>().sharedMesh == bakedMesh;

        /** Regenerate on enable — the live mesh is HideFlags.DontSave, so scene loads start empty.
         *  Skipped when frozen to a baked asset (that's the whole point of baking). */
        private void OnEnable()
        {
            if (!IsBaked) Rebuild();
        }

        /** The live mesh is owned by nobody (DontSave) — release it with the object so
         *  runtime spawn/despawn of live-builder pieces can't leak meshes. Baked assets
         *  are untouched. */
        private void OnDestroy()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
        }

#if UNITY_EDITOR
        /** Edit-mode polling: rebuild when the spline or settings fingerprint changes. */
        private void Update()
        {
            if (Application.isPlaying || !autoRebuild || spline == null || IsBaked) return;
            if (ComputeHash() != _lastHash) Rebuild();
        }

        /**
         * Freezes the current generated mesh into a project asset and points the MeshFilter
         * at it. First bake creates the asset in bakeFolder; later bakes overwrite the same
         * asset IN PLACE, so prefabs referencing it update everywhere. While baked, live
         * regeneration is suspended — the object behaves as a plain mesh (the builder and
         * even the SpriteShapeController can be deleted if the shape is final).
         */
        [FoldoutGroup("Baking")]
        [Button(ButtonSizes.Medium)]
        public void BakeToAsset()
        {
            Rebuild(); // always bake a fresh result (also drops any stale frozen state)
            if (_mesh == null) { Debug.LogWarning($"{name}: nothing to bake — no spline?", this); return; }

            if (bakedMesh == null)
            {
                // First bake: instantiate a persistent copy and create the asset.
                EnsureFolder(bakeFolder);
                string path = AssetDatabase.GenerateUniqueAssetPath($"{bakeFolder}/{gameObject.name}_Fill.asset");
                bakedMesh = Instantiate(_mesh);
                bakedMesh.hideFlags = HideFlags.None;
                bakedMesh.name = System.IO.Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(bakedMesh, path);
            }
            else
            {
                // Re-bake: overwrite the existing asset in place so references survive.
                string keepName = bakedMesh.name;
                EditorUtility.CopySerialized(_mesh, bakedMesh);
                bakedMesh.hideFlags = HideFlags.None; // CopySerialized drags DontSave along
                bakedMesh.name = keepName;
            }
            AssetDatabase.SaveAssets();

            GetComponent<MeshFilter>().sharedMesh = bakedMesh;
            EditorUtility.SetDirty(this);
            Debug.Log($"{name}: baked fill mesh -> {AssetDatabase.GetAssetPath(bakedMesh)}", this);
        }

        /** Creates the folder chain for an "Assets/..." path if it doesn't exist yet. */
        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
        }
#endif

        // -------------------------------------------------------
        // Mesh generation
        // -------------------------------------------------------

        /**
         * Full rebuild: sample the spline outline, inset it by the band width, triangulate
         * band + interior, bake tiling UVs and the edge data, and push it to the MeshFilter.
         */
        [Button(ButtonSizes.Medium)]
        public void Rebuild()
        {
            // Resolve the source spline (same object or any parent).
            if (spline == null) spline = GetComponentInParent<SpriteShapeController>();
            if (spline == null) return;
            _lastHash = ComputeHash();

            Spline sp = spline.spline;
            if (sp.isOpenEnded) { Debug.LogWarning($"{name}: spline must be closed for a fill mesh.", this); return; }

            // Outline in OUR local space, wound counter-clockwise.
            List<Vector2> outline = SampleOutline(sp);
            if (outline.Count < 3) return;
            if (SignedArea(outline) < 0f) outline.Reverse();

            // Outward miter direction per outline vertex (unit dir + corner-scaled inset offset).
            int n = outline.Count;
            var outDirs = new Vector2[n];
            var inset = new Vector2[n];
            ComputeMitersAndInset(outline, outDirs, inset);

            // Vertices: outline ring [0..n), inset ring [n..2n). Interior triangles reuse the inset ring.
            var verts = new List<Vector3>(n * 2);
            var uvs = new List<Vector2>(n * 2);
            var edge = new List<Vector4>(n * 2);
            // TEXCOORD1 edge contract (shared by every generated-mesh builder — see
            // Mesh2DLitSpecular.shader): xy = outward dir, z = NORMALIZED band distance
            // (edge band / Form Shape), w = WORLD-UNIT edge distance (constant-width
            // outline + rim emission). Interior verts reuse the inset ring, so they
            // carry z = 1 / w = band width — outline widths should stay under the band.
            for (int i = 0; i < n; i++)
            {
                verts.Add(outline[i]); uvs.Add(FillUV(outline[i]));
                edge.Add(new Vector4(outDirs[i].x, outDirs[i].y, 0f, 0f)); // on the rim
            }
            for (int i = 0; i < n; i++)
            {
                verts.Add(inset[i]); uvs.Add(FillUV(inset[i]));
                edge.Add(new Vector4(outDirs[i].x, outDirs[i].y, 1f, edgeBandWidth)); // band's inner edge
            }

            // Band quads between the rings (CCW like the interior triangulation).
            var tris = new List<int>(n * 6);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                tris.Add(i); tris.Add(j); tris.Add(n + j);
                tris.Add(i); tris.Add(n + j); tris.Add(n + i);
            }

            // Interior: ear-clip the inset polygon, indices offset onto the inset ring.
            List<int> interior = Triangulate(inset);
            for (int i = 0; i < interior.Count; i++) tris.Add(n + interior[i]);

            // Flat camera-facing normals/tangents (N = -Z, T = +X, w = -1 → bitangent +Y),
            // matching sprite meshes so the NormalsRendering pass shades identically.
            var normals = new List<Vector3>(verts.Count);
            var tangents = new List<Vector4>(verts.Count);
            for (int i = 0; i < verts.Count; i++)
            {
                normals.Add(new Vector3(0f, 0f, -1f));
                tangents.Add(new Vector4(1f, 0f, 0f, -1f));
            }

            // Push to the mesh (created once, never saved — OnEnable rebuilds after loads).
            if (_mesh == null) _mesh = new Mesh { name = "SplineFill (generated)", hideFlags = HideFlags.DontSave };
            _mesh.Clear();
            _mesh.SetVertices(verts);
            _mesh.SetUVs(0, uvs);
            _mesh.SetUVs(1, edge);
            _mesh.SetNormals(normals);
            _mesh.SetTangents(tangents);
            _mesh.SetTriangles(tris, 0);
            _mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = _mesh;

            // Sorting + optionally silence the source SpriteShape's own rendering.
            var mr = GetComponent<MeshRenderer>();
            mr.sortingLayerName = sortingLayerName;
            mr.sortingOrder = sortingOrder;
            if (hideSourceRenderer)
            {
                var ssr = spline.GetComponent<SpriteShapeRenderer>();
                if (ssr != null && ssr.enabled) ssr.enabled = false;
            }
        }

        // -------------------------------------------------------
        // Outline sampling
        // -------------------------------------------------------

        /**
         * Samples the closed spline into a polygon in THIS object's local space. Bezier
         * segments get subdivisionsPerSegment samples; straight segments (both tangents
         * zero) emit just their start point. Near-duplicate points are dropped so the
         * miter math never sees a zero-length edge.
         */
        private List<Vector2> SampleOutline(Spline sp)
        {
            var pts = new List<Vector2>();
            int count = sp.GetPointCount();
            Transform src = spline.transform;

            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                Vector3 p0 = sp.GetPosition(i);
                Vector3 p3 = sp.GetPosition(j);
                Vector3 p1 = p0 + sp.GetRightTangent(i);
                Vector3 p2 = p3 + sp.GetLeftTangent(j);

                // Straight segment → its endpoints are enough (the next segment adds p3).
                bool linear = sp.GetRightTangent(i).sqrMagnitude < 1e-8f && sp.GetLeftTangent(j).sqrMagnitude < 1e-8f;
                int steps = linear ? 1 : subdivisionsPerSegment;

                for (int s = 0; s < steps; s++)
                {
                    float t = s / (float)steps;
                    // Cubic bezier: (1-t)³p0 + 3(1-t)²t p1 + 3(1-t)t² p2 + t³ p3
                    float u = 1f - t;
                    Vector3 pos = u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
                    // Spline-local → world → our local (child may sit anywhere under the controller).
                    Vector2 local = transform.InverseTransformPoint(src.TransformPoint(pos));
                    if (pts.Count == 0 || (local - pts[pts.Count - 1]).sqrMagnitude > 1e-6f) pts.Add(local);
                }
            }

            // Close-the-loop dedup: last sample can coincide with the first.
            if (pts.Count > 1 && (pts[0] - pts[pts.Count - 1]).sqrMagnitude < 1e-6f) pts.RemoveAt(pts.Count - 1);
            return pts;
        }

        // -------------------------------------------------------
        // Edge band geometry
        // -------------------------------------------------------

        /**
         * Per outline vertex: the unit OUTWARD direction (average of the adjacent edge
         * normals — this is what the shader bevels along) and the inset ring position
         * (vertex moved inward so both adjacent edges shift by exactly the band width,
         * with the miter length clamped so razor corners can't spike).
         */
        private void ComputeMitersAndInset(List<Vector2> pts, Vector2[] outDirs, Vector2[] inset)
        {
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = pts[i];
                Vector2 nPrev = EdgeNormal(pts[(i - 1 + n) % n], p);
                Vector2 nNext = EdgeNormal(p, pts[(i + 1) % n]);

                // Miter dir = bisector of the two edge normals (180° folds fall back to one side).
                Vector2 m = nPrev + nNext;
                m = m.sqrMagnitude < 1e-8f ? nNext : m.normalized;
                outDirs[i] = m;

                // Miter length = width / cos(halfAngle), clamped to miterLimit × width.
                float cosHalf = Mathf.Max(Vector2.Dot(m, nNext), 1f / miterLimit);
                inset[i] = p - m * (edgeBandWidth / cosHalf);
            }
        }

        /** Outward normal of edge a→b for a CCW-wound polygon. */
        private static Vector2 EdgeNormal(Vector2 a, Vector2 b)
        {
            Vector2 d = (b - a).normalized;
            return new Vector2(d.y, -d.x);
        }

        /** Signed polygon area (positive = counter-clockwise). */
        private static float SignedArea(List<Vector2> pts)
        {
            float sum = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector2 a = pts[i], b = pts[(i + 1) % pts.Count];
                sum += (a.x * b.y - b.x * a.y);
            }
            return 0.5f * sum;
        }

        // -------------------------------------------------------
        // Interior triangulation (ear clipping)
        // -------------------------------------------------------

        /**
         * Ear-clips a simple CCW polygon into triangle indices. O(n²) — fine for terrain
         * outlines. Degenerate leftovers (self-intersections from an over-wide inset,
         * collinear runs) fall back to a fan so a mesh always comes out.
         */
        private static List<int> Triangulate(Vector2[] poly)
        {
            var tris = new List<int>();
            int n = poly.Length;
            var idx = new List<int>(n);
            for (int i = 0; i < n; i++) idx.Add(i);

            while (idx.Count > 3)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int ia = idx[(i - 1 + idx.Count) % idx.Count];
                    int ib = idx[i];
                    int ic = idx[(i + 1) % idx.Count];
                    Vector2 a = poly[ia], b = poly[ib], c = poly[ic];

                    // Reflex corner can't be an ear (CCW → convex = positive cross).
                    if (Cross(b - a, c - b) <= 0f) continue;

                    // Reject the ear if any other remaining vertex sits strictly inside it.
                    bool blocked = false;
                    for (int k = 0; k < idx.Count; k++)
                    {
                        int ik = idx[k];
                        if (ik == ia || ik == ib || ik == ic) continue;
                        if (PointInTriangle(poly[ik], a, b, c)) { blocked = true; break; }
                    }
                    if (blocked) continue;

                    tris.Add(ia); tris.Add(ib); tris.Add(ic);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }

                // No ear found → degenerate input; emit a fan over what's left and stop.
                if (!clipped)
                {
                    for (int i = 1; i < idx.Count - 1; i++)
                    {
                        tris.Add(idx[0]); tris.Add(idx[i]); tris.Add(idx[i + 1]);
                    }
                    return tris;
                }
            }

            tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]);
            return tris;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        /** Strictly-inside test (boundary points don't block an ear). */
        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            const float eps = 1e-7f;
            return Cross(b - a, p - a) > eps && Cross(c - b, p - b) > eps && Cross(a - c, p - c) > eps;
        }

        // -------------------------------------------------------
        // Helpers
        // -------------------------------------------------------

        /** Planar tiling UV for a local-space vertex (world-space optional). */
        private Vector2 FillUV(Vector2 local)
        {
            Vector2 p = worldSpaceUVs ? (Vector2)transform.TransformPoint(local) : local;
            return p * uvTilesPerUnit;
        }

        /**
         * Fingerprint of everything a rebuild depends on: spline control points, the
         * spline↔mesh relative transform, and the generation settings. Cheap enough to
         * poll every editor frame (terrain outlines are tens of points).
         */
        private int ComputeHash()
        {
            unchecked
            {
                Spline sp = spline.spline;
                int h = 17;
                int count = sp.GetPointCount();
                h = h * 31 + count;
                for (int i = 0; i < count; i++)
                {
                    h = h * 31 + sp.GetPosition(i).GetHashCode();
                    h = h * 31 + sp.GetLeftTangent(i).GetHashCode();
                    h = h * 31 + sp.GetRightTangent(i).GetHashCode();
                }
                h = h * 31 + (transform.worldToLocalMatrix * spline.transform.localToWorldMatrix).GetHashCode();
                h = h * 31 + subdivisionsPerSegment;
                h = h * 31 + edgeBandWidth.GetHashCode();
                h = h * 31 + miterLimit.GetHashCode();
                h = h * 31 + uvTilesPerUnit.GetHashCode();
                h = h * 31 + (worldSpaceUVs ? 1 : 0);
                h = h * 31 + (sortingLayerName?.GetHashCode() ?? 0);
                h = h * 31 + sortingOrder;
                return h;
            }
        }
    }
}
