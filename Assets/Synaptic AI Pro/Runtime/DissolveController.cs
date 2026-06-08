using UnityEngine;
using System.Collections;

namespace SynapticPro
{
    /// <summary>
    /// Controller for Synaptic DissolvePro shader
    /// Provides easy animation control and particle system integration
    /// </summary>
    [ExecuteAlways]
    public class DissolveController : MonoBehaviour
    {
        [Header("Target")]
        public Renderer targetRenderer;
        public int materialIndex = 0;

        [Header("Dissolve Settings")]
        [Range(0f, 1f)]
        public float dissolveAmount = 0f;

        [Header("Direction")]
        public DissolveDirection direction = DissolveDirection.Up;
        public Vector3 customDirection = Vector3.up;
        public Transform directionSource;

        [Header("Animation")]
        public float animationDuration = 2f;
        public AnimationCurve dissolveCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        public bool autoReverse = false;
        public float reverseDelay = 0.5f;

        [Header("Particles")]
        public ParticleSystem dissolveParticles;
        public bool spawnParticlesOnEdge = true;
        public float particleEmissionRate = 50f;

        [Header("Audio")]
        public AudioSource dissolveAudio;
        public AudioClip dissolveSound;

        [Header("Events")]
        public UnityEngine.Events.UnityEvent onDissolveStart;
        public UnityEngine.Events.UnityEvent onDissolveComplete;
        public UnityEngine.Events.UnityEvent onAppearStart;
        public UnityEngine.Events.UnityEvent onAppearComplete;

        public enum DissolveDirection
        {
            Up,
            Down,
            Left,
            Right,
            Forward,
            Back,
            Spherical,
            Custom
        }

        private Material material;
        private Coroutine animationCoroutine;
        private bool isAnimating = false;

        // Shader property IDs
        private static readonly int DissolveAmountID = Shader.PropertyToID("_DissolveAmount");
        private static readonly int DissolveDirectionID = Shader.PropertyToID("_DissolveDirection");
        private static readonly int DirectionalDissolveID = Shader.PropertyToID("_DirectionalDissolve");

        private void OnEnable()
        {
            SetupMaterial();
        }

        private void SetupMaterial()
        {
            if (targetRenderer == null)
                targetRenderer = GetComponent<Renderer>();

            if (targetRenderer != null && targetRenderer.sharedMaterials.Length > materialIndex)
            {
                // Use instance material in play mode, shared in edit mode
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

        private void Update()
        {
            if (material == null)
                return;

            // Update dissolve amount
            material.SetFloat(DissolveAmountID, dissolveAmount);

            // Update direction
            Vector3 dir = GetDissolveDirection();
            material.SetVector(DissolveDirectionID, new Vector4(dir.x, dir.y, dir.z, 0));
            material.SetFloat(DirectionalDissolveID, direction == DissolveDirection.Spherical ? 0f : 1f);

            // Update particles
            UpdateParticles();
        }

        private Vector3 GetDissolveDirection()
        {
            switch (direction)
            {
                case DissolveDirection.Up: return Vector3.up;
                case DissolveDirection.Down: return Vector3.down;
                case DissolveDirection.Left: return Vector3.left;
                case DissolveDirection.Right: return Vector3.right;
                case DissolveDirection.Forward: return Vector3.forward;
                case DissolveDirection.Back: return Vector3.back;
                case DissolveDirection.Spherical: return Vector3.zero;
                case DissolveDirection.Custom:
                    if (directionSource != null)
                        return (directionSource.position - transform.position).normalized;
                    return customDirection.normalized;
                default: return Vector3.up;
            }
        }

        private void UpdateParticles()
        {
            if (dissolveParticles == null || !spawnParticlesOnEdge)
                return;

            var emission = dissolveParticles.emission;

            // Only emit particles while dissolving is active and in progress
            if (isAnimating && dissolveAmount > 0.01f && dissolveAmount < 0.99f)
            {
                emission.rateOverTime = particleEmissionRate;

                // Position particles at dissolve edge (approximate)
                Vector3 edgePosition = transform.position + GetDissolveDirection() * dissolveAmount * 2f;
                dissolveParticles.transform.position = edgePosition;
            }
            else
            {
                emission.rateOverTime = 0;
            }
        }

        /// <summary>
        /// Start dissolving the object
        /// </summary>
        public void Dissolve()
        {
            if (animationCoroutine != null)
                StopCoroutine(animationCoroutine);

            animationCoroutine = StartCoroutine(AnimateDissolve(0f, 1f, onDissolveStart, onDissolveComplete));
        }

        /// <summary>
        /// Make the object appear (reverse dissolve)
        /// </summary>
        public void Appear()
        {
            if (animationCoroutine != null)
                StopCoroutine(animationCoroutine);

            animationCoroutine = StartCoroutine(AnimateDissolve(1f, 0f, onAppearStart, onAppearComplete));
        }

        /// <summary>
        /// Toggle between dissolved and visible states
        /// </summary>
        public void Toggle()
        {
            if (dissolveAmount < 0.5f)
                Dissolve();
            else
                Appear();
        }

        /// <summary>
        /// Set dissolve amount instantly
        /// </summary>
        public void SetDissolveInstant(float amount)
        {
            if (animationCoroutine != null)
                StopCoroutine(animationCoroutine);

            dissolveAmount = Mathf.Clamp01(amount);
            isAnimating = false;
        }

        private IEnumerator AnimateDissolve(float from, float to,
            UnityEngine.Events.UnityEvent onStart, UnityEngine.Events.UnityEvent onComplete)
        {
            isAnimating = true;
            onStart?.Invoke();

            // Play audio
            if (dissolveAudio != null && dissolveSound != null)
            {
                dissolveAudio.clip = dissolveSound;
                dissolveAudio.Play();
            }

            float elapsed = 0f;
            dissolveAmount = from;

            while (elapsed < animationDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / animationDuration;
                float curveT = dissolveCurve.Evaluate(t);
                dissolveAmount = Mathf.Lerp(from, to, curveT);
                yield return null;
            }

            dissolveAmount = to;
            isAnimating = false;
            onComplete?.Invoke();

            // Auto reverse
            if (autoReverse)
            {
                yield return new WaitForSeconds(reverseDelay);

                if (to > 0.5f)
                    Appear();
                else
                    Dissolve();
            }
        }

        /// <summary>
        /// Trigger dissolve from damage or hit
        /// </summary>
        public void OnDamage(Vector3 hitPoint)
        {
            // Set direction from hit point
            direction = DissolveDirection.Custom;
            customDirection = (transform.position - hitPoint).normalized;

            Dissolve();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            SetupMaterial();
        }

        private void OnDrawGizmosSelected()
        {
            // Draw dissolve direction
            Gizmos.color = Color.cyan;
            Vector3 dir = GetDissolveDirection();
            if (dir.magnitude > 0.01f)
            {
                Gizmos.DrawRay(transform.position, dir * 2f);
                Gizmos.DrawWireSphere(transform.position + dir * dissolveAmount * 2f, 0.1f);
            }
        }
#endif
    }
}
