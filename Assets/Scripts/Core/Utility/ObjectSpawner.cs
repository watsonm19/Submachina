using UnityEngine;
using Sirenix.OdinInspector;

namespace Utility
{
    /**
     * Utility component for spawning prefabs at runtime.
     * Supports spawning at default location or specified Vector3 position,
     * with optional parent transform, rotation, and random offset.
     * Can be invoked via Unity Events or code.
     */
    public class ObjectSpawner : MonoBehaviour
    {
        [TitleGroup("Prefab Settings")]
        [Tooltip("The prefab to spawn. Must be assigned for spawning to work.")]
        [Required]
        [SerializeField]
        private GameObject prefabToSpawn;

        [TitleGroup("Spawn Settings")]
        [Tooltip("Optional parent transform for spawned objects. If null, objects spawn at root level.")]
        [SerializeField]
        private Transform parentTransform;

        [TitleGroup("Spawn Settings")]
        [Tooltip("Default rotation for spawned objects. Only used when spawning without explicit rotation.")]
        [SerializeField]
        private Quaternion defaultRotation = Quaternion.identity;

        [TitleGroup("Spawn Settings")]
        [Tooltip("If true, spawns at this GameObject's transform position when no location is specified.")]
        [SerializeField]
        private bool useTransformPosition = true;

        [TitleGroup("Random Offset")] [Tooltip("Enable to add random offset to spawn position")] [SerializeField]
        private bool useRandomOffset = false;

        [TitleGroup("Random Offset")]
        [ShowIf(nameof(useRandomOffset))]
        [Tooltip("Maximum random offset in X, Y, Z directions")]
        [SerializeField]
        private Vector3 randomOffsetRange = Vector3.zero;

        [TitleGroup("Spawn Limits")]
        [Tooltip("Maximum number of objects that can be spawned. 0 = unlimited")]
        [MinValue(0)]
        [SerializeField]
        private int maxSpawnCount = 0;

        [TitleGroup("Auto Spawn")]
        [Tooltip("If true, automatically spawns the prefab when the GameObject is enabled")]
        [SerializeField]
        private bool spawnOnEnable = false;

        [TitleGroup("Debug")] [ShowInInspector, ReadOnly] [Tooltip("Current count of spawned objects")]
        private int currentSpawnCount = 0;

        // Track spawned objects if we have a limit
        private System.Collections.Generic.List<GameObject> _spawnedObjects;

        /**
         * Unity lifecycle - auto-spawn if configured to do so.
         */
        private void OnEnable()
        {
            if (spawnOnEnable)
            {
                Spawn();
            }
        }

        /**
         * Spawns the prefab at the default location (this transform's position or Vector3.zero).
         * Can be called via Unity Events or code.
         */
        [Button("Spawn Object", ButtonSizes.Medium)]
        [TitleGroup("Debug")]
        public void Spawn()
        {
            Vector3 spawnPosition = useTransformPosition ? transform.position : Vector3.zero;
            SpawnAtPosition(spawnPosition);
        }

        /**
         * Spawns the prefab at the specified Vector3 position.
         * Can be called via Unity Events or code.
         *
         * @param position The world position where the object should be spawned
         */
        public void SpawnAtPosition(Vector3 position)
        {
            SpawnAtPositionAndRotation(position, defaultRotation);
        }

        /**
         * Spawns the prefab at the specified position with the specified rotation.
         *
         * @param position The world position where the object should be spawned
         * @param rotation The rotation for the spawned object
         */
        public void SpawnAtPositionAndRotation(Vector3 position, Quaternion rotation)
        {
            // Validate prefab is assigned
            if (prefabToSpawn == null)
            {
                Debug.LogError("ObjectSpawner: No prefab assigned! Cannot spawn.", this);
                return;
            }

            // Check spawn limit if one is set
            if (maxSpawnCount > 0)
            {
                // Clean up destroyed objects from the list
                if (_spawnedObjects != null)
                {
                    _spawnedObjects.RemoveAll(obj => obj == null);
                }
                else
                {
                    _spawnedObjects = new System.Collections.Generic.List<GameObject>();
                }

                // Check if we've reached the limit
                if (_spawnedObjects.Count >= maxSpawnCount)
                {
                    Debug.LogWarning(
                        $"ObjectSpawner: Spawn limit ({maxSpawnCount}) reached. Cannot spawn more objects.", this);
                    return;
                }
            }

            // Apply random offset if enabled
            Vector3 finalPosition = position;
            if (useRandomOffset)
            {
                float offsetX = Random.Range(-randomOffsetRange.x, randomOffsetRange.x);
                float offsetY = Random.Range(-randomOffsetRange.y, randomOffsetRange.y);
                float offsetZ = Random.Range(-randomOffsetRange.z, randomOffsetRange.z);
                finalPosition += new Vector3(offsetX, offsetY, offsetZ);
            }

            // Instantiate the prefab
            GameObject spawnedObject = Instantiate(prefabToSpawn, finalPosition, rotation, parentTransform);

            // Track spawned object if we have a limit
            if (maxSpawnCount > 0)
            {
                _spawnedObjects.Add(spawnedObject);
                currentSpawnCount = _spawnedObjects.Count;
            }
        }

        /**
         * Clears all spawned objects tracked by this spawner.
         * Only works if maxSpawnCount is set (objects are being tracked).
         */
        [Button("Clear Spawned Objects", ButtonSizes.Medium)]
        [TitleGroup("Debug")]
        [EnableIf("@maxSpawnCount > 0")]
        public void ClearSpawnedObjects()
        {
            if (_spawnedObjects == null) return;

            // Destroy all tracked objects
            foreach (GameObject obj in _spawnedObjects)
            {
                if (obj != null)
                {
                    Destroy(obj);
                }
            }

            _spawnedObjects.Clear();
            currentSpawnCount = 0;
        }
    }
}

