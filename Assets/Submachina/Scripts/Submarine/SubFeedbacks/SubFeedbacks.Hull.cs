namespace Submachina.Core
{
    // Hull feedback keys — used by HullSystem and CollisionDamage.
    public static partial class SubFeedbacks
    {
        private const int HullCat = 9;

        /** One-shot creak/groan as structural reserve drops through warning bands. */
        public static readonly FeedbackId HullCreak = new(HullCat, 0);

        /** An impact exceeded the hull's remaining margin — real damage taken. */
        public static readonly FeedbackId HullOverload = new(HullCat, 1);

        /** Looping cue while the sub is past rated depth and accruing pressure strain. */
        public static readonly FeedbackId CrushZone = new(HullCat, 2);

        /** One-shot per pressure-damage tick — hull actively losing HP to depth. */
        public static readonly FeedbackId PressureDamage = new(HullCat, 3);

        /** Air pumped into the hull — pressure boost applied (pump-to-hull). */
        public static readonly FeedbackId HullPressurize = new(HullCat, 4);
    }
}
