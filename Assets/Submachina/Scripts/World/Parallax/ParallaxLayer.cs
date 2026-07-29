using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Seam for per-layer add-ons (tiling wrap, decor spawning) that must run
     * AFTER the layer has been positioned each frame. The layer discovers and
     * invokes these in component order, so there is no execution-order
     * fragility between separate scripts.
     */
    public interface IParallaxLayerExtension
    {
        void OnLayerUpdated(Vector3 camPos);
    }

    /**
     * One parallax layer — a child of the ParallaxController's GameObject.
     *
     * Movement factor convention (per axis):
     *   w = 0  → camera-locked (infinitely far, never scrolls)
     *   w = 1  → world-locked (moves like gameplay geometry)
     *   w > 1  → foreground (whizzes past faster than the world)
     *
     * Position is a pure function of camera position (see ParallaxMath), so
     * teleports and SnapToTargets need no special handling.
     *
     * The Fit tooling solves the bounded-level problem: given the level size
     * (LevelBounds) and the worst-case view size (max zoom-out at a wide
     * aspect), either compute the factor that makes this layer's art exactly
     * span the camera's travel, or scale the art to match a chosen factor.
     */
    [ExecuteAlways]
    public class ParallaxLayer : MonoBehaviour
    {
        /** How this layer's factor/scale relationship is authored. */
        public enum FitMode
        {
            /** Factor set by hand — no fit math. For decor/foreground layers. */
            Manual,

            /** Unbounded-level backdrop: factor forced to (0,0), art scaled to cover the view. */
            CameraLocked,

            /** Bounded-level backdrop: compute the factor from the art's current world size. */
            FitFactorFromSize,

            /** Bounded-level backdrop: scale the art to suit a hand-picked factor. */
            ScaleFromFactor
        }

        // =====================
        // Motion
        // =====================

        [FoldoutGroup("Motion")]
        [Tooltip("Per-axis movement factor: 0 = camera-locked (infinitely far), 1 = world-locked, " +
                 ">1 = foreground moving faster than the world.")]
        [SerializeField] private Vector2 movementFactor = new Vector2(0.5f, 0.5f);

        [FoldoutGroup("Motion")]
        [Tooltip("Camera position at which this layer shows its rest position. " +
                 "Set automatically to the level centre by Capture Rest Position.")]
        [SerializeField] private Vector2 anchor;

        [FoldoutGroup("Motion")]
        [Tooltip("The authored 'home' position of this layer (captured via button). " +
                 "Shown exactly when the camera sits at the anchor.")]
        [SerializeField] private Vector3 restPosition;

        // =====================
        // Fit
        // =====================

        [FoldoutGroup("Fit")]
        [InfoBox("Fit math sizes the layer so it can never reveal an edge over the camera's full travel. " +
                 "Worst case is max zoom-out at a wide monitor, so Max Ortho Size should match the " +
                 "camera's maximum and Reference Aspect should be generous (2.4 ≈ 21:9).")]
        [SerializeField] private FitMode fitMode = FitMode.Manual;

        [FoldoutGroup("Fit"), HideIf(nameof(fitMode), FitMode.Manual)]
        [Tooltip("Largest orthographic size the camera can reach (MultiTargetCamera2D max size).")]
        [SerializeField, Min(1f)] private float maxOrthoSize = 16f;

        [FoldoutGroup("Fit"), HideIf(nameof(fitMode), FitMode.Manual)]
        [Tooltip("Aspect ratio assumed for the widest view. 2.4 covers ultrawide monitors safely.")]
        [SerializeField, Min(0.5f)] private float referenceAspect = 2.4f;

        [FoldoutGroup("Fit"), HideIf(nameof(fitMode), FitMode.Manual)]
        [Tooltip("Level extents used by the fit math. Auto-resolved from the scene when left empty.")]
        [SerializeField] private LevelBounds levelBounds;

        [FoldoutGroup("Fit"), HideIf(nameof(fitMode), FitMode.Manual)]
        [Tooltip("Extra coverage margin applied when scaling art to fit (1.05 = 5% oversize).")]
        [SerializeField, Range(1f, 1.5f)] private float scaleMargin = 1.05f;

        // =====================
        // State
        // =====================

        // Cached extension components (tiling, spawner) — refreshed lazily and on enable
        private IParallaxLayerExtension[] _extensions;

        /** This layer's movement factor (read by extensions for layer-space math). */
        public Vector2 MovementFactor => movementFactor;

        /** Camera anchor position (read by extensions for layer-space math). */
        public Vector2 Anchor => anchor;

        /** Authored home position (read by extensions for layer-space math). */
        public Vector3 RestPosition => restPosition;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void OnEnable()
        {
            RefreshExtensions();
        }

        private void OnValidate()
        {
            // CameraLocked is the unbounded-backdrop mode: it must never scroll
            if (fitMode == FitMode.CameraLocked) movementFactor = Vector2.zero;
        }

        // -------------------------------------------------------
        // Core
        // -------------------------------------------------------

        /**
         * Repositions this layer from the camera (pure function — safe on
         * teleport), then lets extensions react in deterministic order.
         * Called by ParallaxController every LateUpdate after the camera moves.
         */
        public void UpdateFromCamera(Vector3 camPos)
        {
            transform.position = ParallaxMath.LayerPosition(restPosition, anchor, movementFactor, camPos);

            if (_extensions == null) RefreshExtensions();
            for (int i = 0; i < _extensions.Length; i++)
            {
                // Respect the enabled checkbox on extension components (e.g. a spawner that disabled itself)
                if (_extensions[i] is Behaviour behaviour && !behaviour.enabled) continue;
                _extensions[i].OnLayerUpdated(camPos);
            }
        }

        /** Re-discovers extension components. Call after adding/removing extensions at runtime. */
        public void RefreshExtensions()
        {
            _extensions = GetComponents<IParallaxLayerExtension>();
        }

        /** Restores the layer to its authored rest position (used by the controller's preview restore). */
        public void RestoreRestPosition()
        {
            transform.position = restPosition;
        }

        // -------------------------------------------------------
        // Fit tooling (editor)
        // -------------------------------------------------------

#if UNITY_EDITOR
        [FoldoutGroup("Fit"), ShowInInspector, ReadOnly]
        [LabelText("Coverage")]
        private string CoverageReport
        {
            get
            {
                SpriteRenderer sr = GetComponentInChildren<SpriteRenderer>();
                if (sr == null) return "no SpriteRenderer found";
                if (fitMode == FitMode.Manual) return "manual mode — no fit check";

                Vector2 required = ComputeRequiredExtents();
                Vector2 actual = sr.bounds.size;

                string Axis(string label, float need, float have) => have >= need - 0.01f
                    ? $"{label} covered ({have:F1} ≥ {need:F1})"
                    : $"{label} SHORT by {need - have:F1} units";

                return $"{Axis("X", required.x, actual.x)}  |  {Axis("Y", required.y, actual.y)}";
            }
        }

        [FoldoutGroup("Fit")]
        [Button("Capture Rest Position"), GUIColor(0.6f, 0.8f, 1f)]
        [Tooltip("Stores the current position as this layer's home and anchors it to the level centre.")]
        public void CaptureRestPosition()
        {
            UnityEditor.Undo.RecordObject(this, "Capture Parallax Rest Position");
            restPosition = transform.position;

            // Anchor at the level centre on bounded axes (camera-at-centre shows the rest pose),
            // and at the origin on unbounded axes.
            LevelBounds lb = ResolveBounds();
            anchor = new Vector2(
                lb != null && lb.HorizontallyBounded ? lb.Centre.x : 0f,
                lb != null && lb.VerticallyBounded ? lb.Centre.y : 0f);
        }

        [FoldoutGroup("Fit")]
        [Button("Compute Fit Factor From Size"), GUIColor(0.6f, 1f, 0.7f)]
        [ShowIf(nameof(fitMode), FitMode.FitFactorFromSize)]
        [Tooltip("Reads the art's current world size and sets the movement factor so it exactly spans the level.")]
        private void ComputeFitFactor()
        {
            SpriteRenderer sr = GetComponentInChildren<SpriteRenderer>();
            LevelBounds lb = ResolveBounds();
            if (sr == null || lb == null) { Debug.LogWarning("[ParallaxLayer] Needs a child SpriteRenderer and a LevelBounds."); return; }

            // Per-axis fit: unbounded axes fall back to camera-locked (factor 0)
            Vector2 view = CameraViewUtil.ViewHalfExtents(maxOrthoSize, referenceAspect) * 2f;
            Vector2 size = sr.bounds.size;

            UnityEditor.Undo.RecordObject(this, "Compute Parallax Fit Factor");
            movementFactor = new Vector2(
                lb.HorizontallyBounded ? ParallaxMath.FitFactor(size.x, lb.Size.x, view.x) : 0f,
                lb.VerticallyBounded ? ParallaxMath.FitFactor(size.y, lb.Size.y, view.y) : 0f);

            Debug.Log($"[ParallaxLayer] {name}: fit factor = {movementFactor}");
        }

        [FoldoutGroup("Fit")]
        [Button("Scale Art To Fit Factor"), GUIColor(0.6f, 1f, 0.7f)]
        [HideIf(nameof(fitMode), FitMode.Manual)]
        [ShowIf("@fitMode == FitMode.ScaleFromFactor || fitMode == FitMode.CameraLocked")]
        [Tooltip("Scales the art so the current movement factor gives exact coverage (plus margin). " +
                 "CameraLocked mode sizes to the max view only.")]
        private void ScaleToFitFactor()
        {
            SpriteRenderer sr = GetComponentInChildren<SpriteRenderer>();
            if (sr == null) { Debug.LogWarning("[ParallaxLayer] Needs a child SpriteRenderer."); return; }

            Vector2 required = ComputeRequiredExtents() * scaleMargin;
            Vector2 actual = sr.bounds.size;
            if (actual.x <= 0f || actual.y <= 0f) { Debug.LogWarning("[ParallaxLayer] Sprite has zero size."); return; }

            // Multiply the renderer's current scale by the shortfall ratio per axis
            Transform t = sr.transform;
            UnityEditor.Undo.RecordObject(t, "Scale Parallax Layer To Fit");
            Vector3 scale = t.localScale;
            scale.x *= required.x / actual.x;
            scale.y *= required.y / actual.y;
            t.localScale = scale;

            Debug.Log($"[ParallaxLayer] {name}: scaled to {scale} for coverage {required}");
        }

        /** Required world extents for full coverage in the current fit mode. */
        private Vector2 ComputeRequiredExtents()
        {
            Vector2 view = CameraViewUtil.ViewHalfExtents(maxOrthoSize, referenceAspect) * 2f;

            // Camera-locked (or unbounded axes): only the view itself must be covered
            LevelBounds lb = ResolveBounds();
            if (fitMode == FitMode.CameraLocked || lb == null) return view;

            return new Vector2(
                lb.HorizontallyBounded ? ParallaxMath.RequiredExtent(lb.Size.x, movementFactor.x, view.x) : view.x,
                lb.VerticallyBounded ? ParallaxMath.RequiredExtent(lb.Size.y, movementFactor.y, view.y) : view.y);
        }

        private LevelBounds ResolveBounds()
        {
            if (levelBounds == null) levelBounds = FindFirstObjectByType<LevelBounds>();
            return levelBounds;
        }
#endif
    }
}
