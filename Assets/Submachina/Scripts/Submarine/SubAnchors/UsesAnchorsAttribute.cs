using System;

namespace Submachina.Core
{
    /**
     * Declares which anchor keys a SubmarineComponent resolves at runtime.
     *
     * Purely metadata — the attribute has no runtime behavior. In the editor,
     * SubmarineComponent's banner can read this via reflection and render the
     * anchor names as chips so designers see at a glance which mount points a
     * component references, alongside its feedback chips.
     *
     * Pass field names via nameof so the compiler verifies they exist:
     *   [UsesAnchors(nameof(SubAnchors.Muzzle))]
     *   public class PlayerAttack : SubmarineComponent { ... }
     */
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class UsesAnchorsAttribute : Attribute
    {
        public string[] AnchorNames { get; }

        public UsesAnchorsAttribute(params string[] anchorNames)
        {
            AnchorNames = anchorNames;
        }
    }
}