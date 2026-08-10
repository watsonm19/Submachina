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

        /** Looping cue while ambient pressure alone exceeds hull resistance (cascade). */
        public static readonly FeedbackId CrushZone = new(HullCat, 2);
    }
}
