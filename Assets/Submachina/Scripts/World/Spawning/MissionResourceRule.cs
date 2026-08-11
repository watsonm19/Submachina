using System;
using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;
using Submachina.Meta;

namespace Submachina.Core
{
    /**
     * The mission-aware resource spawner — the "placeholder rule" a mission
     * profile carries instead of fixed resource rules.
     *
     * At chunk-generation time it expands the ACTIVE mission's scanner forecast
     * (MissionContext.Current.forecast) into one concrete SpawnRuleData per
     * resource prefab variant, so the world actually contains what the hub
     * advertised, at the advertised frequency:
     *   - count per chunk = countPerChunkAtFull × forecast abundance
     *     (TRACE ~0.2 → occasional single nodes; RICH ~1.4 → dense patches)
     *   - each type spawns only inside its native depth band
     *     (ResourceType.depthBand × MissionGenerator.WorldDepthScale), with a
     *     triangular prevalence peak at the band center — the same shape the
     *     forecast math uses, so the scanner report is honest.
     *
     * Types missing from the forecast (or below the scanner's reporting floor)
     * simply don't spawn — hauling them home is impossible, exactly as the
     * report implied. Played WITHOUT a mission (sandbox / direct scene play),
     * every configured type spawns at fallbackAbundance so test scenes still
     * have ore.
     *
     * Determinism: expansion is a pure function of the mission spec, and the
     * per-chunk RNG comes from the normal chunk pipeline, so identical seeds
     * still produce identical chunks. The expanded list is cached per spec.
     *
     * NOTE: this class reads the Meta layer (MissionContext) from the spawning
     * layer — the one sanctioned upward reference, documented here and in
     * Meta/context.md. Future biome packs can subclass SpawnRule the same way.
     */
    [CreateAssetMenu(fileName = "MissionResources", menuName = "Submachina/Spawning/Mission Resource Rule")]
    public class MissionResourceRule : SpawnRule
    {
        /** How one resource type spawns when its forecast calls for it. */
        [Serializable]
        public class ResourceTemplate
        {
            [Tooltip("The resource type this template covers (matched to forecast entries by Key).")]
            [Required] public ResourceType type;

            [Tooltip("World prefab variants for this resource (each carries a MiningResource tagged with the type). " +
                     "Multiple variants split the count budget evenly.")]
            public GameObject[] prefabVariants;

            [Tooltip("Total instances per chunk at abundance 1.0, across all variants. " +
                     "Scaled by the mission forecast: TRACE (~0.2) → rare finds, RICH (~1.4) → dense.")]
            [Min(0f)] public float countPerChunkAtFull = 2.5f;

            [Tooltip("Minimum spacing between instances of the same variant (0 = none).")]
            [Min(0f)] public float minSpacing = 2f;
        }

        // =====================
        // Settings
        // =====================

        [Title("Resource Templates")]
        [Tooltip("One template per spawnable resource type. Forecast entries without a template are skipped (warned once).")]
        public List<ResourceTemplate> templates = new();

        [Title("No-Mission Fallback")]
        [Tooltip("Abundance applied to EVERY template when no mission is active (sandbox / direct scene play). 0 disables fallback spawning.")]
        [SerializeField, Range(0f, 2f)] private float fallbackAbundance = 0.6f;

        [Tooltip("Prevalence at the edges of a type's depth band (peak at the center is 1). " +
                 "Small but non-zero so bands feather instead of hard-cutting.")]
        [SerializeField, Range(0f, 1f)] private float bandEdgePrevalence = 0.15f;

        // =====================
        // Expansion cache
        // =====================

        // Rebuilt whenever the active mission spec changes (reference compare —
        // specs are immutable once launched). Null spec = fallback expansion.
        [NonSerialized] private List<SpawnRuleData> _expanded;
        [NonSerialized] private MissionSpec _expandedFor;
        [NonSerialized] private bool _expandedOnce;

        /** Contributes the expanded per-type rules instead of the base single rule. */
        public override IEnumerable<SpawnRuleData> Rules
        {
            get
            {
                var spec = MissionContext.Current;
                if (!_expandedOnce || !ReferenceEquals(spec, _expandedFor))
                {
                    _expanded = Expand(spec);
                    _expandedFor = spec;
                    _expandedOnce = true;
                }
                return _expanded;
            }
        }

        // -------------------------------------------------------
        // Expansion
        // -------------------------------------------------------

        /**
         * Builds the concrete rule list for a mission spec (or the fallback mix
         * when spec is null / has no forecast).
         */
        private List<SpawnRuleData> Expand(MissionSpec spec)
        {
            var rules = new List<SpawnRuleData>();

            // Resolve the abundance per template: forecast-driven, else fallback
            foreach (var template in templates)
            {
                if (template?.type == null || template.prefabVariants == null || template.prefabVariants.Length == 0) continue;

                float abundance = ResolveAbundance(spec, template.type.Key);
                if (abundance <= 0f) continue;

                AddRulesFor(rules, template, abundance);
            }

            // Warn (once per expansion) about forecast entries we can't spawn
            if (spec?.forecast != null)
                foreach (var entry in spec.forecast)
                    if (FindTemplate(entry.resourceKey) == null)
                        Debug.LogWarning($"[MissionResourceRule] Forecast lists '{entry.resourceKey}' but no template covers it — it will not spawn.");

            return rules;
        }

        /** Forecast abundance for a key; fallbackAbundance when no mission is active; 0 when unlisted. */
        private float ResolveAbundance(MissionSpec spec, string key)
        {
            if (spec == null || spec.forecast == null || spec.forecast.Count == 0) return fallbackAbundance;

            foreach (var entry in spec.forecast)
                if (entry.resourceKey == key) return entry.abundance;
            return 0f;   // scanner didn't report it → it isn't there
        }

        /** One SpawnRuleData per prefab variant, splitting the type's count budget evenly. */
        private void AddRulesFor(List<SpawnRuleData> rules, ResourceTemplate template, float abundance)
        {
            // Depth band in absolute metres, prevalence peaking at the band center —
            // the same triangle the mission generator's forecast math uses
            float minDepth = template.type.depthBand.x * MissionGenerator.WorldDepthScale;
            float maxDepth = template.type.depthBand.y * MissionGenerator.WorldDepthScale;
            float center = (minDepth + maxDepth) * 0.5f;

            var prevalence = new AnimationCurve(
                new Keyframe(minDepth, bandEdgePrevalence),
                new Keyframe(center, 1f),
                new Keyframe(maxDepth, bandEdgePrevalence));

            // Split the abundance-scaled budget across the variants.
            // Example: budget 2.8 over 2 variants → each Range(1, 2) per chunk,
            // thinned toward the band edges by the prevalence curve.
            float perVariant = template.countPerChunkAtFull * abundance / template.prefabVariants.Length;

            foreach (var prefab in template.prefabVariants)
            {
                if (prefab == null) continue;

                rules.Add(new SpawnRuleData
                {
                    ruleName = $"Mission: {template.type.Key}",
                    developerNotes = "Runtime-expanded by MissionResourceRule — do not author by hand.",
                    prefab = prefab,
                    depth = new DepthRange { minDepth = minDepth, hasMax = true, maxDepth = maxDepth },
                    prevalenceByDepth = prevalence,
                    count = new CountModel
                    {
                        kind = CountKind.Range,
                        min = Mathf.FloorToInt(perVariant),
                        max = Mathf.CeilToInt(perVariant),
                    },
                    minSpacing = template.minSpacing,
                    placement = new ScatterPlacement(),
                });
            }
        }

        private ResourceTemplate FindTemplate(string key)
        {
            foreach (var template in templates)
                if (template?.type != null && template.type.Key == key) return template;
            return null;
        }
    }
}
