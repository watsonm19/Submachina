using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core
{
    /**
     * SubmarinePumpRouter — central registry that decides which pump is "active".
     *
     * The sub can carry more than one sweet-spot pump (today: the manual bellows
     * pump and the O2 intake pump), but only one drives the shared pump input and
     * the on-screen bar at a time. Rather than each pump and bar holding hand-wired
     * references to one another, every ISweetSpotPump registers here on enable and
     * this router exposes the single Active pump for everyone to read.
     *
     * Arbitration (see Active): among all registered pumps that currently want
     * control, the highest ControlPriority wins. The intake pump outranks the
     * manual pump, so it takes over near bubbles; the manual pump is the baseline
     * that resumes the moment the intake pump lets go. With only one pump present,
     * that pump is simply active whenever it wants control — so each pump works
     * standalone as upgrades add or remove them.
     *
     * Accessed sibling-side via Sub.Pumps. Mirrors SubmarineFeedbackRouter:
     *   1. Add to the submarine root alongside O2System.
     *   2. Pumps self-register — no manual wiring needed.
     */
    public class SubmarinePumpRouter : SubmarineComponent
    {
        // Live set of pumps on this sub. Populated by Register/Unregister as
        // pumps enable/disable, so runtime upgrade swaps stay current.
        private readonly List<ISweetSpotPump> _pumps = new();

        // =====================
        // Registration
        // =====================

        /**
         * Adds a pump to the active-arbitration set. Called by each pump in OnEnable.
         * Ignores duplicates so a re-enable can't list the same pump twice.
         */
        public void Register(ISweetSpotPump pump)
        {
            if (pump == null || _pumps.Contains(pump)) return;
            _pumps.Add(pump);
        }

        /** Removes a pump from the set. Called by each pump in OnDisable / on destroy. */
        public void Unregister(ISweetSpotPump pump)
        {
            _pumps.Remove(pump);
        }

        // =====================
        // Arbitration
        // =====================

        /**
         * The pump that currently owns the input and bar, or null if none wants control.
         *
         * Scans the registered pumps for the highest-priority one whose WantsControl
         * is true. Example: with the intake pump (priority 10) looping near a bubble
         * and the manual pump (priority 0) idle, the intake pump is Active; once the
         * bubble is gone the intake pump stops wanting control and the manual pump
         * becomes Active again.
         */
        public ISweetSpotPump Active
        {
            get
            {
                ISweetSpotPump best = null;
                int bestPriority = int.MinValue;

                for (int i = 0; i < _pumps.Count; i++)
                {
                    ISweetSpotPump pump = _pumps[i];
                    if (pump == null || !pump.WantsControl) continue;

                    // Higher ControlPriority wins; first-registered breaks exact ties
                    if (pump.ControlPriority > bestPriority)
                    {
                        best = pump;
                        bestPriority = pump.ControlPriority;
                    }
                }

                return best;
            }
        }

        /** Convenience for pumps: true when the given pump is the one currently in control. */
        public bool IsActive(ISweetSpotPump pump) => pump != null && ReferenceEquals(Active, pump);

        // =====================
        // Debug
        // =====================

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private int RegisteredPumps => _pumps.Count;

        [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
        private string ActivePump =>
            Active is MonoBehaviour mb ? mb.GetType().Name : "(none)";
    }
}
