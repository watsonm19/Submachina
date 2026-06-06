using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;

namespace Utility
{
    /**
     * Groups collections of UnityEvents under named identifiers and exposes a
     * cursor-based API for sequential playback, as well as direct index/name invocation.
     *
     * Each ActionGroup holds four event slots — parameterless, int, float, and string —
     * so callers can pass optional context without separate wiring. Only the slot matching
     * the call overload fires; unused slots are ignored.
     *
     * All public methods are no-ops when the component is disabled or inactive.
     */
    [AddComponentMenu("Utility/Action Group Driver")]
    public class ActionGroupDriver : MonoBehaviour
    {
        // ── Nested types ──────────────────────────────────────────────

        [Serializable]
        public class ActionGroup
        {
            [LabelWidth(60)]
            public string name;

            // Four event slots; use whichever variant matches your call site.
            [FoldoutGroup("Events")]
            public UnityEvent onPlay;

            [FoldoutGroup("Events")]
            public UnityEvent<int> onPlayInt;

            [FoldoutGroup("Events")]
            public UnityEvent<float> onPlayFloat;

            [FoldoutGroup("Events")]
            public UnityEvent<string> onPlayString;
        }

        // ── Serialized fields ─────────────────────────────────────────

        [TitleGroup("Groups")]
        [ListDrawerSettings(ShowIndexLabels = true, DraggableItems = true)]
        [SerializeField] List<ActionGroup> groups = new();

        // ── Runtime state ─────────────────────────────────────────────

        int _cursor;

        // ── Properties ────────────────────────────────────────────────

        /** Current cursor index. */
        public int Cursor => _cursor;

        /** Number of defined groups. */
        public int GroupCount => groups.Count;

        // ── Debug display ─────────────────────────────────────────────

        [TitleGroup("Debug")]
        [ShowInInspector, ReadOnly]
        string CurrentGroupName
        {
            get
            {
                if (groups.Count == 0) return "(none)";
                return _cursor >= 0 && _cursor < groups.Count ? $"[{_cursor}] {groups[_cursor].name}" : "(invalid)";
            }
        }

        // ── Public API — cursor control ───────────────────────────────

        /**
         * Advances the cursor to the next group.
         * Wraps from the last group back to index 0.
         */
        public void Advance()
        {
            if (!isActiveAndEnabled || groups.Count == 0) return;
            _cursor = (_cursor + 1) % groups.Count;
        }

        /**
         * Retreats the cursor to the previous group.
         * Wraps from index 0 back to the last group.
         */
        public void Retreat()
        {
            if (!isActiveAndEnabled || groups.Count == 0) return;
            _cursor = (_cursor - 1 + groups.Count) % groups.Count;
        }

        /** Moves the cursor to the specified index without playing anything. */
        public void SetCursor(int index)
        {
            if (!isActiveAndEnabled || !ValidateIndex(index)) return;
            _cursor = index;
        }

        /** Moves the cursor to the named group without playing anything. */
        public void SetCursor(string groupName)
        {
            if (!isActiveAndEnabled) return;
            int index = FindIndexByName(groupName);
            if (index >= 0) _cursor = index;
        }

        // ── Public API — play at cursor ───────────────────────────────

        /** Fires the current group's parameterless event. Cursor does not move. */
        public void PlayCurrent()
        {
            if (!isActiveAndEnabled || !HasGroups()) return;
            FireAt(_cursor);
        }

        /** Fires the current group's int event. Cursor does not move. */
        public void PlayCurrent(int value)
        {
            if (!isActiveAndEnabled || !HasGroups()) return;
            FireAt(_cursor, value);
        }

        /** Fires the current group's float event. Cursor does not move. */
        public void PlayCurrent(float value)
        {
            if (!isActiveAndEnabled || !HasGroups()) return;
            FireAt(_cursor, value);
        }

        /** Fires the current group's string event. Cursor does not move. */
        public void PlayCurrent(string value)
        {
            if (!isActiveAndEnabled || !HasGroups()) return;
            FireAt(_cursor, value);
        }

        // ── Public API — play at cursor + advance ─────────────────────

        /** Fires the current group's parameterless event, then advances the cursor. */
        public void PlayAndAdvance()
        {
            if (!isActiveAndEnabled || !HasGroups()) return;
            FireAt(_cursor);
            _cursor = (_cursor + 1) % groups.Count;
        }

        /** Fires the current group's int event, then advances the cursor. */
        public void PlayAndAdvance(int value)
        {
            if (!isActiveAndEnabled || !HasGroups()) return;
            FireAt(_cursor, value);
            _cursor = (_cursor + 1) % groups.Count;
        }

        /** Fires the current group's float event, then advances the cursor. */
        public void PlayAndAdvance(float value)
        {
            if (!isActiveAndEnabled || !HasGroups()) return;
            FireAt(_cursor, value);
            _cursor = (_cursor + 1) % groups.Count;
        }

        /** Fires the current group's string event, then advances the cursor. */
        public void PlayAndAdvance(string value)
        {
            if (!isActiveAndEnabled || !HasGroups()) return;
            FireAt(_cursor, value);
            _cursor = (_cursor + 1) % groups.Count;
        }

        // ── Public API — play at cursor + retreat ─────────────────────

        /** Fires the current group's parameterless event, then retreats the cursor. */
        public void PlayAndRetreat()
        {
            if (!isActiveAndEnabled || !HasGroups()) return;
            FireAt(_cursor);
            _cursor = (_cursor - 1 + groups.Count) % groups.Count;
        }

        /** Fires the current group's int event, then retreats the cursor. */
        public void PlayAndRetreat(int value)
        {
            if (!isActiveAndEnabled || !HasGroups()) return;
            FireAt(_cursor, value);
            _cursor = (_cursor - 1 + groups.Count) % groups.Count;
        }

        /** Fires the current group's float event, then retreats the cursor. */
        public void PlayAndRetreat(float value)
        {
            if (!isActiveAndEnabled || !HasGroups()) return;
            FireAt(_cursor, value);
            _cursor = (_cursor - 1 + groups.Count) % groups.Count;
        }

        /** Fires the current group's string event, then retreats the cursor. */
        public void PlayAndRetreat(string value)
        {
            if (!isActiveAndEnabled || !HasGroups()) return;
            FireAt(_cursor, value);
            _cursor = (_cursor - 1 + groups.Count) % groups.Count;
        }

        // ── Public API — direct index play ────────────────────────────

        /**
         * Jumps the cursor to the given index and fires its parameterless event.
         * Use this when you need to trigger a specific group regardless of cursor position.
         */
        public void PlayAt(int index)
        {
            if (!isActiveAndEnabled || !ValidateIndex(index)) return;
            _cursor = index;
            FireAt(index);
        }

        /** Jumps the cursor to the given index and fires its int event. */
        public void PlayAt(int index, int value)
        {
            if (!isActiveAndEnabled || !ValidateIndex(index)) return;
            _cursor = index;
            FireAt(index, value);
        }

        /** Jumps the cursor to the given index and fires its float event. */
        public void PlayAt(int index, float value)
        {
            if (!isActiveAndEnabled || !ValidateIndex(index)) return;
            _cursor = index;
            FireAt(index, value);
        }

        /** Jumps the cursor to the given index and fires its string event. */
        public void PlayAt(int index, string value)
        {
            if (!isActiveAndEnabled || !ValidateIndex(index)) return;
            _cursor = index;
            FireAt(index, value);
        }

        // ── Public API — direct name play ─────────────────────────────

        /**
         * Finds the group by name, jumps the cursor there, and fires its parameterless event.
         * Logs a warning if the name is not found.
         */
        public void PlayNamed(string groupName)
        {
            if (!isActiveAndEnabled) return;
            int index = FindIndexByName(groupName);
            if (index < 0) return;
            _cursor = index;
            FireAt(index);
        }

        /** Finds the group by name, jumps the cursor there, and fires its int event. */
        public void PlayNamed(string groupName, int value)
        {
            if (!isActiveAndEnabled) return;
            int index = FindIndexByName(groupName);
            if (index < 0) return;
            _cursor = index;
            FireAt(index, value);
        }

        /** Finds the group by name, jumps the cursor there, and fires its float event. */
        public void PlayNamed(string groupName, float value)
        {
            if (!isActiveAndEnabled) return;
            int index = FindIndexByName(groupName);
            if (index < 0) return;
            _cursor = index;
            FireAt(index, value);
        }

        /** Finds the group by name, jumps the cursor there, and fires its string event. */
        public void PlayNamed(string groupName, string value)
        {
            if (!isActiveAndEnabled) return;
            int index = FindIndexByName(groupName);
            if (index < 0) return;
            _cursor = index;
            FireAt(index, value);
        }

        // ── Testing buttons (Odin) ────────────────────────────────────

        [TitleGroup("Testing")]
        [HorizontalGroup("Testing/Cursor"), Button("◀ Retreat")]
        void TestRetreat() => Retreat();

        [HorizontalGroup("Testing/Cursor"), Button("Advance ▶")]
        void TestAdvance() => Advance();

        [TitleGroup("Testing")]
        [HorizontalGroup("Testing/Play"), Button("Play Current")]
        void TestPlayCurrent() => PlayCurrent();

        [HorizontalGroup("Testing/Play"), Button("Play & Advance")]
        void TestPlayAndAdvance() => PlayAndAdvance();

        [HorizontalGroup("Testing/Play"), Button("Play & Retreat")]
        void TestPlayAndRetreat() => PlayAndRetreat();

        [TitleGroup("Testing")]
        [HorizontalGroup("Testing/Direct"), Button("Play At Index")]
        void TestPlayAt(int index) => PlayAt(index);

        [HorizontalGroup("Testing/Direct"), Button("Play Named")]
        void TestPlayNamed(string groupName) => PlayNamed(groupName);

        // ── Internals ─────────────────────────────────────────────────

        /**
         * Guards against an empty or out-of-range cursor before playback.
         * Also clamps the cursor defensively in case groups were removed in the inspector.
         * Example: cursor was 3, then group list shrank to 2 items → clamps to 1.
         */
        bool HasGroups()
        {
            if (groups.Count == 0) return false;
            _cursor = Mathf.Clamp(_cursor, 0, groups.Count - 1);
            return true;
        }

        void FireAt(int index) => groups[index].onPlay?.Invoke();
        void FireAt(int index, int value) => groups[index].onPlayInt?.Invoke(value);
        void FireAt(int index, float value) => groups[index].onPlayFloat?.Invoke(value);
        void FireAt(int index, string value) => groups[index].onPlayString?.Invoke(value);

        /** Validates that an index is within the groups list. Logs a warning if not. */
        bool ValidateIndex(int index)
        {
            if (index >= 0 && index < groups.Count) return true;
            Debug.LogWarning($"[ActionGroupDriver] Index {index} out of range (0..{groups.Count - 1})", this);
            return false;
        }

        /** Finds a group index by name. Logs a warning and returns -1 if not found. */
        int FindIndexByName(string groupName)
        {
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i].name == groupName) return i;
            }
            Debug.LogWarning($"[ActionGroupDriver] Group '{groupName}' not found", this);
            return -1;
        }
    }
}
