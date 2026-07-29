using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Drives every ParallaxLayer child from the camera's world position.
     *
     * Runs in LateUpdate AFTER the camera scripts ([DefaultExecutionOrder(100)];
     * MultiTargetCamera2D / CameraFollow are order 0), so layers always read the
     * camera's final settled position for the frame. Reading the Main Camera's
     * WORLD position means MMF camera shake propagates into the layers — and
     * because each layer scales motion by (1 - w), shake attenuates naturally
     * with distance: far layers barely move, world-locked layers shake in
     * lockstep with gameplay geometry.
     *
     * Layer positions are pure functions of camera position (no accumulation),
     * so teleports and SnapToTargets are automatically correct; ForceUpdate
     * exists only to avoid a single frame of lag after an instant snap.
     *
     * Place on a root scene object named "Parallax" with one child per layer.
     */
    [ExecuteAlways]
    [DefaultExecutionOrder(100)]
    public class ParallaxController : MonoBehaviour
    {
        // =====================
        // References
        // =====================

        [FoldoutGroup("References")]
        [Tooltip("The camera whose position drives the layers. Auto-resolves to Camera.main when empty.")]
        [SerializeField] private Camera cam;

        [FoldoutGroup("References")]
        [Tooltip("Level extents, passed through for layer fit tooling. Auto-resolved when empty.")]
        [SerializeField] private LevelBounds levelBounds;

        // =====================
        // Edit Preview
        // =====================

        [FoldoutGroup("Edit Preview")]
        [InfoBox("When enabled, layers track the camera in EDIT mode too — drag the Main Camera around " +
                 "the scene view to preview parallax. This MOVES layer transforms in the scene; " +
                 "captured rest positions make Restore lossless.")]
        [SerializeField] private bool editModePreview = false;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private int LayerCount => _layers.Count;

        // =====================
        // State
        // =====================

        // Cached child layers — refreshed when the hierarchy under us changes
        private readonly List<ParallaxLayer> _layers = new List<ParallaxLayer>();

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void OnEnable()
        {
            RefreshLayers();
        }

        private void OnTransformChildrenChanged()
        {
            RefreshLayers();
        }

        private void LateUpdate()
        {
            // Edit mode only moves layers when the designer opts into preview
            if (!Application.isPlaying && !editModePreview) return;

            EnsureReferences();
            if (cam == null) return;

            // Self-heal an empty cache: a layer component added after our OnEnable
            // (or nested under a plain child) doesn't fire OnTransformChildrenChanged
            if (_layers.Count == 0) RefreshLayers();

            UpdateLayers(cam.transform.position);
        }

        // -------------------------------------------------------
        // Core
        // -------------------------------------------------------

        /**
         * Repositions every layer immediately from the camera's current
         * position. Called by camera scripts right after an instant snap
         * (e.g. MultiTargetCamera2D.SnapToTargets) so parallax is correct
         * the same frame instead of one frame late.
         */
        public void ForceUpdate()
        {
            EnsureReferences();
            if (cam == null) return;

            // Manual/rare call — always re-collect so editor tooling never acts on a stale list
            RefreshLayers();
            UpdateLayers(cam.transform.position);
        }

        /** Positions each child layer for the given camera position. */
        private void UpdateLayers(Vector3 camPos)
        {
            for (int i = 0; i < _layers.Count; i++)
            {
                if (_layers[i] == null || !_layers[i].isActiveAndEnabled) continue;
                _layers[i].UpdateFromCamera(camPos);
            }
        }

        /** Re-collects ParallaxLayer children (direct and nested). */
        private void RefreshLayers()
        {
            _layers.Clear();
            GetComponentsInChildren(true, _layers);
        }

        /**
         * Resolves the camera and bounds lazily — safe to call every frame,
         * only does work while a reference is missing (same pattern as
         * MultiTargetCamera2D.EnsureCamera).
         */
        private void EnsureReferences()
        {
            if (cam == null) cam = Camera.main;
            if (levelBounds == null) levelBounds = LevelBounds.Find();
        }

        // -------------------------------------------------------
        // Editor Utilities
        // -------------------------------------------------------

#if UNITY_EDITOR
        [FoldoutGroup("Edit Preview")]
        [Button("Capture All Rest Positions"), GUIColor(0.6f, 0.8f, 1f)]
        [Tooltip("Stores every layer's current position as its authored home (see ParallaxLayer.CaptureRestPosition).")]
        private void CaptureAllRestPositions()
        {
            RefreshLayers();
            foreach (ParallaxLayer layer in _layers)
            {
                // Invoke the layer's own capture so anchor logic stays in one place
                layer.CaptureRestPosition();
            }
            Debug.Log($"[ParallaxController] Captured rest positions for {_layers.Count} layers.");
        }

        [FoldoutGroup("Edit Preview")]
        [Button("Restore All Rest Positions"), GUIColor(1f, 0.85f, 0.6f)]
        [Tooltip("Returns every layer to its authored home position — undoes preview movement losslessly.")]
        private void RestoreAllRestPositions()
        {
            RefreshLayers();
            foreach (ParallaxLayer layer in _layers)
            {
                UnityEditor.Undo.RecordObject(layer.transform, "Restore Parallax Rest Positions");
                layer.RestoreRestPosition();
            }
            Debug.Log($"[ParallaxController] Restored {_layers.Count} layers to rest positions.");
        }
#endif
    }
}
