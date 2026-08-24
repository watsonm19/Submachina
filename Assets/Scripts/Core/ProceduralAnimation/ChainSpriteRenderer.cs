using Core.Rendering;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Core.ProceduralAnimation
{
    /**
     * Renders a ChainSimulator as a run of SpriteRenderers — the "sprite per tentacle
     * segment / leg segment" option. Where the mesh strip gives a continuous silhouette,
     * this gives art-directed segments (armor plates, crab leg shells, chain links) that
     * batch through the normal sprite pipeline.
     *
     * Segments are distributed by arc length along the solved chain, so visual density is
     * decoupled from simulation resolution: Segment Count plus spacing/width/length profile
     * curves (and an optional fixed-aspect mode) shape the run precisely. Segments can be
     * plain SpriteRenderers (optionally sharing a Segment Material) or clones of a Segment
     * Template prefab/scene object that carries extra components (SpecularController,
     * colliders, feedback players...). Whenever the children are rebuilt, components
     * implementing Core.Rendering.IChildRenderersChangedListener on this object or its
     * parents are notified so renderer-driving systems can re-cache the new renderers.
     */
    [ExecuteAlways]
    [DefaultExecutionOrder(60)]
    public class ChainSpriteRenderer : MonoBehaviour
    {
        // =====================
        // Source
        // =====================

        [FoldoutGroup("Source")]
        [Tooltip("Chain to render. Auto-resolves from this object or its parents if left empty.")]
        [SerializeField] private ChainSimulator chain;

        // =====================
        // Sprites
        // =====================

        [FoldoutGroup("Sprites")]
        [Tooltip("Sprite used for every segment. Ignored for segments covered by the Variants list; when a Segment Template brings its own sprite, leave this empty to keep it.")]
        [SerializeField] private Sprite sprite;

        [FoldoutGroup("Sprites")]
        [Tooltip("Optional per-segment sprites. Head To Tail: index 0 = head, segments beyond the list fall back to Sprite. Random Per Segment: each segment picks one, seeded by Random Seed.")]
        [SerializeField] private Sprite[] spriteVariants;

        [FoldoutGroup("Sprites")]
        [Tooltip("How the Variants list maps onto segments.")]
        [SerializeField] private SpriteVariantMode variantMode = SpriteVariantMode.HeadToTail;

        [FoldoutGroup("Sprites")]
        [Tooltip("Extra rotation (degrees) if the sprite art doesn't point along +X. Width and length stay chain-relative: an offset near ±90° automatically swaps which sprite axis carries the length so art drawn pointing up still sizes correctly.")]
        [SerializeField] private float rotationOffset = 0f;

        [FoldoutGroup("Sprites")]
        [Tooltip("Additional rotation added per segment toward the tail (degrees) — a progressive twist/fan for shape variation. E.g. 5 rotates segment 0 by 0°, segment 4 by 20°.")]
        [SerializeField] private float rotationStepPerSegment = 0f;

        [FoldoutGroup("Sprites")]
        [Tooltip("Random per-segment rotation up to ± this many degrees. Deterministic from Random Seed, so the look is stable across frames and sessions.")]
        [SerializeField, Min(0f)] private float rotationJitter = 0f;

        [FoldoutGroup("Sprites")]
        [ShowIf(nameof(UsesRandomness))]
        [Tooltip("Seed for random variant picks and rotation jitter — change to reshuffle.")]
        [SerializeField] private int randomSeed = 12345;

        [FoldoutGroup("Sprites")]
        [Tooltip("Mirror every other segment vertically — cheap way to break up repetition with identical shell/plate art.")]
        [SerializeField] private bool alternateFlipY = false;

        public enum SpriteVariantMode { HeadToTail, RandomPerSegment }

        // =====================
        // Appearance
        // =====================

        [FoldoutGroup("Appearance")]
        [Tooltip("Shared material for every segment (e.g. a SpriteLitSpecular material). Empty = Unity's default sprite material, or whatever the Segment Template carries. Pair with a SpecularController on this object — it drives all child renderers with one property block and is re-synced automatically on rebuilds.")]
        [SerializeField] private Material segmentMaterial;

        [FoldoutGroup("Appearance")]
        [Tooltip("Optional prefab or (disabled) scene child cloned for each segment — extra components on it (SpecularController, colliders, MMF players...) come along for the ride. Needs a SpriteRenderer at its root (one is added if missing). Empty = plain SpriteRenderers.")]
        [SerializeField] private GameObject segmentTemplate;

        [FoldoutGroup("Appearance")]
        [Tooltip("Hide the generated segment objects in the hierarchy for a tidier scene.")]
        [SerializeField] private bool hideSegmentsInHierarchy = false;

        // =====================
        // Shape
        // =====================

        [FoldoutGroup("Shape")]
        [Tooltip("Number of sprites along the chain. 0 = one per simulation segment; set explicitly to decouple visual density from chain resolution.")]
        [SerializeField, Min(0)] private int segmentCount = 0;

        [FoldoutGroup("Shape")]
        [Tooltip("Segment height (across the chain) in world units at the widest point of the profile.")]
        [SerializeField, Min(0.01f)] private float maxWidth = 0.4f;

        [FoldoutGroup("Shape")]
        [Tooltip("Width across the chain (x: 0 = head, 1 = tail; y: fraction of Max Width).")]
        [SerializeField] private AnimationCurve widthProfile = AnimationCurve.Linear(0f, 1f, 1f, 0.3f);

        [FoldoutGroup("Shape")]
        [Tooltip("How each segment's length (along the chain) is decided. Overlap Spacing: a multiple of its slot of the chain, like before. Fixed Aspect: locked to width × Aspect Ratio, so pieces hold their proportions exactly.")]
        [SerializeField] private SegmentSizeMode sizeMode = SegmentSizeMode.OverlapSpacing;

        [FoldoutGroup("Shape")]
        [ShowIf(nameof(sizeMode), SegmentSizeMode.OverlapSpacing)]
        [Tooltip("Segment length as a multiple of its slot along the chain. >1 overlaps neighbours so joints never show gaps.")]
        [SerializeField, Range(0.1f, 3f)] private float lengthOverlap = 1.25f;

        [FoldoutGroup("Shape")]
        [ShowIf(nameof(sizeMode), SegmentSizeMode.FixedAspect)]
        [Tooltip("Length ÷ width for every segment — e.g. 2 keeps pieces twice as long as they are wide regardless of chain spacing, using each segment's profiled width.")]
        [SerializeField, Min(0.05f)] private float aspectRatio = 1.5f;

        [FoldoutGroup("Shape")]
        [Tooltip("Length multiplier along the chain (x: 0 = head, 1 = tail) applied on top of the size mode — e.g. taper plate length toward the tail.")]
        [SerializeField] private AnimationCurve lengthProfile = AnimationCurve.Constant(0f, 1f, 1f);

        [FoldoutGroup("Shape")]
        [Tooltip("Relative spacing weights along the chain (x: 0 = head, 1 = tail). Normalized so segments always span the whole chain — a rising curve packs pieces tightly at the head and spreads them toward the tail.")]
        [SerializeField] private AnimationCurve spacingProfile = AnimationCurve.Constant(0f, 1f, 1f);

        public enum SegmentSizeMode { OverlapSpacing, FixedAspect }

        // =====================
        // Color & Sorting
        // =====================

        [FoldoutGroup("Color & Sorting")]
        [Tooltip("Tint along the chain (multiplied into each segment's SpriteRenderer color, on top of any template color).")]
        [SerializeField] private Gradient colorAlongLength = DefaultGradient();

        [FoldoutGroup("Color & Sorting")]
        [Tooltip("Sorting layer for all segment sprites.")]
        [SerializeField] private string sortingLayerName = "Default";

        [FoldoutGroup("Color & Sorting")]
        [Tooltip("Sorting order of the head segment.")]
        [SerializeField] private int sortingOrder = 0;

        [FoldoutGroup("Color & Sorting")]
        [Tooltip("Order change per segment toward the tail. -1 stacks the head on top (natural for overlapping shells).")]
        [SerializeField] private int sortingOrderStep = -1;

        private SpriteRenderer[] _segments;
        private Color[] _baseColors;     // template tint captured at spawn, multiplied under the gradient
        private float[] _arc;            // cumulative arc length per chain point
        private GameObject _builtTemplate;

        /** Live generated segment renderers, head to tail (null before the first build). */
        public SpriteRenderer[] Segments => _segments;

        private int ResolvedCount => segmentCount > 0 ? segmentCount : Mathf.Max(1, chain.PointCount - 1);
        private bool UsesRandomness => rotationJitter > 0f || variantMode == SpriteVariantMode.RandomPerSegment;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void OnEnable()
        {
            if (chain == null) chain = GetComponentInParent<ChainSimulator>();
            EnsureSegments();
        }

        private void OnValidate()
        {
            // Domain reload is disabled in this project — force a refresh on inspector
            // tweaks so sprite/color/material/sorting changes aren't stuck showing stale values.
            if (isActiveAndEnabled) EnsureSegments();
        }

        private void LateUpdate()
        {
            if (chain == null) chain = GetComponentInParent<ChainSimulator>();
            if (chain == null || chain.Chain == null) return;
            if (_segments == null || _segments.Length != ResolvedCount) EnsureSegments();
            if (_segments == null) return;

            // Edit mode: solve the rest pose each refresh so the preview stays live while tuning.
            if (!Application.isPlaying) chain.PrepareEditorPreview();

            // Arc-length table over the solved points — placement is independent of sim resolution.
            BuildArcTable();
            float total = _arc[_arc.Length - 1];
            if (total <= 1e-6f) return;

            // Spacing weights → slot widths, normalized so the run always spans the whole chain.
            int count = _segments.Length;
            float weightSum = 0f;
            for (int i = 0; i < count; i++) weightSum += SlotWeight(i, count);
            if (weightSum <= 0f) return;

            // Rotation Offset re-aims the art relative to the chain. When it puts the sprite's
            // Y axis along the chain (offset nearer ±90° than 0/180°), length and width swap
            // which local scale axis they drive — so "width" is always across the chain and
            // "length" always along it, no matter how the source art is drawn.
            bool sideways = Mathf.Abs(Mathf.Sin(rotationOffset * Mathf.Deg2Rad)) >
                            Mathf.Abs(Mathf.Cos(rotationOffset * Mathf.Deg2Rad));

            float s = 0f;
            for (int i = 0; i < count; i++)
            {
                float slot = SlotWeight(i, count) / weightSum * total;
                float center = s + slot * 0.5f;
                s += slot;

                var sr = _segments[i];
                if (sr == null) continue;
                float t = count > 1 ? i / (float)(count - 1) : 0f;

                // Size: width from the profile; length from slot overlap or a locked aspect ratio.
                float wid = Mathf.Max(0.001f, widthProfile.Evaluate(t)) * maxWidth;
                float baseLen = sizeMode == SegmentSizeMode.FixedAspect ? wid * aspectRatio : slot * lengthOverlap;
                float len = Mathf.Max(0.001f, baseLen * Mathf.Max(0.01f, lengthProfile.Evaluate(t)));

                // Pose: centered in its slot, aimed along the stretch of path it covers.
                Vector2 pos = SampleAt(center);
                Vector2 dir = SampleAt(center + len * 0.5f) - SampleAt(center - len * 0.5f);

                var tr = sr.transform;
                tr.position = pos;
                if (dir.sqrMagnitude > 1e-8f)
                {
                    float extra = rotationOffset + rotationStepPerSegment * i + JitterDegrees(i);
                    tr.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + extra);
                }

                // Scale sprite art to the target world size in chain space (with optional
                // alternating mirror): length lands on whichever sprite axis the Rotation
                // Offset aims down the chain, and the mirror always flips across the chain.
                Vector2 size = SpriteWorldSize(sr.sprite);
                float flip = alternateFlipY && (i & 1) == 1 ? -1f : 1f;
                tr.localScale = sideways
                    ? new Vector3(flip * wid / size.x, len / size.y, 1f)
                    : new Vector3(len / size.x, flip * wid / size.y, 1f);
            }
        }

        // -------------------------------------------------------
        // Segment management
        // -------------------------------------------------------

        /**
         * Creates/refreshes the pooled child renderers to match the resolved count, applying
         * sprite/color/material/sorting per segment. Notifies IChildRenderersChangedListener
         * components up the hierarchy when the child set or materials actually changed, so
         * renderer drivers like SpecularController re-gather and re-drive the new renderers.
         */
        private void EnsureSegments()
        {
            if (chain == null) return;
            int needed = ResolvedCount;
            bool structureChanged = false;

            // Lost tracking (recompile) or a template swap invalidates everything — sweep and rebuild.
            if (_segments == null || _builtTemplate != segmentTemplate)
            {
                DestroyAllSegments();
                _builtTemplate = segmentTemplate;
            }

            // A scene-object template is an authoring source only — keep it invisible.
            if (segmentTemplate != null && segmentTemplate.scene.IsValid() && segmentTemplate.activeSelf)
                segmentTemplate.SetActive(false);

            // Tear down extras beyond the needed count.
            if (_segments != null)
                for (int i = needed; i < _segments.Length; i++)
                    if (_segments[i] != null)
                    {
                        DestroyObject(_segments[i].gameObject);
                        structureChanged = true;
                    }

            var flags = HideFlags.DontSave | (hideSegmentsInHierarchy ? HideFlags.HideInHierarchy : HideFlags.None);
            var arr = new SpriteRenderer[needed];
            var baseCols = new Color[needed];

            for (int i = 0; i < needed; i++)
            {
                // Reuse live children; build the rest (template clone or plain SpriteRenderer).
                if (_segments != null && i < _segments.Length && _segments[i] != null)
                {
                    arr[i] = _segments[i];
                    baseCols[i] = _baseColors != null && i < _baseColors.Length ? _baseColors[i] : Color.white;
                }
                else
                {
                    arr[i] = CreateSegment(i, out baseCols[i]);
                    structureChanged = true;
                }

                // Refresh the per-segment look: sprite, tint, material, sorting, hierarchy visibility.
                float t = needed > 1 ? i / (float)(needed - 1) : 0f;
                var sr = arr[i];
                sr.gameObject.hideFlags = flags;
                var spr = SpriteFor(i);
                if (spr != null) sr.sprite = spr;
                sr.color = baseCols[i] * colorAlongLength.Evaluate(t);
                if (segmentMaterial != null && sr.sharedMaterial != segmentMaterial)
                {
                    sr.sharedMaterial = segmentMaterial;
                    structureChanged = true;
                }
                sr.sortingLayerName = sortingLayerName;
                sr.sortingOrder = sortingOrder + sortingOrderStep * i;
            }
            _segments = arr;
            _baseColors = baseCols;

            if (structureChanged) NotifyListeners();
        }

        /** Spawns one segment child — a clone of the template when set, else a bare SpriteRenderer. */
        private SpriteRenderer CreateSegment(int i, out Color baseColor)
        {
            GameObject go;
            if (segmentTemplate != null)
            {
                go = Instantiate(segmentTemplate, transform);
                go.name = $"Segment_{i}";
                go.SetActive(true); // scene-object templates are kept disabled
            }
            else
            {
                go = new GameObject($"Segment_{i}");
                go.transform.SetParent(transform, worldPositionStays: false);
            }

            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null) sr = go.AddComponent<SpriteRenderer>();
            baseColor = segmentTemplate != null ? sr.color : Color.white;
            return sr;
        }

        /** Destroys every generated child — tracked ones plus orphans left over from a recompile. */
        private void DestroyAllSegments()
        {
            if (_segments != null)
                foreach (var s in _segments)
                    if (s != null) DestroyObject(s.gameObject);
            _segments = null;
            _baseColors = null;

            // DontSave children survive script recompiles after our tracking array is lost — sweep by name.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i).gameObject;
                if (c != segmentTemplate && c.name.StartsWith("Segment_")) DestroyObject(c);
            }
        }

        /** Manual rebuild hatch — nukes and regenerates all segment children from current settings. */
        [FoldoutGroup("Appearance")]
        [Button("Rebuild Segments"), PropertyOrder(90)]
        public void RebuildSegments()
        {
            DestroyAllSegments();
            EnsureSegments();
        }

        /** Tells renderer-driving components up the hierarchy that the child renderer set changed. */
        private void NotifyListeners()
        {
            foreach (var l in GetComponentsInParent<IChildRenderersChangedListener>(true))
                l.OnChildRenderersChanged();
        }

        private static void DestroyObject(GameObject go)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        // -------------------------------------------------------
        // Arc sampling & per-segment helpers
        // -------------------------------------------------------

        /** Cumulative arc length per chain point — lets sprites sit at any fractional distance along the path. */
        private void BuildArcTable()
        {
            int n = chain.PointCount;
            if (_arc == null || _arc.Length != n) _arc = new float[n];
            _arc[0] = 0f;
            for (int i = 1; i < n; i++)
                _arc[i] = _arc[i - 1] + Vector2.Distance(chain.GetPoint(i - 1), chain.GetPoint(i));
        }

        /** World position at arc distance s along the chain polyline (clamped to the path ends). */
        private Vector2 SampleAt(float s)
        {
            s = Mathf.Clamp(s, 0f, _arc[_arc.Length - 1]);

            // Binary search for the polyline piece containing s, then lerp inside it.
            int lo = 0, hi = _arc.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (_arc[mid] <= s) lo = mid;
                else hi = mid;
            }
            float span = _arc[lo + 1] - _arc[lo];
            float u = span > 1e-6f ? (s - _arc[lo]) / span : 0f;
            return Vector2.Lerp(chain.GetPoint(lo), chain.GetPoint(lo + 1), u);
        }

        /** Spacing weight for segment i's slot, sampled at the slot midpoint of the profile. */
        private float SlotWeight(int i, int count) =>
            Mathf.Max(0.001f, spacingProfile.Evaluate((i + 0.5f) / count));

        /** Sprite for segment i honoring the variant mode; null means "keep what the template brought". */
        private Sprite SpriteFor(int i)
        {
            if (spriteVariants != null && spriteVariants.Length > 0)
            {
                int idx = variantMode == SpriteVariantMode.RandomPerSegment
                    ? Mathf.Min(spriteVariants.Length - 1, (int)(Hash01(i * 2) * spriteVariants.Length))
                    : i;
                if (idx < spriteVariants.Length && spriteVariants[idx] != null) return spriteVariants[idx];
            }
            return sprite;
        }

        /** Signed jitter angle for segment i (± Rotation Jitter degrees), stable per seed. */
        private float JitterDegrees(int i) =>
            rotationJitter <= 0f ? 0f : (Hash01(i * 2 + 1) * 2f - 1f) * rotationJitter;

        /** Deterministic 0-1 hash per key — stable across frames, reshuffled by Random Seed. */
        private float Hash01(int k)
        {
            float v = Mathf.Sin((k + 1) * 127.1f + randomSeed * 0.7f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }

        private static Vector2 SpriteWorldSize(Sprite s)
        {
            if (s == null) return Vector2.one;
            Vector2 size = s.bounds.size;
            return new Vector2(Mathf.Max(0.001f, size.x), Mathf.Max(0.001f, size.y));
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
