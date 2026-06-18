namespace Submachina.Core
{
    /**
     * Shared base for every sweet-spot timing pump (ManualBellowsPump, O2PickupPump).
     *
     * Owns the one piece of lifecycle every pump must get right: registering with
     * the sub's SubmarinePumpRouter so the router can arbitrate which pump is Active
     * and BellowsBar can follow it. Registration happens in BOTH OnEnable and Start:
     *
     *   - OnEnable is the normal path.
     *   - Start is a fallback for the Awake-ordering race — on a runtime-instantiated
     *     or runtime-enabled sub (e.g. a second local player), a pump's OnEnable can
     *     fire before SubmarinePumpRouter has registered with Submarine, leaving
     *     Sub.Pumps null so the OnEnable Register no-ops. By Start the slot is
     *     populated. Register() is duplicate-safe, so the double call is harmless.
     *
     * These lifecycle methods are intentionally not virtual: a pump can't accidentally
     * override away its own registration. Pump-specific enable/disable work (input
     * actions, ring overrides, etc.) goes in the OnPumpEnabled/OnPumpDisabled hooks.
     *
     * Derived pumps implement the ISweetSpotPump contract (declared abstract here) and
     * may override Awake — calling base.Awake() first, per SubmarineComponent.
     */
    public abstract class SweetSpotPump : SubmarineComponent, ISweetSpotPump
    {
        // =====================
        // ISweetSpotPump — implemented by derived pumps
        // =====================

        /** Current charge progress (0–1) — drives the BellowsBar fill width. */
        public abstract float ChargeProgress { get; }

        /** True while the pump is locked out by the Air Lock penalty. */
        public abstract bool IsAirLocked { get; }

        /** True while the pump is in a post-action cooldown — the bar hides while this holds. */
        public abstract bool IsOnCooldown { get; }

        /** True while the charge currently sits within the sweet spot window. */
        public abstract bool IsInSweetSpot { get; }

        /** Lower bound of the sweet spot window (0–1) — positions the left marker. */
        public abstract float SweetSpotMin { get; }

        /** Upper bound of the sweet spot window (0–1) — positions the right marker. */
        public abstract float SweetSpotMax { get; }

        /** True when this pump currently wants to own the shared pump input and bar. */
        public abstract bool WantsControl { get; }

        /** Tie-break priority among pumps that want control — higher wins. */
        public abstract int ControlPriority { get; }

        // =====================
        // Router Registration (owned here, once)
        // =====================

        /** Registers with the router and runs the pump's own enable logic. */
        protected void OnEnable()
        {
            Sub?.Pumps?.Register(this);
            OnPumpEnabled();
        }

        /** Awake-order race fallback — re-register once the router slot is guaranteed populated. */
        protected void Start()
        {
            Sub?.Pumps?.Register(this);
        }

        /** Unregisters from the router and runs the pump's own disable logic. */
        protected void OnDisable()
        {
            Sub?.Pumps?.Unregister(this);
            OnPumpDisabled();
        }

        // =====================
        // Hooks for derived pumps
        // =====================

        /** Called after the pump registers with the router. Enable input actions, etc. */
        protected virtual void OnPumpEnabled() { }

        /** Called after the pump unregisters from the router. Disable input, clear ring overrides, etc. */
        protected virtual void OnPumpDisabled() { }
    }
}
