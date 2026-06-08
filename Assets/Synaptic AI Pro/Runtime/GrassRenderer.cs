using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SynapticPro
{
    /// <summary>
    /// GPU-based grass rendering system using Compute Shaders
    /// Supports LOD, frustum culling, and massive instance counts (1M+)
    /// </summary>
    [ExecuteAlways]
    public class GrassRenderer : MonoBehaviour
    {
        [System.Serializable]
        public struct GrassInstance
        {
            public Vector3 position;
            public Vector3 normal;
            public Vector2 uv;
            public float height;
            public float width;
            public float rotation;
            public float stiffness;
            public float windPhase;

            public static int Size => sizeof(float) * 14;
        }

        [Header("Grass Mesh")]
        public Mesh grassMesh;
        public Material grassMaterial;

        [Header("Terrain Source")]
        public Terrain sourceTerrain;
        public LayerMask groundLayers = -1;

        [Header("Density Settings")]
        [Range(0.1f, 5f)]
        public float density = 1f;
        [Range(0.5f, 3f)]
        public float heightMin = 0.5f;
        [Range(0.5f, 3f)]
        public float heightMax = 1.5f;
        [Range(0.1f, 1f)]
        public float widthMin = 0.3f;
        [Range(0.1f, 1f)]
        public float widthMax = 0.5f;

        [Header("Culling & LOD")]
        public float maxRenderDistance = 100f;
        public float lod0Distance = 20f;
        public float lod1Distance = 50f;
        public float lod2Distance = 80f;
        [Range(0f, 1f)]
        public float densityFalloff = 0.5f;
        public float frustumCullMargin = 2f;

        [Header("Wind")]
        public Vector3 windDirection = new Vector3(1, 0, 0.5f);
        [Range(0f, 2f)]
        public float windStrength = 0.5f;
        public float windSpeed = 1f;
        public float windFrequency = 1f;

        [Header("Compute Shader")]
        public ComputeShader grassCompute;

        // Buffers
        private ComputeBuffer sourceBuffer;
        private ComputeBuffer culledBuffer;
        private ComputeBuffer argsBuffer;
        private ComputeBuffer counterBuffer;

        // Instance data
        private GrassInstance[] instances;
        private int totalInstances;
        private int maxVisibleInstances;

        // Kernel IDs
        private int mainKernel;
        private int clearKernel;

        // Shader property IDs
        private static readonly int ViewProjectionMatrixID = Shader.PropertyToID("_ViewProjectionMatrix");
        private static readonly int ViewMatrixID = Shader.PropertyToID("_ViewMatrix");
        private static readonly int CameraPositionID = Shader.PropertyToID("_CameraPosition");
        private static readonly int CameraForwardID = Shader.PropertyToID("_CameraForward");
        private static readonly int FrustumCullMarginID = Shader.PropertyToID("_FrustumCullMargin");
        private static readonly int MaxRenderDistanceID = Shader.PropertyToID("_MaxRenderDistance");
        private static readonly int LOD0DistanceID = Shader.PropertyToID("_LOD0Distance");
        private static readonly int LOD1DistanceID = Shader.PropertyToID("_LOD1Distance");
        private static readonly int LOD2DistanceID = Shader.PropertyToID("_LOD2Distance");
        private static readonly int DensityFalloffID = Shader.PropertyToID("_DensityFalloff");
        private static readonly int TimeID = Shader.PropertyToID("_Time");
        private static readonly int WindDirectionID = Shader.PropertyToID("_WindDirection");
        private static readonly int WindStrengthID = Shader.PropertyToID("_WindStrength");
        private static readonly int WindSpeedID = Shader.PropertyToID("_WindSpeed");
        private static readonly int WindFrequencyID = Shader.PropertyToID("_WindFrequency");
        private static readonly int TotalInstancesID = Shader.PropertyToID("_TotalInstances");
        private static readonly int MaxVisibleInstancesID = Shader.PropertyToID("_MaxVisibleInstances");
        private static readonly int LODGroupID = Shader.PropertyToID("_LODGroup");
        private static readonly int FrustumPlanesID = Shader.PropertyToID("_FrustumPlanes");

        private Camera mainCamera;
        private Plane[] frustumPlanes = new Plane[6];
        private Vector4[] frustumPlaneVectors = new Vector4[6];

        private bool isInitialized = false;

        private void OnEnable()
        {
            Initialize();
        }

        private void OnDisable()
        {
            ReleaseBuffers();
        }

        private void Initialize()
        {
            if (grassCompute == null || grassMesh == null || grassMaterial == null)
            {
                Debug.LogWarning("[GrassRenderer] Missing required references.");
                return;
            }

            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                Debug.LogWarning("[GrassRenderer] No main camera found.");
                return;
            }

            // Get kernel IDs
            mainKernel = grassCompute.FindKernel("CSMain");
            clearKernel = grassCompute.FindKernel("CSClear");

            // Generate grass instances
            GenerateGrassInstances();

            if (totalInstances == 0)
            {
                Debug.LogWarning("[GrassRenderer] No grass instances generated.");
                return;
            }

            // Create buffers
            CreateBuffers();

            isInitialized = true;
            Debug.Log($"[GrassRenderer] Initialized with {totalInstances} grass instances.");
        }

        private void GenerateGrassInstances()
        {
            List<GrassInstance> instanceList = new List<GrassInstance>();
            Bounds bounds;

            if (sourceTerrain != null)
            {
                bounds = sourceTerrain.terrainData.bounds;
                bounds.center = sourceTerrain.transform.position + bounds.center;
            }
            else
            {
                // Use a default area around the object
                bounds = new Bounds(transform.position, new Vector3(50, 10, 50));
            }

            float spacing = 1f / density;
            int gridSizeX = Mathf.CeilToInt(bounds.size.x / spacing);
            int gridSizeZ = Mathf.CeilToInt(bounds.size.z / spacing);

            for (int x = 0; x < gridSizeX; x++)
            {
                for (int z = 0; z < gridSizeZ; z++)
                {
                    // Add some randomness to position
                    float offsetX = Random.Range(-spacing * 0.4f, spacing * 0.4f);
                    float offsetZ = Random.Range(-spacing * 0.4f, spacing * 0.4f);

                    Vector3 position = new Vector3(
                        bounds.min.x + x * spacing + offsetX,
                        bounds.max.y + 10f,
                        bounds.min.z + z * spacing + offsetZ
                    );

                    // Raycast to find ground
                    if (Physics.Raycast(position, Vector3.down, out RaycastHit hit, bounds.size.y + 20f, groundLayers))
                    {
                        // Skip if on steep slopes
                        if (hit.normal.y < 0.5f)
                            continue;

                        GrassInstance instance = new GrassInstance
                        {
                            position = hit.point,
                            normal = hit.normal,
                            uv = new Vector2(
                                (hit.point.x - bounds.min.x) / bounds.size.x,
                                (hit.point.z - bounds.min.z) / bounds.size.z
                            ),
                            height = Random.Range(heightMin, heightMax),
                            width = Random.Range(widthMin, widthMax),
                            rotation = Random.Range(0f, 360f),
                            stiffness = Random.Range(0f, 0.5f),
                            windPhase = Random.Range(0f, Mathf.PI * 2f)
                        };

                        instanceList.Add(instance);
                    }
                }
            }

            instances = instanceList.ToArray();
            totalInstances = instances.Length;
            maxVisibleInstances = Mathf.Min(totalInstances, 500000); // Cap for performance
        }

        private void CreateBuffers()
        {
            ReleaseBuffers();

            // Source instances buffer
            sourceBuffer = new ComputeBuffer(totalInstances, GrassInstance.Size);
            sourceBuffer.SetData(instances);

            // Culled instances buffer (visible instances)
            culledBuffer = new ComputeBuffer(maxVisibleInstances, GrassInstance.Size);

            // Indirect args buffer
            uint[] args = new uint[5] { grassMesh.GetIndexCount(0), 0, 0, 0, 0 };
            argsBuffer = new ComputeBuffer(1, sizeof(uint) * 5, ComputeBufferType.IndirectArguments);
            argsBuffer.SetData(args);

            // Counter buffer
            counterBuffer = new ComputeBuffer(1, sizeof(uint));
            counterBuffer.SetData(new uint[] { 0 });

            // Set buffers to compute shader
            grassCompute.SetBuffer(mainKernel, "_SourceInstances", sourceBuffer);
            grassCompute.SetBuffer(mainKernel, "_CulledInstances", culledBuffer);
            grassCompute.SetBuffer(mainKernel, "_IndirectArgs", argsBuffer);
            grassCompute.SetBuffer(mainKernel, "_InstanceCounter", counterBuffer);

            grassCompute.SetBuffer(clearKernel, "_InstanceCounter", counterBuffer);
            grassCompute.SetBuffer(clearKernel, "_IndirectArgs", argsBuffer);

            // Set material buffer
            grassMaterial.SetBuffer("_GrassInstances", culledBuffer);
        }

        private void ReleaseBuffers()
        {
            sourceBuffer?.Release();
            culledBuffer?.Release();
            argsBuffer?.Release();
            counterBuffer?.Release();

            sourceBuffer = null;
            culledBuffer = null;
            argsBuffer = null;
            counterBuffer = null;
        }

        private void Update()
        {
            if (!isInitialized || mainCamera == null)
                return;

            // Update frustum planes
            GeometryUtility.CalculateFrustumPlanes(mainCamera, frustumPlanes);
            for (int i = 0; i < 6; i++)
            {
                frustumPlaneVectors[i] = new Vector4(
                    frustumPlanes[i].normal.x,
                    frustumPlanes[i].normal.y,
                    frustumPlanes[i].normal.z,
                    frustumPlanes[i].distance
                );
            }

            // Set compute shader parameters
            Matrix4x4 viewMatrix = mainCamera.worldToCameraMatrix;
            Matrix4x4 projMatrix = mainCamera.projectionMatrix;
            Matrix4x4 viewProjMatrix = projMatrix * viewMatrix;

            grassCompute.SetMatrix(ViewProjectionMatrixID, viewProjMatrix);
            grassCompute.SetMatrix(ViewMatrixID, viewMatrix);
            grassCompute.SetVector(CameraPositionID, mainCamera.transform.position);
            grassCompute.SetVector(CameraForwardID, mainCamera.transform.forward);
            grassCompute.SetFloat(FrustumCullMarginID, frustumCullMargin);
            grassCompute.SetFloat(MaxRenderDistanceID, maxRenderDistance);
            grassCompute.SetFloat(LOD0DistanceID, lod0Distance);
            grassCompute.SetFloat(LOD1DistanceID, lod1Distance);
            grassCompute.SetFloat(LOD2DistanceID, lod2Distance);
            grassCompute.SetFloat(DensityFalloffID, densityFalloff);
            grassCompute.SetFloat(TimeID, Time.time);
            grassCompute.SetVector(WindDirectionID, windDirection.normalized);
            grassCompute.SetFloat(WindStrengthID, windStrength);
            grassCompute.SetFloat(WindSpeedID, windSpeed);
            grassCompute.SetFloat(WindFrequencyID, windFrequency);
            grassCompute.SetInt(TotalInstancesID, totalInstances);
            grassCompute.SetInt(MaxVisibleInstancesID, maxVisibleInstances);
            grassCompute.SetVectorArray(FrustumPlanesID, frustumPlaneVectors);

            // Clear counter
            grassCompute.Dispatch(clearKernel, 1, 1, 1);

            // Run culling for each LOD group
            int threadGroups = Mathf.CeilToInt(totalInstances / 256f);

            for (int lod = 0; lod < 3; lod++)
            {
                grassCompute.SetInt(LODGroupID, lod);
                grassCompute.Dispatch(mainKernel, threadGroups, 1, 1);
            }

            // Update indirect args with actual count
            uint[] count = new uint[1];
            counterBuffer.GetData(count);

            uint[] args = new uint[5];
            argsBuffer.GetData(args);
            args[1] = count[0];
            argsBuffer.SetData(args);

            // Update material properties
            grassMaterial.SetVector("_WindDirection", windDirection.normalized);
            grassMaterial.SetFloat("_WindStrength", windStrength);
            grassMaterial.SetFloat("_WindSpeed", windSpeed);
        }

        private void LateUpdate()
        {
            if (!isInitialized)
                return;

            // Draw grass using GPU instancing
            Bounds drawBounds = new Bounds(transform.position, Vector3.one * maxRenderDistance * 2f);
            Graphics.DrawMeshInstancedIndirect(
                grassMesh,
                0,
                grassMaterial,
                drawBounds,
                argsBuffer,
                0,
                null,
                ShadowCastingMode.Off,
                false,
                gameObject.layer
            );
        }

        private void OnDrawGizmosSelected()
        {
            if (sourceTerrain != null)
            {
                Gizmos.color = Color.green;
                Bounds bounds = sourceTerrain.terrainData.bounds;
                bounds.center = sourceTerrain.transform.position + bounds.center;
                Gizmos.DrawWireCube(bounds.center, bounds.size);
            }

            // Draw LOD distances
            Gizmos.color = new Color(0, 1, 0, 0.3f);
            Gizmos.DrawWireSphere(transform.position, lod0Distance);

            Gizmos.color = new Color(1, 1, 0, 0.3f);
            Gizmos.DrawWireSphere(transform.position, lod1Distance);

            Gizmos.color = new Color(1, 0, 0, 0.3f);
            Gizmos.DrawWireSphere(transform.position, lod2Distance);
        }

        /// <summary>
        /// Regenerate grass instances (call after terrain changes)
        /// </summary>
        public void Regenerate()
        {
            ReleaseBuffers();
            GenerateGrassInstances();
            if (totalInstances > 0)
            {
                CreateBuffers();
                isInitialized = true;
            }
        }

        /// <summary>
        /// Add interaction point (player position)
        /// </summary>
        public void SetInteractionPoint(Vector3 position, float radius = 2f)
        {
            if (grassMaterial != null)
            {
                grassMaterial.SetVector("_InteractionPosition", new Vector4(position.x, position.y, position.z, radius));
            }
        }
    }
}
