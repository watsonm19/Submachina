using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Central facade for a submarine and all of its subsystems.
     *
     * Lives on the submarine root GameObject. Child SubmarineComponents
     * auto-register via GetComponentInParent<Submarine>() in their Awake,
     * so the registry is populated without manual wiring.
     *
     * Other systems access a submarine's subsystems through this facade:
     *   - Sibling SubmarineComponents use the protected Sub property
     *     they inherit from SubmarineComponent (e.g. Sub.O2, Sub.Physics).
     *   - External entities (enemies, pickups) resolve the relevant
     *     Submarine from collision context or from the static All list.
     *
     * Supports runtime swapping: destroy old module, instantiate new one
     * under this root — SubmarineComponent.Awake registers it; OnDestroy
     * unregisters the old one.
     *
     * Config-driven assembly: call Build(SubmarineConfig) to instantiate
     * a full submarine loadout from a ScriptableObject definition.
     */
    public class Submarine : MonoBehaviour
    {
        // =====================
        // Static — all active submarines in the scene
        // =====================

        /** Every active Submarine in the scene. Used by enemies, spawners, and any
         *  system that needs to find submarines without a direct reference. */
        public static readonly List<Submarine> All = new();

        // =====================
        // Subsystem Slots
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        public O2System O2 { get; private set; }

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        public SubmarinePhysicsController Physics { get; private set; }

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        public Health Health { get; private set; }

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        public TurretAim Turret { get; private set; }

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        public ResourceManager Resources { get; private set; }

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        public ScrapManager Scrap { get; private set; }

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        public SubmarineFeedbackRouter Feedbacks { get; private set; }

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        public SubmarinePumpRouter Pumps { get; private set; }

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        public SubmarineAnchorRouter Anchors { get; private set; }

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        public PickupRangeDetector PickupRange { get; private set; }

        // =====================
        // Lifecycle
        // =====================

        private void Awake()
        {
            // Health lives on the root alongside Submarine, so grab it directly
            Health = GetComponent<Health>();
        }

        private void OnEnable()  => All.Add(this);
        private void OnDisable() => All.Remove(this);

        // =====================
        // Registration
        // =====================

        /**
         * Called automatically by SubmarineComponent.Awake.
         * Routes the component to the correct typed slot via pattern matching.
         * Add new cases here as new subsystem types are created.
         */
        public void Register(SubmarineComponent component)
        {
            switch (component)
            {
                case O2System o2:                      O2 = o2;       break;
                case SubmarinePhysicsController phys:   Physics = phys; break;
                case TurretAim turret:                  Turret = turret; break;
                case ResourceManager res:               Resources = res; break;
                case ScrapManager scrap:                Scrap = scrap;  break;
                case SubmarineFeedbackRouter fb:        Feedbacks = fb;  break;
                case SubmarinePumpRouter pumps:         Pumps = pumps;  break;
                case SubmarineAnchorRouter anchors:     Anchors = anchors;         break;
                case PickupRangeDetector pickupRange:   PickupRange = pickupRange; break;
            }
        }

        /**
         * Called automatically by SubmarineComponent.OnDestroy.
         * Clears the slot only if it still points to the departing component —
         * a replacement may have already registered before the old one was destroyed.
         */
        public void Unregister(SubmarineComponent component)
        {
            switch (component)
            {
                case O2System o2 when O2 == o2:                                           O2 = null;          break;
                case SubmarinePhysicsController phys when Physics == phys:                Physics = null;     break;
                case TurretAim turret when Turret == turret:                              Turret = null;      break;
                case ResourceManager res when Resources == res:                           Resources = null;   break;
                case ScrapManager scrap when Scrap == scrap:                             Scrap = null;       break;
                case SubmarineFeedbackRouter fb when Feedbacks == fb:                    Feedbacks = null;   break;
                case SubmarinePumpRouter pumps when Pumps == pumps:                      Pumps = null;       break;
                case SubmarineAnchorRouter anchors when Anchors == anchors:              Anchors = null;     break;
                case PickupRangeDetector pr when PickupRange == pr:                      PickupRange = null; break;
            }
        }

        // =====================
        // Config-Driven Assembly
        // =====================

        /**
         * Instantiates a full submarine loadout from a SubmarineConfig asset.
         * Each instantiated prefab becomes a child of this transform.
         * SubmarineComponent.Awake on each child handles registration automatically.
         */
        public void Build(SubmarineConfig config)
        {
            if (config == null) return;

            // Core systems
            if (config.hullPrefab)       Instantiate(config.hullPrefab, transform);
            if (config.o2SystemPrefab)   Instantiate(config.o2SystemPrefab, transform);
            if (config.propulsionPrefab) Instantiate(config.propulsionPrefab, transform);
            if (config.turretPrefab)     Instantiate(config.turretPrefab, transform);

            // Modular systems
            for (int i = 0; i < config.weapons.Count; i++)
                if (config.weapons[i]) Instantiate(config.weapons[i], transform);

            for (int i = 0; i < config.abilities.Count; i++)
                if (config.abilities[i]) Instantiate(config.abilities[i], transform);

            for (int i = 0; i < config.utilities.Count; i++)
                if (config.utilities[i]) Instantiate(config.utilities[i], transform);
        }

        // =====================
        // Queries
        // =====================

        /**
         * Returns the nearest Submarine to a world position.
         * Used by enemies and spawned entities that need to find a target.
         * Returns null if no submarines exist in the scene.
         */
        public static Submarine FindNearest(Vector3 worldPosition)
        {
            Submarine nearest = null;
            float bestSqrDist = float.MaxValue;

            for (int i = 0; i < All.Count; i++)
            {
                float sqrDist = (All[i].transform.position - worldPosition).sqrMagnitude;
                if (sqrDist < bestSqrDist)
                {
                    bestSqrDist = sqrDist;
                    nearest = All[i];
                }
            }

            return nearest;
        }
    }
}
