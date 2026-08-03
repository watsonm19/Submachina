namespace Core.Modulation
{
    /**
     * Generic composited float target that only raises its onValueApplied UnityEvent.
     * Escape hatch for wiring modulation to anything in the inspector without a
     * dedicated adapter (animator floats, custom components, etc).
     */
    public class UnityEventFloatTarget : ModulatedFloatTarget
    {
        // The base class already raises onValueApplied — nothing extra to write.
        protected override void ApplyValue(float value) { }
    }
}
