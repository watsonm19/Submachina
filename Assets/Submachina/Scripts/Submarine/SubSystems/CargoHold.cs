using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Typed cargo storage for mined resources (Sub.Cargo).
     *
     * MiningResource.Collect adds units of a ResourceType here; the hold enforces
     * a total unit capacity (upgradeable via SubStats.CargoCapacity). Contents are
     * banked to the player profile on mission extraction and lost on death.
     *
     * Carried cargo contributes mass to the sub (units × ResourceType.unitMass)
     * through SubmarinePhysicsController.RegisterMass — a full hold makes the sub
     * noticeably heavier and slower to accelerate.
     *
     * Jettison: holding the dump action releases units overboard (heaviest type
     * first, for maximum weight relief) as CargoPickup world objects that can be
     * re-collected. This is the escape valve for the ballast interplay — a hold
     * heavy enough to outweigh a full ballast tank must be dumpable or the sub
     * could be stuck on the bottom.
     */
    [UsesFeedbacks(nameof(SubFeedbacks.CargoAdded), nameof(SubFeedbacks.CargoFull),
                   nameof(SubFeedbacks.CargoRejected), nameof(SubFeedbacks.CargoDumped))]
    public class CargoHold : InputSubmarineComponent
    {
        // =====================
        // Settings
        // =====================

        [FoldoutGroup("Settings")]
        [Tooltip("Total units the hold can carry, all resource types combined. Upgradeable via SubStats.CargoCapacity.")]
        [SerializeField, Min(1)] private int baseCapacity = 20;

        // =====================
        // Dumping
        // =====================

        [FoldoutGroup("Dumping")]
        [Tooltip("Hold to jettison cargo overboard, heaviest resource type first.")]
        [SerializeField] private InputActionReference dumpAction;

        [FoldoutGroup("Dumping")]
        [Tooltip("Units released per second while the dump action is held.")]
        [SerializeField, Min(0.1f)] private float dumpRate = 2f;

        [FoldoutGroup("Dumping")]
        [Tooltip("World pickup spawned per dumped unit so jettisoned cargo can be re-collected. " +
                 "Leave empty to dump into the void.")]
        [SerializeField] private CargoPickup cargoPickupPrefab;

        [FoldoutGroup("Dumping")]
        [Tooltip("Dumped pickups scatter this far around the sub.")]
        [SerializeField, Min(0f)] private float dumpScatterRadius = 1.5f;

        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")] public UnityEvent<ResourceType, int> onCargoAdded;
        [FoldoutGroup("Events")] public UnityEvent onCargoFull;
        [FoldoutGroup("Events")] public UnityEvent<ResourceType, int> onCargoRejected;
        [FoldoutGroup("Events")] public UnityEvent<ResourceType, int> onCargoDumped;
        [FoldoutGroup("Events")] public UnityEvent onCargoChanged;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        public int TotalUnits { get; private set; }

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private int CapacityNow => Capacity;

        // =====================
        // State
        // =====================

        private readonly Dictionary<ResourceType, int> _cargo = new();
        private float _dumpAccumulator;   // fractional units accrued while the dump action is held

        /** Capacity resolved through the upgrade table. */
        public int Capacity => Mathf.RoundToInt(Sub?.Upgrades?.Stats.Resolve(SubStats.CargoCapacity, baseCapacity) ?? baseCapacity);

        public bool IsFull => TotalUnits >= Capacity;
        public int FreeSpace => Mathf.Max(0, Capacity - TotalUnits);
        public IReadOnlyDictionary<ResourceType, int> Contents => _cargo;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        protected override void Awake()
        {
            base.Awake();
            RegisterAction(dumpAction);
        }

        /** Jettison while the dump action is held: dumpRate units/sec, heaviest type first. */
        private void Update()
        {
            var action = ResolveAction(dumpAction);
            bool dumping = action != null && action.IsPressed() && TotalUnits > 0;
            if (!dumping) { _dumpAccumulator = 0f; return; }

            // Accumulate fractional units so any dumpRate feels smooth
            _dumpAccumulator += dumpRate * Time.deltaTime;
            while (_dumpAccumulator >= 1f && TotalUnits > 0)
            {
                _dumpAccumulator -= 1f;
                DumpOneUnit();
            }
        }

        // -------------------------------------------------------
        // Cargo API
        // -------------------------------------------------------

        /**
         * Stores units of a resource type, clamped to remaining capacity.
         * Returns the number of units actually stored (0 when full).
         * Example: 3 units offered with 1 slot free → 1 stored, 2 rejected,
         * fires onCargoAdded(type,1), onCargoRejected(type,2) and onCargoFull.
         */
        public int Add(ResourceType type, int units)
        {
            if (type == null || units <= 0) return 0;

            // Clamp to free space — partial stores are allowed
            int stored = Mathf.Min(units, FreeSpace);
            int rejected = units - stored;

            if (stored > 0)
            {
                _cargo.TryGetValue(type, out int have);
                _cargo[type] = have + stored;
                TotalUnits += stored;

                onCargoAdded?.Invoke(type, stored);
                onCargoChanged?.Invoke();
                Sub?.Feedbacks?.Play(SubFeedbacks.CargoAdded, transform.position);
                UpdateMassContribution();
            }

            // Report overflow so mining can feedback "hold full" without hard-blocking
            if (rejected > 0)
            {
                onCargoRejected?.Invoke(type, rejected);
                Sub?.Feedbacks?.Play(SubFeedbacks.CargoRejected, transform.position);
            }

            if (IsFull && stored > 0)
            {
                onCargoFull?.Invoke();
                Sub?.Feedbacks?.Play(SubFeedbacks.CargoFull, transform.position);
            }

            return stored;
        }

        /** Units currently held of one type. */
        public int Count(ResourceType type) =>
            type != null && _cargo.TryGetValue(type, out int have) ? have : 0;

        /**
         * Empties the hold (after banking at the hub, or on death) and
         * releases the cargo's mass contribution.
         */
        public void Clear()
        {
            _cargo.Clear();
            TotalUnits = 0;
            onCargoChanged?.Invoke();
            UpdateMassContribution();
        }

        // -------------------------------------------------------
        // Dumping
        // -------------------------------------------------------

        /**
         * Releases one unit of the HEAVIEST held type (max unitMass — the best
         * weight relief per unit) as a re-collectible world pickup behind the sub.
         */
        private void DumpOneUnit()
        {
            // Pick the heaviest type still in the hold
            ResourceType heaviest = null;
            foreach (var kvp in _cargo)
                if (kvp.Value > 0 && (heaviest == null || kvp.Key.unitMass > heaviest.unitMass)) heaviest = kvp.Key;
            if (heaviest == null) return;

            // Remove from the hold
            _cargo[heaviest] -= 1;
            if (_cargo[heaviest] <= 0) _cargo.Remove(heaviest);
            TotalUnits -= 1;
            UpdateMassContribution();

            // Spawn the re-collectible pickup, scattered near the sub
            if (cargoPickupPrefab != null)
            {
                Vector2 scatter = Random.insideUnitCircle * dumpScatterRadius;
                var pickup = Instantiate(cargoPickupPrefab, transform.position + (Vector3)scatter, Quaternion.identity);
                pickup.Init(heaviest, 1);
            }

            onCargoDumped?.Invoke(heaviest, 1);
            onCargoChanged?.Invoke();
            Sub?.Feedbacks?.Play(SubFeedbacks.CargoDumped, transform.position);
        }

        // -------------------------------------------------------
        // Mass contribution
        // -------------------------------------------------------

        /** Pushes the hold's current mass (Σ units × unitMass) into the physics aggregator. */
        private void UpdateMassContribution()
        {
            if (Sub?.Physics == null) return;

            float mass = 0f;
            foreach (var kvp in _cargo) mass += kvp.Value * kvp.Key.unitMass;
            Sub.Physics.RegisterMass(this, mass);
        }

        protected override void OnDestroy()
        {
            Sub?.Physics?.UnregisterMass(this);
            base.OnDestroy();
        }
    }
}
