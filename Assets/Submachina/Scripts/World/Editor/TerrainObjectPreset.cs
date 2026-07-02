#if UNITY_EDITOR
using UnityEngine;
using Sirenix.OdinInspector;

namespace Submachina.Core.EditorTools
{
    /**
     * A saved snapshot of TerrainObjectGenerator settings so past generation setups can be
     * recalled, tweaked, and re-baked. Create via the generator window's "Save As…" button
     * (or Assets > Create > Submachina > Terrain Object Preset) and load it back with "Load".
     *
     * The asset can also be edited directly in the inspector — it draws the same tabbed
     * settings UI as the window.
     */
    [CreateAssetMenu(menuName = "Submachina/Terrain Object Preset", fileName = "TerrainObjectPreset")]
    public class TerrainObjectPreset : ScriptableObject
    {
        [HideLabel, InlineProperty]
        public TerrainObjectSettings settings = new TerrainObjectSettings();
    }
}
#endif
