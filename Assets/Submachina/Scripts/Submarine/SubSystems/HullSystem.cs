using UnityEngine;
using UnityEngine.Events;
using UnityAtoms.BaseAtoms;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Unified hull strength + depth pressure model (Sub.Hull).
     *
     * The sub has a single Hull Strength stat; Integrity is the existing Health
     * component's percent (Health stays the one damage store, so scrap-healing,
     * HitFlash and the HealthBar keep working unchanged).
     *
     *   Hull Resistance    = Strength × Integrity
     *   Pressure Load      = depth × pressurePerMeter × PressureLoadMult
     *   Structural Reserve = Resistance − Pressure Load   (margin left for impacts)
     *
     * An impact only damages the hull if PressureLoad + ImpactLoad exceeds the
     * current resistance — the excess is "overload" and converts to Health damage.
     * So the same bump that is harmless near the surface cracks the hull at depth,
     * and as integrity falls resistance falls too (failure spiral).
     *
     * If ambient pressure ALONE exceeds resistance (past crush depth), the hull
     * takes continuous cascade damage until the sub ascends or dies.
     *
     * CollisionDamage routes its impacts through EvaluateImpact; if no HullSystem
     * is present it falls back to its legacy flat damage, so old scenes still work.
     */
    [UsesFeedbacks(nameof(SubFeedbacks.HullCreak), nameof(SubFeedbacks.HullOverload),
                   nameof(SubFeedbacks.CrushZone))]
    public class HullSystem : SubmarineComponent
    {
        // =====================
        // Hull Settings
        // =====================

        [FoldoutGroup("Hull")]
        [Tooltip("Base structural strength. Rated depth ≈ strength / pressurePerMeter × safety factor. Upgradeable via SubStats.HullStrength.")]
        [SerializeField, Min(1f)] private float hullStrength = 120f;

        [FoldoutGroup("Hull")]
        [Tooltip("Pressure load units per meter of depth. 1.0 → 120 strength resists 120 m at full integrity.")]
        [SerializeField, Min(0.01f)] private float pressurePerMeter = 1f;

        [FoldoutGroup("Hull")]
        [Tooltip("Fraction of theoretical crush depth advertised as the 'rated depth' (safety margin for the hub UI and mission gating).")]
        [SerializeField, Range(0.1f, 1f)] private float ratedDepthSafetyFactor = 0.8f;

        // =====================
        // Impact Settings
        // =====================

        [FoldoutGroup("Impacts")]
        [Tooltip("Converts impact speed (m/s) into load units. Example: 12 → a 5 m/s hit adds 60 load on top of the pressure load.")]
        [SerializeField, Min(0f)] private float impactLoadScale = 12f;

        [FoldoutGroup("Impacts")]
        [Tooltip("Health damage per point of overload. Example: 0.5 → 30 overload = 15 damage.")]
        [SerializeField, Min(0f)] private float overloadToDamage = 0.5f;

        // =====================
        // Crush Cascade
        // =====================

        [FoldoutGroup("Crush Cascade")]
        [Tooltip("Health damage per second per point of pressure excess while past crush depth. Deeper past the limit = faster failure.")]
        [SerializeField, Min(0f)] private float cascadeDamageRate = 0.25f;

        // =====================
        // Warning Bands
        // =====================

        [FoldoutGroup("Warnings")]
        [Tooltip("Reserve fractions (of full resistance) at which a HullCreak cue fires while descending. Example: 0.5 then 0.25 then 0.1.")]
        [SerializeField] private float[] creakThresholds = { 0.5f, 0.25f, 0.1f };

        // =====================
        // Depth Source
        // =====================

        [FoldoutGroup("Atoms")]
        [Tooltip("Shared depth atom written by DepthTracker. If unassigned, depth is derived from this transform's Y (surface at Y=0).")]
        [SerializeField] private FloatVariable currentDepth;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")] public UnityEvent<float> onOverload;          // overload amount
        [FoldoutGroup("Events")] public UnityEvent onCrushZoneEntered;
        [FoldoutGroup("Events")] public UnityEvent onCrushZoneExited;
        [FoldoutGroup("Events")] public UnityEvent<float> onReserveChanged;    // normalized 0..1

        // =====================
        // Public State
        // =====================

        /** Depth in meters, from the atom when assigned, else from transform Y. */
        public float Depth => currentDepth != null ? currentDepth.Value : Mathf.Max(0f, -transform.position.y);

        public float Integrity => Sub?.Health != null ? Sub.Health.HealthPercent : 1f;
        public float StrengthMod => Sub?.Upgrades?.Stats.Resolve(SubStats.HullStrength, hullStrength) ?? hullStrength;
        public float PressureMultMod => Sub?.Upgrades?.Stats.Resolve(SubStats.PressureLoadMult, 1f) ?? 1f;
        public float ImpactMultMod => Sub?.Upgrades?.Stats.Resolve(SubStats.ImpactLoadMult, 1f) ?? 1f;

        public float HullResistance => StrengthMod * Integrity;
        public float PressureLoad => Depth * pressurePerMeter * PressureMultMod;
        public float StructuralReserve => Mathf.Max(0f, HullResistance - PressureLoad);

        /** Reserve as a fraction of full resistance — HUD bar + creak band driver. */
        public float ReserveFraction => HullResistance > 0f ? StructuralReserve / HullResistance : 0f;

        /** Depth shown in the hub and used for mission gating (with safety margin, at FULL integrity). */
        public float RatedDepth => StrengthMod * ratedDepthSafetyFactor / (pressurePerMeter * PressureMultMod);

        /** True while ambient pressure alone exceeds hull resistance. */
        public bool InCrushZone { get; private set; }

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector] private float DbgDepth => Depth;
        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector] private float DbgResistance => HullResistance;
        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector] private float DbgReserve => StructuralReserve;
        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector] private float DbgRatedDepth => RatedDepth;

        // =====================
        // Internals
        // =====================

        private float _cascadeAccumulator;   // fractional damage carried between frames
        private int _creakBandIndex;         // next creak threshold to cross
        private float _lastReserveFraction = 1f;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Update()
        {
            UpdateCrushZone();
            UpdateCreakBands();
        }

        // -------------------------------------------------------
        // Impacts
        // -------------------------------------------------------

        /**
         * Evaluates a collision at the current depth and returns the Health damage
         * it should deal (0 when the hull absorbs it within its margin).
         *
         * Example at 80 m, strength 120, full integrity: reserve = 120 − 80 = 40.
         * A 5 m/s hit (load 60) overloads by 20 → 10 damage. The same hit at 20 m
         * (reserve 100) deals nothing.
         */
        public int EvaluateImpact(float impactSpeed)
        {
            float impactLoad = impactSpeed * impactLoadScale * ImpactMultMod;
            float overload = PressureLoad + impactLoad - HullResistance;
            if (overload <= 0f) return 0;

            onOverload?.Invoke(overload);
            Sub?.Feedbacks?.Play(SubFeedbacks.HullOverload, transform.position, Mathf.Clamp01(overload / StrengthMod));

            return Mathf.CeilToInt(overload * overloadToDamage);
        }

        // -------------------------------------------------------
        // Crush cascade
        // -------------------------------------------------------

        /**
         * While pressure alone exceeds resistance, bleed continuous Health damage
         * proportional to the excess. Damage lowers integrity, which lowers
         * resistance, which raises the excess — the failure cascade.
         */
        private void UpdateCrushZone()
        {
            float excess = PressureLoad - HullResistance;
            bool inZone = excess > 0f && !(Sub?.Health?.IsDead ?? false);

            // Edge-trigger the zone state + looping feedback
            if (inZone != InCrushZone)
            {
                InCrushZone = inZone;
                if (inZone) { onCrushZoneEntered?.Invoke(); Sub?.Feedbacks?.Play(SubFeedbacks.CrushZone, transform.position); }
                else        { onCrushZoneExited?.Invoke();  Sub?.Feedbacks?.Stop(SubFeedbacks.CrushZone); }
            }

            if (!inZone) return;

            // Accumulate fractional damage so low rates still bite (same idiom as O2 bleed)
            _cascadeAccumulator += excess * cascadeDamageRate * Time.deltaTime;
            if (_cascadeAccumulator >= 1f)
            {
                int damage = Mathf.FloorToInt(_cascadeAccumulator);
                _cascadeAccumulator -= damage;
                Sub?.Health?.TakeDamage(damage);
            }
        }

        // -------------------------------------------------------
        // Warning creaks
        // -------------------------------------------------------

        /**
         * Fires a HullCreak cue each time the reserve fraction drops through the
         * next configured threshold, and re-arms bands when the sub recovers above
         * them (ascending or repairing).
         */
        private void UpdateCreakBands()
        {
            float fraction = ReserveFraction;

            // Descending through the next band → creak, harder the deeper the band
            while (_creakBandIndex < creakThresholds.Length && fraction <= creakThresholds[_creakBandIndex])
            {
                float intensity = 1f - creakThresholds[_creakBandIndex];
                Sub?.Feedbacks?.Play(SubFeedbacks.HullCreak, transform.position, intensity);
                _creakBandIndex++;
            }

            // Recovering above the previous band → re-arm it
            while (_creakBandIndex > 0 && fraction > creakThresholds[_creakBandIndex - 1])
                _creakBandIndex--;

            if (!Mathf.Approximately(fraction, _lastReserveFraction))
            {
                _lastReserveFraction = fraction;
                onReserveChanged?.Invoke(fraction);
            }
        }

        protected override void OnDestroy()
        {
            // Release the looping crush cue if we die/despawn inside the zone
            if (InCrushZone) Sub?.Feedbacks?.Stop(SubFeedbacks.CrushZone);
            base.OnDestroy();
        }
    }
}
