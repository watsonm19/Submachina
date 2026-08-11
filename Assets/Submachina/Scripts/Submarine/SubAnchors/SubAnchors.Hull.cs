namespace Submachina.Core
{
    // Hull anchor keys — structural mount points on the sub body.
    public static partial class SubAnchors
    {
        private const int HullCat = 1;

        public static readonly AnchorId Center = new(HullCat, 0);
        public static readonly AnchorId Front = new(HullCat, 1);
        public static readonly AnchorId Tail = new(HullCat, 2);
        public static readonly AnchorId Top = new(HullCat, 3);
        public static readonly AnchorId Bottom = new(HullCat, 4);
        public static readonly AnchorId Propeller = new(HullCat, 5);
        public static readonly AnchorId Canvas = new(HullCat, 6);
    }
}