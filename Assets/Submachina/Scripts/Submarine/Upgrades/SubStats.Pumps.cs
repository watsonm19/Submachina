namespace Submachina.Core
{
    public static partial class SubStats
    {
        private const int PumpsCat = 2;

        // Manual bellows pump
        public static readonly StatId PerfectPumpAir    = new(PumpsCat, 0);
        public static readonly StatId WeakPumpAir       = new(PumpsCat, 1);
        public static readonly StatId PumpChargeSpeed   = new(PumpsCat, 2);
        public static readonly StatId PumpCooldown      = new(PumpsCat, 3);
        public static readonly StatId SpamPressLimit    = new(PumpsCat, 4);
        public static readonly StatId AirLockDuration   = new(PumpsCat, 5);

        // O2 intake pump
        public static readonly StatId IntakeSweetMultiplier = new(PumpsCat, 10);
        public static readonly StatId IntakeWeakMultiplier  = new(PumpsCat, 11);
        public static readonly StatId IntakeChargeSpeed     = new(PumpsCat, 12);
    }
}
