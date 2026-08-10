using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * An O2 bubble collectible dropped by enemies when killed.
     *
     * When the player's collider overlaps this trigger, it calls AddO2 on
     * the O2System and destroys itself. Enemies will instantiate this prefab
     * on death — the pickup itself has no knowledge of how it was spawned.
     *
     * Setup:
     *   - Attach to a prefab with a CircleCollider2D set as Is Trigger.
     *   - Assign the scene's O2System (injected at runtime by WorldChunk).
     *   - Tag the player GameObject as "Player".
     */
    [RequireComponent(typeof(Collider2D))]
    public class O2Pickup : MonoBehaviour
    {
        // =====================
        // Settings
        // =====================

        [FoldoutGroup("Settings")]
        [Tooltip("How much air pressure this bubble restores when collected.")]
        [SerializeField, Min(0f)] private float replenishAmount = 10f;

        [FoldoutGroup("Settings")]
        [Tooltip("Smallest size value that can be passed to SetSize(). " +
                 "Sizes below this are clamped. Also defines the left edge of sizeRewardCurve's X axis.")]
        [SerializeField, Min(0.1f)] private float sizeRangeMin = 0.5f;

        [FoldoutGroup("Settings")]
        [Tooltip("Largest size value that can be passed to SetSize(). " +
                 "Sizes above this are clamped. Also defines the right edge of sizeRewardCurve's X axis.")]
        [SerializeField, Min(0.1f)] private float sizeRangeMax = 5f;

        [FoldoutGroup("Settings")]
        [Tooltip("Maps bubble size (X axis) to a reward multiplier (Y axis). " +
                 "Both replenishAmount and capacityRestoreAmount are scaled by this curve's output. " +
                 "The X axis should span from sizeRangeMin to sizeRangeMax.")]
        [InfoBox("$SizeRewardPreview")]
        [SerializeField]
        private AnimationCurve sizeRewardCurve = AnimationCurve.Linear(0.5f, 0.3f, 5f, 15f);

        private string SizeRewardPreview
        {
            get
            {
                if (sizeRewardCurve == null) return "No curve assigned.";
                if (sizeRangeMin >= sizeRangeMax) return "sizeRangeMin must be less than sizeRangeMax.";

                // Sample 5 evenly spaced points across the size range
                string result = "";
                for (int i = 0; i <= 4; i++)
                {
                    float size = Mathf.Lerp(sizeRangeMin, sizeRangeMax, i / 4f);
                    float mult = sizeRewardCurve.Evaluate(size);
                    float air = replenishAmount * mult;
                    if (i > 0) result += "\n";
                    result += $"Size {size:F1}:  x{mult:F1} mult  ->  {air:F1} air";
                }
                return result;
            }
        }

        [FoldoutGroup("Settings")]
        [Tooltip("If true, the player collects this pickup just by touching it. " +
                 "Disabled by default — collection now goes through O2PickupPump's " +
                 "sweet spot mechanic, which calls Collect() directly.")]
        [SerializeField] private bool collectOnContact;
        // =====================
        // Events
        // =====================

        [FoldoutGroup("Events")]
        [Tooltip("Fired the moment this bubble is collected, before it is destroyed. " +
                 "Wire VFX/SFX (e.g. a ripple emit) here.")]
        public UnityEvent onCollected;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private float SizeMultiplier => _sizeMultiplier;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector, LabelText("Effective Air")]
        private float EffectiveAir => replenishAmount * _sizeMultiplier;

        // =====================
        // State
        // =====================

        private Vector3 _baseScale;
        private float _sizeMultiplier = 1f;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            // Capture the prefab's authored scale so SetSize works relative to it
            _baseScale = transform.localScale;

            // Ensure the collider is a trigger — pickups should never block movement
            Collider2D col = GetComponent<Collider2D>();
            col.isTrigger = true;
        }

        // -------------------------------------------------------
        // Size Configuration
        // -------------------------------------------------------

        /**
         * Configures this pickup's visual size and reward multiplier.
         * Call immediately after Instantiate().
         *
         * The size value scales relative to the prefab's authored scale —
         * size 1.0 preserves the original visual, size 3.0 makes it 3x larger.
         * The reward multiplier is evaluated from sizeRewardCurve independently,
         * so large bubbles can grant disproportionately more air than their
         * visual size suggests.
         *
         * Example: size=2.0, curve maps 2.0→3.0 → bubble is 2x bigger and
         * grants 3x the base replenish/capacity amounts.
         */
        public void SetSize(float size)
        {
            size = Mathf.Clamp(size, sizeRangeMin, sizeRangeMax);

            // Visual: scale relative to the prefab's authored scale
            transform.localScale = _baseScale * size;

            // Reward: evaluate the designer-tuned curve
            _sizeMultiplier = sizeRewardCurve.Evaluate(size);
        }

        // -------------------------------------------------------
        // Collection
        // -------------------------------------------------------

        /**
         * Fires when any collider enters this trigger.
         * Resolves which submarine collected the pickup from the collision context.
         */
        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!collectOnContact) return;

            Submarine sub = other.GetComponentInParent<Submarine>();
            if (sub == null) return;

            Collect(sub);
        }

        /**
         * Restores O2 (or, with the ballast pump destination set to Ballast,
         * fills the ballast tank with the air instead) and destroys this pickup. Returns
         * the actual air amount added/spent (after size and timing multipliers)
         * so callers can display it.
         *
         * airMultiplier scales the air granted — lets the collector grade the
         * reward by timing quality. This is orthogonal to the size multiplier:
         * both stack multiplicatively.
         *
         * Example: replenishAmount=10, sizeMultiplier=3.0, airMultiplier=0.35
         * → weak pump on a large bubble restores 10 * 3.0 * 0.35 = 10.5 air.
         *
         * Ballast routing: when the sub carries a BallastTank and its shared
         * pump destination is set to Ballast, the captured air fills the tank
         * (adding lift) instead of topping up the O2 reserve; overflow past a
         * full tank still banks into the reserve.
         */
        public float Collect(Submarine sub, float airMultiplier = 1f)
        {
            float airAmount = 0f;

            if (sub?.O2 != null)
            {
                airAmount = replenishAmount * _sizeMultiplier * airMultiplier;

                if (sub.Ballast != null && sub.Ballast.Destination == PumpDestination.Ballast)
                {
                    // Fill the ballast tank with the captured air; overflow past a
                    // full tank still banks into the main reserve (never wasted)
                    float overflow = sub.Ballast.AddAirToBallast(airAmount);
                    if (overflow > 0f) sub.O2.AddAir(overflow);
                }
                else
                    sub.O2.AddAir(airAmount);
            }
            else
                Debug.LogWarning("[O2Pickup] No Submarine O2System available — pickup consumed but air not restored.");

            onCollected?.Invoke();
            Destroy(gameObject);
            return airAmount;
        }
    }
}
