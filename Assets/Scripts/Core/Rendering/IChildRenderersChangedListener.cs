namespace Core.Rendering
{
    /**
     * Contract owned by the renderer-driving side of the rendering system: components like
     * SpecularController cache GetComponentsInChildren renderer lists and write per-renderer
     * state (material property blocks) that newly created renderers won't have. Anything that
     * creates, destroys, or re-materials renderers beneath such a driver (procedural segment
     * spawners, pooled visuals...) should broadcast this upward — e.g. via
     * GetComponentsInParent — so drivers re-gather and re-apply to the current set.
     */
    public interface IChildRenderersChangedListener
    {
        /** Called after renderers below this component were created, destroyed, or re-materialed. */
        void OnChildRenderersChanged();
    }
}
