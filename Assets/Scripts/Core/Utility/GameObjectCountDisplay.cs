using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Utility
{
    /**
     * Enables the first N GameObjects in a list and disables the rest, where N
     * comes from an external count (e.g. remaining harvests, health pips, ammo).
     *
     * Two modes of operation:
     *   Toggle mode (prefab is null): enables/disables objects directly.
     *   Prefab mode (prefab assigned): treats the objects list as spawn locations
     *     and instantiates/destroys prefab instances at those positions.
     *
     * Wire the public SetCount(int) method to any UnityEvent<int> — for example,
     * HarvestableEffect.onHarvestCountChanged — and drag your visual objects into
     * the list in the order they should appear.
     *
     * Objects at index 0..count-1 are enabled; objects at index count..end are disabled.
     * A count of 0 disables everything; a count >= list length enables everything.
     */
    [AddComponentMenu("Utility/GameObject Count Display")]
    public class GameObjectCountDisplay : MonoBehaviour
    {
        // ── Configuration ────────────────────────────────────────────────

        [TitleGroup("Objects")]
        [Tooltip("GameObjects to enable/disable based on count. Index 0 is enabled first.")]
        [ListDrawerSettings(ShowIndexLabels = true, DraggableItems = true)]
        [SerializeField]
        List<GameObject> objects = new();

        [TitleGroup("Prefab Mode")]
        [Tooltip("Optional prefab to instantiate at each object's transform. " +
                 "When assigned, objects are treated as spawn locations rather than toggled directly.")]
        [SerializeField]
        GameObject prefab;

        [TitleGroup("Prefab Mode")]
        [Tooltip("When true, spawned instances inherit the location object's local scale.")]
        [SerializeField]
        bool inheritScale = false;

        [TitleGroup("Options")]
        [Tooltip("When true, initializes count to the full list length on Awake " +
                 "(all objects enabled). Disable if the source event fires before " +
                 "or during Awake with the correct initial value.")]
        [SerializeField]
        bool initializeToMax = true;

        // ── Runtime State ────────────────────────────────────────────────

        [TitleGroup("Runtime")]
        [ShowInInspector, ReadOnly]
        int currentCount;

        // Tracks spawned instances in prefab mode (parallel to objects list, null when unused)
        List<GameObject> _instances;

        // ── Lifecycle ────────────────────────────────────────────────────

        void Awake()
        {
            EnsureInstanceList();
            if (initializeToMax) SetCount(objects.Count);
        }

        void OnDestroy()
        {
            // Clean up any spawned instances
            if (_instances == null) return;
            for (int i = 0; i < _instances.Count; i++)
            {
                if (_instances[i] != null) Destroy(_instances[i]);
            }
        }

        // ── Public API ───────────────────────────────────────────────────

        /**
         * Sets how many objects should be active. Clamped to [0, list length].
         *
         * Toggle mode: enables index 0..count-1, disables the rest.
         * Prefab mode: ensures prefab instances exist at locations 0..count-1,
         *   destroys instances beyond that range.
         *
         * Example: 3 objects in the list, SetCount(2) → slots 0 and 1 active,
         * slot 2 inactive/destroyed.
         */
        public void SetCount(int count)
        {
            currentCount = Mathf.Clamp(count, 0, objects.Count);

            if (UsePrefabMode)
                ApplyPrefabMode();
            else
                ApplyToggleMode();
        }

        // ── Internal ─────────────────────────────────────────────────────

        bool UsePrefabMode => prefab != null;

        /** Lazily initializes or grows the instance tracking list (guards against pre-Awake calls). */
        void EnsureInstanceList()
        {
            if (!UsePrefabMode) return;

            if (_instances == null)
            {
                _instances = new List<GameObject>(new GameObject[objects.Count]);
                return;
            }

            // If objects list grew since last init (e.g. runtime additions), pad to match
            while (_instances.Count < objects.Count)
                _instances.Add(null);
        }

        /** Toggle mode: enable/disable the objects directly. */
        void ApplyToggleMode()
        {
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i] != null) objects[i].SetActive(i < currentCount);
            }
        }

        /**
         * Prefab mode: replace placeholder objects with prefab instances on first use.
         * Placeholders are permanently hidden once replaced. Count changes simply
         * toggle the spawned instances active/inactive.
         */
        void ApplyPrefabMode()
        {
            EnsureInstanceList();

            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i] == null) continue;

                // First time seeing this slot — spawn the instance and retire the placeholder
                if (_instances[i] == null)
                {
                    var placeholder = objects[i].transform;
                    var parent = placeholder.parent;
                    int siblingIndex = placeholder.GetSiblingIndex();

                    var instance = Instantiate(prefab, placeholder.position, placeholder.rotation, parent);
                    instance.transform.localScale = inheritScale
                        ? placeholder.localScale
                        : instance.transform.localScale;
                    instance.transform.SetSiblingIndex(siblingIndex);

                    // Copy RectTransform anchoring so UI layout matches the placeholder
                    if (placeholder is RectTransform srcRect &&
                        instance.transform is RectTransform dstRect)
                    {
                        dstRect.anchorMin = srcRect.anchorMin;
                        dstRect.anchorMax = srcRect.anchorMax;
                        dstRect.anchoredPosition = srcRect.anchoredPosition;
                        dstRect.sizeDelta = srcRect.sizeDelta;
                        dstRect.pivot = srcRect.pivot;
                    }

                    // Permanently hide the placeholder
                    objects[i].SetActive(false);
                    _instances[i] = instance;
                }

                // Toggle the spawned instance based on count
                _instances[i].SetActive(i < currentCount);
            }
        }

        // ── Testing (Odin) ───────────────────────────────────────────────

        [TitleGroup("Testing")]
        [Button("Set Count"), PropertyRange(0, "MaxCount")]
        void TestSetCount(int count) => SetCount(count);

        int MaxCount => objects.Count;
    }
}
