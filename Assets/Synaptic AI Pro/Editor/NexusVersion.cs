using System;
using System.IO;
using UnityEngine;
using SynapticAIPro;

namespace SynapticAIPro
{
    /// <summary>
    /// Centralized version management - reads from package.json
    /// </summary>
    public static class NexusVersion
    {
        private static string _cachedVersion = null;
        private static string _packageJsonPath = null;

        /// <summary>
        /// Get the current version from package.json
        /// </summary>
        public static string Current
        {
            get
            {
                if (_cachedVersion == null)
                {
                    _cachedVersion = ReadVersionFromPackageJson();
                }
                return _cachedVersion;
            }
        }

        /// <summary>
        /// Force refresh the cached version (useful after package update)
        /// </summary>
        public static void RefreshCache()
        {
            _cachedVersion = null;
        }

        private static string ReadVersionFromPackageJson()
        {
            try
            {
                if (_packageJsonPath == null)
                {
                    _packageJsonPath = Path.Combine(Application.dataPath, "Synaptic AI Pro/package.json");
                }

                if (File.Exists(_packageJsonPath))
                {
                    var json = File.ReadAllText(_packageJsonPath);
                    // Simple parsing - find "version": "x.x.x"
                    var versionIndex = json.IndexOf("\"version\"");
                    if (versionIndex >= 0)
                    {
                        var colonIndex = json.IndexOf(':', versionIndex);
                        var firstQuote = json.IndexOf('"', colonIndex);
                        var secondQuote = json.IndexOf('"', firstQuote + 1);
                        if (firstQuote >= 0 && secondQuote > firstQuote)
                        {
                            return json.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                SynLog.Warn($"[Synaptic] Failed to read version from package.json: {e.Message}");
            }

            return "1.0.0"; // Fallback
        }
    }
}
