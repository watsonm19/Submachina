using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Creates invisible wall colliders on the left and right edges of the play area.
     *
     * The walls are parented to this GameObject so they follow it each frame.
     * Place this script on the Main Camera — the walls then scroll with the camera,
     * forming effectively infinite vertical barriers that match the chunk width.
     *
     * halfWidth must match ChunkSpawner's playAreaHalfWidth so the walls sit
     * exactly at the edge of the generated world.
     *
     * Setup:
     *   1. Add this component to the Main Camera.
     *   2. Set Half Width to match ChunkSpawner's Play Area Half Width (default 9).
     */
    public class WorldBoundary : MonoBehaviour
    {
        [FoldoutGroup("Settings")]
        [Tooltip("Distance from world center to each wall. Must match ChunkSpawner's Play Area Half Width.")]
        [SerializeField] private float halfWidth = 9f;

        [FoldoutGroup("Settings")]
        [Tooltip("Vertical height of each wall collider in world units. Large enough to always cover " +
                 "the camera view plus a generous buffer above and below.")]
        [SerializeField] private float wallHeight = 400f;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Start()
        {
            CreateWall("BoundaryLeft",  -halfWidth - 0.5f);
            CreateWall("BoundaryRight",  halfWidth + 0.5f);
        }

        // -------------------------------------------------------
        // Wall Creation
        // -------------------------------------------------------

        /**
         * Spawns a single invisible wall at the given local X offset.
         * Parented to this transform so it follows the camera automatically.
         * The collider is sized so it always covers the visible area plus buffer.
         */
        private void CreateWall(string wallName, float localX)
        {
            GameObject wall = new GameObject(wallName);
            wall.transform.SetParent(transform);
            wall.transform.localPosition = new Vector3(localX, 0f, 0f);

            BoxCollider2D col = wall.AddComponent<BoxCollider2D>();
            col.size = new Vector2(1f, wallHeight);
        }
    }
}
