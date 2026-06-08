using UnityEngine;
using System.Collections.Generic;

namespace SynapticPro
{
    /// <summary>
    /// Controller for Synaptic ShieldPro shader
    /// Manages hit effects, ripples, and shield state
    /// </summary>
    [ExecuteAlways]
    public class ShieldController : MonoBehaviour
    {
        [System.Serializable]
        public class HitRipple
        {
            public Vector3 worldPosition;
            public float startTime;
            public float strength;
            public float speed;
            public bool active;
        }

        [Header("Target")]
        public Renderer targetRenderer;
        public int materialIndex = 0;

        [Header("Shield State")]
        public bool shieldActive = true;
        [Range(0f, 1f)]
        public float shieldStrength = 1f;
        [Range(0f, 1f)]
        public float shieldOpacity = 0.5f;

        [Header("Hit Ripples")]
        public int maxRipples = 4;
        public float rippleDuration = 1f;
        public float rippleSpeed = 5f;
        public float rippleStrength = 1f;

        [Header("Hit Flash")]
        public bool enableHitFlash = true;
        public Color hitFlashColor = new Color(1f, 0.8f, 0.5f, 1f);
        public float hitFlashDuration = 0.1f;
        public float hitFlashIntensity = 2f;

        [Header("Damage State")]
        public bool enableDamageState = true;
        [Range(0f, 1f)]
        public float damageLevel = 0f;
        public Color damagedColor = new Color(1f, 0.3f, 0.2f, 1f);
        public float damageFlickerSpeed = 10f;
        public float damageFlickerIntensity = 0.3f;

        [Header("Break Effect")]
        public float breakThreshold = 0.9f;
        public ParticleSystem breakParticles;
        public AudioSource breakAudio;
        public AudioClip breakSound;

        [Header("Regeneration")]
        public bool enableRegeneration = true;
        public float regenerationDelay = 3f;
        public float regenerationRate = 0.2f;

        [Header("Events")]
        public UnityEngine.Events.UnityEvent onShieldHit;
        public UnityEngine.Events.UnityEvent onShieldBreak;
        public UnityEngine.Events.UnityEvent onShieldRestore;

        private Material material;
        private List<HitRipple> ripples = new List<HitRipple>();
        private float lastHitTime;
        private float hitFlashTimer;
        private bool isFlashing;
        private bool isBroken;

        // Shader property IDs
        private static readonly int ShieldStrengthID = Shader.PropertyToID("_ShieldStrength");
        private static readonly int ShieldOpacityID = Shader.PropertyToID("_ShieldOpacity");
        private static readonly int HitPositionID = Shader.PropertyToID("_HitPosition");
        private static readonly int HitTimeID = Shader.PropertyToID("_HitTime");
        private static readonly int RippleSpeedID = Shader.PropertyToID("_RippleSpeed");
        private static readonly int RippleStrengthID = Shader.PropertyToID("_RippleStrength");
        private static readonly int DamageLevelID = Shader.PropertyToID("_DamageLevel");
        private static readonly int HitFlashColorID = Shader.PropertyToID("_HitFlashColor");
        private static readonly int HitFlashIntensityID = Shader.PropertyToID("_HitFlashIntensity");

        // For multiple ripples
        private static readonly int RipplePositions1ID = Shader.PropertyToID("_RipplePosition1");
        private static readonly int RipplePositions2ID = Shader.PropertyToID("_RipplePosition2");
        private static readonly int RipplePositions3ID = Shader.PropertyToID("_RipplePosition3");
        private static readonly int RipplePositions4ID = Shader.PropertyToID("_RipplePosition4");
        private static readonly int RippleTimes1ID = Shader.PropertyToID("_RippleTime1");
        private static readonly int RippleTimes2ID = Shader.PropertyToID("_RippleTime2");
        private static readonly int RippleTimes3ID = Shader.PropertyToID("_RippleTime3");
        private static readonly int RippleTimes4ID = Shader.PropertyToID("_RippleTime4");

        private void OnEnable()
        {
            SetupMaterial();
            InitializeRipples();
        }

        private void SetupMaterial()
        {
            if (targetRenderer == null)
                targetRenderer = GetComponent<Renderer>();

            if (targetRenderer != null && targetRenderer.sharedMaterials.Length > materialIndex)
            {
                if (Application.isPlaying)
                {
                    material = targetRenderer.materials[materialIndex];
                }
                else
                {
                    material = targetRenderer.sharedMaterials[materialIndex];
                }
            }
        }

        private void InitializeRipples()
        {
            ripples.Clear();
            for (int i = 0; i < maxRipples; i++)
            {
                ripples.Add(new HitRipple());
            }
        }

        private void Update()
        {
            if (material == null)
                return;

            // Update base shield properties
            material.SetFloat(ShieldStrengthID, shieldActive ? shieldStrength : 0f);
            material.SetFloat(ShieldOpacityID, shieldOpacity);

            // Update ripples
            UpdateRipples();

            // Update hit flash
            UpdateHitFlash();

            // Update damage state
            UpdateDamageState();

            // Handle regeneration
            UpdateRegeneration();
        }

        private void UpdateRipples()
        {
            float currentTime = Time.time;

            // Deactivate expired ripples
            foreach (var ripple in ripples)
            {
                if (ripple.active && currentTime - ripple.startTime > rippleDuration)
                {
                    ripple.active = false;
                }
            }

            // Send ripple data to shader
            for (int i = 0; i < Mathf.Min(4, ripples.Count); i++)
            {
                var ripple = ripples[i];
                Vector4 posStrength = ripple.active ?
                    new Vector4(ripple.worldPosition.x, ripple.worldPosition.y, ripple.worldPosition.z, ripple.strength) :
                    Vector4.zero;
                float time = ripple.active ? currentTime - ripple.startTime : -1f;

                switch (i)
                {
                    case 0:
                        material.SetVector(RipplePositions1ID, posStrength);
                        material.SetFloat(RippleTimes1ID, time);
                        break;
                    case 1:
                        material.SetVector(RipplePositions2ID, posStrength);
                        material.SetFloat(RippleTimes2ID, time);
                        break;
                    case 2:
                        material.SetVector(RipplePositions3ID, posStrength);
                        material.SetFloat(RippleTimes3ID, time);
                        break;
                    case 3:
                        material.SetVector(RipplePositions4ID, posStrength);
                        material.SetFloat(RippleTimes4ID, time);
                        break;
                }
            }

            material.SetFloat(RippleSpeedID, rippleSpeed);
            material.SetFloat(RippleStrengthID, rippleStrength);
        }

        private void UpdateHitFlash()
        {
            if (!enableHitFlash || !isFlashing)
                return;

            hitFlashTimer -= Time.deltaTime;

            if (hitFlashTimer <= 0)
            {
                isFlashing = false;
                material.SetFloat(HitFlashIntensityID, 0f);
            }
            else
            {
                float flashAmount = (hitFlashTimer / hitFlashDuration) * hitFlashIntensity;
                material.SetColor(HitFlashColorID, hitFlashColor);
                material.SetFloat(HitFlashIntensityID, flashAmount);
            }
        }

        private void UpdateDamageState()
        {
            if (!enableDamageState)
                return;

            material.SetFloat(DamageLevelID, damageLevel);

            // Flickering when damaged
            if (damageLevel > 0.5f)
            {
                float flicker = Mathf.Sin(Time.time * damageFlickerSpeed) * damageFlickerIntensity * damageLevel;
                material.SetFloat(ShieldOpacityID, shieldOpacity + flicker);
            }
        }

        private void UpdateRegeneration()
        {
            if (!enableRegeneration || !Application.isPlaying)
                return;

            if (isBroken)
            {
                // Wait for regeneration delay after break
                if (Time.time - lastHitTime > regenerationDelay)
                {
                    isBroken = false;
                    shieldActive = true;
                    damageLevel = 0.5f; // Start at half strength
                    onShieldRestore?.Invoke();
                }
            }
            else if (damageLevel > 0 && Time.time - lastHitTime > regenerationDelay * 0.5f)
            {
                // Gradual regeneration
                damageLevel = Mathf.Max(0, damageLevel - regenerationRate * Time.deltaTime);
            }
        }

        /// <summary>
        /// Register a hit on the shield
        /// </summary>
        public void OnHit(Vector3 worldPosition, float damage = 0.1f)
        {
            if (!shieldActive || isBroken)
                return;

            lastHitTime = Time.time;

            // Add ripple
            AddRipple(worldPosition);

            // Trigger flash
            if (enableHitFlash)
            {
                isFlashing = true;
                hitFlashTimer = hitFlashDuration;
            }

            // Apply damage
            if (enableDamageState)
            {
                damageLevel = Mathf.Min(1f, damageLevel + damage);

                // Check for break
                if (damageLevel >= breakThreshold)
                {
                    BreakShield();
                }
            }

            onShieldHit?.Invoke();
        }

        /// <summary>
        /// Add a ripple effect at the specified world position
        /// </summary>
        public void AddRipple(Vector3 worldPosition)
        {
            // Find inactive ripple or oldest ripple
            HitRipple targetRipple = null;
            float oldestTime = float.MaxValue;

            foreach (var ripple in ripples)
            {
                if (!ripple.active)
                {
                    targetRipple = ripple;
                    break;
                }
                else if (ripple.startTime < oldestTime)
                {
                    oldestTime = ripple.startTime;
                    targetRipple = ripple;
                }
            }

            if (targetRipple != null)
            {
                targetRipple.worldPosition = worldPosition;
                targetRipple.startTime = Time.time;
                targetRipple.strength = rippleStrength;
                targetRipple.speed = rippleSpeed;
                targetRipple.active = true;
            }
        }

        /// <summary>
        /// Break the shield
        /// </summary>
        public void BreakShield()
        {
            if (isBroken)
                return;

            isBroken = true;
            shieldActive = false;
            damageLevel = 1f;

            // Play break effects
            if (breakParticles != null)
            {
                breakParticles.transform.position = transform.position;
                breakParticles.Play();
            }

            if (breakAudio != null && breakSound != null)
            {
                breakAudio.clip = breakSound;
                breakAudio.Play();
            }

            onShieldBreak?.Invoke();
        }

        /// <summary>
        /// Instantly restore shield to full
        /// </summary>
        public void RestoreShield()
        {
            isBroken = false;
            shieldActive = true;
            damageLevel = 0f;
            onShieldRestore?.Invoke();
        }

        /// <summary>
        /// Set shield state
        /// </summary>
        public void SetShieldActive(bool active)
        {
            if (isBroken && active)
                RestoreShield();
            else
                shieldActive = active;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            SetupMaterial();
            InitializeRipples();
        }

        private void OnDrawGizmosSelected()
        {
            // Draw active ripples
            Gizmos.color = Color.cyan;
            foreach (var ripple in ripples)
            {
                if (ripple.active)
                {
                    float radius = (Time.time - ripple.startTime) * rippleSpeed;
                    Gizmos.DrawWireSphere(ripple.worldPosition, Mathf.Min(radius, 5f));
                }
            }
        }
#endif
    }
}
