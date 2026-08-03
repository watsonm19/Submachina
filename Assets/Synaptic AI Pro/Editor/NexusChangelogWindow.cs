using UnityEditor;
using UnityEngine;
using SynapticAIPro;

namespace SynapticPro
{
    /// <summary>
    /// Changelog dialog shown on first import or version update
    /// </summary>
    [InitializeOnLoad]
    public class NexusChangelogWindow : EditorWindow
    {
        private static string CURRENT_VERSION => NexusVersion.Current;
        private const string PREF_KEY_LAST_VERSION = "SynapticPro_LastShownVersion";
        private const string PREF_KEY_DONT_SHOW = "SynapticPro_DontShowChangelog";
        private const string PREF_KEY_LANGUAGE = "SynapticPro_ChangelogLanguage";

        private enum Language { English, Japanese }
        private static Language currentLanguage = Language.English;

        private static bool dontShowAgain = false;
        private Vector2 scrollPosition;

        static NexusChangelogWindow()
        {
            EditorApplication.delayCall += ShowOnStartupIfNeeded;
        }

        private static void ShowOnStartupIfNeeded()
        {
            // Don't show during play mode
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            // Check if user disabled
            if (EditorPrefs.GetBool(PREF_KEY_DONT_SHOW, false))
                return;

            // Check if already shown for this version
            string lastVersion = EditorPrefs.GetString(PREF_KEY_LAST_VERSION, "");
            if (lastVersion == CURRENT_VERSION)
                return;

            // Show dialog
            ShowWindow();

            // Mark as shown
            EditorPrefs.SetString(PREF_KEY_LAST_VERSION, CURRENT_VERSION);
        }

        [MenuItem("Tools/Synaptic Pro/What's New", false, 100)]
        public static void ShowWindow()
        {
            var window = GetWindow<NexusChangelogWindow>(true, "Synaptic AI Pro - What's New", true);
            window.minSize = new Vector2(500, 450);
            window.maxSize = new Vector2(600, 650);
            window.ShowUtility();
        }

        private void OnEnable()
        {
            dontShowAgain = EditorPrefs.GetBool(PREF_KEY_DONT_SHOW, false);
            currentLanguage = (Language)EditorPrefs.GetInt(PREF_KEY_LANGUAGE, 0);
        }

        private void OnGUI()
        {
            // Language selector (top right)
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(currentLanguage == Language.English ? "Language:" : "言語:", GUILayout.Width(60));
            var newLang = (Language)EditorGUILayout.Popup((int)currentLanguage, new string[] { "English", "日本語" }, GUILayout.Width(80));
            if (newLang != currentLanguage)
            {
                currentLanguage = newLang;
                EditorPrefs.SetInt(PREF_KEY_LANGUAGE, (int)currentLanguage);
            }
            EditorGUILayout.EndHorizontal();

            // Header
            GUILayout.Space(5);
            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleCenter
            };
            GUILayout.Label($"Synaptic AI Pro v{CURRENT_VERSION}", headerStyle);
            GUILayout.Space(5);

            var subtitleStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Italic
            };
            GUILayout.Label(L("What's New", "更新内容"), subtitleStyle);

            GUILayout.Space(15);

            // Changelog content
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            DrawChangelogContent();

            EditorGUILayout.EndScrollView();

            GUILayout.Space(10);

            // Don't show again toggle
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            bool newDontShow = EditorGUILayout.ToggleLeft(
                L("Don't show on startup", "起動時に表示しない"),
                dontShowAgain, GUILayout.Width(180));
            if (newDontShow != dontShowAgain)
            {
                dontShowAgain = newDontShow;
                EditorPrefs.SetBool(PREF_KEY_DONT_SHOW, dontShowAgain);
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(5);

            // Buttons
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(L("Open Setup", "セットアップを開く"), GUILayout.Width(120), GUILayout.Height(30)))
            {
                NexusMCPSetupWindow.ShowWindow();
                Close();
            }

            GUILayout.Space(10);

            if (GUILayout.Button(L("Close", "閉じる"), GUILayout.Width(80), GUILayout.Height(30)))
            {
                Close();
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10);
        }

        // Localization helper
        private string L(string en, string ja)
        {
            return currentLanguage == Language.Japanese ? ja : en;
        }

        private void DrawChangelogContent()
        {
            var sectionStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14
            };

            var itemStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true,
                richText = true
            };

            // v1.2.26
            GUILayout.Label(L("v1.2.26 - Unity 6.4 / 6.5 compatibility overhaul (UGUI/TMP reflection + obsolete API)", "v1.2.26 - Unity 6.4 / 6.5 互換性大改修 (UGUI/TMP Reflection 化 + Obsolete API 対応)"), sectionStyle);
            GUILayout.Space(5);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Label(L("<b>★ Critical Fix (ESC-0195〜0208): Unity 6.4 (com.unity.ugui 2.0.0) で Tools メニューが消える問題</b>", "<b>★ Critical 修正 (ESC-0195〜0208): Unity 6.4 (com.unity.ugui 2.0.0) で Tools メニューが消える問題</b>"), itemStyle);
            GUILayout.Label(L("• Unity 6.4+ の com.unity.ugui 2.0.0 は asmdef を持たないため、Synaptic asmdef references の \"Unity.ugui\" / \"Unity.TextMeshPro\" が解決失敗し CS0234 連鎖で Editor スクリプトが全滅していた", "• Unity 6.4+ の com.unity.ugui 2.0.0 は asmdef を持たないため、Synaptic asmdef references の \"Unity.ugui\" / \"Unity.TextMeshPro\" が解決失敗し CS0234 連鎖で Editor スクリプトが全滅していた"), itemStyle);
            GUILayout.Label(L("• Solution: 全 UnityEngine.UI / TMPro 型参照を新規 UIReflection ヘルパー経由の Reflection アクセスに置換 (260+ call site)。asmdef references から Unity.ugui / Unity.TextMeshPro を削除", "• 対応: 全 UnityEngine.UI / TMPro 型参照を新規 UIReflection ヘルパー経由の Reflection アクセスに置換 (260+ 箇所)。asmdef references から Unity.ugui / Unity.TextMeshPro を削除"), itemStyle);
            GUILayout.Label(L("• Result: UGUI/TMP のパッケージ形式 (旧 asmdef 形式 / 新 engine 統合形式) を問わず動作。Unity 2022.3 LTS / Unity 6.0〜6.5 全互換", "• 結果: UGUI/TMP のパッケージ形式 (旧 asmdef 形式 / 新 engine 統合形式) を問わず動作。Unity 2022.3 LTS / Unity 6.0〜6.5 全互換"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ Fix (ESC-0234 周辺): Unity 6.5 obsolete InstanceID API への対応</b>", "<b>★ 修正 (ESC-0234 周辺): Unity 6.5 obsolete InstanceID API への対応</b>"), itemStyle);
            GUILayout.Label(L("• Unity 6.5 で GetInstanceID() / EditorUtility.InstanceIDToObject() / SerializedProperty.objectReferenceInstanceIDValue が [Obsolete(IsError=true)] 化され、ハードエラー (CS0619) になっていた", "• Unity 6.5 で GetInstanceID() / EditorUtility.InstanceIDToObject() / SerializedProperty.objectReferenceInstanceIDValue が [Obsolete(IsError=true)] 化され、ハードエラー (CS0619) になっていた"), itemStyle);
            GUILayout.Label(L("• 新規 IdCompat ヘルパーで GetEntityId() / EntityIdToObject() / objectReferenceEntityIdValue へ #if 切替。Unity 2022.3 〜 Unity 6.5 全対応", "• 新規 IdCompat ヘルパーで GetEntityId() / EntityIdToObject() / objectReferenceEntityIdValue へ #if 切替。Unity 2022.3 〜 Unity 6.5 全対応"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ Fix (ESC-0201): unity_update_component で非アクティブ GameObject が見つからない</b>", "<b>★ 修正 (ESC-0201): unity_update_component で非アクティブ GameObject が見つからない</b>"), itemStyle);
            GUILayout.Label(L("• GameObject.Find() は非アクティブを返さないため、FindObjectsOfType(includeInactive: true) に切り替えて部分一致探索を実装", "• GameObject.Find() は非アクティブを返さないため、FindObjectsOfType(includeInactive: true) に切り替えて部分一致探索を実装"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ Improvement (ESC-0186): ログ出力先を Library/Synaptic AI Pro/logs/ に変更</b>", "<b>★ 改善 (ESC-0186): ログ出力先を Library/Synaptic AI Pro/logs/ に変更</b>"), itemStyle);
            GUILayout.Label(L("• 旧 Assets/Synaptic AI Pro/logs/ から Library/ 配下に移動。Asset DB の余計な再インポートとリポジトリ汚染を解消", "• 旧 Assets/Synaptic AI Pro/logs/ から Library/ 配下に移動。Asset DB の余計な再インポートとリポジトリ汚染を解消"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ Improvement: Bootstrap asmdef + UGUI/TMP パッケージ自動検出</b>", "<b>★ 改善: Bootstrap asmdef + UGUI/TMP パッケージ自動検出</b>"), itemStyle);
            GUILayout.Label(L("• 独立した Bootstrap asmdef (依存ゼロ) で本体 asmdef が壊れていてもインストール誘導ダイアログを必ず表示。UGUI / TextMeshPro 未導入環境を検知して 1-click インストール提案", "• 独立した Bootstrap asmdef (依存ゼロ) で本体 asmdef が壊れていてもインストール誘導ダイアログを必ず表示。UGUI / TextMeshPro 未導入環境を検知して 1-click インストール提案"), itemStyle);

            EditorGUILayout.EndVertical();
            GUILayout.Space(15);

            // v1.2.25
            GUILayout.Label(L("v1.2.25 - Hotfix: Windows 11 Insider IPv6 connection failure", "v1.2.25 - ホットフィックス: Windows 11 Insider IPv6 接続失敗"), sectionStyle);
            GUILayout.Space(5);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Label(L("<b>★ Fix (ESC-0168 / ESC-0170): MCP Disconnected on Windows 11 Insider build 26200+</b>", "<b>★ 修正 (ESC-0168 / ESC-0170): Windows 11 Insider build 26200+ で MCP Disconnected 継続する問題</b>"), itemStyle);
            GUILayout.Label(L("• Windows 11 Insider Preview (26200番台) resolves 'localhost' to ::1 (IPv6) only, while Unity / Node servers bound to the IPv4 path. WebSocket and HTTP clients silently failed to connect", "• Windows 11 Insider Preview (26200番台) では localhost が ::1 (IPv6) のみに解決されるのに対し、Unity / Node 側サーバは IPv4 側にバインドしていたため WebSocket / HTTP クライアントが silent に接続失敗していた"), itemStyle);
            GUILayout.Label(L("• NexusEditorMCPService now pins WebSocket URL, TcpClient port probe, /health checks and the Claude Desktop config rewrite to 127.0.0.1 explicitly", "• NexusEditorMCPService の WebSocket URL / TcpClient ポートプローブ / /health チェック / Claude Desktop config 書換 を全て 127.0.0.1 に明示固定"), itemStyle);
            GUILayout.Label(L("• MCPServer/http-server.js now binds with server.listen(PORT, '127.0.0.1', ...) so the HTTP server on port 8086 stays on IPv4", "• MCPServer/http-server.js の listen を server.listen(PORT, '127.0.0.1', ...) に変更し、port 8086 の HTTP サーバも IPv4 側で待受"), itemStyle);
            GUILayout.Label(L("• No action needed on the user side — just install v1.2.25 and reconnect", "• ユーザー側の追加操作は不要、v1.2.25 を入れて再接続するだけ"), itemStyle);

            EditorGUILayout.EndVertical();
            GUILayout.Space(15);

            // v1.2.24
            GUILayout.Label(L("v1.2.24 - Visual fixes (Rain / Bloom / Terrain) + menu reorganization", "v1.2.24 - 視覚系修正 (雨 / Bloom / Terrain) + メニュー整理"), sectionStyle);
            GUILayout.Space(5);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Label(L("<b>★ Fix (ESC-0136): Rain particles flying sideways</b>", "<b>★ 修正 (ESC-0136): 雨パーティクルが横向きに飛ぶ問題</b>"), itemStyle);
            GUILayout.Label(L("• CreateRainEffect now sets gravityModifier = 1 and rotates the Box shape by (90, 0, 0) so emission points downward by default", "• CreateRainEffect で gravityModifier = 1 を設定し、Box shape の rotation を (90, 0, 0) にして放出方向を真下に向けるよう修正"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ Fix (ESC-0136): Bloom / neon glow not appearing in URP</b>", "<b>★ 修正 (ESC-0136): URP で Bloom / ネオン発光が画面に出ない問題</b>"), itemStyle);
            GUILayout.Label(L("• CreatePostProcessVolume previously added only a BoxCollider. Now reflection-based URP detection generates a full Volume + VolumeProfile + Bloom Override (threshold 0.9 / intensity 1.0 / scatter 0.7)", "• 旧 CreatePostProcessVolume は BoxCollider しか追加していなかった。URP 検出時に reflection で Volume + VolumeProfile + Bloom Override (threshold 0.9 / intensity 1.0 / scatter 0.7) まで自動生成"), itemStyle);
            GUILayout.Label(L("• Non-URP projects still get the original BoxCollider only — no compile-time dependency on URP package", "• URP 非インストール環境は従来通り BoxCollider のみ生成 (URP パッケージへの compile-time 依存無し)"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ Fix (ESC-0144): CREATE_TERRAIN only made flat terrain</b>", "<b>★ 修正 (ESC-0144): CREATE_TERRAIN が平坦地形しか作れなかった問題</b>"), itemStyle);
            GUILayout.Label(L("• Added terrainType parameter (flat / hills / mountains / valleys / plateau / noise) and Perlin fBm heightmap generation with seed / heightScale / noiseFrequency / noiseOctaves controls", "• terrainType パラメータ (flat / hills / mountains / valleys / plateau / noise) と Perlin fBm ハイトマップ生成を追加。seed / heightScale / noiseFrequency / noiseOctaves で調整可"), itemStyle);
            GUILayout.Label(L("• Default remains \"flat\" for backward compatibility", "• デフォルトは互換性のため \"flat\" のまま"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ Fix (ESC-0149): HTTP \"operation\" message type silently dropped</b>", "<b>★ 修正 (ESC-0149): HTTP 経由の \"operation\" メッセージ型が silent drop されていた問題</b>"), itemStyle);
            GUILayout.Label(L("• ProcessMessage switch now handles \"operation\" alongside \"unity_operation\" and \"tool_call\" so http-server.js requests work", "• ProcessMessage の switch に \"operation\" ケースを追加し、http-server.js のリクエストが正しく処理されるように"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ Fix (ESC-0112): console read count/limit mismatch in PlayMode</b>", "<b>★ 修正 (ESC-0112): PlayMode で console read の count/limit が一致しない問題</b>"), itemStyle);
            GUILayout.Label(L("• LogEntries reflection now wrapped with StartGettingEntries / EndGettingEntries try/finally, so the realtime buffer + Unity internal log count are combined correctly", "• LogEntries の reflection 呼び出しを StartGettingEntries / EndGettingEntries で囲んで realtime バッファと Unity 内部ログ件数を正しく合算"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ Added (ESC-0113): External AI Reconnect via HTTP /reconnect</b>", "<b>★ 追加 (ESC-0113): HTTP /reconnect エンドポイントで外部 AI から再接続可能に</b>"), itemStyle);
            GUILayout.Label(L("• POST http://localhost:8086/reconnect now dispatches a system_command to invoke QuickReconnect on the Unity side. CLINE etc. can trigger reconnect without touching Unity", "• POST http://localhost:8086/reconnect が system_command を発行し Unity 側で QuickReconnect を呼ぶ。CLINE 等から Unity を触らずに再接続実行可"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ UX (ESC-0162): Notice added near Complete MCP Setup button</b>", "<b>★ UX (ESC-0162): Complete MCP Setup ボタン付近に注意書きを追加</b>"), itemStyle);
            GUILayout.Label(L("• HelpBox above the button explicitly tells Synaptic Code users to use the HTTP Server tab instead. The two entry points sit adjacent and the misclick was common", "• ボタン上部に HelpBox を追加し、Synaptic Code 利用時は HTTP Server タブを使うよう明示。隣接タブの誤クリックが多発していたため"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ UX: Tools/Synaptic Pro menu consolidated 17 → 5 items</b>", "<b>★ UX: Tools/Synaptic Pro メニューを 17 → 5 項目に集約</b>"), itemStyle);
            GUILayout.Label(L("• Kept on top: Synaptic Setup, AI Reconnect (No Dialog), Join Discord, About, What's New", "• トップに残したのは: Synaptic Setup / AI Reconnect (No Dialog) / Join Discord / About / What's New"), itemStyle);
            GUILayout.Label(L("• Moved into Synaptic Setup → Diagnostics tab: AI Connection Status, AI Reconnect, Auto Reconnect toggle, MCP Server Start/Stop (advanced), Show Port Mapping, Detect Cinemachine Version, Update Shaders for Pipeline, Reset Changelog Preference", "• Synaptic Setup → Diagnostics タブに統合: AI Connection Status / AI Reconnect / Auto Reconnect トグル / MCP Server Start/Stop (高度機能) / Show Port Mapping / Detect Cinemachine Version / Update Shaders for Pipeline / Reset Changelog Preference"), itemStyle);
            GUILayout.Label(L("• External scripts that called the removed menu paths via unity_execute_menu_item must update — the underlying methods are still public, only the [MenuItem] attribute was removed", "• unity_execute_menu_item で旧メニューパスを叩いていた外部スクリプトは要更新。メソッド本体は public のまま残り [MenuItem] 属性だけ外している"), itemStyle);

            EditorGUILayout.EndVertical();
            GUILayout.Space(15);

            // v1.2.23
            GUILayout.Label(L("v1.2.23 - run_csharp result capture + HTTP server stability", "v1.2.23 - run_csharp 戻り値捕捉 + HTTP サーバー安定化"), sectionStyle);
            GUILayout.Space(5);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Label(L("<b>★ Fix (ESC-0107): run_csharp returned result:null for most snippets</b>", "<b>★ 修正 (ESC-0107): run_csharp が殆どの snippet で result:null を返していた問題</b>"), itemStyle);
            GUILayout.Label(L("• Mono.CSharp.Evaluator.Run discards return values. We now detect the last top-level return keyword (depth-aware) and rewrite `return X;` into a static-field sink call, capturing X across multi-statement bodies / foreach{}/if{} blocks / multiplication expressions.", "• Mono.CSharp.Evaluator.Run は戻り値を破棄するため、最後の top-level return キーワードを深度考慮で検出し、`return X;` を静的フィールド経由の sink 呼び出しに書き換え。複文 / foreach { } / if { } / 乗算式すべてで X が捕捉されるようになった"), itemStyle);
            GUILayout.Label(L("• Application.logMessageReceived hook added so Debug.Log / LogWarning / LogError lines appear in the `output` field (was missing — only Console.Out was captured)", "• Application.logMessageReceived フック追加。Debug.Log / LogWarning / LogError が output フィールドに反映 (従来は Console.Out のみで取得漏れ)"), itemStyle);
            GUILayout.Label(L("• Known limitation: Mono parser cannot instantiate generic TYPES (new List&lt;int&gt;() etc). Workaround: use arrays / ArrayList / generic methods (FindObjectsByType&lt;T&gt;)", "• 既知制限: Mono パーサが generic 型インスタンス化 (new List&lt;int&gt;() 等) を解釈できない。回避策は配列 / ArrayList / generic メソッド (FindObjectsByType&lt;T&gt;) 利用"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ Fix (ESC-0108): HTTP server WebSocket dropped every ~30s</b>", "<b>★ 修正 (ESC-0108): HTTP サーバー WebSocket が約30秒で切断</b>"), itemStyle);
            GUILayout.Label(L("• Mono ClientWebSocket doesn't auto-pong protocol-level pings (unlike .NET 5+). Node side terminated the connection every heartbeat interval", "• Mono ClientWebSocket は .NET 5+ と異なり protocol-level ping に自動 pong しない。結果 Node 側が毎ハートビートで切断"), itemStyle);
            GUILayout.Label(L("• http-server.js heartbeat now uses last-message-seen timestamps. Configurable via UNITY_STALE_TIMEOUT_MS env (default 60s)", "• http-server.js の heartbeat を last-message-seen 方式に置換。UNITY_STALE_TIMEOUT_MS 環境変数で調整可 (デフォルト 60s)"), itemStyle);
            GUILayout.Label(L("• Reported and verified by xvpower. — thank you!", "• xvpower. さん報告・検証ありがとうございます"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ Fix: HTTP server died on macOS/Linux during domain reload</b>", "<b>★ 修正: macOS/Linux で domain reload のたびに HTTP サーバーが死亡</b>"), itemStyle);
            GUILayout.Label(L("• Previous Process.Start with piped stdout/stderr caused node to hit SIGPIPE when Unity's C# domain reloaded the pipe readers", "• 旧 Process.Start は stdout/stderr を C# 側 pipe にリダイレクトしており、domain reload で pipe reader が消えて node が SIGPIPE で死亡"), itemStyle);
            GUILayout.Label(L("• Replaced with `nohup node ... >log 2>&1 </dev/null &` detach. Process is independent of Unity's lifecycle, survives all recompiles", "• `nohup node ... >log 2>&1 </dev/null &` 経由の detach に変更。Unity ライフサイクルから独立、recompile で死なない"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ Fix: Auto Reconnect didn't engage on fresh installs</b>", "<b>★ 修正: 新規インストール時に Auto Reconnect が機能しなかった問題</b>"), itemStyle);
            GUILayout.Label(L("• enableMCP default flipped from false → true. Unity is always a CLIENT of the MCP server (port 8090), the opt-in flag was a UX trap", "• enableMCP デフォルトを false → true に変更。Unity は常に MCP サーバー (port 8090) のクライアントなので opt-in 必要なフラグは UX トラップだった"), itemStyle);
            GUILayout.Label(L("• Manual AI Reconnect, Auto Reconnect toggle, and successful connect all force enableMCP=true so it persists across domain reloads", "• 手動 AI Reconnect、Auto Reconnect トグル、接続成功時のいずれでも enableMCP=true を永続化、domain reload を跨いで維持"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ Added: AI Connection tab connection-controls bar</b>", "<b>★ 追加: AI Connection タブに接続コントロールバー</b>"), itemStyle);
            GUILayout.Label(L("• Live MCP status indicator + `AI Reconnect` button (silent) + `Auto Reconnect` checkbox + `Discord` shortcut. Surfaces Tools-menu items in the Setup window where users actually troubleshoot", "• MCP 接続ライブステータス + `AI Reconnect` ボタン (確認ダイアログなし) + `Auto Reconnect` チェックボックス + `Discord` ショートカット。Tools メニュー項目をユーザーが普段デバッグする Setup Window 内に集約"), itemStyle);
            GUILayout.Label(L("• MCP Server: Start/Stop kept in Tools menu (advanced)", "• MCP Server: Start/Stop は引き続き Tools メニュー (上級向け)"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ Fix: port-mapping JSON corruption infinite log loop</b>", "<b>★ 修正: port-mapping JSON 破損による Console ログ無限ループ</b>"), itemStyle);
            GUILayout.Label(L("• NexusProjectPortManager.LoadMapping recovery now deletes .backup before File.Move, then writes a fresh empty mapping. Previously the silent catch left the corrupt file in place, causing frame-rate Console spam after any concurrent write race", "• NexusProjectPortManager.LoadMapping の復旧処理を強化。.backup を事前削除し、復旧後すぐ空の有効 JSON を書き出す。従来は silent catch で破損ファイルが残存し書き込み競合後に毎フレーム Console エラーが出続けていた"), itemStyle);

            EditorGUILayout.EndVertical();
            GUILayout.Space(15);

            // v1.2.22
            GUILayout.Label(L("v1.2.22 - EMERGENCY HOTFIX: MCP timeout (ESC-0102)", "v1.2.22 - 緊急修正: MCP timeout 問題 (ESC-0102)"), sectionStyle);
            GUILayout.Space(5);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Label(L("<b>★ Critical fix: MCP timeout after v1.2.20/21 update</b>", "<b>★ 重要修正: v1.2.20/21 アップデート後の MCP タイムアウト</b>"), itemStyle);
            GUILayout.Label(L("• SynLog.Info called EditorPrefs.GetBool on every log — main-thread only, threw silently on background WebSocket Receive thread and killed the listener Task", "• SynLog.Info が毎回 EditorPrefs.GetBool を呼び、これがメインスレッド限定だったため WebSocket 受信スレッドで例外を投げ、リスナータスクが silent kill されていた"), itemStyle);
            GUILayout.Label(L("• Fix: SynLog now caches verbose flag in a volatile bool, initialized via [InitializeOnLoadMethod]", "• 修正: SynLog の verbose flag を volatile bool でキャッシュ、[InitializeOnLoadMethod] で初期化"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ Fix: NexusEditorMCPService reconnect storm</b>", "<b>★ 修正: NexusEditorMCPService 再接続ストーム</b>"), itemStyle);
            GUILayout.Label(L("• lastConnectionCheckTime was written via Stopwatch-since-classload but compared against Time.realtimeSinceStartup. After domain reload Stopwatch reset to 0 → reconnect gate always true → reconnects every frame", "• lastConnectionCheckTime が Stopwatch ベースで書き込まれ、Time.realtimeSinceStartup で読まれていた。ドメインリロード後に Stopwatch=0 になり差分が常に大きく、毎フレーム再接続ループ発生"), itemStyle);
            GUILayout.Label(L("• Fix: ThreadSafeTime() now calibrates against Time.realtimeSinceStartup on first main-thread tick", "• 修正: ThreadSafeTime() を初回 main-thread tick で Time.realtimeSinceStartup と同期キャリブレーション"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ New: unity_run_csharp tool (equivalent of Blender's run_python)</b>", "<b>★ 新規: unity_run_csharp ツール (Blender run_python 相当)</b>"), itemStyle);
            GUILayout.Label(L("• Execute arbitrary C# against the running Editor — UnityEngine / UnityEditor / Linq / Newtonsoft.Json pre-imported", "• 任意 C# を Editor 内で実行可能。UnityEngine / UnityEditor / Linq / Newtonsoft.Json プリインポート済み"), itemStyle);
            GUILayout.Label(L("• Uses Mono.CSharp.Evaluator instance API + AppDomain assembly injection, does NOT trigger AssemblyReload", "• Mono.CSharp.Evaluator のインスタンス API + AppDomain アセンブリ注入。AssemblyReload を起こさない"), itemStyle);
            GUILayout.Label(L("• Promoted to a SuperSave top-level meta-tool for direct invocation", "• SuperSave のトップレベル meta-tool に昇格、直接呼び出し可"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ Diagnostics</b>", "<b>★ 診断改善</b>"), itemStyle);
            GUILayout.Label(L("• index-supersave.js: ws error handlers, send callback, connection diagnostics", "• index-supersave.js: ws エラーハンドラ、send コールバック、接続診断ログ追加"), itemStyle);
            GUILayout.Label(L("• NexusWebSocketClient.ReceiveLoop (HTTP path): fixed missing EndOfMessage concatenation that truncated >4KB messages", "• NexusWebSocketClient.ReceiveLoop (HTTP 経路): 4KB 超メッセージが切れる EndOfMessage 連結欠落バグ修正"), itemStyle);

            EditorGUILayout.EndVertical();
            GUILayout.Space(15);

            // v1.2.21
            GUILayout.Label(L("v1.2.21 - Windows HTTP Server Cascade Kill Fix (ESC-0095)", "v1.2.21 - Windows HTTP サーバー連鎖死問題の修正 (ESC-0095)"), sectionStyle);
            GUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Label(L("<b>★ Root cause finally identified</b>", "<b>★ 長年未解決だった根本原因を特定</b>"), itemStyle);
            GUILayout.Label(L("• Unity Editor on Windows assigns itself a Win32 Job Object with KILL_ON_JOB_CLOSE", "• Unity Editor (Windows) は自身を Win32 Job Object に登録し KILL_ON_JOB_CLOSE フラグで管理している"), itemStyle);
            GUILayout.Label(L("• Process.Start children inherit the Job and die on assembly reload / PlayMode transitions", "• Process.Start で起動した子プロセスはこの Job を継承し、アセンブリリロード / PlayMode 遷移で殺される"), itemStyle);
            GUILayout.Label(L("• This is why the v1.2.10 → v1.2.11 internal→external rewrite did not fix it (node.exe was still in Unity's Job)", "• v1.2.10 → v1.2.11 で内部 C# → 外部 Node.js に切り出しても改善しなかったのはこのため (node.exe も Unity の Job 内にいた)"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ Fix: CreateProcessW with CREATE_BREAKAWAY_FROM_JOB</b>", "<b>★ 修正: CreateProcessW + CREATE_BREAKAWAY_FROM_JOB</b>"), itemStyle);
            GUILayout.Label(L("• On Windows, node.exe is now spawned via P/Invoke CreateProcessW with BREAKAWAY_FROM_JOB | DETACHED_PROCESS | NEW_PROCESS_GROUP", "• Windows では P/Invoke で CreateProcessW を直接呼び、BREAKAWAY_FROM_JOB | DETACHED_PROCESS | NEW_PROCESS_GROUP を立てて spawn"), itemStyle);
            GUILayout.Label(L("• The spawned node.exe now runs fully independent of Unity's Job Object", "• 起動した node.exe は Unity の Job から完全に独立"), itemStyle);
            GUILayout.Label(L("• Same technique used by Unity's own Burst Compiler (BclApp.cs)", "• Unity 公式の Burst Compiler (BclApp.cs) と同じ技法"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ Fix: PID Recovery After Domain Reload</b>", "<b>★ 修正: ドメインリロード後の PID 復元</b>"), itemStyle);
            GUILayout.Label(L("• Node PID stored in SessionState + EditorPrefs", "• Node PID を SessionState + EditorPrefs に保存"), itemStyle);
            GUILayout.Label(L("• [InitializeOnLoadMethod] re-attaches by PID after reload and reconnects WebSocket only", "• [InitializeOnLoadMethod] でリロード後に PID から再接続。WebSocket だけ繋ぎ直し"), itemStyle);
            GUILayout.Label(L("• No more 'Connect Unity Only' button required after script edits", "• スクリプト編集後に 'Connect Unity Only' を押す必要が無くなる"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ Fix: Parent-PID Watchdog (orphan guard)</b>", "<b>★ 修正: 親 PID watchdog (孤児防止)</b>"), itemStyle);
            GUILayout.Label(L("• http-server.js now self-terminates 5s after Unity dies", "• http-server.js は Unity 死亡後 5秒以内に self-exit"), itemStyle);
            GUILayout.Label(L("• Prevents zombie node.exe even when BREAKAWAY succeeds", "• BREAKAWAY 成功時でも node.exe ゾンビ化を防止"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ Change: Detached log file</b>", "<b>★ 変更: detached モードのログファイル化</b>"), itemStyle);
            GUILayout.Label(L("• DETACHED_PROCESS breaks stdout pipes, so node now writes to MCPServer/logs/http-server.log", "• DETACHED_PROCESS では stdout パイプが使えないため node 側でログファイル直書き (MCPServer/logs/http-server.log)"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ macOS / Linux behaviour unchanged</b>", "<b>★ macOS / Linux は従来通り</b>"), itemStyle);
            GUILayout.Label(L("• No Job-Object-equivalent cascade-kill on these platforms — legacy Process.Start path retained", "• Job Object 相当の仕組みが無いため、従来の Process.Start 経路を維持"), itemStyle);

            EditorGUILayout.EndVertical();
            GUILayout.Space(15);

            // v1.2.20
            GUILayout.Label(L("v1.2.20 - Async Crash Fix & Log Cleanup", "v1.2.20 - 非同期クラッシュ修正 & ログ整理"), sectionStyle);
            GUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Label(L("<b>★ Fix: Async Thread Crash on Disconnect (ESC-0025)</b>", "<b>★ 修正: 切断時の非同期スレッドクラッシュ (ESC-0025)</b>"), itemStyle);
            GUILayout.Label(L("• OnConnectionLost no longer crashes when called from non-main threads", "• OnConnectionLost がメインスレッド外から呼ばれてもクラッシュしない"), itemStyle);
            GUILayout.Label(L("• Introduced ThreadSafeTime() using Stopwatch for async-safe timing", "• Stopwatch ベースの ThreadSafeTime() を導入し非同期安全な時刻取得に"), itemStyle);
            GUILayout.Label(L("• Tool execution self-recovers without Unity restart", "• Unity を再起動せずにツール実行が自己復旧"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ Change: Verbose Log Toggle</b>", "<b>★ 変更: 詳細ログのトグル</b>"), itemStyle);
            GUILayout.Label(L("• Setup Window > HTTP Server > 'Verbose Logs' toggle", "• Setup Window > HTTP Server > 'Verbose Logs' で切り替え"), itemStyle);
            GUILayout.Label(L("• Errors are always logged regardless of toggle", "• Error は常に表示される"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ Change: Smaller Setup Window Min Size</b>", "<b>★ 変更: Setup Window の最小サイズ縮小</b>"), itemStyle);
            GUILayout.Label(L("• 800x800 → 480x480, fits on small laptops and dockable", "• 800x800 → 480x480、小型ラップトップやドッキングに対応"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ Safeguard: Auto-Update Validation</b>", "<b>★ セーフガード: 自動アップデート検証</b>"), itemStyle);
            GUILayout.Label(L("• File size and marker checks prevent partial-archive overwrites", "• ファイルサイズとマーカー検証で破損アーカイブによる上書きを防止"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ Fix: Main Window Repaint Recursion</b>", "<b>★ 修正: メインウィンドウ再描画の無限再帰</b>"), itemStyle);
            GUILayout.Label(L("• ThrottledRepaint() no longer recurses into itself", "• ThrottledRepaint() が自分自身を呼び続ける問題を修正"), itemStyle);

            EditorGUILayout.EndVertical();
            GUILayout.Space(15);

            // v1.2.19
            GUILayout.Label(L("v1.2.19 - Windows Stability (Community Contribution)", "v1.2.19 - Windows安定性 (コミュニティ貢献)"), sectionStyle);
            GUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Label(L("<b>★ Fix: Windows HTTP WebSocket Stability</b>", "<b>★ 修正: Windows HTTP WebSocket安定性</b>"), itemStyle);
            GUILayout.Label(L("• Fixed HTTP server tab becoming unresponsive on Windows", "• WindowsでHTTPサーバータブが無反応になる問題を修正"), itemStyle);
            GUILayout.Label(L("• Added reentrancy guard to prevent concurrent WebSocket connections", "• 同時WebSocket接続を防ぐ再入ガードを追加"), itemStyle);
            GUILayout.Label(L("• Connect timeout (5s) prevents indefinite hang", "• 接続タイムアウト(5秒)で無限ハングを防止"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ Fix: MCP Reconnect Storm Prevention</b>", "<b>★ 修正: MCPリコネクト嵐の防止</b>"), itemStyle);
            GUILayout.Label(L("• Added 10-second minimum interval between reconnect attempts", "• リコネクト試行間に最低10秒のインターバル追加"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ Fix: Setup Window UI Freeze</b>", "<b>★ 修正: Setup Window UIフリーズ</b>"), itemStyle);
            GUILayout.Label(L("• Port check moved to background thread", "• ポートチェックをバックグラウンドスレッドに移動"), itemStyle);
            GUILayout.Label(L("• Main Window repaint throttled to 10Hz", "• メインウィンドウの再描画を10Hzに制限"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ New: MCP Server Start/Stop Menu</b>", "<b>★ 新機能: MCP Server Start/Stopメニュー</b>"), itemStyle);
            GUILayout.Label(L("• Tools > Synaptic Pro > MCP Server: Start/Stop", "• Tools > Synaptic Pro > MCP Server: Start/Stop"), itemStyle);

            GUILayout.Space(5);
            var creditStyle = new GUIStyle(itemStyle) { fontStyle = FontStyle.Italic };
            GUILayout.Label(L("Special thanks to OverlordMethuselah777 for contributing these fixes!", "OverlordMethuselah777氏の修正貢献に感謝します！"), creditStyle);

            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            // v1.2.18
            GUILayout.Label(L("v1.2.18 - Auto-Update & WebSocket Stability", "v1.2.18 - 自動アップデート・WebSocket安定性"), sectionStyle);
            GUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Label(L("<b>★ New: Auto-Update System</b>", "<b>★ 新機能: 自動アップデート</b>"), itemStyle);
            GUILayout.Label(L("• Update check on Unity startup (once per day)", "• Unity起動時にアップデート確認（1日1回）"), itemStyle);
            GUILayout.Label(L("• One-click update from Setup Window banner", "• Setup Windowのバナーからワンクリック更新"), itemStyle);
            GUILayout.Label(L("<b>★ Fix: WebSocket Connection Stability</b>", "<b>★ 修正: WebSocket接続安定性</b>"), itemStyle);
            GUILayout.Label(L("• Added ping/pong heartbeat every 30 seconds", "• 30秒ごとのping/pongハートビート追加"), itemStyle);
            GUILayout.Label(L("• Reconnect attempts increased to 30 (2s intervals)", "• 再接続試行を30回に増加（2秒間隔）"), itemStyle);

            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            // v1.2.17
            GUILayout.Label(L("v1.2.17 - Editor Performance Fix", "v1.2.17 - エディタパフォーマンス修正"), sectionStyle);
            GUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Label(L("<b>★ Fix: HTTP Server Tab Performance</b>", "<b>★ 修正: HTTP Serverタブのパフォーマンス</b>"), itemStyle);
            GUILayout.Label(L("• Fixed editor slowdown when HTTP Server tab is open but server not started", "• HTTPサーバー未起動時にHTTP Serverタブを開くとエディタが重くなる問題を修正"), itemStyle);
            GUILayout.Label(L("• Server status check now runs every 5 seconds instead of every frame", "• サーバーステータス確認を毎フレームから5秒間隔に変更"), itemStyle);
            GUILayout.Label(L("• Added UTF-8 encoding for Node.js process output", "• Node.jsプロセス出力のUTF-8エンコーディングを追加"), itemStyle);

            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            // v1.2.16
            GUILayout.Label(L("v1.2.16 - Unity 6 GUILayout Fix", "v1.2.16 - Unity 6 GUILayout修正"), sectionStyle);
            GUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Label(L("<b>★ Fix: Unity 6 GUILayout & Auto-Start</b>", "<b>★ 修正: Unity 6 GUILayout・Auto-Start</b>"), itemStyle);
            GUILayout.Label(L("• Fixed GUILayout Begin/End mismatch error in DrawHeader and HTTP Server UI", "• DrawHeaderとHTTP Server UIのGUILayout Begin/End不一致エラーを修正"), itemStyle);
            GUILayout.Label(L("• Fixed Auto-Start not working after domain reload", "• ドメインリロード後にAuto-Startが動作しない問題を修正"), itemStyle);
            GUILayout.Label(L("• Auto-Start now detects existing server and reconnects instead of restarting", "• Auto-Start時に既存サーバーを検出し再接続するように改善"), itemStyle);
            GUILayout.Label(L("• Added UTF-8 encoding for Node.js process output", "• Node.jsプロセス出力のUTF-8エンコーディングを追加"), itemStyle);

            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            // v1.2.15
            GUILayout.Label(L("v1.2.15 - Setup Window Performance Fix", "v1.2.15 - Setup Windowパフォーマンス修正"), sectionStyle);
            GUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Label(L("<b>★ Fix: Setup Window Freeze on Large Projects</b>", "<b>★ 修正: 大規模プロジェクトでのSetup Windowフリーズ</b>"), itemStyle);
            GUILayout.Label(L("• FindMCPServerPath now uses cached result instead of searching every call", "• FindMCPServerPathの結果をキャッシュし毎回の検索を排除"), itemStyle);
            GUILayout.Label(L("• Eliminated recursive AllDirectories search that caused 'Hold on' dialog on large projects", "• 大規模プロジェクトで「Hold on」を引き起こしていた再帰的全ディレクトリ検索を廃止"), itemStyle);
            GUILayout.Label(L("• PackageCache search limited to com.synaptic* packages only", "• PackageCache検索をcom.synaptic*パッケージのみに限定"), itemStyle);

            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            // v1.2.14
            GUILayout.Label(L("v1.2.14 - Windows Stability & Process Management Fix", "v1.2.14 - Windows安定性・プロセス管理修正"), sectionStyle);
            GUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Label(L("<b>★ Fix: Hold on Dialog & Process Cleanup</b>", "<b>★ 修正: Hold onダイアログ・プロセス管理</b>"), itemStyle);
            GUILayout.Label(L("• Fixed 'Hold on' dialog caused by blocking CheckSetupStatus on main thread", "• メインスレッドをブロックするCheckSetupStatusによる「Hold on」を修正"), itemStyle);
            GUILayout.Label(L("• Improved Windows command path detection (git/node/npm) - matching MCP config robustness", "• Windowsでのコマンドパス検出を強化（git/node/npm）- MCP設定と同等の堅牢性"), itemStyle);
            GUILayout.Label(L("• Node.js process now properly killed on Unity quit and domain reload", "• Unity終了時・ドメインリロード時にNode.jsプロセスを確実に停止"), itemStyle);
            GUILayout.Label(L("• Auto-Start now kills stale processes before launching", "• Auto-Start時に残存プロセスを終了してから起動"), itemStyle);
            GUILayout.Label(L("• Fixed MCP port conflict when multiple Claude Code sessions connect", "• 複数Claude Codeセッション接続時のMCPポート競合を修正"), itemStyle);

            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            // v1.2.13
            GUILayout.Label(L("v1.2.13 - Setup Window Stability Fix", "v1.2.13 - Setup Window安定性修正"), sectionStyle);
            GUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Label(L("<b>★ Fix: Setup Window Repaint Loop</b>", "<b>★ 修正: Setup Windowの再描画ループ</b>"), itemStyle);
            GUILayout.Label(L("• Fixed 'Hold on' dialog appearing endlessly when switching tabs during MCP connection", "• MCP接続中にタブを切り替えると「Hold on」ダイアログが無限に出る問題を修正"), itemStyle);
            GUILayout.Label(L("• Repaint animation now only runs on AI Connection tab", "• 再描画アニメーションをAI Connectionタブのみに限定"), itemStyle);
            GUILayout.Label(L("• Reduced console debug log output", "• コンソールのデバッグログ出力を削減"), itemStyle);

            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            // v1.2.12
            GUILayout.Label(L("v1.2.12 - Windows HTTP Server Fix", "v1.2.12 - Windows HTTPサーバー修正"), sectionStyle);
            GUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Label(L("<b>★ Fix: Windows HTTP Server Launch</b>", "<b>★ 修正: Windows HTTPサーバー起動</b>"), itemStyle);
            GUILayout.Label(L("• Fixed HTTP Server failing to start on Windows when project path contains spaces", "• プロジェクトパスにスペースが含まれる場合にHTTPサーバーが起動しない問題を修正"), itemStyle);
            GUILayout.Label(L("• Windows now launches Node.js via cmd.exe for reliable path handling", "• Windowsではcmd.exe経由でNode.jsを起動し、パス処理の信頼性を向上"), itemStyle);
            GUILayout.Label(L("• Improved WebSocket connection retry logic (3 attempts with 2s intervals)", "• WebSocket接続のリトライロジックを改善（2秒間隔で3回リトライ）"), itemStyle);

            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            // v1.2.11
            GUILayout.Label(L("v1.2.11 - Claude Desktop CWD Fix", "v1.2.11 - Claude Desktop CWD修正"), sectionStyle);
            GUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Label(L("<b>★ Critical Fix: Claude Desktop MCP Configuration</b>", "<b>★ 重要修正: Claude Desktop MCP設定</b>"), itemStyle);
            GUILayout.Label(L("• Added missing 'cwd' to Claude Desktop config (was only set for VS Code/Gemini/Cursor)", "• Claude Desktop設定に欠けていた'cwd'を追加（VS Code/Gemini/Cursor用には設定済みだった）"), itemStyle);
            GUILayout.Label(L("• Fixes connection timeout on non-standard paths (spaces, Japanese, external drives)", "• 非標準パス（スペース、日本語、外部ドライブ）での接続タイムアウトを修正"), itemStyle);
            GUILayout.Label(L("• Node.js now runs from MCPServer directory for reliable require() resolution", "• Node.jsがMCPServerディレクトリから実行され、require()の解決が確実に"), itemStyle);

            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            // v1.2.10
            GUILayout.Label(L("v1.2.10 - MCP Process Fix & Resources", "v1.2.10 - MCPプロセス修正 & リソース"), sectionStyle);
            GUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Label(L("<b>★ Critical Fix: MCP Dual-Process Startup Bug</b>", "<b>★ 重要修正: MCP二重プロセス起動バグ</b>"), itemStyle);
            GUILayout.Label(L("• Fixed Claude Code starting 2 MCP processes simultaneously", "• Claude Codeが2つのMCPプロセスを同時起動する問題を修正"), itemStyle);
            GUILayout.Label(L("• Added retry logic with WebSocket shutdown handover", "• WebSocketシャットダウンハンドオーバー付きリトライロジック追加"), itemStyle);
            GUILayout.Label(L("• Graceful process takeover for reliable connection", "• 確実な接続のためのグレースフルプロセステイクオーバー"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ New: MCP Resources Protocol</b>", "<b>★ 新機能: MCPリソースプロトコル</b>"), itemStyle);
            GUILayout.Label(L("• synaptic://tools/reference - Compact tools reference", "• synaptic://tools/reference - コンパクトツールリファレンス"), itemStyle);
            GUILayout.Label(L("• synaptic://tools/reference/full - Full with inputSchema", "• synaptic://tools/reference/full - inputSchema付きフル版"), itemStyle);
            GUILayout.Label(L("• GET /resources, /resources/read HTTP endpoints", "• GET /resources, /resources/read HTTPエンドポイント"), itemStyle);

            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            // v1.2.9
            GUILayout.Label(L("v1.2.9 - TcpListener HTTP Server", "v1.2.9 - TcpListener HTTPサーバー"), sectionStyle);
            GUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Label(L("<b>★ Critical Fix: Windows Port Reuse (Domain Reload)</b>", "<b>★ 重要修正: Windowsポート再利用 (ドメインリロード)</b>"), itemStyle);
            GUILayout.Label(L("• Replaced HttpListener with TcpListener", "• HttpListenerをTcpListenerに置換"), itemStyle);
            GUILayout.Label(L("• Added SO_REUSEADDR socket option", "• SO_REUSEADDRソケットオプション追加"), itemStyle);
            GUILayout.Label(L("• Bypasses HTTP.sys kernel driver on Windows", "• WindowsでHTTP.sysカーネルドライバをバイパス"), itemStyle);
            GUILayout.Label(L("• Reliable port recovery after script recompilation", "• スクリプトリコンパイル後のポート復帰が確実に"), itemStyle);
            GUILayout.Label(L("• Removed dangerous ForceKillPortProcess code", "• 危険なForceKillPortProcessコードを削除"), itemStyle);

            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            // v1.2.8
            GUILayout.Label(L("v1.2.8 - Tool Search & Material Fix", "v1.2.8 - ツール検索 & マテリアル修正"), sectionStyle);
            GUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Label(L("<b>★ New: Tool Search Endpoint</b>", "<b>★ 新機能: ツール検索エンドポイント</b>"), itemStyle);
            GUILayout.Label(L("• GET /tools/search?q=keyword - Search by name, title, description", "• GET /tools/search?q=keyword - 名前・タイトル・説明で検索"), itemStyle);
            GUILayout.Label(L("• Optional category filter and result limit", "• カテゴリフィルタと結果数制限をサポート"), itemStyle);
            GUILayout.Label(L("• Relevance scoring for better results", "• 関連性スコアリングで最適な結果を表示"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ New: MCP search_tools Meta-Tool</b>", "<b>★ 新機能: MCP search_tools メタツール</b>"), itemStyle);
            GUILayout.Label(L("• Search tools without knowing exact category", "• カテゴリ不明でもキーワード検索可能"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>Improved: HTTP Server Port Recovery</b>", "<b>改善: HTTPサーバーポート復帰</b>"), itemStyle);
            GUILayout.Label(L("• Force kill process blocking port on startup", "• 起動時にポートをブロックするプロセスを強制終了"), itemStyle);
            GUILayout.Label(L("• No more 'port already in use' errors", "• 「ポート使用中」エラーが発生しなくなりました"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>Fix: MeshRenderer Material Assignment</b>", "<b>修正: MeshRendererマテリアル設定</b>"), itemStyle);
            GUILayout.Label(L("• Fixed circular reference error (Color.linear)", "• 循環参照エラー(Color.linear)を修正"), itemStyle);
            GUILayout.Label(L("• Material/Texture now serialize correctly", "• Material/Textureが正常にシリアライズ"), itemStyle);

            GUILayout.Space(5);
            GUILayout.Label(L("<b>★ New: Console Log Filtering</b>", "<b>★ 新機能: コンソールログフィルタリング</b>"), itemStyle);
            GUILayout.Label(L("• excludeSynaptic: Auto-filter internal logs (default: true)", "• excludeSynaptic: 内部ログ自動除外 (デフォルト: true)"), itemStyle);
            GUILayout.Label(L("• filter/exclude: Custom include/exclude patterns", "• filter/exclude: カスタムパターンで絞り込み"), itemStyle);
            GUILayout.Label(L("• groupByMessage: Deduplicate with count", "• groupByMessage: 重複ログを件数表示でまとめる"), itemStyle);
            GUILayout.Label(L("• Reduces token usage significantly", "• トークン消費を大幅削減"), itemStyle);

            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            // v1.2.7
            GUILayout.Label(L("v1.2.7 - Windows Domain Reload Fix", "v1.2.7 - Windowsドメインリロード修正"), sectionStyle);
            GUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Label(L("<b>★ Critical Fix: Domain Reload Recovery (Windows)</b>", "<b>★ 重要修正: ドメインリロード復帰 (Windows)</b>"), itemStyle);
            GUILayout.Label(L("• Fixed HTTP server not recovering after script recompilation", "• スクリプトリコンパイル後にHTTPサーバーが復帰しない問題を修正"), itemStyle);
            GUILayout.Label(L("• Added ForceReleasePort() before assembly reload", "• アセンブリリロード前にForceReleasePort()を追加"), itemStyle);
            GUILayout.Label(L("• GC.Collect() ensures port is properly released", "• GC.Collect()でポートを確実に解放"), itemStyle);
            GUILayout.Label(L("• Thread.Join(500) for graceful thread termination", "• Thread.Join(500)でスレッドを適切に終了"), itemStyle);
            GUILayout.Label(L("• Increased retry attempts (15x) and delay (1s)", "• リトライ回数(15回)と間隔(1秒)を増加"), itemStyle);

            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            // v1.2.6
            GUILayout.Label(L("v1.2.6 - HTTP Server Stability", "v1.2.6 - HTTPサーバー安定性向上"), sectionStyle);
            GUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Label(L("<b>Port Release & Binding Improvements</b>", "<b>ポート解放・バインディング改善</b>"), itemStyle);
            GUILayout.Label(L("• Added Abort() on Stop for immediate port release", "• Stop時にAbort()追加で即座にポート解放"), itemStyle);
            GUILayout.Label(L("• Added retry logic for TIME_WAIT state (Windows)", "• TIME_WAIT状態へのリトライ処理追加(Windows)"), itemStyle);
            GUILayout.Label(L("• Up to 5 retries with 500ms delay", "• 最大5回リトライ、500ms間隔"), itemStyle);
            GUILayout.Label(L("• Prevents 'port already in use' errors", "• 「ポートが使用中」エラーを防止"), itemStyle);

            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            // v1.2.5
            GUILayout.Label(L("v1.2.5 - VFX Graph Fixes & HTTP Prompt API", "v1.2.5 - VFX Graph修正 & HTTPプロンプトAPI"), sectionStyle);
            GUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Label(L("<b>★ New: HTTP Prompt Endpoint</b>", "<b>★ 新機能: HTTPプロンプトエンドポイント</b>"), itemStyle);
            GUILayout.Label(L("• GET /prompt - Fetch AI control prompt directly", "• GET /prompt - AIプロンプトを直接取得可能"), itemStyle);

            GUILayout.Space(10);

            GUILayout.Label(L("<b>★ New: Test Runner Auto-Execution</b>", "<b>★ 新機能: テストランナー自動実行</b>"), itemStyle);
            GUILayout.Label(L("• unity_run_tests now executes tests automatically", "• unity_run_testsがテストを自動実行"), itemStyle);
            GUILayout.Label(L("• operation: run, results, list", "• operation: run, results, list"), itemStyle);

            GUILayout.Space(10);

            GUILayout.Label(L("<b>VFX Graph Fixes</b>", "<b>VFX Graph修正</b>"), itemStyle);
            GUILayout.Label(L("• Fixed add_context, add_parameter, add_block", "• add_context, add_parameter, add_blockを修正"), itemStyle);
            GUILayout.Label(L("• Fixed set_attribute 'Ambiguous match' error", "• set_attributeの'Ambiguous match'エラー修正"), itemStyle);
            GUILayout.Label(L("• Improved reflection handling for Unity 2022.3+", "• Unity 2022.3+のリフレクション処理改善"), itemStyle);

            GUILayout.Space(10);

            GUILayout.Label(L("<b>Other Fixes</b>", "<b>その他の修正</b>"), itemStyle);
            GUILayout.Label(L("• HTTP server: Play mode transition stability", "• HTTPサーバー: Playモード切替時の安定性向上"), itemStyle);
            GUILayout.Label(L("• Animator window now updates after script changes", "• スクリプト変更後にAnimatorウィンドウが更新"), itemStyle);
            GUILayout.Label(L("• Reduced MCP connection log noise", "• MCP接続ログのノイズ削減"), itemStyle);

            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            // v1.2.4
            GUILayout.Label(L("v1.2.4 - Dynamic Meta-Tools & Critical Fix", "v1.2.4 - 動的メタツール & 重要修正"), sectionStyle);
            GUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Label(L("<b>★ New: Dynamic Meta-Tools</b>", "<b>★ 新機能: 動的メタツール</b>"), itemStyle);
            GUILayout.Label(L("• unity_dynamic_inspect - Inspect any component", "• unity_dynamic_inspect - 任意コンポーネントの検査"), itemStyle);
            GUILayout.Label(L("• unity_dynamic_modify - Modify any property", "• unity_dynamic_modify - 任意プロパティの変更"), itemStyle);
            GUILayout.Label(L("• unity_dynamic_create - Create objects/prefabs", "• unity_dynamic_create - オブジェクト/プレハブ作成"), itemStyle);
            GUILayout.Label(L("• Available as inspect/modify/create in SuperSave", "• SuperSaveでinspect/modify/createとして利用可"), itemStyle);

            GUILayout.Space(10);

            GUILayout.Label(L("<b>Critical Fix</b>", "<b>重要な修正</b>"), itemStyle);
            GUILayout.Label(L("• Fixed prefabs contaminating scene analysis", "• シーン分析にプレハブが混入する問題を修正"), itemStyle);
            GUILayout.Label(L("• analyze_draw_calls now returns only scene objects", "• analyze_draw_callsがシーン内オブジェクトのみ返す"), itemStyle);

            GUILayout.Space(10);

            GUILayout.Label(L("<b>Other Fixes</b>", "<b>その他の修正</b>"), itemStyle);
            GUILayout.Label(L("• HTTP server port cleanup on recompile", "• HTTPサーバーのポート解放問題修正"), itemStyle);
            GUILayout.Label(L("• Script creation path parameter added", "• スクリプト作成のpath引数追加"), itemStyle);
            GUILayout.Label(L("• Windows Node.js path detection improved", "• Windows Node.jsパス検出改善"), itemStyle);

            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            // v1.2.3
            GUILayout.Label(L("v1.2.3 - HTTP API Fix", "v1.2.3 - HTTP API修正"), sectionStyle);
            GUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(L("<b>Fixed</b>", "<b>修正</b>"), itemStyle);
            GUILayout.Label(L("• HTTP API: All tools now work via /execute, /batch", "• HTTP API: 全ツールが/execute, /batchで動作"), itemStyle);
            GUILayout.Label(L("• Fixed 'Unknown operation' error for unmapped tools", "• マッピングなしツールの'Unknown operation'エラー修正"), itemStyle);
            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            // v1.2.2
            GUILayout.Label(L("v1.2.2 - SuperSave Fixes", "v1.2.2 - SuperSave修正"), sectionStyle);
            GUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(L("<b>Fixed</b>", "<b>修正</b>"), itemStyle);
            GUILayout.Label(L("• SuperSave execute tool now works correctly", "• SuperSaveのexecuteツールが正常に動作"), itemStyle);
            GUILayout.Label(L("• All MCP clients use selected server mode", "• 全MCPクライアントで選択モードを使用"), itemStyle);
            GUILayout.Label(L("• Component details for all types (not null)", "• 全コンポーネントの詳細情報を取得可能"), itemStyle);
            GUILayout.Label(L("• Filter aliases: tag, layer, name accepted", "• フィルタ別名: tag, layer, name対応"), itemStyle);
            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            // v1.2.1
            GUILayout.Label(L("v1.2.1 - Hotfix", "v1.2.1 - 緊急修正"), sectionStyle);
            GUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(L("<b>Fixed</b>", "<b>修正</b>"), itemStyle);
            GUILayout.Label(L("• SuperSave Mode: Added shutdown handlers", "• SuperSave: シャットダウン処理追加"), itemStyle);
            GUILayout.Label(L("• Proper MCP client cleanup on exit", "• MCP終了時の適切なクリーンアップ"), itemStyle);
            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            // v1.2.0
            GUILayout.Label(L("v1.2.0 - Token SuperSave Mode", "v1.2.0 - トークン SuperSave モード"), sectionStyle);
            GUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Label(L("<b>★ New: Token SuperSave Mode [Recommended]</b>", "<b>★ 新機能: Token SuperSave モード [推奨]</b>"), itemStyle);
            GUILayout.Label(L("• 99% context reduction with only 3 meta-tools", "• 3つのメタツールで99%のコンテキスト削減"), itemStyle);
            GUILayout.Label(L("• list_categories() - Discover tool categories", "• list_categories() - カテゴリ一覧"), itemStyle);
            GUILayout.Label(L("• list_tools(category) - See tools & parameters", "• list_tools(category) - ツール詳細"), itemStyle);
            GUILayout.Label(L("• execute(tool, params) - Run any of 350+ tools", "• execute(tool, params) - 350+ツール実行"), itemStyle);
            GUILayout.Label(L("• Works with all MCP clients", "• 全MCPクライアント対応"), itemStyle);
            GUILayout.Label(L("• Best for long AI sessions", "• 長いAIセッションに最適"), itemStyle);

            GUILayout.Space(10);

            GUILayout.Label(L("<b>Improvements</b>", "<b>改善</b>"), itemStyle);
            GUILayout.Label(L("• Setup window redesigned with mode selection", "• セットアップ画面をモード選択式に刷新"), itemStyle);
            GUILayout.Label(L("• SuperSave Mode set as default", "• SuperSaveモードをデフォルトに"), itemStyle);
            GUILayout.Label(L("• Better error messages with suggestions", "• エラーメッセージに提案を追加"), itemStyle);
            GUILayout.Label(L("• Tool registry loaded from JSON dynamically", "• ツール定義をJSONから動的読み込み"), itemStyle);

            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            // v1.1.9
            GUILayout.Label(L("v1.1.9 - Stability Fixes", "v1.1.9 - 安定性修正"), sectionStyle);
            GUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(L("• Batch tool format conversion fix", "• バッチツールのフォーマット変換修正"), itemStyle);
            GUILayout.Label(L("• MCP stdio protocol stability (JSON-RPC)", "• MCP stdio プロトコル安定化"), itemStyle);
            GUILayout.Label(L("• HTTP server localhost binding fix", "• HTTPサーバーのlocalhost接続修正"), itemStyle);
            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            // v1.1.8
            GUILayout.Label(L("v1.1.8 - Sphere Skybox", "v1.1.8 - 球体スカイボックス"), sectionStyle);
            GUILayout.Space(5);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(L("• Sphere Skybox: Create skybox from any photo", "• 球体スカイボックス: 写真からスカイボックス生成"), itemStyle);
            GUILayout.Label(L("• Multi-pipeline shader support", "• マルチパイプラインシェーダー対応"), itemStyle);
            GUILayout.Label(L("• MCP server renamed to unity-synaptic", "• MCPサーバー名をunity-synapticに変更"), itemStyle);
            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            // Links
            GUILayout.Label(L("Links", "リンク"), sectionStyle);
            GUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(L("Documentation", "ドキュメント"), GUILayout.Height(25)))
            {
                Application.OpenURL("https://synaptic-ai.net/docs");
            }
            if (GUILayout.Button("Discord", GUILayout.Height(25)))
            {
                Application.OpenURL("https://discord.gg/MXwHCVWmPe");
            }
            if (GUILayout.Button(L("Website", "ウェブサイト"), GUILayout.Height(25)))
            {
                Application.OpenURL("https://synaptic-ai.net");
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Reset the "don't show" preference (for testing)
        /// </summary>
        // v1.2.24: Diagnostics タブに統合、メニューからは除外
        // [MenuItem("Tools/Synaptic Pro/Reset Changelog Preference", false, 101)]
        public static void ResetPreference()
        {
            EditorPrefs.DeleteKey(PREF_KEY_LAST_VERSION);
            EditorPrefs.DeleteKey(PREF_KEY_DONT_SHOW);
            SynLog.Info("[Synaptic] Changelog preference reset. Will show on next startup.");
        }
    }
}
