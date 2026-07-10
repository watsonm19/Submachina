namespace Submachina.Core
{
    // Thrust feedback keys — used by SubmarinePhysicsController.
    // All four are looping toggle cues: Play when the matching input begins,
    // Stop when it ends. ThrustActive is the umbrella key (any thrust at all);
    // the other three isolate a single direction for targeted effects.
    public static partial class SubFeedbacks
    {
        private const int ThrustCat = 8;

        public static readonly FeedbackId ThrustActive   = new(ThrustCat, 0); // any thrust applied
        public static readonly FeedbackId ThrustLateral  = new(ThrustCat, 1); // left/right thrust
        public static readonly FeedbackId ThrustCounter  = new(ThrustCat, 2); // upward counter-thrust vs. current
        public static readonly FeedbackId ThrustDownward = new(ThrustCat, 3); // downward thrust (when allowed)
    }
}
