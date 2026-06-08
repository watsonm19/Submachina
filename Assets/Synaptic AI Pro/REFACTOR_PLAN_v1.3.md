# Synaptic AI Pro for Unity - リファクタリング計画 v1.3

## 背景

ESC-0083 (ixora_xxo さんからの指摘):
> NexusExecutor.cs 1ファイルで1148分岐しているのですか？

### 現状 (v1.2.21)
- ファイル: `Editor/NexusExecutor.cs`
- 行数: **51,041 行**
- case 分岐: **283 件** (内部 dispatch table)
- private 関数: **766 個**
- リージョン (// === ...): 114 件

### 問題
- 単一ファイルの肥大化で IDE が遅い (Cursor / VSCode で `Find References` が秒単位)
- コードレビュー困難
- マージ衝突発生確率高
- 新規開発者の理解コスト大

## v1.3 リファクタ方針

### Phase 1: partial class 分割 (互換性 100%)

`NexusExecutor` を **partial class** として複数ファイルに分割。
クラス名・public API は完全に変わらないため**呼び出し側ゼロ修正**。

### 分割案 (機能カテゴリ別)

| ファイル | 内容 | 推定行数 |
|---|---|---|
| `NexusExecutor.cs` | クラス宣言、dispatch メインスイッチ、共通ヘルパー | 3,000 |
| `NexusExecutor.GameObject.cs` | CREATE/UPDATE/DELETE/DUPLICATE_GAMEOBJECT 等 | 4,000 |
| `NexusExecutor.Transform.cs` | SET_TRANSFORM、PARENT、HIERARCHY 操作 | 2,000 |
| `NexusExecutor.Material.cs` | CREATE_MATERIAL / SETUP_MATERIAL / シェーダー解決 | 5,000 |
| `NexusExecutor.Shader.cs` | SHADER_GRAPH、VFX、ParticleSystem 関連 | 6,000 |
| `NexusExecutor.Animation.cs` | Animator、AnimationClip、Timeline、Avatar | 5,000 |
| `NexusExecutor.Physics.cs` | Rigidbody、Collider、Joint | 2,000 |
| `NexusExecutor.UI.cs` | uGUI 関連 (Canvas/Button/Layout/Anchor/Style) | 5,000 |
| `NexusExecutor.Lighting.cs` | Light、ReflectionProbe、LightProbe、ベイク | 2,000 |
| `NexusExecutor.Scene.cs` | Scene 管理、保存、ロード | 1,500 |
| `NexusExecutor.Asset.cs` | AssetDatabase、Prefab、Import 設定 | 3,000 |
| `NexusExecutor.Audio.cs` | AudioSource、AudioClip、AudioMixer | 1,500 |
| `NexusExecutor.Cinemachine.cs` | Cinemachine 関連 | 2,000 |
| `NexusExecutor.AI.cs` | BehaviorTree、GOAP 関連 | 2,500 |
| `NexusExecutor.Environment.cs` | Weather、Sky、TimeOfDay、Terrain | 2,500 |
| `NexusExecutor.Inspect.cs` | GET_GAMEOBJECT_DETAILS、Inspector 関連 | 2,000 |
| `NexusExecutor.Script.cs` | CREATE_SCRIPT、コード生成 | 1,500 |
| `NexusExecutor.Debug.cs` | Console、Log、Monitoring | 1,000 |
| **合計** | | **51,000 行** |

### 実装手順

1. `NexusExecutor` クラスに `partial` キーワード追加
2. カテゴリ別 .cs ファイル作成 (空 partial)
3. メソッドを 1 カテゴリずつ移動
   - private メソッド → 移動先で private
   - private const → 共通定数は元ファイル、専用は移動先
   - private static フィールド → 共通は元、専用は移動先
4. case 分岐の dispatch は元ファイルに残す (switch がデフォルト)
5. ビルド確認 → 1 カテゴリずつ commit

### ESC-0088 命名統合と同時実施

dispatch の重複ツール (GET_GAMEOBJECT_DETAIL / DETAILS など) を統合:

| Before | After |
|---|---|
| GET_GAMEOBJECT_DETAIL | unity_get_gameobject (パラメータで詳細度) |
| GET_GAMEOBJECT_DETAILS | (deprecated alias) |
| GET_SCENE_INFO | unity_get_scene (detail パラメータ) |
| GET_SCENE_SUMMARY | (deprecated alias) |
| GET_INSPECTOR_INFO | unity_inspect (target=inspector) |
| GET_COMPONENT_DETAILS | unity_inspect (target=component) |

旧名称は alias として 6 ヶ月維持 → v1.4 で削除。

## 工数試算

| Phase | 工数 |
|---|---|
| Phase 1 (partial 分割) | 8-12 時間 |
| Phase 2 (命名統合 + alias) | 4-6 時間 |
| Phase 3 (テスト・ビルド検証) | 2-4 時間 |
| 合計 | **14-22 時間 (2-3 営業日)** |

## リリースタイミング

- v1.2.21: 今回 (description 補完、SynLog 移行、応答フォーマット統一)
- v1.3.0: 上記リファクタ完了後 (1-2 ヶ月以内目標)
- v1.4.0: 旧 alias 削除、新 API 標準化

## 互換性

- 全 MCP ツール名 (unity_*) は変わらない
- API レスポンス形式は変わらない
- ユーザー側ゼロ修正で動作継続

## 検証

- 既存プロジェクトで全 355 ツール動作確認
- VS Code / Cursor / Claude Desktop / GitHub Copilot で MCP 接続確認
- BOOTH / Asset Store 既購入者全員へ通知

## ESC ステータス

- ESC-0083: v1.3 リリース時に close
- ESC-0088: v1.3 リリース時に close
