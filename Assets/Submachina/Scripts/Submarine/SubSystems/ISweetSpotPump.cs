namespace Submachina.Core
{
    /**
     * Read-only view of a sweet-spot timing pump, consumed by UI such as BellowsBar.
     *
     * Implemented by ManualBellowsPump (hold-and-release charge) and
     * O2PickupPump (looping charge). Lets display components render any pump
     * variant without knowing which mechanic drives it.
     */
    public interface ISweetSpotPump
    {
        /** Current charge progress (0–1) — drives the fill width. */
        float ChargeProgress { get; }

        /** True while the pump is locked out by the Air Lock penalty. */
        bool IsAirLocked { get; }

        /** True while the pump is in a post-action cooldown — the bar hides while this holds.
         *  Pumps with no cooldown mechanic simply return false. */
        bool IsOnCooldown { get; }

        /** True while the charge currently sits within the sweet spot window. */
        bool IsInSweetSpot { get; }

        /** Lower bound of the sweet spot window (0–1) — positions the left marker. */
        float SweetSpotMin { get; }

        /** Upper bound of the sweet spot window (0–1) — positions the right marker. */
        float SweetSpotMax { get; }

        /**
         * True when this pump currently wants to own the shared pump input and bar.
         * The SubmarinePumpRouter picks the active pump from those wanting control.
         * Example: O2PickupPump wants control while looping or with a pickup in range;
         * ManualBellowsPump wants control whenever manual pumping is enabled (the baseline).
         */
        bool WantsControl { get; }

        /**
         * Tie-break priority among pumps that want control — higher wins.
         * The contextual intake pump outranks the always-on manual pump, so it
         * takes over near bubbles and the manual pump resumes when it lets go.
         */
        int ControlPriority { get; }
    }
}
