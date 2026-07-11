using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;
using Core.Rendering;

namespace Submachina.Core
{
    /**
     * Procedurally populates this object with a scatter cluster described by a
     * ClusterConfig: weighted inert rocks (randomly sized/rotated/flipped) that
     * sometimes include one value prefab, with SpawnLuck pity protecting the
     * value roll against long droughts.
     *
     * Two ways a cluster gets built:
     *   1. Chunk pipeline — a spawn rule instantiates the cluster prefab and its
     *      ClusterBuildConfigurator calls Build(depth, rng) with the chunk's
     *      deterministic RNG (synchronously, before Start runs).
     *   2. Hand-placed in a scene — Start() notices it was never built and
     *      self-seeds from its world position, so designers can just drop the
     *      prefab anywhere.
     *
     * Determinism contract:
     *   - Build consumes exactly ONE draw from the caller's rng and derives two
     *     internal streams from it, so cluster tuning (rock counts, pity state,
     *     config edits) can never shift the draws seen by later rules in the
     *     same chunk.
     *   - layoutRng drives everything about the rocks — layout is a pure
     *     function of the seed.
     *   - valueRng (an isolated stream) drives the pity roll and the value
     *     prefab's placement — position and (for Middle/Random) its sorting
     *     slot. Value PRESENCE is pity/play-order-dependent by design; its
     *     position and slot for a given seed are not.
     *   - tintRng (a third isolated stream) drives the sprite tint rolls, so
     *     toggling or tuning tint settings never shifts the layout or value
     *     placement a seed produces.
     *
     * The prefab root should carry a SortingGroup and NO authored children —
     * the builder owns (and clears) everything under it.
     */
    public class ClusterBuilder : MonoBehaviour
    {
        // Matches SpawnRuleData.SpacingRetries — attempts to satisfy minSpacing before skipping a rock
        private const int SpacingRetries = 8;

        // =====================
        // Settings
        // =====================

        [Tooltip("The cluster recipe: rock pool, layout shape, value prefab and pity settings.")]
        [Required, InlineEditor(objectFieldMode: InlineEditorObjectFieldModes.Boxed)]
        [SerializeField] private ClusterConfig config;

        /** The cluster recipe — settable so editor tools can wire up test instances. */
        public ClusterConfig Config { get => config; set => config = value; }

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        [Tooltip("Seed of the last build — reuse it in the preview to reproduce a layout seen in-game.")]
        private int _lastSeed;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private bool HasValue => _hasValue;

        // =====================
        // State
        // =====================

        private bool _built;
        private bool _hasValue;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        /**
         * Fallback for hand-placed instances: if the chunk pipeline never built
         * us (its configurator runs during Instantiate, before Start), self-seed
         * from the world position using the same primes as ChunkSpawner.SeedFor
         * so nearby hand-placed clusters still look distinct.
         */
        private void Start()
        {
            if (_built) return;
            BuildFromSeed(PositionHash(), Mathf.Max(0f, -transform.position.y));
        }

        // -------------------------------------------------------
        // Building
        // -------------------------------------------------------

        /**
         * Primary entry for the chunk pipeline. Consumes exactly ONE draw from
         * the caller's rng (see the determinism contract in the class header).
         */
        public void Build(float depth, System.Random rng) => BuildFromSeed(rng.Next(), depth);

        /**
         * Clears any previous children and rebuilds the cluster. Rock layout is
         * a pure function of the seed; the value roll additionally consults
         * SpawnLuck's pity streak. Depth is forwarded for future depth-aware
         * configs (unused by the current parameters).
         */
        public void BuildFromSeed(int seed, float depth)
        {
            _built = true;
            _lastSeed = seed;
            _hasValue = false;
            Clear();

            if (config == null) { Debug.LogWarning("[ClusterBuilder] No ClusterConfig assigned — cluster left empty.", this); return; }

            // Three isolated streams: layout stays seed-pure, value absorbs the pity-dependent
            // draws, and tint rolls can be tuned/toggled without disturbing either
            var layoutRng = new System.Random(seed);
            var valueRng = new System.Random(seed ^ unchecked((int)0x9E3779B9));
            var tintRng = new System.Random(seed ^ unchecked((int)0x85EBCA6B));

            // --- Rocks: count, then per rock pick → place → dress, all from layoutRng ---
            List<Vector2> placed = new List<Vector2>();
            List<GameObject> rocks = new List<GameObject>(); // kept so the value can slot into their sort order
            int count = layoutRng.Next(config.countMin, Mathf.Max(config.countMin, config.countMax) + 1);
            for (int i = 0; i < count; i++)
            {
                GameObject prefab = config.PickRock(layoutRng);
                if (prefab == null) break; // empty/broken pool — no point looping

                // Polar placement through the falloff curve, retrying if too crowded
                Vector2 localPos = RollPosition(layoutRng, 1f);
                if (config.minSpacing > 0f && TooClose(placed, localPos))
                {
                    bool found = false;
                    for (int r = 0; r < SpacingRetries; r++)
                    {
                        localPos = RollPosition(layoutRng, 1f);
                        if (!TooClose(placed, localPos)) { found = true; break; }
                    }
                    if (!found) continue; // skip this rock — cluster is just a little sparser
                }

                GameObject rock = Instantiate(prefab, transform);
                ShadowCaster2DRefresher.RefreshHierarchy(rock); // URP 2D casters don't rebuild their mesh on clone — force it
                ApplyRandomTransform(rock.transform, localPos, layoutRng);
                if (config.varySpriteTint) ApplyRandomTint(rock, tintRng);
                if (config.orderChildrenBySpawnIndex) SetSortingOrder(rock, placed.Count);
                placed.Add(localPos);
                rocks.Add(rock);
            }

            // --- Value: pity-protected roll + center-biased placement, all from valueRng ---
            if (config.valuePrefab != null && config.valueChance > 0f
                && SpawnLuck.Roll(config.luckKey, config.valueChance, in config.pity, valueRng))
            {
                // Same radial sampling as the rocks, squeezed toward center by the bias
                Vector2 localPos = RollPosition(valueRng, 1f - config.valueCenterBias);

                GameObject value = Instantiate(config.valuePrefab, transform);
                ShadowCaster2DRefresher.RefreshHierarchy(value); // same URP 2D shadow-mesh rebuild kick as the rocks
                if (config.randomizeValueTransform)
                    ApplyRandomTransform(value.transform, localPos, valueRng);
                else
                    value.transform.localPosition = localPos;

                // Optionally give the value the same color variety as the rocks
                if (config.varySpriteTint && config.tintValuePrefab) ApplyRandomTint(value, tintRng);

                // Slot the value into the rocks' sorting stack per the configured mode
                if (config.orderChildrenBySpawnIndex) PlaceValueInSortOrder(value, rocks, valueRng);
                _hasValue = true;
            }
        }

        /**
         * Destroys every child — the builder owns the whole subtree. Collects
         * first because destroying while iterating reorders the child list.
         */
        public void Clear()
        {
            List<GameObject> children = new List<GameObject>(transform.childCount);
            foreach (Transform child in transform) children.Add(child.gameObject);
            foreach (GameObject child in children)
            {
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }

        // -------------------------------------------------------
        // Sampling helpers
        // -------------------------------------------------------

        /**
         * Polar position sample: uniform angle + falloff-curved distance, with the
         * distance optionally squeezed toward center (radiusScale 0 = dead center).
         * Always consumes exactly two rng draws.
         */
        private Vector2 RollPosition(System.Random rng, float radiusScale)
        {
            float angle = rng.NextFloat(0f, Mathf.PI * 2f);
            float dist = SizeSampler.Sample(0f, config.radius, config.radialFalloff, rng.NextFloat01()) * radiusScale;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;
        }

        /**
         * Dresses an instance with the config's scale/rotation/flip rolls.
         * Flips are negative scale components so child colliders mirror too.
         * Always consumes exactly four rng draws.
         */
        private void ApplyRandomTransform(Transform t, Vector2 localPos, System.Random rng)
        {
            float scale = SizeSampler.Sample(config.scaleMin, config.scaleMax, config.scaleDistribution, rng.NextFloat01());
            float rotZ = rng.NextFloat(-config.maxRotation, config.maxRotation);
            float flipX = rng.NextFloat01() < config.flipXChance ? -1f : 1f;
            float flipY = rng.NextFloat01() < config.flipYChance ? -1f : 1f;

            t.localPosition = localPos;
            t.localRotation = Quaternion.Euler(0f, 0f, rotZ);
            t.localScale = new Vector3(scale * flipX, scale * flipY, 1f);
        }

        /**
         * Rolls one tint per instance and multiplies it into every SpriteRenderer
         * beneath it (no-op if the prefab has none). SpriteRenderer.color is a
         * multiply tint, so the hue cast is a low-saturation color (strength =
         * saturation) and brightness can only darken. One roll per instance keeps
         * multi-sprite prefabs internally consistent. Always consumes exactly two
         * rng draws.
         *
         * Example: hueRange (180, 260), strength 0.15, brightness 0.9 → a mild
         * teal-to-blue cast at 90% brightness on all of the instance's sprites.
         */
        private void ApplyRandomTint(GameObject instance, System.Random rng)
        {
            // Roll the cast hue and brightness, then bake them into a multiply color
            float hue = Mathf.Repeat(rng.NextFloat(config.tintHueRange.x, config.tintHueRange.y) / 360f, 1f);
            float brightness = rng.NextFloat(config.tintBrightnessRange.x, config.tintBrightnessRange.y);
            Color tint = Color.HSVToRGB(hue, config.tintHueStrength, 1f) * brightness;

            // Multiply into each renderer so authored per-sprite colors still show through
            foreach (SpriteRenderer sr in instance.GetComponentsInChildren<SpriteRenderer>(true))
            {
                Color c = sr.color * tint;
                c.a = sr.color.a; // tint never touches transparency
                sr.color = c;
            }

            // Appearance layers that live behind property blocks / custom shaders (e.g. the
            // specular glint) can't be reached via sr.color — hand them the same tint.
            foreach (ITintReceiver receiver in instance.GetComponentsInChildren<ITintReceiver>(true))
                receiver.ApplyTint(tint);
        }

        /** True if a candidate position violates minSpacing against any already-placed rock. */
        private bool TooClose(List<Vector2> placed, Vector2 candidate)
        {
            float minSq = config.minSpacing * config.minSpacing;
            foreach (Vector2 p in placed)
                if ((p - candidate).sqrMagnitude < minSq) return true;
            return false;
        }

        /**
         * Drops the value prefab into the rocks' sorting stack at a slot chosen
         * by config.valueSortMode, then re-stamps everything so orders stay
         * gap-free and tie-free: rocks below the slot keep their order, the value
         * takes the slot, and rocks at/above it shift up by one.
         *
         * Slot k lives in [0, N] where N = rock count (0 = under every rock,
         * N = over every rock). Middle/Random draw k from the value rng so the
         * slot stays a pure function of the seed. Middle picks an interior slot
         * [1, N-1] when there's room; with 0-1 rocks there is no true middle, so
         * it falls back to a random slot (matches "random when only 1-2 spawns").
         */
        private void PlaceValueInSortOrder(GameObject value, List<GameObject> rocks, System.Random rng)
        {
            int n = rocks.Count;

            // Pick the insertion slot in [0, n] per the mode
            int slot;
            switch (config.valueSortMode)
            {
                case ValueSortMode.Bottom: slot = 0; break;
                case ValueSortMode.Random: slot = rng.Next(0, n + 1); break;
                case ValueSortMode.Middle: slot = n <= 1 ? rng.Next(0, n + 1) : rng.Next(1, n); break;
                default:                   slot = n; break; // Top
            }

            // Re-stamp rocks around the slot, then drop the value in — no ties, no gaps
            for (int i = 0; i < n; i++) SetSortingOrder(rocks[i], i < slot ? i : i + 1);
            SetSortingOrder(value, slot);
        }

        /** Stamps every SpriteRenderer under the instance with one sorting order. */
        private static void SetSortingOrder(GameObject instance, int order)
        {
            foreach (SpriteRenderer sr in instance.GetComponentsInChildren<SpriteRenderer>(true))
                sr.sortingOrder = order;
        }

        /** Position-derived seed (same primes as ChunkSpawner.SeedFor), 0.1-unit resolution. */
        private int PositionHash()
        {
            int x = Mathf.RoundToInt(transform.position.x * 10f);
            int y = Mathf.RoundToInt(transform.position.y * 10f);
            unchecked { return x * 73856093 ^ y * 19349663; }
        }

        // -------------------------------------------------------
        // Editor preview
        // -------------------------------------------------------

#if UNITY_EDITOR
        // NOTE: previewing on a prefab INSTANCE in a scene records the children as
        // prefab overrides — preview in Prefab Mode or on a test object, then Clear
        // before saving. Pity state is live even in edit mode (static table), so
        // repeated previews advance the streak; use Reset Pity to start fresh.

        [FoldoutGroup("Preview"), LabelWidth(110)]
        [Tooltip("On: rebuilds use the fixed seed below (reproduce a specific layout). Off: random seed each time.")]
        [SerializeField] private bool usePreviewSeed;

        [FoldoutGroup("Preview"), LabelWidth(110), EnableIf(nameof(usePreviewSeed))]
        [SerializeField] private int previewSeed = 12345;

        [FoldoutGroup("Preview")]
        [Button("Rebuild Preview", ButtonSizes.Medium), GUIColor(0.6f, 0.9f, 0.6f)]
        private void RebuildPreview()
        {
            int seed = usePreviewSeed ? previewSeed : Random.Range(int.MinValue, int.MaxValue);
            BuildFromSeed(seed, Mathf.Max(0f, -transform.position.y));
            Debug.Log($"[ClusterBuilder] Built with seed {seed} — {transform.childCount} children" +
                      $"{(_hasValue ? " (value spawned!)" : "")}.", this);
        }

        [FoldoutGroup("Preview")]
        [Button("Clear"), GUIColor(0.95f, 0.7f, 0.6f)]
        private void ClearPreview() => Clear();

        [FoldoutGroup("Preview")]
        [Button("Reset Pity For This Key")]
        private void ResetPity()
        {
            if (config == null) return;
            SpawnLuck.Reset(config.luckKey);
            Debug.Log($"[ClusterBuilder] Pity streak reset for key '{config.luckKey}'.");
        }
#endif
    }
}
