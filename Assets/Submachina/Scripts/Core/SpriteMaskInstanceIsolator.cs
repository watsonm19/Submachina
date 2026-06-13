using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * Confines a SpriteMask to only affect renderers inside its own prefab instance.
     *
     * Problem this solves: SpriteMasks are global — any renderer in the scene with
     * Mask Interaction enabled is affected by EVERY active mask. When several copies
     * of a masked effect coexist (e.g. pooled attack VFX), each instance's mask
     * "reveals" the other instances' particles outside their own arcs.
     *
     * Fix: each instance claims a unique sorting-order band. All of its child
     * renderers are shifted into that band (keeping their relative order), and the
     * mask's Custom Range is set to cover only that band. Masks in band A then
     * ignore renderers in band B entirely.
     *
     * Example with bandStride 10: instance 0 → orders 0..9, instance 1 → 10..19, etc.
     * Slots cycle after maxSlots instances so sorting orders stay bounded.
     *
     * Works with pooling: bands are claimed once per instance in Awake, so pooled
     * objects keep their band across reuse.
     */
    [RequireComponent(typeof(SpriteMask))]
    public class SpriteMaskInstanceIsolator : MonoBehaviour
    {
        // =====================
        // Config
        // =====================

        [FoldoutGroup("Config")]
        [Tooltip("Sorting-order width reserved per instance. Must exceed the spread of " +
                 "sorting orders used by this effect's renderers (usually they're all 0).")]
        [SerializeField] private int bandStride = 10;

        [FoldoutGroup("Config")]
        [Tooltip("How many unique bands exist before slots cycle. Only needs to cover " +
                 "the number of instances alive at the same time.")]
        [SerializeField] private int maxSlots = 32;

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
        private int _baseOrder = -1;

        // =====================
        // State
        // =====================

        // Global slot counter shared by all isolators — each instance grabs the next slot.
        private static int _nextSlot;

        private SpriteMask _mask;
        private Renderer[] _renderers;
        private int[] _originalOrders;

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------

        /**
         * Claims a sorting band and rewires the mask + child renderers into it.
         * Runs once per instance; pooled reuse keeps the same band.
         */
        private void Awake()
        {
            _mask = GetComponent<SpriteMask>();
            CacheRenderers();
            ApplyBand((_nextSlot++ % maxSlots) * bandStride);
        }

        // -------------------------------------------------------
        // Band Assignment
        // -------------------------------------------------------

        /**
         * Collects every child renderer (excluding SpriteMasks, which also derive
         * from Renderer) and remembers their authored sorting orders so the band
         * shift preserves the effect's internal layering.
         */
        private void CacheRenderers()
        {
            var all = GetComponentsInChildren<Renderer>(true);

            // Count non-mask renderers first so the arrays are exact
            int count = 0;
            foreach (var r in all) if (!(r is SpriteMask)) count++;

            _renderers = new Renderer[count];
            _originalOrders = new int[count];
            int i = 0;
            foreach (var r in all)
            {
                if (r is SpriteMask) continue;
                _renderers[i] = r;
                _originalOrders[i] = r.sortingOrder;
                i++;
            }
        }

        /**
         * Shifts all renderers into the band starting at baseOrder and points the
         * mask's Custom Range at exactly that band, e.g. base 20 with renderers
         * authored at 0/1 → orders 20/21, mask range covers 19..22.
         */
        private void ApplyBand(int baseOrder)
        {
            _baseOrder = baseOrder;

            // Move renderers into the band, preserving their relative order
            int min = int.MaxValue, max = int.MinValue;
            for (int i = 0; i < _renderers.Length; i++)
            {
                _renderers[i].sortingOrder = baseOrder + _originalOrders[i];
                if (_originalOrders[i] < min) min = _originalOrders[i];
                if (_originalOrders[i] > max) max = _originalOrders[i];
            }
            if (_renderers.Length == 0) { min = 0; max = 0; }

            // Confine the mask to this band only — other instances' bands are untouched
            int layerId = _renderers.Length > 0 ? _renderers[0].sortingLayerID : 0;
            _mask.isCustomRangeActive = true;
            _mask.backSortingLayerID = layerId;
            _mask.frontSortingLayerID = layerId;
            _mask.backSortingOrder = baseOrder + min - 1;
            _mask.frontSortingOrder = baseOrder + max + 1;
        }
    }
}
