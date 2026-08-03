// Synaptic AI Pro for Unity - Package Bootstrap
//
// ESC-0194/0204/0205/0206/0207/0208/0197 対策 (2026-06-28):
// Synaptic.MCP.Unity.Editor.asmdef は Unity.ugui / Unity.TextMeshPro を unconditional に
// references している。Unity 6.4 で新規プロジェクトを作成すると UGUI / TMP パッケージが
// デフォルト未導入のことがあり、その状態で asmdef がロードされると参照解決失敗で全スクリプト
// がコンパイル不能 → Tools メニュー消失。
//
// このファイルは他の Synaptic 系コードに一切依存しない独立 asmdef に置かれているので、
// メイン asmdef が壊れていても確実に Editor 起動時に実行される。UGUI / TMP の有無を検出し、
// 未導入なら自動インストールを 1-tap でユーザーに提案する。

using System;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace SynapticPro.Bootstrap
{
    [InitializeOnLoad]
    public static class SynapticPackageBootstrap
    {
        private const string PrefKey_AskedToInstall = "SynapticPro.Bootstrap.AskedToInstallPkgs.v1";

        private static readonly string[] RequiredPackages = new[]
        {
            "com.unity.ugui",          // UnityEngine.UI (Unity 6+ では TextMeshPro も統合)
            "com.unity.textmeshpro",   // Unity ≤ 6.3 までは別パッケージ
        };

        private static ListRequest _listRequest;
        private static AddRequest _addRequest;

        static SynapticPackageBootstrap()
        {
            // 既に確認済みのプロジェクトでは何もしない (再起動毎の煩わしさ回避)。
            // 一度プロンプトに答えれば再表示されない。
            if (SessionState.GetBool("SynapticPro.Bootstrap.CheckedThisSession", false)) return;
            SessionState.SetBool("SynapticPro.Bootstrap.CheckedThisSession", true);

            // 既に install 拒否したユーザーには再表示しない (User-level)。
            // Synaptic を初めて入れた人だけに確認が出る。
            if (EditorPrefs.GetBool(PrefKey_AskedToInstall, false)) return;

            EditorApplication.delayCall += KickoffPackageCheck;
        }

        private static void KickoffPackageCheck()
        {
            try
            {
                _listRequest = Client.List(true /* offlineMode */, true /* includeIndirectDependencies */);
                EditorApplication.update += OnListProgress;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Synaptic Bootstrap] Package list failed: {e.Message}");
            }
        }

        private static void OnListProgress()
        {
            if (_listRequest == null || !_listRequest.IsCompleted) return;
            EditorApplication.update -= OnListProgress;

            try
            {
                if (_listRequest.Status != StatusCode.Success)
                {
                    Debug.LogWarning("[Synaptic Bootstrap] Package list status: " + _listRequest.Status);
                    return;
                }

                var installed = _listRequest.Result.Select(p => p.name).ToHashSet();
                var missing = RequiredPackages.Where(p => !installed.Contains(p)).ToList();
                if (missing.Count == 0) return;

                // Unity 6 では com.unity.ugui が TMP を内包するので、UGUI さえ入っていれば
                // TMP が「単独パッケージ」として無くてもよい。UGUI 単独で OK 判定する。
                if (missing.Count == 1 && missing[0] == "com.unity.textmeshpro" &&
                    installed.Contains("com.unity.ugui"))
                {
                    return;
                }

                var message =
                    "Synaptic AI Pro for Unity needs the following Unity packages to compile:\n\n" +
                    string.Join("\n", missing.Select(m => "  - " + m)) +
                    "\n\nWithout them, the Tools > Synaptic AI Pro menu will not appear.\n\n" +
                    "Install them now? (Recommended)";

                int choice = EditorUtility.DisplayDialogComplex(
                    "Synaptic AI Pro: Missing Packages",
                    message,
                    "Install",
                    "Skip (I'll install manually)",
                    "Don't ask again");

                if (choice == 0)
                {
                    InstallNext(missing, 0);
                }
                else if (choice == 2)
                {
                    EditorPrefs.SetBool(PrefKey_AskedToInstall, true);
                }
            }
            finally
            {
                _listRequest = null;
            }
        }

        private static void InstallNext(System.Collections.Generic.List<string> queue, int index)
        {
            if (index >= queue.Count)
            {
                EditorUtility.DisplayDialog(
                    "Synaptic AI Pro",
                    "All required packages installed. Unity will recompile and the Tools > Synaptic AI Pro menu should appear shortly.",
                    "OK");
                return;
            }

            string pkg = queue[index];
            Debug.Log("[Synaptic Bootstrap] Installing " + pkg + "...");
            _addRequest = Client.Add(pkg);

            void OnAddProgress()
            {
                if (_addRequest == null || !_addRequest.IsCompleted) return;
                EditorApplication.update -= OnAddProgress;
                try
                {
                    if (_addRequest.Status == StatusCode.Success)
                    {
                        Debug.Log("[Synaptic Bootstrap] Installed: " + pkg);
                    }
                    else
                    {
                        Debug.LogWarning("[Synaptic Bootstrap] Install failed for " + pkg + ": " + _addRequest.Error?.message);
                    }
                }
                finally
                {
                    _addRequest = null;
                    InstallNext(queue, index + 1);
                }
            }

            EditorApplication.update += OnAddProgress;
        }
    }
}
