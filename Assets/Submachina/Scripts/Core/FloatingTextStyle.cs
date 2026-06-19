using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Appearance + animation recipe for one kind of floating-text popup
     * (e.g. "air gained", "HP lost", "passive decay").
     *
     * FloatingTextPool holds several of these — one per channel — and builds a
     * small dedicated pool for each. Keeping the look data in a reusable block
     * means a new popup type is just another serialized FloatingTextStyle, and
     * designers tune colour/size/format per channel without touching code.
     *
     * Two rendering paths:
     *   - prefab assigned  → the pool instantiates it (e.g. a TMP wrapped in a
     *     translucent O2 bubble). The prefab authors its own font/sprite; this
     *     style still drives colour, motion, lifetime and scale.
     *   - prefab null      → the pool generates a bare world-space TextMeshPro
     *     using fontSize + the pool's sorting settings.
     */
    [System.Serializable]
    public class FloatingTextStyle
    {
        [Tooltip("Spawn colour for the number. Alpha fades to 0 over the lifetime. " +
                 "Use distinct shades per channel (e.g. cyan O2 gain vs orange O2 loss).")]
        public Color color = Color.white;

        [Tooltip("C# format string for the value. {0} is the (positive) amount.\n\n" +
                 "Examples: '+{0:F0}' → '+12',  '-{0:F0}' → '-3',  '{0:F1}' → '12.3'")]
        public string numberFormat = "+{0:F0}";

        [Tooltip("Font size for BARE (no-prefab) popups. Ignored when a prefab is " +
                 "assigned — the prefab authors its own text size.")]
        [Min(0.1f)] public float fontSize = 3f;

        [Tooltip("Uniform scale multiplier applied on top of the prefab's authored " +
                 "scale. Use < 1 to make a channel quieter (e.g. passive-decay ticks).")]
        [Min(0.05f)] public float scale = 1f;

        [Tooltip("Upward drift speed in world units per second.")]
        [Min(0f)] public float floatSpeed = 1.5f;

        [Tooltip("Seconds the popup stays visible before fully fading out.")]
        [Min(0.1f)] public float lifetime = 2f;

        [Tooltip("How many instances to pre-allocate for this channel. If all are " +
                 "active the oldest is recycled. Bump up for channels that burst.")]
        [Min(1)] public int poolSize = 6;

        [Tooltip("Optional prefab to spawn instead of a bare TextMeshPro. Must contain " +
                 "a FloatingText (with its required TextMeshPro); an optional child " +
                 "SpriteRenderer (e.g. a bubble) is faded along with the text.")]
        [AssetsOnly] public GameObject prefab;
    }
}
