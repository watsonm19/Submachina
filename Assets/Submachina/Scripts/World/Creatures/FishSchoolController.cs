using System.Collections.Generic;
using Core.ProceduralAnimation;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Events;

namespace Submachina.Core
{
    /**
     * Ambient background boid school — pure atmosphere/production-value, not an enemy.
     * A cluster of small fish that swim in a loose flock, migrate slowly around their
     * spawn point, and scatter in a snappy dart-burst when a submarine gets close.
     *
     * Performance design: this is meant to be dropped into a level many times over,
     * so the whole simulation avoids per-fish MonoBehaviours entirely. Boid state
     * (position, velocity, depth, per-fish noise seed, dart timer) lives in flat
     * arrays on the controller and is stepped with a classic O(n^2) neighbor scan —
     * fine up to the 48-fish cap this component allows, and far cheaper than 48
     * separate Update() calls and component lookups would be. All arrays are
     * preallocated in Awake; steady-state Update() makes zero allocations.
     *
     * Visuals are decoupled from simulation: each boid drives a purely-visual child
     * (either a pre-built fish prefab carrying its own ChainSimulator/ChainStripRenderer
     * spine, or a simple fallback sprite) by writing its transform only. The
     * ChainSimulator on a fish prefab animates its own body wiggle automatically
     * from the transform's motion (FacingMode.Velocity) — the controller never
     * touches chain internals directly.
     */
    public class FishSchoolController : MonoBehaviour
    {
        // =====================
        // Spawn
        // =====================

        [FoldoutGroup("Spawn")]
        [Tooltip("Fish visual prefab — should already contain a configured ChainSimulator + ChainStripRenderer spine (FacingMode.Velocity). Instantiated as a child; the controller drives only its transform. If null, falls back to a plain sprite (or no visual at all).")]
        [SerializeField] private GameObject fishPrefab;

        [FoldoutGroup("Spawn")]
        [Tooltip("Sprite used for fallback fish when fishPrefab is not set. If this is also left empty, fish are simulated but have no visual representation.")]
        [SerializeField] private Sprite fallbackSprite;

        [FoldoutGroup("Spawn")]
        [Tooltip("Number of fish in the school. Simulation is O(n^2) per frame — 48 is a sane ceiling for a background flock.")]
        [SerializeField, Range(1, 48)] private int fishCount = 16;

        [FoldoutGroup("Spawn")]
        [Tooltip("Radius around this transform that fish are scattered into at spawn.")]
        [SerializeField, Min(0.1f)] private float spawnRadius = 3f;

        // =====================
        // Boids
        // =====================

        [FoldoutGroup("Boids")]
        [Tooltip("Neighbor sensing range for alignment and cohesion (world units).")]
        [SerializeField, Min(0.1f)] private float neighborRadius = 2f;

        [FoldoutGroup("Boids")]
        [Tooltip("Tighter range within which fish actively push apart to avoid overlap (world units).")]
        [SerializeField, Min(0.05f)] private float separationRadius = 0.6f;

        [FoldoutGroup("Boids")]
        [Tooltip("Strength of the push-apart force from close neighbors.")]
        [SerializeField, Min(0f)] private float separationWeight = 4f;

        [FoldoutGroup("Boids")]
        [Tooltip("Strength of steering toward the average heading of nearby fish.")]
        [SerializeField, Min(0f)] private float alignmentWeight = 1.5f;

        [FoldoutGroup("Boids")]
        [Tooltip("Strength of steering toward the average position of nearby fish (flock centering).")]
        [SerializeField, Min(0f)] private float cohesionWeight = 1f;

        // =====================
        // Movement
        // =====================

        [FoldoutGroup("Movement")]
        [Tooltip("Cruise top speed (world units/sec) while not fleeing.")]
        [SerializeField, Min(0.1f)] private float maxSpeed = 3f;

        [FoldoutGroup("Movement")]
        [Tooltip("Floor speed — fish never fully stop, keeps the school visibly alive.")]
        [SerializeField, Min(0f)] private float minSpeed = 0.8f;

        [FoldoutGroup("Movement")]
        [Tooltip("Maximum steering acceleration (world units/sec^2) — caps how sharply a fish can turn per frame.")]
        [SerializeField, Min(0.1f)] private float steerAccelLimit = 6f;

        [FoldoutGroup("Movement")]
        [Tooltip("Strength of per-fish Perlin-driven wander — small idle exploration so the school doesn't move as one rigid blob.")]
        [SerializeField, Min(0f)] private float wanderStrength = 0.5f;

        [FoldoutGroup("Movement")]
        [Tooltip("How fast each fish's wander heading drifts. Low values = lazy meandering.")]
        [SerializeField, Min(0f)] private float wanderNoiseSpeed = 0.3f;

        // =====================
        // Roaming & Containment
        // =====================

        [FoldoutGroup("Roaming & Containment")]
        [Tooltip("Fish beyond this distance from the current roam center are steered back in — keeps the school from wandering off screen.")]
        [SerializeField, Min(0.5f)] private float containmentRadius = 6f;

        [FoldoutGroup("Roaming & Containment")]
        [Tooltip("Strength of the pull back toward the roam center once containmentRadius is exceeded.")]
        [SerializeField, Min(0f)] private float containmentWeight = 2f;

        [FoldoutGroup("Roaming & Containment")]
        [Tooltip("Maximum distance the roam center itself is allowed to drift from the original spawn position — the whole school slowly migrates within this range.")]
        [SerializeField, Min(0f)] private float roamDriftRadius = 4f;

        [FoldoutGroup("Roaming & Containment")]
        [Tooltip("How fast the roam center's Perlin drift evolves. Low values = slow oceanic migration.")]
        [SerializeField, Min(0f)] private float roamDriftSpeed = 0.05f;

        // =====================
        // Flee
        // =====================

        [FoldoutGroup("Flee")]
        [Tooltip("Submarines within this range of a fish trigger a flee response (strong push away + speed burst).")]
        [SerializeField, Min(0.1f)] private float fleeRadius = 4f;

        [FoldoutGroup("Flee")]
        [Tooltip("Strength of the push-away force from a nearby submarine. Deliberately strong — this is the school-scatter 'wow moment'.")]
        [SerializeField, Min(0f)] private float fleeWeight = 10f;

        [FoldoutGroup("Flee")]
        [Tooltip("Speed multiplier applied to maxSpeed while a fish's dart burst is active.")]
        [SerializeField, Min(1f)] private float dartSpeedMultiplier = 2.5f;

        [FoldoutGroup("Flee")]
        [Tooltip("How long the dart burst takes to decay back to normal speed after the last scare (seconds). Kept short so the scatter reads as a snap, not a fade.")]
        [SerializeField, Min(0.05f)] private float dartDecayDuration = 0.6f;

        [FoldoutGroup("Flee")]
        [Tooltip("Fired once when the school transitions from calm to fleeing — wire feedbacks/audio/particles here.")]
        public UnityEvent onSchoolScatter;

        // =====================
        // Depth (2.5D illusion)
        // =====================

        [FoldoutGroup("Depth (2.5D)")]
        [Tooltip("Tint blended in for fish at maximum simulated far-depth. Near fish stay untinted (white).")]
        [SerializeField] private Color backTint = new Color(0.55f, 0.68f, 0.78f, 1f);

        [FoldoutGroup("Depth (2.5D)")]
        [Tooltip("Sorting-order spread (+/-) applied across the near/far depth range, added on top of each fish visual's own base sorting order.")]
        [SerializeField, Min(0)] private int depthSortingRange = 10;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float DebugAverageSpeed => AverageSpeed;

        // =====================
        // Public read-only state
        // =====================

        /** Number of fish simulated by this school. Fixed at spawn (Awake). */
        public int FishCount => _fishCount;

        /** Current average speed across all fish — handy for debugging/tuning in the inspector. */
        public float AverageSpeed { get; private set; }

        // =====================
        // Runtime state (flat arrays — no per-fish objects)
        // =====================

        private int _fishCount;
        private Vector2[] _positions;
        private Vector2[] _velocities;
        private Vector2[] _accelerations;
        private float[] _depths;          // pseudo-depth z in [-0.5, 0.5], purely visual
        private float[] _wanderSeeds;
        private float[] _dartTimers;       // counts down from dartDecayDuration when fleeing
        private int[] _baseSortingOrders;

        private Transform[] _fishTransforms;
        private Vector3[] _baseScales;
        private ChainSimulator[] _chainSimulators;
        private ChainStripRenderer[] _stripRenderers; // present only for prefab-based fish
        private SpriteRenderer[] _spriteRenderers;    // present only for fallback fish

        private Vector2 _spawnPosition;
        private Vector2 _roamCenter;
        private float _roamSeed;
        private bool _wasFleeing;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            _spawnPosition = transform.position;
            _roamCenter = _spawnPosition;
            _roamSeed = Random.value * 1000f;

            AllocateBuffers();
            SpawnFish();
        }

        private void OnEnable()
        {
            // Re-snap visuals to the last simulated positions and reset their chains so
            // re-enabling after culling doesn't whip the body across the screen.
            if (_fishTransforms == null) return;

            for (int i = 0; i < _fishCount; i++)
            {
                Transform t = _fishTransforms[i];
                if (t == null) continue;

                t.position = new Vector3(_positions[i].x, _positions[i].y, 0f);
                if (_chainSimulators[i] != null) _chainSimulators[i].SnapToAnchor();
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            UpdateRoamCenter(dt);
            SimulateAndApply(dt);
        }

        // -------------------------------------------------------
        // Setup
        // -------------------------------------------------------

        /** Sizes every flat array once so steady-state Update() never allocates. */
        private void AllocateBuffers()
        {
            _fishCount = fishCount;

            _positions = new Vector2[_fishCount];
            _velocities = new Vector2[_fishCount];
            _accelerations = new Vector2[_fishCount];
            _depths = new float[_fishCount];
            _wanderSeeds = new float[_fishCount];
            _dartTimers = new float[_fishCount];
            _baseSortingOrders = new int[_fishCount];

            _fishTransforms = new Transform[_fishCount];
            _baseScales = new Vector3[_fishCount];
            _chainSimulators = new ChainSimulator[_fishCount];
            _stripRenderers = new ChainStripRenderer[_fishCount];
            _spriteRenderers = new SpriteRenderer[_fishCount];
        }

        /**
         * Scatters fish around the spawn point and builds one visual child per fish
         * (prefab instance, fallback sprite, or none). Initial velocities point in a
         * random direction at minSpeed so orientation is well-defined from frame one.
         */
        private void SpawnFish()
        {
            for (int i = 0; i < _fishCount; i++)
            {
                _positions[i] = _spawnPosition + Random.insideUnitCircle * spawnRadius;

                Vector2 randomHeading = Random.insideUnitCircle.normalized;
                _velocities[i] = randomHeading * minSpeed;

                _depths[i] = Random.Range(-0.5f, 0.5f);
                _wanderSeeds[i] = Random.value * 1000f;
                _dartTimers[i] = 0f;

                BuildFishVisual(i);
            }
        }

        /** Instantiates the visual for one fish: prefab spine, fallback sprite, or nothing. */
        private void BuildFishVisual(int i)
        {
            if (fishPrefab != null)
            {
                GameObject instance = Instantiate(fishPrefab, transform);
                instance.transform.position = new Vector3(_positions[i].x, _positions[i].y, 0f);

                _fishTransforms[i] = instance.transform;
                _baseScales[i] = instance.transform.localScale;
                _chainSimulators[i] = instance.GetComponentInChildren<ChainSimulator>();
                _stripRenderers[i] = instance.GetComponentInChildren<ChainStripRenderer>();

                // Preserve whatever sorting order the prefab's mesh renderer was authored
                // with; depth only offsets from there.
                _baseSortingOrders[i] = _stripRenderers[i] != null && _stripRenderers[i].Renderer != null
                    ? _stripRenderers[i].Renderer.sortingOrder
                    : 0;

                // Depth tint via the strip's property-block channel — constant per fish,
                // so it's applied once here rather than every frame.
                if (_stripRenderers[i] != null)
                    _stripRenderers[i].SetTint(Color.Lerp(Color.white, backTint, _depths[i] + 0.5f));
                return;
            }

            if (fallbackSprite == null) return; // no prefab, no sprite: simulate only, no visual

            var go = new GameObject("Fish (fallback)");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(_positions[i].x, _positions[i].y, 0f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = fallbackSprite;

            _fishTransforms[i] = go.transform;
            _baseScales[i] = go.transform.localScale;
            _spriteRenderers[i] = sr;
            _baseSortingOrders[i] = sr.sortingOrder;
        }

        // -------------------------------------------------------
        // Simulation
        // -------------------------------------------------------

        /**
         * Drifts the roam center slowly away from the spawn position using two
         * decorrelated Perlin channels, clamped to roamDriftRadius. This is what
         * makes the whole school migrate over time instead of orbiting a fixed point.
         */
        private void UpdateRoamCenter(float dt)
        {
            float t = Time.time * roamDriftSpeed;
            float nx = Mathf.PerlinNoise(t + _roamSeed, 0.37f) - 0.5f;
            float ny = Mathf.PerlinNoise(0.91f, t + _roamSeed + 42f) - 0.5f;

            // e.g. roamDriftRadius 4: the center can wander up to 4 units from spawn
            // in any direction, easing back and forth as the noise channels evolve.
            _roamCenter = _spawnPosition + new Vector2(nx, ny) * (roamDriftRadius * 2f);
        }

        /**
         * Full boid step: accumulate steering forces for every fish from the current
         * (unmodified) state, then integrate velocity/position and push the result to
         * each fish's visual transform. Two passes keep neighbor forces order-independent
         * — a fish's force never depends on another fish that already moved this frame.
         */
        private void SimulateAndApply(float dt)
        {
            float neighborSqr = neighborRadius * neighborRadius;
            float separationSqr = separationRadius * separationRadius;
            float fleeSqr = fleeRadius * fleeRadius;
            List<Submarine> subs = Submarine.All;
            bool anyFleeingThisFrame = false;

            // ---- Pass 1: accumulate steering forces per fish ----
            for (int j = 0; j < _fishCount; j++)
            {
                Vector2 posJ = _positions[j];
                Vector2 separation = Vector2.zero;
                Vector2 aliSum = Vector2.zero;
                Vector2 cohSum = Vector2.zero;
                int aliCount = 0;
                int cohCount = 0;

                // Neighbor scan — O(n) per fish, O(n^2) total, fine up to the 48-fish cap.
                for (int k = 0; k < _fishCount; k++)
                {
                    if (k == j) continue;

                    Vector2 offset = posJ - _positions[k];
                    float sqrDist = offset.sqrMagnitude;
                    if (sqrDist > neighborSqr || sqrDist < 0.0001f) continue;

                    aliSum += _velocities[k];
                    cohSum += _positions[k];
                    aliCount++;
                    cohCount++;

                    // Closer neighbors push harder (inverse-distance weighting).
                    if (sqrDist < separationSqr) separation += offset / sqrDist;
                }

                Vector2 alignment = aliCount > 0 ? (aliSum / aliCount - _velocities[j]) : Vector2.zero;
                Vector2 cohesion = cohCount > 0 ? (cohSum / cohCount - posJ) : Vector2.zero;

                Vector2 force = separation * separationWeight
                                + alignment * alignmentWeight
                                + cohesion * cohesionWeight;

                // ---- Containment: soft pull back once the fish strays past the roam radius ----
                Vector2 toCenter = _roamCenter - posJ;
                float distFromCenter = toCenter.magnitude;
                if (distFromCenter > containmentRadius)
                    force += toCenter.normalized * (containmentWeight * (distFromCenter - containmentRadius));

                // ---- Wander: slow per-fish Perlin heading so the school isn't a rigid blob ----
                if (wanderStrength > 0f)
                {
                    float noise = Mathf.PerlinNoise(_wanderSeeds[j], Time.time * wanderNoiseSpeed);
                    float angle = noise * Mathf.PI * 4f;
                    force += new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * wanderStrength;
                }

                // ---- Flee: any nearby submarine scatters this fish and starts its dart burst ----
                for (int s = 0; s < subs.Count; s++)
                {
                    Submarine sub = subs[s];
                    if (sub == null) continue;

                    Vector2 offset = posJ - (Vector2)sub.transform.position;
                    float sqrDist = offset.sqrMagnitude;
                    if (sqrDist > fleeSqr || sqrDist < 0.0001f) continue;

                    // Falloff so subs right on top of a fish scare it harder than ones at the edge of fleeRadius.
                    float dist = Mathf.Sqrt(sqrDist);
                    float falloff = 1f - dist / fleeRadius;
                    force += offset.normalized * (fleeWeight * falloff);

                    _dartTimers[j] = dartDecayDuration; // snappy re-trigger, not additive
                    anyFleeingThisFrame = true;
                }

                _accelerations[j] = Vector2.ClampMagnitude(force, steerAccelLimit);
            }

            // ---- Fire the scatter event once, on the calm -> fleeing transition ----
            if (anyFleeingThisFrame && !_wasFleeing) onSchoolScatter?.Invoke();
            _wasFleeing = anyFleeingThisFrame;

            // ---- Pass 2: integrate velocity/position and push to visuals ----
            float speedSum = 0f;
            for (int i = 0; i < _fishCount; i++)
            {
                _velocities[i] += _accelerations[i] * dt;

                // Dart timer decays linearly; a fish's speed ceiling eases back down from
                // the dart multiplier to normal maxSpeed over dartDecayDuration seconds.
                _dartTimers[i] = Mathf.Max(0f, _dartTimers[i] - dt);
                float dartT = dartDecayDuration > 0f ? _dartTimers[i] / dartDecayDuration : 0f;
                float effectiveMax = Mathf.Lerp(maxSpeed, maxSpeed * dartSpeedMultiplier, dartT);

                float speed = _velocities[i].magnitude;
                if (speed > effectiveMax) _velocities[i] = _velocities[i] / speed * effectiveMax;
                else if (speed < minSpeed && speed > 0.0001f) _velocities[i] = _velocities[i] / speed * minSpeed;
                else if (speed <= 0.0001f) _velocities[i] = Vector2.right * minSpeed; // never fully stall

                _positions[i] += _velocities[i] * dt;
                speedSum += _velocities[i].magnitude;

                ApplyVisual(i);
            }

            AverageSpeed = _fishCount > 0 ? speedSum / _fishCount : 0f;
        }

        /**
         * Pushes one fish's simulated state to its visual child: position, facing
         * rotation (+X along velocity), depth-driven scale, and — where the public
         * API allows it — depth-driven sorting order and tint.
         */
        private void ApplyVisual(int i)
        {
            Transform t = _fishTransforms[i];
            if (t == null) return;

            Vector2 pos = _positions[i];
            Vector2 vel = _velocities[i];
            t.position = new Vector3(pos.x, pos.y, 0f);

            float angle = Mathf.Atan2(vel.y, vel.x) * Mathf.Rad2Deg;
            t.rotation = Quaternion.Euler(0f, 0f, angle);

            // e.g. depth 0.5 (far): scale x0.8 and sorted further back;
            // depth -0.5 (near): scale x1.2 and sorted further forward.
            float z = _depths[i];
            float scaleMul = 1f - z * 0.4f;
            t.localScale = _baseScales[i] * scaleMul;

            int sortingOffset = Mathf.RoundToInt(-z * depthSortingRange);

            if (_spriteRenderers[i] != null)
            {
                float tintT = z + 0.5f; // depth -0.5..0.5 -> tint 0..1
                _spriteRenderers[i].color = Color.Lerp(Color.white, backTint, tintT);
                _spriteRenderers[i].sortingOrder = _baseSortingOrders[i] + sortingOffset;
            }
            else if (_stripRenderers[i] != null && _stripRenderers[i].Renderer != null)
            {
                // Tint was applied once at spawn via SetTint (property block, survives
                // enable cycles); sorting is re-applied per frame because the strip's own
                // OnEnable re-stamps its serialized order after a cull-restore.
                _stripRenderers[i].Renderer.sortingOrder = _baseSortingOrders[i] + sortingOffset;
            }
        }

        // -------------------------------------------------------
        // Gizmos
        // -------------------------------------------------------

        private void OnDrawGizmosSelected()
        {
            Vector3 center = Application.isPlaying ? (Vector3)_roamCenter : transform.position;

            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireSphere(center, containmentRadius);

            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, fleeRadius);
        }
    }
}
