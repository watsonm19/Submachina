using UnityEngine;
using System.Collections.Generic;

namespace Synaptic
{
    /// <summary>
    /// Water surface component - attach to water plane
    /// Provides water height queries and wave animation
    /// </summary>
    public class WaterSurface : MonoBehaviour
    {
        [Header("Wave Settings")]
        public float waveSpeed = 1f;
        public float waveStrength = 0.1f;
        public float waveFrequency = 1f;
        public Vector2 waveDirectionA = new Vector2(1, 0);
        public Vector2 waveDirectionB = new Vector2(0, 1);

        [Header("Physics")]
        public float waterDensity = 1000f; // kg/m³ (water = 1000)

        private static List<WaterSurface> activeSurfaces = new List<WaterSurface>();

        public static WaterSurface GetWaterSurfaceAt(Vector3 position)
        {
            foreach (var surface in activeSurfaces)
            {
                if (surface.IsPointAboveWater(position))
                {
                    return surface;
                }
            }
            return null;
        }

        void OnEnable()
        {
            if (!activeSurfaces.Contains(this))
            {
                activeSurfaces.Add(this);
            }
        }

        void OnDisable()
        {
            activeSurfaces.Remove(this);
        }

        /// <summary>
        /// Get water height at world position using Gerstner waves
        /// </summary>
        public float GetWaterHeight(Vector3 worldPosition)
        {
            float baseHeight = transform.position.y;
            float time = Time.time * waveSpeed;

            // Gerstner wave calculation
            float height = 0f;
            height += GerstnerWaveHeight(worldPosition, waveDirectionA, waveStrength, waveFrequency * 10f, time);
            height += GerstnerWaveHeight(worldPosition, waveDirectionB, waveStrength * 0.5f, waveFrequency * 7f, time * 1.3f);
            height += GerstnerWaveHeight(worldPosition, new Vector2(0.7f, 0.7f), waveStrength * 0.3f, waveFrequency * 5f, time * 0.8f);

            return baseHeight + height;
        }

        private float GerstnerWaveHeight(Vector3 position, Vector2 direction, float steepness, float wavelength, float time)
        {
            float k = 2f * Mathf.PI / wavelength;
            float c = Mathf.Sqrt(9.8f / k);
            Vector2 d = direction.normalized;
            float f = k * (Vector2.Dot(d, new Vector2(position.x, position.z)) - c * time);
            float a = steepness / k;

            return a * Mathf.Sin(f);
        }

        /// <summary>
        /// Check if a point is within the water area (XZ bounds)
        /// </summary>
        public bool IsPointAboveWater(Vector3 point)
        {
            // Simple bounds check using renderer bounds
            var renderer = GetComponent<Renderer>();
            if (renderer != null)
            {
                var bounds = renderer.bounds;
                return point.x >= bounds.min.x && point.x <= bounds.max.x &&
                       point.z >= bounds.min.z && point.z <= bounds.max.z;
            }

            // Fallback to transform scale
            Vector3 localPoint = transform.InverseTransformPoint(point);
            return Mathf.Abs(localPoint.x) <= 0.5f && Mathf.Abs(localPoint.z) <= 0.5f;
        }

        /// <summary>
        /// Get wave normal at position for physics calculations
        /// </summary>
        public Vector3 GetWaveNormal(Vector3 worldPosition)
        {
            float delta = 0.1f;
            float h0 = GetWaterHeight(worldPosition);
            float hx = GetWaterHeight(worldPosition + Vector3.right * delta);
            float hz = GetWaterHeight(worldPosition + Vector3.forward * delta);

            Vector3 tangentX = new Vector3(delta, hx - h0, 0).normalized;
            Vector3 tangentZ = new Vector3(0, hz - h0, delta).normalized;

            return Vector3.Cross(tangentZ, tangentX).normalized;
        }
    }

    /// <summary>
    /// Buoyancy component - makes objects float on water
    /// Attach to any Rigidbody that should float
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Buoyancy : MonoBehaviour
    {
        [Header("Buoyancy Settings")]
        [Tooltip("Points where buoyancy force is applied")]
        public Transform[] floatPoints;

        [Tooltip("How much the object floats (1 = neutrally buoyant)")]
        [Range(0f, 3f)]
        public float buoyancyStrength = 1.5f;

        [Tooltip("Underwater drag multiplier")]
        public float underwaterDrag = 3f;

        [Tooltip("Underwater angular drag multiplier")]
        public float underwaterAngularDrag = 1f;

        [Header("Effects")]
        public bool createSplashOnEnter = true;
        public GameObject splashPrefab;
        public float splashThreshold = 2f; // Minimum velocity to create splash

        private Rigidbody rb;
        private float originalDrag;
        private float originalAngularDrag;
        private bool wasUnderwater = false;
        private WaterSurface currentWater;

        void Start()
        {
            rb = GetComponent<Rigidbody>();
            originalDrag = rb.linearDamping;
            originalAngularDrag = rb.angularDamping;

            // Auto-generate float points if not set
            if (floatPoints == null || floatPoints.Length == 0)
            {
                GenerateFloatPoints();
            }
        }

        void GenerateFloatPoints()
        {
            var collider = GetComponent<Collider>();
            if (collider != null)
            {
                var bounds = collider.bounds;
                var points = new List<Transform>();

                // Create 4 corner points + center
                Vector3[] offsets = new Vector3[]
                {
                    new Vector3(-0.4f, -0.5f, -0.4f),
                    new Vector3(0.4f, -0.5f, -0.4f),
                    new Vector3(-0.4f, -0.5f, 0.4f),
                    new Vector3(0.4f, -0.5f, 0.4f),
                    new Vector3(0, -0.5f, 0)
                };

                foreach (var offset in offsets)
                {
                    var point = new GameObject("FloatPoint").transform;
                    point.parent = transform;
                    point.localPosition = Vector3.Scale(offset, bounds.size);
                    points.Add(point);
                }

                floatPoints = points.ToArray();
            }
        }

        void FixedUpdate()
        {
            currentWater = WaterSurface.GetWaterSurfaceAt(transform.position);
            if (currentWater == null)
            {
                // Reset drag when out of water
                rb.linearDamping = originalDrag;
                rb.angularDamping = originalAngularDrag;
                wasUnderwater = false;
                return;
            }

            bool isUnderwater = false;
            int underwaterPoints = 0;

            foreach (var point in floatPoints)
            {
                if (point == null) continue;

                float waterHeight = currentWater.GetWaterHeight(point.position);
                float depth = waterHeight - point.position.y;

                if (depth > 0)
                {
                    isUnderwater = true;
                    underwaterPoints++;

                    // Calculate buoyancy force
                    float displacementMultiplier = Mathf.Clamp01(depth / 0.5f);
                    float buoyancyForce = currentWater.waterDensity * Physics.gravity.magnitude * displacementMultiplier * buoyancyStrength;

                    // Apply force at float point
                    Vector3 force = Vector3.up * buoyancyForce / floatPoints.Length;
                    rb.AddForceAtPosition(force, point.position, ForceMode.Force);

                    // Add wave influence
                    Vector3 waveNormal = currentWater.GetWaveNormal(point.position);
                    rb.AddForceAtPosition(waveNormal * buoyancyForce * 0.1f, point.position, ForceMode.Force);
                }
            }

            // Apply underwater drag
            if (isUnderwater)
            {
                float submergedRatio = (float)underwaterPoints / floatPoints.Length;
                rb.linearDamping = Mathf.Lerp(originalDrag, underwaterDrag, submergedRatio);
                rb.angularDamping = Mathf.Lerp(originalAngularDrag, underwaterAngularDrag, submergedRatio);
            }
            else
            {
                rb.linearDamping = originalDrag;
                rb.angularDamping = originalAngularDrag;
            }

            // Splash effect on water entry
            if (createSplashOnEnter && isUnderwater && !wasUnderwater)
            {
                if (rb.linearVelocity.magnitude > splashThreshold)
                {
                    CreateSplash();
                }
            }

            wasUnderwater = isUnderwater;
        }

        void CreateSplash()
        {
            if (splashPrefab != null)
            {
                float waterHeight = currentWater.GetWaterHeight(transform.position);
                Vector3 splashPos = new Vector3(transform.position.x, waterHeight, transform.position.z);
                Instantiate(splashPrefab, splashPos, Quaternion.identity);
            }
            else
            {
                // Create simple particle splash
                float waterHeight = currentWater.GetWaterHeight(transform.position);
                Vector3 splashPos = new Vector3(transform.position.x, waterHeight, transform.position.z);

                var splashGO = new GameObject("Splash");
                splashGO.transform.position = splashPos;

                var ps = splashGO.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.startLifetime = 1f;
                main.startSpeed = 3f;
                main.startSize = 0.1f;
                main.startColor = new Color(0.8f, 0.9f, 1f, 0.7f);
                main.gravityModifier = 1f;
                main.maxParticles = 50;
                main.duration = 0.3f;
                main.loop = false;

                var emission = ps.emission;
                emission.rateOverTime = 0;
                emission.SetBurst(0, new ParticleSystem.Burst(0f, 30));

                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Hemisphere;
                shape.radius = 0.3f;

                ps.Play();
                Destroy(splashGO, 2f);
            }
        }

        void OnDrawGizmosSelected()
        {
            if (floatPoints == null) return;

            Gizmos.color = Color.cyan;
            foreach (var point in floatPoints)
            {
                if (point != null)
                {
                    Gizmos.DrawWireSphere(point.position, 0.1f);
                }
            }
        }
    }

    /// <summary>
    /// Water interaction trigger - creates ripples and splashes when objects enter
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class WaterInteraction : MonoBehaviour
    {
        [Header("Ripple Settings")]
        public bool createRipples = true;
        public float rippleInterval = 0.5f;
        public GameObject ripplePrefab;

        [Header("Splash Settings")]
        public bool createSplashes = true;
        public float minSplashVelocity = 1f;
        public GameObject splashPrefab;

        private float lastRippleTime;

        void OnTriggerEnter(Collider other)
        {
            if (!createSplashes) return;

            var rb = other.GetComponent<Rigidbody>();
            if (rb != null && rb.linearVelocity.magnitude > minSplashVelocity)
            {
                CreateSplashAt(other.ClosestPoint(transform.position), rb.linearVelocity.magnitude);
            }
        }

        void OnTriggerStay(Collider other)
        {
            if (!createRipples) return;
            if (Time.time - lastRippleTime < rippleInterval) return;

            var rb = other.GetComponent<Rigidbody>();
            if (rb != null && rb.linearVelocity.magnitude > 0.1f)
            {
                CreateRippleAt(other.ClosestPoint(transform.position));
                lastRippleTime = Time.time;
            }
        }

        void CreateSplashAt(Vector3 position, float intensity)
        {
            if (splashPrefab != null)
            {
                var splash = Instantiate(splashPrefab, position, Quaternion.identity);
                Destroy(splash, 3f);
            }
        }

        void CreateRippleAt(Vector3 position)
        {
            if (ripplePrefab != null)
            {
                var ripple = Instantiate(ripplePrefab, position, Quaternion.Euler(90, 0, 0));
                Destroy(ripple, 2f);
            }
        }
    }
}
