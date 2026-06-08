using UnityEngine;

namespace SynapticPro
{
    /// <summary>
    /// Makes a sky sphere follow the main camera position.
    /// Used for landscape photo skybox effect.
    /// </summary>
    [AddComponentMenu("Synaptic Pro/Sky Sphere Camera Follow")]
    public class SkySphereCameraFollow : MonoBehaviour
    {
        [Tooltip("Target camera to follow. If null, uses Camera.main")]
        public Camera targetCamera;

        [Tooltip("Offset from camera position")]
        public Vector3 offset = Vector3.zero;

        [Tooltip("Enable rotation sync with camera")]
        public bool syncRotation = false;

        [Tooltip("Only sync Y-axis rotation")]
        public bool yAxisOnly = true;

        private Transform _cameraTransform;

        public void Initialize()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            if (targetCamera != null)
            {
                _cameraTransform = targetCamera.transform;
            }
        }

        private void Start()
        {
            Initialize();
        }

        private void LateUpdate()
        {
            if (_cameraTransform == null)
            {
                if (targetCamera != null)
                {
                    _cameraTransform = targetCamera.transform;
                }
                else
                {
                    targetCamera = Camera.main;
                    if (targetCamera != null)
                    {
                        _cameraTransform = targetCamera.transform;
                    }
                }

                if (_cameraTransform == null) return;
            }

            // Follow camera position
            transform.position = _cameraTransform.position + offset;

            // Optionally sync rotation
            if (syncRotation)
            {
                if (yAxisOnly)
                {
                    var euler = transform.eulerAngles;
                    euler.y = _cameraTransform.eulerAngles.y;
                    transform.eulerAngles = euler;
                }
                else
                {
                    transform.rotation = _cameraTransform.rotation;
                }
            }
        }

        /// <summary>
        /// Set the rotation offset of the sky sphere
        /// </summary>
        public void SetRotation(float yRotation)
        {
            transform.rotation = Quaternion.Euler(0, yRotation, 0);
        }

        /// <summary>
        /// Set the scale (radius) of the sky sphere
        /// </summary>
        public void SetRadius(float radius)
        {
            transform.localScale = Vector3.one * radius;
        }
    }
}
