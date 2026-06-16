using System;

namespace Submachina.Core
{
    /**
     * Declares which feedback keys a SubmarineComponent triggers at runtime.
     *
     * Purely metadata — the attribute has no runtime behavior. In the editor,
     * SubmarineComponent's banner reads this via reflection and renders the
     * feedback names as colored chips so designers can see at a glance which
     * feedbacks a component will fire without reading the code.
     *
     * Pass field names via nameof so the compiler verifies they exist:
     *   [UsesFeedbacks(nameof(SubFeedbacks.MiningActive), nameof(SubFeedbacks.MiningCollect))]
     *   public class MiningLaser : SubmarineComponent { ... }
     */
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class UsesFeedbacksAttribute : Attribute
    {
        public string[] FeedbackNames { get; }

        public UsesFeedbacksAttribute(params string[] feedbackNames)
        {
            FeedbackNames = feedbackNames;
        }
    }
}
