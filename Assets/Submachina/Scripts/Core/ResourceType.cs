using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Identity asset for a mineable resource type (Ferrite Nodules, Vent Brass,
     * Clathrate Ice, Luminite, Abyssite).
     *
     * The asset itself IS the identifier — systems match by reference
     * (rename-safe, typo-proof, mirrors the UpgradeFeature pattern), and
     * persistence keys off the stable asset name via Key.
     *
     * Consumed by: MiningResource (what a node yields), CargoHold (typed storage
     * + per-unit mass), the hub shop (upgrade costs), and the mission generator
     * (depth-band abundance weighting).
     */
    [CreateAssetMenu(menuName = "Submachina/Resource Type", fileName = "ResourceType")]
    public class ResourceType : ScriptableObject
    {
        // =====================
        // Identity
        // =====================

        [Title("Identity")]
        [Tooltip("Player-facing name, e.g. 'Vent Brass'.")]
        public string displayName;

        [TextArea]
        [Tooltip("Flavor / usage description shown in hub UI.")]
        public string description;

        [Tooltip("Signature color — UI accents and tinting generic node art.")]
        public Color tint = Color.white;

        [Tooltip("Optional icon for HUD / hub displays.")]
        public Sprite icon;

        // =====================
        // Gameplay
        // =====================

        [Title("Gameplay")]
        [Tooltip("Mass added to the sub per cargo unit carried. Example: 0.05 → a full 20-unit hold adds +1.0 mass.")]
        [Min(0f)] public float unitMass = 0.05f;

        [Tooltip("Normalized depth band where this resource is native (0 = surface, 1 = deepest). " +
                 "Used as a UI hint and for mission-generator abundance weighting; actual spawning " +
                 "is governed by SpawnRule depth ranges.")]
        [MinMaxSlider(0f, 1f, true)] public Vector2 depthBand = new(0f, 0.4f);

        /** Stable key used by persistence and mission specs (the asset name). */
        public string Key => name;
    }
}
