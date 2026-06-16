using System;

namespace Submachina.Core
{
    /**
     * Declares which SubFeedback keys a SubmarineComponent triggers at runtime.
     *
     * Purely metadata — the attribute has no runtime behavior. In the editor,
     * SubmarineComponent's banner reads this via reflection and renders the
     * feedback keys as colored chips so designers can see at a glance which
     * feedbacks a component will fire without reading the code.
     *
     * Usage:
     *   [UsesFeedbacks(SubFeedback.MiningActive, SubFeedback.MiningCollect)]
     *   public class MiningLaser : SubmarineComponent { ... }
     */
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class UsesFeedbacksAttribute : Attribute
    {
        public SubFeedback[] Feedbacks { get; }

        public UsesFeedbacksAttribute(params SubFeedback[] feedbacks)
        {
            Feedbacks = feedbacks;
        }
    }
}
