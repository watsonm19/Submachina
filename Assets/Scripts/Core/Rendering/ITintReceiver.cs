using UnityEngine;

namespace Core.Rendering
{
    /**
     * A component whose per-instance appearance can't be reached through
     * SpriteRenderer.color (MaterialPropertyBlocks, custom shader uniforms, ...).
     *
     * Spawners and randomizers (e.g. ClusterBuilder's hue/brightness variation)
     * roll a single multiply tint, write it to the plain SpriteRenderers, then
     * hand the SAME tint to every ITintReceiver under the instance — so secondary
     * appearance layers (like the specular glint) follow the albedo without the
     * caller knowing they exist.
     *
     * Implementations should multiply the tint's RGB into their look and preserve
     * their own alpha / HDR magnitude. Calls may repeat — each multiplies in.
     */
    public interface ITintReceiver
    {
        /** Multiply this tint (RGB) into the component's per-instance appearance. */
        void ApplyTint(Color tint);
    }
}
