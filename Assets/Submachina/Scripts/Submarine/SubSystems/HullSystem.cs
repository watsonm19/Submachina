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
     *   Rated Depth       = Strength × safety / (pressurePerMeter × PressureLoadMult)
     *   Pressure Load     = depth × pressurePerMeter × PressureLoadMult − PressureBoost
     *   Impact Resistance = Strength × lerp(integrityFloor, 1, Integrity)
     *   Reserve           = Impact Resistance − Pressure Load   (margin for impacts)
     *
     * Rated depth is FIXED by loadout — damage never lowers it, so ascending
     * above rated depth always stops pressure damage (cascades are recoverable).
     * Integrity only degrades impact absorption, floored at integrityFloor so a
     * battered sub gets fragile but never spirals to zero resistance.
     *
     * Past rated depth, strain time accrues and pressure damage ramps along
     * strainRampCurve — a short dip is cheap, loitering is lethal. Strain
     * recovers (faster) once back above rated depth.
     *
     * Impacts only damage the hull when pressure + impact load exceeds impact
     * resistance (the excess is "overload"); enemy attacks are scaled by depth
     * via EvaluateAttack up to maxAttackVulnerability at rated depth.
     *
     * PressureBoost (pump-to-hull) cancels part of the pressure load — internal
     * air pressure pushing back against the sea — and decays over time.
     */
    [UsesFeedbacks(nameof(SubFeedbacks.HullCreak), nameof(SubFeedbacks.HullOverload),
                   nameof(SubFeedbacks.CrushZone), nameof(SubFeedbacks.PressureDamage),
                   nameof(SubFeedbacks.HullPressurize))]
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
        [Tooltip("Fraction of theoretical crush depth used as the rated depth — the depth where pressure strain (and damage) begins.")]
        [SerializeField, Range(0.1f, 1f)] private float ratedDepthSafetyFactor = 0.8f;

        [FoldoutGroup("Hull")]
        [Tooltip("Minimum fraction of strength kept as impact resistance at zero integrity. Example: 0.5 → a wrecked hull still absorbs half its rated load, so damage weakens you without an unwinnable spiral.")]
        [SerializeField, Range(0f, 1f)] private float integrityFloor = 0.5f;

        // =====================
        // Impact Settings
        // =====================

        [FoldoutGroup("Impacts")]
        [Tooltip("Converts impact speed (m/s) into load units. Example: 12 → a 5 m/s hit adds 60 load on top of the pressure load.")]
        [SerializeField, Min(0f)] private float impactLoadScale = 12f;

        [FoldoutGroup("Impacts")]
        [Tooltip("Health damage per point of overload. Example: 0.5 → 30 overload = 15 damage.")]
        [SerializeField, Min(0f)] private float overloadToDamage = 0.5f;

        [FoldoutGroup("Impacts")]
        [Tooltip("Enemy attack damage multiplier when at (or past) rated depth. 1 = depth never amplifies attacks; 2 = attacks hit twice as hard at rated depth. Scales linearly from the surface.")]
        [SerializeField, Min(1f)] private float maxAttackVulnerability = 2f;

        // =====================
        // Pressure Strain
        // =====================

        [FoldoutGroup("Pressure Strain")]
        [Tooltip("Health damage per second per point of pressure excess past rated depth, before the strain ramp multiplier.")]
        [SerializeField, Min(0f)] private float cascadeDamageRate = 0.25f;

        [FoldoutGroup("Pressure Strain")]
        [Tooltip("Damage-rate multiplier over seconds spent past rated depth. Example: starts near 0.1 (grace window), climbs past 1 after ~15s — a quick dip is survivable, camping is not.")]
        [SerializeField] private AnimationCurve strainRampCurve = new AnimationCurve(
            new Keyframe(0f, 0.1f), new Keyframe(5f, 0.3f), new Keyframe(15f, 1.25f), new Keyframe(30f, 3f));

        [FoldoutGroup("Pressure Strain")]
        [Tooltip("Strain seconds shed per second while at or above rated depth. Example: 2 → strain recovers twice as fast as it builds.")]
        [SerializeField, Min(0f)] private float strainRecoveryRate = 2f;

        [FoldoutGroup("Pressure Strain")]
        [Tooltip("Pressure boost (load units) lost per second — pumped-in air slowly vents. 0 = boost lasts the whole dive.")]
        [SerializeField, Min(0f)] private float boostDecayPerSecond = 1f;

        // =====================
        // Pressurization (pump-to-hull)
        // =====================

        [FoldoutGroup("Pressurization")]
        [Tooltip("Counter-pressure load units gained per O2 unit pumped into the hull. Example: 1 → a perfect 25-air pump cancels 25 load ≈ 25 m of extra depth headroom.")]
        [SerializeField, Min(0f)] private float boostPerAirUnit = 1f;

        [FoldoutGroup("Pressurization")]
        [Tooltip("HP the hull loses each time air is pumped into it — over-pressurizing stresses the frame.")]
        [SerializeField, Min(0)] private int pressurizeSelfDamage = 25;

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
        [FoldoutGroup("Events")] public UnityEvent onCrushZoneEntered;         // dropped past rated depth
        [FoldoutGroup("Events")] public UnityEvent onCrushZoneExited;          // recovered above rated depth
        [FoldoutGroup("Events")] public UnityEvent<float> onReserveChanged;    // normalized 0..1
        [FoldoutGroup("Events")] public UnityEvent<int> onPressureDamage;      // HP lost to a strain tick
        [FoldoutGroup("Events")] public UnityEvent<float> onPressureBoosted;   // boost load units added

        // =====================
        // Public State
        // =====================

        /** Depth in meters, from the atom when assigned, else from transform Y. */
        public float Depth => currentDepth != null ? currentDepth.Value : Mathf.Max(0f, -transform.position.y);

        public float Integrity => Sub?.Health != null ? Sub.Health.HealthPercent : 1f;
        public float StrengthMod => Sub?.Upgrades?.Stats.Resolve(SubStats.HullStrength, hullStrength) ?? hullStrength;
        public float PressureMultMod => Sub?.Upgrades?.Stats.Resolve(SubStats.PressureLoadMult, 1f) ?? 1f;
        public float ImpactMultMod => Sub?.Upgrades?.Stats.Resolve(SubStats.ImpactLoadMult, 1f) ?? 1f;

        /** Impact absorption — degrades with integrity but never below the floor. */
        public float HullResistance => StrengthMod * Mathf.Lerp(integrityFloor, 1f, Integrity);

        /** Net pressure squeezing the hull: depth load minus pumped-in counter-pressure. */
        public float PressureLoad => Mathf.Max(0f, Depth * pressurePerMeter * PressureMultMod - PressureBoost);

        /** Pressure load at exactly rated depth — the threshold where strain begins. */
        public float RatedPressureLoad => StrengthMod * ratedDepthSafetyFactor;

        public float StructuralReserve => Mathf.Max(0f, HullResistance - PressureLoad);

        /** Reserve as a fraction of full resistance — HUD bar + creak band driver. */
        public float ReserveFraction => HullResistance > 0f ? StructuralReserve / HullResistance : 0f;

        /**
         * Depth where pressure damage begins. FIXED by loadout (strength, upgrades,
         * active boost) — never reduced by damage taken, so ascending always works.
         */
        public float RatedDepth => (RatedPressureLoad + PressureBoost) / (pressurePerMeter * PressureMultMod);

        /**
         * Fastest collision the hull absorbs for FREE right now — the speed where
         * impact load exactly fills the structural reserve. Example: reserve 60,
         * impactLoadScale 12 → hits under 5 m/s deal nothing. Shrinks with depth
         * and with damage taken; the player-facing "how careful must I be" number.
         */
        public float SafeImpactSpeed
        {
            get { float scale = impactLoadScale * ImpactMultMod; return scale > 0f ? StructuralReserve / scale : 0f; }
        }

        /** How much of the rated pressure budget depth currently consumes (0 surface → 1 at rated depth). */
        public float PressureLoadFraction => RatedPressureLoad > 0f ? Mathf.Clamp01(PressureLoad / RatedPressureLoad) : 0f;

        /** Current enemy-damage multiplier: ×1 at the surface → ×maxAttackVulnerability at rated depth. */
        public float AttackDamageMult => Mathf.Lerp(1f, maxAttackVulnerability, PressureLoadFraction);

        /** True while past rated depth and accruing pressure strain. */
        public bool InCrushZone { get; private set; }

        /** Seconds of accumulated over-depth strain (drives the damage ramp). */
        public float StrainTime { get; private set; }

        /** Strain normalized against the ramp curve's full duration — HUD gauge driver. */
        public float StrainFraction => _rampDuration > 0f ? Mathf.Clamp01(StrainTime / _rampDuration) : 0f;

        /** Extra load units cancelled by pumped-in air (pump-to-hull). */
        public float PressureBoost { get; private set; }

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector] private float DbgDepth => Depth;
        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector] private float DbgRatedDepth => RatedDepth;
        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector] private float DbgReserve => StructuralReserve;
        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector] private float DbgStrainTime => StrainTime;
        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector] private float DbgPressureBoost => PressureBoost;
        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector, Tooltip("Collisions below this speed deal no damage right now.")]
        private float DbgSafeImpactSpeed => SafeImpactSpeed;
        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector, Tooltip("Enemy hits are multiplied by this at the current depth.")]
        private float DbgAttackDamageMult => AttackDamageMult;

        // =====================
        // Internals
        // =====================

        private float _cascadeAccumulator;   // fractional damage carried between frames
        private int _creakBandIndex;         // next creak threshold to cross
        private float _lastReserveFraction = 1f;
        private float _rampDuration;         // cached last-key time of strainRampCurve

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();   // registers with the Submarine facade (Sub.Hull)
            CacheRampDuration();
        }

        private void OnValidate()
        {
            CacheRampDuration();
        }

        /** Caches the ramp curve's horizontal extent for StrainFraction normalization. */
        private void CacheRampDuration()
        {
            _rampDuration = strainRampCurve != null && strainRampCurve.length > 0
                ? strainRampCurve.keys[strainRampCurve.length - 1].time : 0f;
        }

        private void Update()
        {
            UpdatePressureBoost();
            UpdateStrain();
            UpdateCreakBands();
        }

        // -------------------------------------------------------
        // Impacts & attacks
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

        /**
         * Scales an enemy attack by how pressure-loaded the hull is: ×1 at the
         * surface, ×maxAttackVulnerability at (or past) rated depth. Based purely
         * on depth — not integrity — so being hurt never amplifies further hits.
         *
         * Example: 5 damage, maxAttackVulnerability 2, at 50% of rated depth →
         * ceil(5 × 1.5) = 8 damage.
         */
        public int EvaluateAttack(int baseDamage)
        {
            return Mathf.CeilToInt(baseDamage * AttackDamageMult);
        }

        // -------------------------------------------------------
        // Pressure boost (pump-to-hull)
        // -------------------------------------------------------

        /**
         * Adds internal counter-pressure (load units), directly cancelling that
         * much pressure load — effectively pushing rated depth deeper until the
         * boost vents away. Fired by the manual pump's Hull destination.
         */
        public void AddPressureBoost(float loadUnits)
        {
            if (loadUnits <= 0f) return;
            PressureBoost += loadUnits;
            onPressureBoosted?.Invoke(loadUnits);
            Sub?.Feedbacks?.Play(SubFeedbacks.HullPressurize, transform.position, Mathf.Clamp01(loadUnits / RatedPressureLoad));
        }

        /**
         * The manual pump's Hull destination: converts pumped O2 into counter-
         * pressure, at the cost of structural stress (pressurizeSelfDamage HP).
         * Only reachable via a sweet-spot pump — see ManualBellowsPump/O2Pickup
         * routing — so the boost always represents a deliberate, well-timed act.
         */
        public void PumpAirIntoHull(float o2Amount)
        {
            if (o2Amount <= 0f) return;
            AddPressureBoost(o2Amount * boostPerAirUnit);
            if (pressurizeSelfDamage > 0) Sub?.Health?.TakeDamage(pressurizeSelfDamage);
        }

        /** Vents the boost at boostDecayPerSecond so pumped depth headroom is temporary. */
        private void UpdatePressureBoost()
        {
            if (PressureBoost <= 0f) return;
            PressureBoost = Mathf.Max(0f, PressureBoost - boostDecayPerSecond * Time.deltaTime);
        }

        // -------------------------------------------------------
        // Pressure strain
        // -------------------------------------------------------

        /**
         * Past rated depth, strain time accrues and Health bleeds at
         * excess × cascadeDamageRate × strainRampCurve(strainTime) — a gentle
         * trickle at first, accelerating the longer the sub loiters. Above rated
         * depth strain recovers at strainRecoveryRate, so damage always stops
         * (and the ramp rewinds) once the player climbs back.
         */
        private void UpdateStrain()
        {
            float excess = PressureLoad - RatedPressureLoad;
            bool inZone = excess > 0f && !(Sub?.Health?.IsDead ?? false);

            // Edge-trigger the zone state + looping feedback
            if (inZone != InCrushZone)
            {
                InCrushZone = inZone;
                if (inZone) { onCrushZoneEntered?.Invoke(); Sub?.Feedbacks?.Play(SubFeedbacks.CrushZone, transform.position); }
                else        { onCrushZoneExited?.Invoke();  Sub?.Feedbacks?.Stop(SubFeedbacks.CrushZone); }
            }

            // Recover strain while safe, then nothing more to do
            if (!inZone)
            {
                StrainTime = Mathf.Max(0f, StrainTime - strainRecoveryRate * Time.deltaTime);
                return;
            }

            // Build strain and bleed ramped damage; accumulate fractions so low rates still bite
            StrainTime += Time.deltaTime;
            float rampMult = strainRampCurve != null ? Mathf.Max(0f, strainRampCurve.Evaluate(StrainTime)) : 1f;
            _cascadeAccumulator += excess * cascadeDamageRate * rampMult * Time.deltaTime;
            if (_cascadeAccumulator >= 1f)
            {
                int damage = Mathf.FloorToInt(_cascadeAccumulator);
                _cascadeAccumulator -= damage;
                Sub?.Health?.TakeDamage(damage);
                onPressureDamage?.Invoke(damage);
                Sub?.Feedbacks?.Play(SubFeedbacks.PressureDamage, transform.position, StrainFraction);
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

        // -------------------------------------------------------
        // Shared formula (hub mirror)
        // -------------------------------------------------------

        /**
         * Rated depth from raw inputs — the ONE formula, shared with HubStats so
         * the hub's projection can't drift from live gameplay. Boost excluded:
         * the hub shows the unpumped baseline.
         */
        public static float ComputeRatedDepth(float strength, float pressurePerMeter, float safetyFactor, float pressureMult)
        {
            return strength * safetyFactor / (pressurePerMeter * Mathf.Max(0.01f, pressureMult));
        }
    }
}
