using UnityEngine;
using UnityEditor;

namespace SynapticAIPro
{
    /// <summary>
    /// Synaptic AI Pro 内部ログのラッパー。
    /// EditorPrefs "Synaptic.VerboseLog" で Info/Warn の出力を抑制可能。
    /// Error は常に出力される（重要なエラーは隠さない）。
    /// </summary>
    public static class SynLog
    {
        private const string PREF_KEY = "Synaptic.VerboseLog";

        // EditorPrefs.GetBool is main-thread only. Calling it from background
        // threads (e.g. WebSocket ReceiveAsync handlers) throws and silently
        // kills the calling Task. We snapshot the value into a volatile bool
        // on the main thread, and Info/Warn read the cache (thread-safe).
        // This was the root cause of v1.2.20/v1.2.21 MCP timeout (ESC-0102).
        private static volatile bool _verboseCached = true;
        private static bool _initialized = false;

        [InitializeOnLoadMethod]
        private static void InitVerboseCache()
        {
            try { _verboseCached = EditorPrefs.GetBool(PREF_KEY, true); }
            catch { _verboseCached = true; }
            _initialized = true;
        }

        public static bool VerboseEnabled
        {
            get => _initialized ? _verboseCached : true;
            set
            {
                _verboseCached = value;
                try { EditorPrefs.SetBool(PREF_KEY, value); } catch { }
            }
        }

        public static void Info(string msg)
        {
            if (_verboseCached) Debug.Log(msg);
        }

        public static void Info(string msg, Object context)
        {
            if (_verboseCached) Debug.Log(msg, context);
        }

        public static void Warn(string msg)
        {
            if (_verboseCached) Debug.LogWarning(msg);
        }

        public static void Warn(string msg, Object context)
        {
            if (_verboseCached) Debug.LogWarning(msg, context);
        }

        // Error は常に出力（重要な情報）
        public static void Error(string msg)
        {
            Debug.LogError(msg);
        }

        public static void Error(string msg, Object context)
        {
            Debug.LogError(msg, context);
        }
    }
}
