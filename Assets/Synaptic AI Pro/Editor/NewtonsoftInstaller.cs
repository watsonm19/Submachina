using UnityEngine;
using SynapticAIPro;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace SynapticPro
{
    [InitializeOnLoad]
    public static class NewtonsoftInstaller
    {
        private const string NEWTONSOFT_PACKAGE = "com.unity.nuget.newtonsoft-json";
        private static AddRequest addRequest;
        private static ListRequest listRequest;

        static NewtonsoftInstaller()
        {
            // Check if Newtonsoft.Json is already installed
            listRequest = Client.List();
            EditorApplication.update += CheckListProgress;
        }

        private static void CheckListProgress()
        {
            if (listRequest == null || !listRequest.IsCompleted)
                return;

            EditorApplication.update -= CheckListProgress;

            if (listRequest.Status == StatusCode.Success)
            {
                bool isInstalled = false;
                foreach (var package in listRequest.Result)
                {
                    if (package.name == NEWTONSOFT_PACKAGE)
                    {
                        isInstalled = true;
                        SynLog.Info($"[Synaptic AI Pro] Newtonsoft.Json is already installed (version {package.version})");
                        break;
                    }
                }

                if (!isInstalled)
                {
                    SynLog.Info("[Synaptic AI Pro] Newtonsoft.Json not found. Installing...");
                    InstallNewtonsoftJson();
                }
            }
            else if (listRequest.Status >= StatusCode.Failure)
            {
                Debug.LogError($"[Synaptic AI Pro] Failed to list packages: {listRequest.Error.message}");
            }

            listRequest = null;
        }

        private static void InstallNewtonsoftJson()
        {
            addRequest = Client.Add(NEWTONSOFT_PACKAGE);
            EditorApplication.update += CheckInstallProgress;
        }

        private static void CheckInstallProgress()
        {
            if (addRequest == null || !addRequest.IsCompleted)
                return;

            EditorApplication.update -= CheckInstallProgress;

            if (addRequest.Status == StatusCode.Success)
            {
                SynLog.Info($"[Synaptic AI Pro] Successfully installed {NEWTONSOFT_PACKAGE}");
            }
            else if (addRequest.Status >= StatusCode.Failure)
            {
                Debug.LogError($"[Synaptic AI Pro] Failed to install {NEWTONSOFT_PACKAGE}: {addRequest.Error.message}");
                SynLog.Warn("[Synaptic AI Pro] Please install Newtonsoft.Json manually via Package Manager:\n" +
                                "Window > Package Manager > + > Add package by name...\n" +
                                "Package name: com.unity.nuget.newtonsoft-json");
            }

            addRequest = null;
        }
    }
}
