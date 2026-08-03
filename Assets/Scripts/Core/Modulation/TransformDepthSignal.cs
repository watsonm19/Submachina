using UnityEngine;

namespace Core.Modulation
{
    /**
     * Reports how far below a surface line a transform is, in world units ("meters of depth").
     * The listener context for environmental modulation is usually the main camera, so this
     * signal can auto-resolve Camera.main when no explicit target is assigned.
     */
    public class TransformDepthSignal : FloatSignal
    {
        [Header("Target")]
        [Tooltip("Transform whose Y position is measured. Leave empty to use the main camera.")]
        [SerializeField] private Transform target;

        [Tooltip("When true and no target is set, the main camera is found and cached at runtime.")]
        [SerializeField] private bool useMainCameraFallback = true;

        [Header("Depth Mapping")]
        [Tooltip("World Y considered the surface (depth zero). Depth increases as the target moves below this line.")]
        [SerializeField] private float surfaceY = 0f;

        private Transform _resolved;

        public override float Value
        {
            get
            {
                var t = Resolve();
                if (t == null) return 0f;
                return Mathf.Max(0f, surfaceY - t.position.y);
            }
        }

        public override bool IsValid => isActiveAndEnabled && Resolve() != null;

        /**
         * Resolves the measured transform: explicit target first, then the cached main camera.
         * Re-resolves automatically if the cached camera was destroyed (scene changes).
         */
        private Transform Resolve()
        {
            if (target != null) return target;
            if (!useMainCameraFallback) return null;
            if (_resolved == null && Camera.main != null) _resolved = Camera.main.transform;
            return _resolved;
        }
    }
}
