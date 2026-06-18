namespace Submachina.Core
{
    /**
     * Global editor toggle for the verbose help/explanation InfoBoxes shown in
     * spawn rule inspectors. Stored in EditorPrefs so it persists across the
     * project and applies to every rule, profile, placement, and configurator
     * at once.
     *
     * The long explanatory InfoBoxes are gated on this via Odin's VisibleIf
     * expression: [InfoBox("...", InfoMessageType.None, "@Submachina.Core.SpawnDocs.ShowHelp")].
     * Short live previews and concise hover tooltips are always available.
     */
    public static class SpawnDocs
    {
#if UNITY_EDITOR
        private const string Key = "Submachina.Spawn.ShowHelp";

        /** Whether verbose help InfoBoxes are shown. Persisted per-project. */
        public static bool ShowHelp
        {
            get => UnityEditor.EditorPrefs.GetBool(Key, true);
            set => UnityEditor.EditorPrefs.SetBool(Key, value);
        }
#else
        // In builds the help system doesn't exist; no-op setter keeps the
        // inspector toggle property compiling without #if guards everywhere.
        public static bool ShowHelp { get => false; set { } }
#endif
    }
}
