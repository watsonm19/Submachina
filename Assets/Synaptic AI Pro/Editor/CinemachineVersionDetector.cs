using UnityEditor;
using UnityEngine;
using SynapticAIPro;
using System.IO;
using System.Linq;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace SynapticPro
{
    /// <summary>
    /// Detects installed Cinemachine version and defines appropriate scripting symbols
    /// Supports both Cinemachine 2.x and 3.x
    /// Automatically installs Unity.Splines dependency for Cinemachine 3.x
    /// </summary>
    [InitializeOnLoad]
    public static class CinemachineVersionDetector
    {
        private const string CINEMACHINE_2_SYMBOL = "CINEMACHINE_2";
        private const string CINEMACHINE_3_SYMBOL = "CINEMACHINE_3";
        private const string CINEMACHINE_SYMBOL = "CINEMACHINE";
        private const string SPLINES_PACKAGE = "com.unity.splines";

        private static AddRequest splinesAddRequest;

        static CinemachineVersionDetector()
        {
            DetectAndSetSymbols();
        }

        // v1.2.24: Diagnostics タブに統合、メニューからは除外
        // [MenuItem("Tools/Synaptic Pro/Detect Cinemachine Version")]
        public static void DetectAndSetSymbols()
        {
            var cinemachineVersion = GetCinemachineVersion();

            if (cinemachineVersion == null)
            {
                SynLog.Info("[Synaptic] Cinemachine not detected. Cinemachine features will be disabled.");
                RemoveAllCinemachineSymbols();
                return;
            }

            SynLog.Info($"[Synaptic] Detected Cinemachine version: {cinemachineVersion}");

            // Parse version
            var versionParts = cinemachineVersion.Split('.');
            if (versionParts.Length > 0 && int.TryParse(versionParts[0], out int majorVersion))
            {
                if (majorVersion >= 3)
                {
                    SetCinemachineSymbol(3);
                    SynLog.Info("[Synaptic] ✅ Cinemachine 3.x detected - Using Cinemachine 3 API");

                    // Cinemachine 3.x requires Unity.Splines package
                    CheckAndInstallSplines();
                }
                else if (majorVersion == 2)
                {
                    SetCinemachineSymbol(2);
                    SynLog.Info("[Synaptic] ✅ Cinemachine 2.x detected - Using Cinemachine 2 API");
                }
                else
                {
                    SynLog.Warn($"[Synaptic] ⚠️ Unsupported Cinemachine version: {cinemachineVersion}. Recommended: 2.9.7 or 3.0+");
                    RemoveAllCinemachineSymbols();
                }
            }
        }

        private static string GetCinemachineVersion()
        {
            // Check via PackageInfo
            var request = UnityEditor.PackageManager.Client.List(true, false);

            // Wait for completion (synchronous for InitializeOnLoad)
            while (!request.IsCompleted)
            {
                System.Threading.Thread.Sleep(10);
            }

            if (request.Status == UnityEditor.PackageManager.StatusCode.Success)
            {
                var cinemachinePackage = request.Result.FirstOrDefault(p => p.name == "com.unity.cinemachine");
                if (cinemachinePackage != null)
                {
                    return cinemachinePackage.version;
                }
            }

            // Fallback: Check if namespace exists via type checking
            var cinemachine2Type = System.Type.GetType("Cinemachine.CinemachineVirtualCamera, Cinemachine");
            var cinemachine3Type = System.Type.GetType("Unity.Cinemachine.CinemachineCamera, Unity.Cinemachine");

            if (cinemachine3Type != null)
            {
                return "3.0.0"; // 3.x detected
            }
            else if (cinemachine2Type != null)
            {
                return "2.9.7"; // 2.x detected
            }

            return null; // Not installed
        }

        private static void SetCinemachineSymbol(int majorVersion)
        {
            var buildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTargetGroup);
            var definesList = defines.Split(';').ToList();

            // Remove old symbols
            definesList.Remove(CINEMACHINE_2_SYMBOL);
            definesList.Remove(CINEMACHINE_3_SYMBOL);
            definesList.Remove(CINEMACHINE_SYMBOL);

            // Add appropriate symbols
            definesList.Add(CINEMACHINE_SYMBOL);
            if (majorVersion == 2)
            {
                definesList.Add(CINEMACHINE_2_SYMBOL);
            }
            else if (majorVersion == 3)
            {
                definesList.Add(CINEMACHINE_3_SYMBOL);
            }

            var newDefines = string.Join(";", definesList.Distinct().Where(s => !string.IsNullOrEmpty(s)));
            PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTargetGroup, newDefines);

            SynLog.Info($"[Synaptic] Scripting symbols updated: {newDefines}");
        }

        private static void RemoveAllCinemachineSymbols()
        {
            var buildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTargetGroup);
            var definesList = defines.Split(';').ToList();

            definesList.Remove(CINEMACHINE_2_SYMBOL);
            definesList.Remove(CINEMACHINE_3_SYMBOL);
            definesList.Remove(CINEMACHINE_SYMBOL);

            var newDefines = string.Join(";", definesList.Distinct().Where(s => !string.IsNullOrEmpty(s)));
            PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTargetGroup, newDefines);

            SynLog.Info("[Synaptic] Cinemachine symbols removed");
        }

        private static void CheckAndInstallSplines()
        {
            var listRequest = Client.List(true, false);

            // Wait for completion (synchronous for simplicity)
            while (!listRequest.IsCompleted)
            {
                System.Threading.Thread.Sleep(10);
            }

            if (listRequest.Status == StatusCode.Success)
            {
                bool isInstalled = false;
                foreach (var package in listRequest.Result)
                {
                    if (package.name == SPLINES_PACKAGE)
                    {
                        isInstalled = true;
                        SynLog.Info($"[Synaptic] Unity.Splines is already installed (version {package.version})");
                        break;
                    }
                }

                if (!isInstalled)
                {
                    SynLog.Info("[Synaptic] Unity.Splines not found. Installing dependency for Cinemachine 3.x...");
                    InstallSplines();
                }
            }
            else if (listRequest.Status >= StatusCode.Failure)
            {
                Debug.LogError($"[Synaptic] Failed to check Splines package: {listRequest.Error.message}");
            }
        }

        private static void InstallSplines()
        {
            splinesAddRequest = Client.Add(SPLINES_PACKAGE);
            EditorApplication.update += CheckSplinesInstallProgress;
        }

        private static void CheckSplinesInstallProgress()
        {
            if (splinesAddRequest == null || !splinesAddRequest.IsCompleted)
                return;

            EditorApplication.update -= CheckSplinesInstallProgress;

            if (splinesAddRequest.Status == StatusCode.Success)
            {
                SynLog.Info($"[Synaptic] ✅ Successfully installed {SPLINES_PACKAGE} for Cinemachine 3.x");
                SynLog.Info("[Synaptic] Please wait for Unity to recompile scripts...");
            }
            else if (splinesAddRequest.Status >= StatusCode.Failure)
            {
                Debug.LogError($"[Synaptic] ❌ Failed to install {SPLINES_PACKAGE}: {splinesAddRequest.Error.message}");
                SynLog.Warn("[Synaptic] Cinemachine 3.x requires Unity.Splines package.\n" +
                                "Please install it manually via Package Manager:\n" +
                                "Window > Package Manager > + > Add package by name...\n" +
                                $"Package name: {SPLINES_PACKAGE}");
            }

            splinesAddRequest = null;
        }
    }
}
