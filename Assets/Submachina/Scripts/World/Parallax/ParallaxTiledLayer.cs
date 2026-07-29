using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Makes a parallax layer repeat forever: after the ParallaxLayer positions
     * itself each frame, this wraps the layer content back toward the camera by
     * WHOLE tile periods, which is visually invisible because the sprite
     * repeats with exactly that period.
     *
     * Use for always-repeating layers (haze bands, distant striations) on any
     * axis the camera can travel far along — especially unbounded levels.
     *
     * Setup requirements (see Parallax/context.md):
     *   - SpriteRenderer in TILED draw mode, sized to cover the max view plus
     *     at least 2 extra tiles on each wrapped axis
     *   - Texture wrap mode = Repeat, sprite Mesh Type = Full Rect
     *
     * Transform-wrap is used instead of scrolling material UVs so stock
     * Sprite-Lit/Unlit materials, SRP batching, and sorting stay untouched.
     */
    [RequireComponent(typeof(ParallaxLayer))]
    public class ParallaxTiledLayer : MonoBehaviour, IParallaxLayerExtension
    {
        // =====================
        // Tiling
        // =====================

        [FoldoutGroup("Tiling")]
        [InfoBox("Tile period = the world-space distance after which the art repeats exactly " +
                 "(normally the source sprite's world size, NOT the tiled renderer size). " +
                 "Wrapping shifts the layer by whole periods, so the jump is invisible.")]
        [Tooltip("Repeat period in world units per axis. Use the Detect button to read it from the sprite.")]
        [SerializeField] private Vector2 tilePeriod = new Vector2(10f, 10f);

        [FoldoutGroup("Tiling")]
        [Tooltip("Which axes wrap. Disable an axis the camera can't travel far along.")]
        [SerializeField] private bool wrapX = true;

        [FoldoutGroup("Tiling")]
        [SerializeField] private bool wrapY = true;

        // -------------------------------------------------------
        // IParallaxLayerExtension
        // -------------------------------------------------------

        /**
         * Called by ParallaxLayer right after it positions the transform.
         * Shifts the layer toward the camera by the whole number of tile
         * periods that brings it as close as possible — keeping the tiled
         * quad centred over the view no matter how far the camera travels.
         */
        public void OnLayerUpdated(Vector3 camPos)
        {
            Vector3 pos = transform.position;

            // Example: camX=305, layerX=2, period=10 → shift = round(303/10)*10 = 300 → layerX=302
            if (wrapX && tilePeriod.x > 0.001f)
                pos.x += Mathf.Round((camPos.x - pos.x) / tilePeriod.x) * tilePeriod.x;

            if (wrapY && tilePeriod.y > 0.001f)
                pos.y += Mathf.Round((camPos.y - pos.y) / tilePeriod.y) * tilePeriod.y;

            transform.position = pos;
        }

        // -------------------------------------------------------
        // Editor Utilities
        // -------------------------------------------------------

#if UNITY_EDITOR
        [FoldoutGroup("Tiling")]
        [Button("Detect Period From Sprite"), GUIColor(0.6f, 0.8f, 1f)]
        [Tooltip("Reads the repeat period from the sprite's native world size × transform scale.")]
        private void DetectPeriod()
        {
            SpriteRenderer sr = GetComponentInChildren<SpriteRenderer>();
            if (sr == null || sr.sprite == null) { Debug.LogWarning("[ParallaxTiledLayer] No sprite found."); return; }

            // One tile = the source sprite's world size scaled by the renderer's transform
            Vector2 native = sr.sprite.bounds.size;
            Vector3 scale = sr.transform.lossyScale;
            UnityEditor.Undo.RecordObject(this, "Detect Tile Period");
            tilePeriod = new Vector2(native.x * Mathf.Abs(scale.x), native.y * Mathf.Abs(scale.y));

            if (sr.drawMode != SpriteDrawMode.Tiled)
                Debug.LogWarning("[ParallaxTiledLayer] SpriteRenderer is not in Tiled draw mode — wrapping will show seams.");
        }
#endif
    }
}
