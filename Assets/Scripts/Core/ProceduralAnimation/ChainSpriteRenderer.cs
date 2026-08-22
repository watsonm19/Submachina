using Sirenix.OdinInspector;
using UnityEngine;

namespace Core.ProceduralAnimation
{
    /**
     * Renders a ChainSimulator as a run of SpriteRenderers, one per segment —
     * the "sprite per tentacle segment / leg segment" option. Where the mesh strip
     * gives a continuous silhouette, this gives art-directed segments (armor plates,
     * crab leg shells, chain links) that batch through the normal sprite pipeline.
     *
     * Child renderers are created and pooled automatically at runtime; position,
     * rotation, scale, and color track the solved chain every LateUpdate.
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
        [Tooltip("Sprite used for every segment. Ignored for segments covered by the Variants list.")]
        [SerializeField] private Sprite sprite;

        [FoldoutGroup("Sprites")]
        [Tooltip("Optional per-segment sprites, index 0 = segment at the head. Segments beyond the list fall back to the single Sprite above.")]
        [SerializeField] private Sprite[] spriteVariants;

        [FoldoutGroup("Sprites")]
        [Tooltip("Extra rotation (degrees) if the sprite art doesn't point along +X.")]
        [SerializeField] private float rotationOffset = 0f;

        // =====================
        // Shape
        // =====================

        [FoldoutGroup("Shape")]
        [Tooltip("Segment height (across the chain) in world units at the widest point of the profile.")]
        [SerializeField, Min(0.01f)] private float maxWidth = 0.4f;

        [FoldoutGroup("Shape")]
        [Tooltip("Width along the chain (x: 0 = head, 1 = tail; y: fraction of Max Width).")]
        [SerializeField] private AnimationCurve widthProfile = AnimationCurve.Linear(0f, 1f, 1f, 0.3f);

        [FoldoutGroup("Shape")]
        [Tooltip("Segment length as a multiple of the chain's segment spacing. >1 overlaps neighbours so joints never show gaps.")]
        [SerializeField, Range(0.5f, 2.5f)] private float lengthOverlap = 1.25f;

        // =====================
        // Color & Sorting
        // =====================

        [FoldoutGroup("Color & Sorting")]
        [Tooltip("Tint along the chain (multiplied into each segment's SpriteRenderer color).")]
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
            // tweaks so sprite/color/sorting changes aren't stuck showing stale values.
            if (isActiveAndEnabled) EnsureSegments();
        }

        private void LateUpdate()
        {
            if (chain == null) chain = GetComponentInParent<ChainSimulator>();
            if (chain == null || chain.Chain == null) return;
            if (_segments == null || _segments.Length != chain.PointCount - 1) EnsureSegments();

            // Edit mode: solve the rest pose each refresh so the preview stays live while tuning.
            if (!Application.isPlaying) chain.PrepareEditorPreview();

            // Each segment sits at its midpoint, rotated along the local tangent.
            int count = _segments.Length;
            for (int i = 0; i < count; i++)
            {
                var sr = _segments[i];
                Vector2 a = chain.GetPoint(i);
                Vector2 b = chain.GetPoint(i + 1);
                Vector2 mid = (a + b) * 0.5f;
                Vector2 dir = b - a;

                var t = sr.transform;
                t.position = mid;
                if (dir.sqrMagnitude > 1e-8f)
                    t.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + rotationOffset);

                // Scale sprite art to the desired world size (length × profile width).
                float along = count > 1 ? i / (float)(count - 1) : 0f;
                Vector2 size = SpriteWorldSize(sr.sprite);
                float targetLen = chain.SegmentLength * lengthOverlap;
                float targetWid = Mathf.Max(0.001f, widthProfile.Evaluate(along)) * maxWidth;
                t.localScale = new Vector3(targetLen / size.x, targetWid / size.y, 1f);
            }
        }

        // -------------------------------------------------------
        // Segment management
        // -------------------------------------------------------

        /** Creates/refreshes the pooled child SpriteRenderers to match the chain length. */
        private void EnsureSegments()
        {
            if (chain == null) return;
            int needed = Mathf.Max(1, chain.PointCount - 1);

            // Tear down extras, keep/create the rest — segments are plain hidden children.
            if (_segments != null)
                for (int i = needed; i < _segments.Length; i++)
                    if (_segments[i] != null)
                    {
                        if (Application.isPlaying) Destroy(_segments[i].gameObject);
                        else DestroyImmediate(_segments[i].gameObject);
                    }

            var arr = new SpriteRenderer[needed];
            for (int i = 0; i < needed; i++)
            {
                if (_segments != null && i < _segments.Length && _segments[i] != null) arr[i] = _segments[i];
                else
                {
                    var go = new GameObject($"Segment_{i}") { hideFlags = HideFlags.DontSave };
                    go.transform.SetParent(transform, worldPositionStays: false);
                    arr[i] = go.AddComponent<SpriteRenderer>();
                }

                float along = needed > 1 ? i / (float)(needed - 1) : 0f;
                arr[i].sprite = SpriteFor(i);
                arr[i].color = colorAlongLength.Evaluate(along);
                arr[i].sortingLayerName = sortingLayerName;
                arr[i].sortingOrder = sortingOrder + sortingOrderStep * i;
            }
            _segments = arr;
        }

        private Sprite SpriteFor(int i) =>
            spriteVariants != null && i < spriteVariants.Length && spriteVariants[i] != null
                ? spriteVariants[i] : sprite;

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
