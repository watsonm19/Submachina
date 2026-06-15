using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Data-driven definition of a submarine's composition.
     *
     * Each slot references a prefab containing one or more SubmarineComponents.
     * Submarine.Build reads this config and instantiates everything as children
     * of the submarine root — SubmarineComponent.Awake handles registration
     * automatically.
     *
     * Create multiple configs to playtest different loadouts:
     *   Assets > Create > Submachina > Submarine Config
     */
    [CreateAssetMenu(menuName = "Submachina/Submarine Config")]
    public class SubmarineConfig : ScriptableObject
    {
        // =====================
        // Core Systems
        // =====================

        [FoldoutGroup("Core")]
        [Tooltip("The base submarine hull/body prefab (visuals, colliders, rigidbody).")]
        public GameObject hullPrefab;

        [FoldoutGroup("Core")]
        [Tooltip("O2 system variant — air tank, decay, health bleed.")]
        public GameObject o2SystemPrefab;

        [FoldoutGroup("Core")]
        [Tooltip("Physics/propulsion variant — thrust, drag, facing.")]
        public GameObject propulsionPrefab;

        [FoldoutGroup("Core")]
        [Tooltip("Turret/aiming system — mouse and gamepad aim.")]
        public GameObject turretPrefab;

        // =====================
        // Modular Systems
        // =====================

        [FoldoutGroup("Weapons")]
        [ListDrawerSettings(ShowPaging = false)]
        [Tooltip("Weapon module prefabs (mining laser, plasma cutter, etc).")]
        public List<GameObject> weapons = new();

        [FoldoutGroup("Abilities")]
        [ListDrawerSettings(ShowPaging = false)]
        [Tooltip("Ability module prefabs (cavitation burst, shield, etc).")]
        public List<GameObject> abilities = new();

        [FoldoutGroup("Utilities")]
        [ListDrawerSettings(ShowPaging = false)]
        [Tooltip("Utility module prefabs (pumps, sensors, resource systems, etc).")]
        public List<GameObject> utilities = new();
    }
}
