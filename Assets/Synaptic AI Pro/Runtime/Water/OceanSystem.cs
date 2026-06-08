using UnityEngine;

namespace Synaptic.Water
{
    /// <summary>
    /// Creates an infinite ocean plane that follows the camera
    /// </summary>
    [ExecuteAlways]
    public class OceanSystem : MonoBehaviour
    {
        [Header("Ocean Settings")]
        public Material oceanMaterial;
        public int gridSize = 128;
        public float tileSize = 100f;
        public int tilesAroundCamera = 3;

        [Header("LOD Settings")]
        public bool useLOD = true;
        public float lodDistance = 200f;
        public int lodLevels = 3;

        [Header("Camera")]
        public Transform followCamera;

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Mesh oceanMesh;
        private Vector3 lastCameraPosition;

        void Start()
        {
            if (followCamera == null)
                followCamera = Camera.main?.transform;

            CreateOceanMesh();
        }

        void Update()
        {
            if (followCamera == null) return;

            // Snap to grid position following camera
            Vector3 camPos = followCamera.position;
            float snapX = Mathf.Floor(camPos.x / tileSize) * tileSize;
            float snapZ = Mathf.Floor(camPos.z / tileSize) * tileSize;

            transform.position = new Vector3(snapX, transform.position.y, snapZ);
        }

        void CreateOceanMesh()
        {
            meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null)
                meshFilter = gameObject.AddComponent<MeshFilter>();

            meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null)
                meshRenderer = gameObject.AddComponent<MeshRenderer>();

            oceanMesh = GenerateOceanMesh(gridSize, tileSize * tilesAroundCamera * 2);
            meshFilter.sharedMesh = oceanMesh;

            if (oceanMaterial != null)
                meshRenderer.sharedMaterial = oceanMaterial;
        }

        Mesh GenerateOceanMesh(int resolution, float size)
        {
            Mesh mesh = new Mesh();
            mesh.name = "OceanMesh";
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            int vertCount = (resolution + 1) * (resolution + 1);
            Vector3[] vertices = new Vector3[vertCount];
            Vector2[] uvs = new Vector2[vertCount];
            Vector3[] normals = new Vector3[vertCount];

            float halfSize = size * 0.5f;
            float step = size / resolution;

            for (int z = 0; z <= resolution; z++)
            {
                for (int x = 0; x <= resolution; x++)
                {
                    int i = z * (resolution + 1) + x;
                    float xPos = x * step - halfSize;
                    float zPos = z * step - halfSize;

                    vertices[i] = new Vector3(xPos, 0, zPos);
                    uvs[i] = new Vector2((float)x / resolution, (float)z / resolution);
                    normals[i] = Vector3.up;
                }
            }

            int[] triangles = new int[resolution * resolution * 6];
            int t = 0;

            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int i = z * (resolution + 1) + x;

                    triangles[t++] = i;
                    triangles[t++] = i + resolution + 1;
                    triangles[t++] = i + 1;

                    triangles[t++] = i + 1;
                    triangles[t++] = i + resolution + 1;
                    triangles[t++] = i + resolution + 2;
                }
            }

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();

            return mesh;
        }

        /// <summary>
        /// Get wave height at world position (for buoyancy)
        /// </summary>
        public float GetWaveHeight(Vector3 worldPos)
        {
            if (oceanMaterial == null) return transform.position.y;

            float time = Time.time * oceanMaterial.GetFloat("_WaveSpeed");
            float oceanScale = oceanMaterial.HasProperty("_OceanScale") ? oceanMaterial.GetFloat("_OceanScale") : 1f;
            float waveHeight = oceanMaterial.HasProperty("_WaveHeight") ? oceanMaterial.GetFloat("_WaveHeight") : 1f;

            Vector3 scaledPos = worldPos * oceanScale;
            float height = transform.position.y;

            // Sample waves A through H
            height += SampleGerstnerWave(oceanMaterial.GetVector("_WaveA"), scaledPos, time * 0.8f) * waveHeight;
            height += SampleGerstnerWave(oceanMaterial.GetVector("_WaveB"), scaledPos, time * 0.9f) * waveHeight;
            height += SampleGerstnerWave(oceanMaterial.GetVector("_WaveC"), scaledPos, time) * waveHeight;
            height += SampleGerstnerWave(oceanMaterial.GetVector("_WaveD"), scaledPos, time * 1.1f) * waveHeight;

            if (oceanMaterial.HasProperty("_WaveE"))
            {
                height += SampleGerstnerWave(oceanMaterial.GetVector("_WaveE"), scaledPos, time * 1.2f) * waveHeight;
                height += SampleGerstnerWave(oceanMaterial.GetVector("_WaveF"), scaledPos, time * 1.4f) * waveHeight;
                height += SampleGerstnerWave(oceanMaterial.GetVector("_WaveG"), scaledPos, time * 1.6f) * waveHeight;
                height += SampleGerstnerWave(oceanMaterial.GetVector("_WaveH"), scaledPos, time * 1.8f) * waveHeight;
            }

            return height;
        }

        float SampleGerstnerWave(Vector4 wave, Vector3 pos, float time)
        {
            float steepness = wave.z;
            float wavelength = wave.w;

            if (wavelength <= 0) return 0;

            float k = 2f * Mathf.PI / wavelength;
            float c = Mathf.Sqrt(9.8f / k);
            Vector2 d = new Vector2(wave.x, wave.y).normalized;
            float f = k * (Vector2.Dot(d, new Vector2(pos.x, pos.z)) - c * time);
            float a = steepness / k;

            return a * Mathf.Sin(f);
        }

        /// <summary>
        /// Get wave normal at world position
        /// </summary>
        public Vector3 GetWaveNormal(Vector3 worldPos)
        {
            float delta = 0.1f;
            float h = GetWaveHeight(worldPos);
            float hX = GetWaveHeight(worldPos + Vector3.right * delta);
            float hZ = GetWaveHeight(worldPos + Vector3.forward * delta);

            Vector3 tangentX = new Vector3(delta, hX - h, 0).normalized;
            Vector3 tangentZ = new Vector3(0, hZ - h, delta).normalized;

            return Vector3.Cross(tangentZ, tangentX).normalized;
        }

        void OnValidate()
        {
            if (Application.isPlaying && oceanMesh != null)
            {
                CreateOceanMesh();
            }
        }
    }
}
