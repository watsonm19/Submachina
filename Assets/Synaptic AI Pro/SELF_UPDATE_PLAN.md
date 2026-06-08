# Synaptic AI Pro - 自動更新機能計画

## 概要
BOOTH/サイト版のみ有効な自動更新機能。Asset Store版は無効。

## 分岐方式
```csharp
private static readonly bool ENABLE_SELF_UPDATE = false; // Asset Store: false, BOOTH: true
```
パッケージビルド時に切り替える。

## 更新フロー
```
1. Unity起動時 → バックエンドにバージョンチェック
   GET https://kawaii-agent-backend.vercel.app/api/synaptic-version?product=unity&current=1.2.16

2. 新バージョンあり → Setup Windowに通知バッジ表示
   「v1.2.17が利用可能です」

3. ユーザーが「更新」ボタンを押す → ダウンロード
   - synaptic-site のダウンロードAPIからZIPを取得
   - 進捗バー表示

4. ZIP展開 → Assets/Synaptic AI Pro/ を差し替え
   - 古いファイルを削除
   - 新しいファイルを展開
   - AssetDatabase.Refresh()

5. 完了通知 → Changelog表示
```

## バックエンド（kawaii-agent-backend）
新規エンドポイント: `/api/synaptic-version`
```json
// Request
GET /api/synaptic-version?product=unity&current=1.2.16

// Response
{
  "latest": "1.2.17",
  "updateAvailable": true,
  "downloadUrl": "https://www.synaptic-ai.net/api/downloads/generate?product=unity-mcp-tools",
  "changelog": "v1.2.17 - ...",
  "required": false
}
```

## ダウンロード（synaptic-site）
既存の `/api/downloads/generate` を利用。
ライセンスキー or トークンで認証。

## 注意点
- Asset Store版は `ENABLE_SELF_UPDATE = false` で完全無効化
- ダウンロード中にUnity操作を止めない（バックグラウンド）
- 差し替え前に現在のバージョンをバックアップ
- ネットワークエラー時はスキップ（フェイルサイレント）
- バージョンチェックは1日1回まで（EditorPrefsで記録）

## 優先度
v2.0.0以降で実装予定。現在はバグ修正の安定化を優先。
