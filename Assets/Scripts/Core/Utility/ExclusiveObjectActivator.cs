using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;

/// <summary>
/// What happens when the component first awakens.
/// </summary>
public enum StartupBehavior
{
    LeaveAsIs,      // Sync to scene state, don't change anything
    DisableAll,     // Deactivate all groups on startup
    ActivateFirst   // Activate the first group on startup
}

/// <summary>
/// Behavior applied when stepping past the first or last slot in the list.
/// </summary>
public enum EndOfListBehavior
{
    Loop,       // Wrap around to the opposite end
    Stay,       // Remain on the current slot (no change)
    DisableAll  // Deactivate all objects and record boundary position
}

/// <summary>
/// A named group of GameObjects that are activated and deactivated together as one logical slot.
/// Using a single-item group is equivalent to the simple one-object-per-slot case.
/// </summary>
[System.Serializable]
public class ObjectGroup
{
    [HorizontalGroup(width: 90), LabelWidth(40)]
    [Tooltip("Optional label for editor clarity.")]
    public string label = "";

    [HorizontalGroup, HideLabel]
    [Tooltip("All objects in this group activate and deactivate together.")]
    public List<GameObject> objects = new();
}

/// <summary>
/// Maintains exclusive activation over a list of ObjectGroups — only one group is active at a time.
/// Each group can contain one or more GameObjects that activate and deactivate together.
/// Exposes Unity Event-compatible methods for stepping through the list or jumping to any index.
///
/// Index conventions:
///   -1 = unstarted (no group has ever been activated via this component)
///    0..Count-1 = normal index (last known active, or boundary anchor when all disabled)
///
/// DisableAll boundary logic:
///   When DisableAll triggers stepping Next off the end:  _currentIndex stays at Count-1.
///     → Subsequent Next calls stay disabled; Prev re-enters from the back.
///   When DisableAll triggers stepping Prev off the start: _currentIndex stays at 0.
///     → Subsequent Prev calls stay disabled; Next re-enters from the front.
///   When _currentIndex is -1 (unstarted): Next activates 0, Prev stays disabled.
/// </summary>
[AddComponentMenu("Utility/Exclusive Object Activator")]
public class ExclusiveObjectActivator : MonoBehaviour
{
    // =====================================================================
    // Inspector

    [BoxGroup("Groups")]
    [Tooltip("Each slot is a group of GameObjects that activate together. Only one slot is active at a time.")]
    [SerializeField] private List<ObjectGroup> _groups = new();

    [BoxGroup("Behavior")]
    [Tooltip("When enabled, activation is cumulative — Next adds groups, Prev peels them back. " +
             "ActivateIndex(n) activates all groups 0..n.")]
    [SerializeField] private bool _additive = false;

    [BoxGroup("Behavior")]
    [Tooltip("Only meaningful when Additive is on. When enabled, Next/Prev/ActivateIndex only " +
             "toggle the single target group and leave the state of other groups untouched. " +
             "Useful when something else may disable a group and you don't want it re-enabled " +
             "on the next step.")]
    [ShowIf("_additive")]
    [SerializeField] private bool _additiveIncrementalOnly = false;

    [BoxGroup("Behavior")]
    [Tooltip("What happens when the component first awakens.")]
    [SerializeField] private StartupBehavior _startupBehavior = StartupBehavior.LeaveAsIs;

    [BoxGroup("Behavior")]
    [Tooltip("What happens when stepping past the first or last slot.")]
    [SerializeField] private EndOfListBehavior _endOfListBehavior = EndOfListBehavior.Loop;

    [BoxGroup("Events")]
    [Tooltip("Fires when the active index changes. Passes -1 when all objects are disabled.")]
    [SerializeField] private UnityEvent<int> _onIndexChanged = new();

    // =====================================================================
    // State

    [BoxGroup("Debug"), ShowInInspector, ReadOnly]
    private int _currentIndex = -1;

    [BoxGroup("Debug"), ShowInInspector, ReadOnly]
    private bool _allDisabled = false;

    // =====================================================================
    // Lifecycle

    private void Awake()
    {
        switch (_startupBehavior)
        {
            // Sync with whatever active state was configured in the scene.
            // Use the first group that contains at least one active object.
            case StartupBehavior.LeaveAsIs:
                _currentIndex = -1;
                _allDisabled = true;
                for (int i = 0; i < _groups.Count; i++)
                {
                    foreach (var obj in _groups[i].objects)
                    {
                        if (obj != null && obj.activeSelf)
                        {
                            _currentIndex = i;
                            _allDisabled = false;
                            goto SyncDone;
                        }
                    }
                }
                SyncDone:;
                break;

            // Deactivate all groups on startup.
            case StartupBehavior.DisableAll:
                DisableAll();
                break;

            // Activate the first group on startup.
            case StartupBehavior.ActivateFirst:
                ActivateIndex(0);
                break;
        }
    }

    // =====================================================================
    // Public API

    /**
     * Activates all objects in the group at the given index, deactivating all objects in every other group.
     * Out-of-range indices are ignored with a warning.
     */
    [BoxGroup("Debug")]
    [Button("Activate Index")]
    public void ActivateIndex(int index)
    {
        if (_groups.Count == 0) return;
        if (index < 0 || index >= _groups.Count)
        {
            Debug.LogWarning($"[ExclusiveObjectActivator] Index {index} is out of range (0–{_groups.Count - 1}).");
            return;
        }

        // Additive + incremental-only: just turn on the target group, leave others as-is.
        // This avoids re-enabling groups that were intentionally disabled by something else.
        if (_additive && _additiveIncrementalOnly)
        {
            foreach (var obj in _groups[index].objects)
                if (obj != null) obj.SetActive(true);
        }
        else
        {
            // In additive mode, activate everything up to and including the target index.
            // In exclusive mode, only the target index is active.
            for (int i = 0; i < _groups.Count; i++)
            {
                bool active = _additive ? i <= index : i == index;
                foreach (var obj in _groups[i].objects)
                    if (obj != null) obj.SetActive(active);
            }
        }

        _currentIndex = index;
        _allDisabled = false;
        _onIndexChanged.Invoke(_currentIndex);
    }

    /**
     * Activates the first group in the list.
     */
    [HorizontalGroup("Debug/Nav"), Button("◀◀ First")]
    public void ActivateFirst()
    {
        if (_groups.Count == 0) return;
        ActivateIndex(0);
    }

    /**
     * Activates the last group in the list.
     */
    [HorizontalGroup("Debug/Nav"), Button("Last ▶▶")]
    public void ActivateLast()
    {
        if (_groups.Count == 0) return;
        ActivateIndex(_groups.Count - 1);
    }

    /**
     * Activates the next group in the list.
     *
     * When all are disabled:
     *   - If _currentIndex is at the end (Count-1): stay disabled (already ran off the end).
     *   - Otherwise (including -1 unstarted): activate index 0.
     *
     * When active and at the last group, applies _endOfListBehavior.
     */
    [HorizontalGroup("Debug/Nav"), Button("Next ▶")]
    public void ActivateNext()
    {
        if (_groups.Count == 0) return;

        // --- Disabled state: decide whether to re-enter or stay disabled ---
        if (_allDisabled)
        {
            if (_currentIndex == _groups.Count - 1) return; // Disabled at end — stay disabled.
            ActivateIndex(0);                                // Unstarted or disabled at start — begin from front.
            return;
        }

        int next = _currentIndex + 1;

        // --- Normal advance ---
        if (next < _groups.Count)
        {
            ActivateIndex(next);
            return;
        }

        // --- At the end — apply configured behavior ---
        switch (_endOfListBehavior)
        {
            case EndOfListBehavior.Loop:
                ActivateIndex(0);
                break;
            case EndOfListBehavior.Stay:
                break; // Already at last, no change.
            case EndOfListBehavior.DisableAll:
                DisableAll();
                // _currentIndex remains Count-1 so subsequent Next calls stay disabled.
                break;
        }
    }

    /**
     * Activates the previous group in the list.
     *
     * Mirrors ActivateNext for its end-of-list (start-of-list) handling:
     *
     * When all are disabled:
     *   - If _currentIndex <= 0 (at start or unstarted): stay disabled.
     *   - If _currentIndex is at the end (Count-1): re-enter from the back.
     *
     * When active and at the first group, applies _endOfListBehavior.
     */
    [HorizontalGroup("Debug/Nav"), Button("◀ Prev")]
    public void ActivatePrevious()
    {
        if (_groups.Count == 0) return;

        // --- Disabled state: decide whether to re-enter or stay disabled ---
        if (_allDisabled)
        {
            if (_currentIndex <= 0) return;             // Disabled at start (or unstarted) — stay disabled.
            ActivateIndex(_groups.Count - 1);           // Disabled at end — re-enter from back.
            return;
        }

        int prev = _currentIndex - 1;

        // --- Normal retreat ---
        if (prev >= 0)
        {
            // Additive + incremental-only: peel back by disabling just the current group,
            // leaving everything below it untouched.
            if (_additive && _additiveIncrementalOnly)
            {
                foreach (var obj in _groups[_currentIndex].objects)
                    if (obj != null) obj.SetActive(false);
                _currentIndex = prev;
                _onIndexChanged.Invoke(_currentIndex);
                return;
            }

            ActivateIndex(prev);
            return;
        }

        // --- At the start — apply configured behavior ---
        switch (_endOfListBehavior)
        {
            case EndOfListBehavior.Loop:
                ActivateIndex(_groups.Count - 1);
                break;
            case EndOfListBehavior.Stay:
                break; // Already at first, no change.
            case EndOfListBehavior.DisableAll:
                DisableAll();
                // _currentIndex remains 0 so subsequent Prev calls stay disabled.
                break;
        }
    }

    /**
     * Deactivates all objects across all groups and fires the change event with index -1.
     * Can be called directly from a UnityEvent.
     */
    [HorizontalGroup("Debug/Nav"), Button("Disable All")]
    public void DisableAll()
    {
        foreach (var group in _groups)
            foreach (var obj in group.objects)
                if (obj != null) obj.SetActive(false);

        _allDisabled = true;
        _onIndexChanged.Invoke(-1);
    }
}
