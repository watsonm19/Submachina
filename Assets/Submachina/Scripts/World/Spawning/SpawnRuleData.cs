using System;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Depth window (in positive metres below the surface) over which a rule is
     * active. hasMax toggles an upper bound so rules can run "from depth X
     * onward" (the common case) or only within a band.
     */
    [Serializable]
    public struct DepthRange
    {
        [HorizontalGroup("Depth"), LabelWidth(70)]
        [Tooltip("Rule is inactive shallower than this depth (m).")]
        [Min(0f)] public float minDepth;

        [HorizontalGroup("Depth"), LabelWidth(70)]
        [Tooltip("If on, the rule is also inactive deeper than maxDepth.")]
        public bool hasMax;

        [HorizontalGroup("Depth"), LabelWidth(70)]
        [ShowIf(nameof(hasMax))]
        [Tooltip("Rule is inactive deeper than this depth (m).")]
        public float maxDepth;

        /** True when the given absolute depth falls inside this window. */
        public readonly bool Contains(float depth)
            => depth >= minDepth && (!hasMax || depth <= maxDepth);
    }

    /**
     * The data for one spawnable type within a chunk — the heart of the
     * data-driven spawner. A designer adds a prefab, picks a depth window, a
     * prevalence curve, a count model, a placement strategy, optional spacing
     * and per-instance configuration, and free-text notes for other devs.
     *
     * Lives as a plain [Serializable] class so it can be authored inline on a
     * SpawnProfile OR wrapped by a reusable SpawnRule asset (see SpawnRule).
     */
    [Serializable]
    public class SpawnRuleData
    {
        // =====================
        // Identity & Docs
        // =====================

        [BoxGroup("$RuleTitle", centerLabel: true)]
        [HorizontalGroup("$RuleTitle/Row")]
        [Tooltip("Designer-facing name for this rule. Shown as the rule's header.")]
        public string ruleName = "New Spawn Rule";

        [HorizontalGroup("$RuleTitle/Row", width: 130)]
        [Tooltip("Optional zone tag for inspector grouping only — NOT a spawn gate. " +
                 "Depth range and prevalence drive actual spawning.")]
        [LabelText("Zone Tag")]
        public ZoneType zoneTag = ZoneType.Shallow;

        [BoxGroup("$RuleTitle")]
        [ShowInInspector, ToggleLeft, PropertyOrder(-1)]
        [LabelText("Show Help Text (applies to all rules)")]
        [Tooltip("Toggles the verbose explanation boxes throughout the spawn inspectors. " +
                 "Hover tooltips and the live previews stay regardless.")]
        private bool ShowHelpToggle
        {
            get => SpawnDocs.ShowHelp;
            set => SpawnDocs.ShowHelp = value;
        }

        [BoxGroup("$RuleTitle")]
        [Title("Developer Notes")]
        [HideLabel]
        [Tooltip("Notes for other developers: explain what this rule is for, why these numbers, gotchas, etc.")]
        [MultiLineProperty(4)]
        public string developerNotes = "";

        // =====================
        // Prefab
        // =====================

        [BoxGroup("$RuleTitle")]
        [Title("What to Spawn")]
        [Tooltip("Any prefab to spawn. Leave null to disable this rule.")]
        [Required, AssetsOnly]
        public GameObject prefab;

        // =====================
        // Depth & Prevalence
        // =====================

        [BoxGroup("$RuleTitle")]
        [Title("Depth & Frequency")]
        [Tooltip("Depth window (m) over which this rule is active.")]
        [InlineProperty, HideLabel]
        public DepthRange depth = new DepthRange { minDepth = 0f, hasMax = false, maxDepth = 400f };

        [BoxGroup("$RuleTitle")]
        [Tooltip("Extra depth-based MULTIPLIER on top of the Count model. X = depth (m), Y = multiplier.\n\n" +
                 "Leave flat at 1 for no change (the default — most rules). Set it above 1 to make this type " +
                 "more common at certain depths, below 1 to thin it out, or 0 to suppress it. It multiplies " +
                 "whatever the Count model produces, so you can shape a 'peak' depth without changing the " +
                 "base counts. Note: this is separate from Curve Range — use Curve Range for the base count, " +
                 "and this only when you want an additional hump or fade.")]
        [InfoBox("Flat line at Y=1 means 'no effect'. Most rules should leave it flat.",
            InfoMessageType.None, "@Submachina.Core.SpawnDocs.ShowHelp")]
        public AnimationCurve prevalenceByDepth = AnimationCurve.Constant(0f, 1000f, 1f);

        // =====================
        // Count & Spacing
        // =====================

        [BoxGroup("$RuleTitle")]
        [Title("How Many Per Chunk")]
        [Tooltip("How many instances spawn per chunk.")]
        [InlineProperty, HideLabel]
        public CountModel count = new CountModel();

        [BoxGroup("$RuleTitle")]
        [Tooltip("Hard cap on instances per chunk (0 = uncapped). Applied after the count model.")]
        [Min(0)] public int maxPerChunk = 0;

        [BoxGroup("$RuleTitle")]
        [Tooltip("Minimum world-space distance between instances of this rule (0 = no spacing). " +
                 "Placement retries a few times to satisfy it before skipping the instance.")]
        [Min(0f)] public float minSpacing = 0f;

        // =====================
        // Placement & Configuration
        // =====================

        [BoxGroup("$RuleTitle")]
        [Title("Placement & Sizing")]
        [Tooltip("WHERE instances are placed in the chunk.")]
        [SerializeReference]
        public PlacementStrategy placement = new ScatterPlacement();

        [BoxGroup("$RuleTitle")]
        [Tooltip("Optional per-instance setup applied after spawn (e.g. depth-driven O2 size, rock scale). " +
                 "Leave as None for prefabs that need no post-processing.")]
        [SerializeReference]
        public InstanceConfigurator configurator;

        // Number of placement retries when minSpacing can't be satisfied immediately
        private const int SpacingRetries = 8;

        // -------------------------------------------------------
        // Execution
        // -------------------------------------------------------

        /**
         * Spawns this rule's instances into the chunk. Called once per chunk by
         * WorldChunk with the chunk's geometry, a deterministic RNG, the
         * profile's global density, and the chunk transform as parent.
         *
         * Flow: depth gate → prevalence × density → count → per-instance
         * placement (with spacing retries) → instantiate → scale/configure.
         */
        public void Execute(in SpawnContext ctx, System.Random rng, float globalDensity, Transform parent)
        {
            // Skip disabled / out-of-range / misconfigured rules
            if (prefab == null || placement == null) return;
            if (!depth.Contains(ctx.depth)) return;

            // Fold the prevalence curve into the density multiplier, then resolve the count
            float prevalence = prevalenceByDepth != null ? prevalenceByDepth.Evaluate(ctx.depth) : 1f;
            int n = count.Evaluate(ctx.depth, rng, globalDensity * prevalence);
            if (maxPerChunk > 0) n = Mathf.Min(n, maxPerChunk);

            // Spacing is enforced against THIS rule's own instances (not other rules)
            int mineStart = ctx.placed.Count;

            // Place each instance, honoring minSpacing where set
            for (int i = 0; i < n; i++)
            {
                PlacementResult result = placement.GetPlacement(in ctx, rng);

                // Retry a few times to satisfy spacing, otherwise skip this instance
                if (minSpacing > 0f && TooClose(ctx, mineStart, result.position))
                {
                    bool placed = false;
                    for (int r = 0; r < SpacingRetries; r++)
                    {
                        result = placement.GetPlacement(in ctx, rng);
                        if (!TooClose(ctx, mineStart, result.position)) { placed = true; break; }
                    }
                    if (!placed) continue;
                }

                // Instantiate at the world position, apply optional scale, then per-instance setup
                GameObject go = UnityEngine.Object.Instantiate(prefab, result.position, Quaternion.identity, parent);
                if (result.scaleOverride.HasValue)
                    go.transform.localScale = result.scaleOverride.Value;
                configurator?.Configure(go, ctx.depth, rng);

                ctx.placed.Add(result.position);
            }
        }

        /**
         * True if a candidate is within minSpacing of an instance this rule
         * already placed. Only positions from mineStart onward are this rule's,
         * so spacing stays per-rule even though ctx.placed is shared.
         */
        private bool TooClose(in SpawnContext ctx, int mineStart, Vector2 candidate)
        {
            float sqr = minSpacing * minSpacing;
            for (int i = mineStart; i < ctx.placed.Count; i++)
                if ((ctx.placed[i] - candidate).sqrMagnitude < sqr) return true;
            return false;
        }

        // -------------------------------------------------------
        // Editor
        // -------------------------------------------------------

        // Header label for the rule's box group: name + prefab summary
        private string RuleTitle => string.IsNullOrEmpty(ruleName) ? "Spawn Rule" : ruleName;
    }
}
