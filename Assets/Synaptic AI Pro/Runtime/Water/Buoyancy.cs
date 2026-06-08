using UnityEngine;

namespace Synaptic.Water
{
    /// <summary>
    /// Applies buoyancy forces to make objects float on the ocean
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Buoyancy : MonoBehaviour
    {
        [Header("Buoyancy Settings")]
        [Tooltip("Reference to the ocean system")]
        public OceanSystem ocean;

        [Tooltip("Buoyancy force multiplier")]
        public float buoyancyForce = 10f;

        [Tooltip("Water drag coefficient")]
        public float waterDrag = 1f;

        [Tooltip("Angular water drag")]
        public float waterAngularDrag = 0.5f;

        [Header("Float Points")]
        [Tooltip("Points where buoyancy is sampled. If empty, uses object center.")]
        public Transform[] floatPoints;

        [Header("Wave Response")]
        [Tooltip("How much the object responds to wave normal")]
        public float waveAlignmentStrength = 0.5f;

        [Tooltip("Maximum rotation speed when aligning to waves")]
        public float maxAlignmentTorque = 5f;

        private Rigidbody rb;
        private float originalDrag;
        private float originalAngularDrag;
        private bool isInWater;

        void Start()
        {
            rb = GetComponent<Rigidbody>();
            originalDrag = rb.linearDamping;
            originalAngularDrag = rb.angularDamping;

            // Auto-find ocean if not set
            if (ocean == null)
                ocean = FindFirstObjectByType<OceanSystem>();

            // Create default float points if none specified
            if (floatPoints == null || floatPoints.Length == 0)
            {
                floatPoints = new Transform[] { transform };
            }
        }

        void FixedUpdate()
        {
            if (ocean == null) return;

            float submergedAmount = 0f;
            Vector3 totalForce = Vector3.zero;
            Vector3 averageWaveNormal = Vector3.zero;
            int submergedPoints = 0;

            foreach (Transform point in floatPoints)
            {
                if (point == null) continue;

                Vector3 pointPos = point.position;
                float waterHeight = ocean.GetWaveHeight(pointPos);
                float depth = waterHeight - pointPos.y;

                if (depth > 0)
                {
                    // Point is underwater
                    submergedPoints++;
                    submergedAmount += Mathf.Clamp01(depth);

                    // Buoyancy force proportional to submersion depth
                    float forceMagnitude = buoyancyForce * Mathf.Clamp01(depth) * Physics.gravity.magnitude;
                    Vector3 force = Vector3.up * forceMagnitude;

                    // Apply force at float point position
                    rb.AddForceAtPosition(force, pointPos, ForceMode.Force);
                    totalForce += force;

                    // Sample wave normal
                    averageWaveNormal += ocean.GetWaveNormal(pointPos);
                }
            }

            // Update water state
            bool wasInWater = isInWater;
            isInWater = submergedPoints > 0;

            // Apply water drag when in water
            if (isInWater)
            {
                float normalizedSubmersion = (float)submergedPoints / floatPoints.Length;
                rb.linearDamping = Mathf.Lerp(originalDrag, waterDrag, normalizedSubmersion);
                rb.angularDamping = Mathf.Lerp(originalAngularDrag, waterAngularDrag, normalizedSubmersion);

                // Align to wave normal
                if (waveAlignmentStrength > 0 && submergedPoints > 0)
                {
                    averageWaveNormal = (averageWaveNormal / submergedPoints).normalized;
                    Vector3 currentUp = transform.up;
                    Vector3 targetUp = Vector3.Lerp(currentUp, averageWaveNormal, waveAlignmentStrength);

                    Quaternion targetRotation = Quaternion.FromToRotation(currentUp, targetUp) * transform.rotation;
                    Vector3 torque = CalculateAlignmentTorque(transform.rotation, targetRotation);
                    rb.AddTorque(Vector3.ClampMagnitude(torque, maxAlignmentTorque), ForceMode.Force);
                }
            }
            else
            {
                rb.linearDamping = originalDrag;
                rb.angularDamping = originalAngularDrag;
            }

            // Water entry/exit events
            if (isInWater && !wasInWater)
            {
                OnWaterEnter();
            }
            else if (!isInWater && wasInWater)
            {
                OnWaterExit();
            }
        }

        Vector3 CalculateAlignmentTorque(Quaternion current, Quaternion target)
        {
            Quaternion delta = target * Quaternion.Inverse(current);
            delta.ToAngleAxis(out float angle, out Vector3 axis);

            if (angle > 180f) angle -= 360f;

            return axis * (angle * Mathf.Deg2Rad);
        }

        protected virtual void OnWaterEnter()
        {
            // Override for splash effects, sounds, etc.
        }

        protected virtual void OnWaterExit()
        {
            // Override for exit effects
        }

        /// <summary>
        /// Check if object is currently in water
        /// </summary>
        public bool IsInWater => isInWater;

        /// <summary>
        /// Get current water height at object position
        /// </summary>
        public float GetWaterHeightAtPosition()
        {
            if (ocean == null) return 0f;
            return ocean.GetWaveHeight(transform.position);
        }

        void OnDrawGizmosSelected()
        {
            if (floatPoints == null) return;

            Gizmos.color = Color.cyan;
            foreach (Transform point in floatPoints)
            {
                if (point != null)
                {
                    Gizmos.DrawWireSphere(point.position, 0.2f);
                }
            }
        }
    }
}
