using UnityEngine;

namespace Submachina.Core
{
    /**
     * Projection-agnostic helpers for "how big is the camera's view at the
     * gameplay plane (z=0)".
     *
     * All camera-bounds and parallax-fit math routes through here so a future
     * switch from orthographic to perspective is a one-file change: consumers
     * never touch cam.orthographicSize directly.
     */
    public static class CameraViewUtil
    {
        /**
         * Half-extents (x = half width, y = half height) of the live camera's
         * view at the z=0 gameplay plane.
         *
         * Ortho:       (size * aspect, size)
         * Perspective: tan(fov/2) * distanceToPlane, scaled by aspect for X
         */
        public static Vector2 ViewHalfExtents(Camera cam)
        {
            if (cam == null) return Vector2.zero;

            if (cam.orthographic)
                return ViewHalfExtents(cam.orthographicSize, cam.aspect);

            // Perspective: extent grows with distance from the camera to the gameplay plane
            float distance = Mathf.Abs(cam.transform.position.z);
            float halfHeight = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * distance;
            return new Vector2(halfHeight * cam.aspect, halfHeight);
        }

        /**
         * Half-extents from an explicit half-height (orthographic size) and aspect —
         * used at authoring time when reasoning about a hypothetical view
         * (e.g. "max zoom-out at 21:9") rather than the live camera.
         */
        public static Vector2 ViewHalfExtents(float halfHeight, float aspect)
        {
            return new Vector2(halfHeight * Mathf.Max(0.0001f, aspect), halfHeight);
        }
    }
}
