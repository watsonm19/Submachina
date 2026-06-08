# Synaptic Pro - MCP Unity Integration
# Synaptic Pro - Unity MCP 統合

[English](#english) | [日本語](#japanese)

---

<a name="english"></a>
## English

Transform your Unity development with AI-powered automation and natural language control. With **354 professional tools**, Synaptic Pro revolutionizes game creation through seamless AI integration.

### Key Features

#### **AI Integration**
- **Multi-Platform Support**: Claude Desktop, ChatGPT Desktop, Claude Code CLI, Gemini CLI, Codex CLI, Windsurf, Cline
- **Natural Language Control**: Control Unity entirely through plain English commands
- **Real-time Synchronization**: Instant feedback between Unity and AI clients
- **One-Click Setup**: Automatic configuration for all supported AI platforms

#### **Token SuperSave Mode** (Recommended)
- **99% Context Reduction**: Only 3 meta-tools instead of 354
- `list_categories()` - Discover available tool categories
- `list_tools(category)` - See tools & parameters in a category
- `execute(tool, params)` - Run any tool by name
- Best for long AI sessions - more context for conversation

#### **HTTP Server Mode**
- **Direct REST API**: No MCP client required
- Port 8086 (configurable)
- Endpoints: `/health`, `/tools`, `/execute`, `/batch`, `/prompt`
- Perfect for custom integrations and automation scripts

#### **354 Professional Tools** (34 Categories)
- **VFX** (47 tools): Visual effects, particles, VFX Graph editing
- **Utility** (26 tools): Batch operations, project analysis
- **UI** (16 tools): Complete UI systems from descriptions
- **Animation** (16 tools): Animators, clips, state machines
- **Asset Management** (16 tools): Import, organize, optimize assets
- **Material** (14 tools): PBR materials, shader properties
- **Audio** (14 tools): 3D spatial audio, adaptive music
- **Monitoring** (14 tools): Performance tracking, debugging
- **Camera** (13 tools): Cinemachine, virtual cameras, dolly tracks
- **Input** (13 tools): Custom mapping, gestures, accessibility
- **Shader** (13 tools): URP/HDRP/Built-in compatible shaders
- **GameObject** (12 tools): Create, transform, manage objects
- **Lighting** (10 tools): Realtime/baked lighting, probes
- **GOAP** (10 tools): Goal-oriented action planning AI
- **+ 20 more categories...**

#### **Dynamic Meta-Tools**
- `unity_dynamic_inspect` - Inspect any GameObject, component, or asset
- `unity_dynamic_modify` - Modify any property using property paths
- `unity_dynamic_create` - Universal creation (objects, prefabs, scenes, components)

### Advanced AI Systems

#### **GOAP (Goal-Oriented Action Planning)**
Full runtime engine with C# implementation:
```
"Create a guard AI that patrols waypoints, investigates noises,
and calls for backup when health is below 30%"
```

Pre-built templates:
- Guard AI (patrol, investigate, engage)
- Collector AI (gather resources, deliver)
- Hunter AI (track and pursue targets)
- Companion AI (follow and assist player)
- Merchant AI (trading and economy)

#### **Behavior Tree Runtime**
- Selector, Sequence, Parallel composites
- Decorators: Inverter, Repeater, Cooldown
- Action nodes: Wait, MoveTo, Log, SetBlackboard

#### **Built-in Shaders** (URP/HDRP/Built-in)
- **SynapticWaterPro**: Ocean with Gerstner waves, foam, caustics
- **SynapticSkyPro**: Procedural sky with volumetric clouds
- **SynapticToonPro**: Anime-style cel shading
- **SynapticGrassPro**: GPU-instanced grass with wind

#### **VFX Textures**
- 150+ CC0 textures (Kenney assets)
- Fire, smoke, explosions, sparks, magic effects

### Technical Architecture

- **WebSocket Server**: Real-time bidirectional communication (port 8090)
- **HTTP Server**: REST API for direct access (port 8086)
- **Main Thread Dispatcher**: Safe Unity API calls from async operations
- **Auto-Retry System**: Handles Unity recompilation gracefully (30 retries, 5 min max)
- **Auto-Reconnection**: Maintains connection stability

### Requirements

- Unity 2022.3 LTS or higher
- Windows 10/11, macOS 10.15+, or Linux Ubuntu 20.04+
- Node.js 16+ (for MCP server)
- 4GB RAM minimum (8GB recommended)

### Quick Start

1. **Installation**
   ```
   1. Import from Unity Asset Store
   2. Go to: Synaptic Pro > Setup
   3. Select mode: SuperSave (Recommended) or Full
   4. Click "Complete MCP Setup"
   5. Click "Start AI Connection"
   ```

2. **Basic Usage**
   ```
   "Create a red cube at position 0,5,0"
   "Add rigidbody to the selected object"
   "Create sunny weather with clouds"
   "Generate a UI health bar in top left"
   ```

3. **Advanced Examples**
   ```
   "Create a complete third-person controller with animations"
   "Setup an inventory system with 5x5 grid"
   "Create a day-night cycle that lasts 10 minutes"
   "Generate an enemy spawner that increases difficulty over time"
   ```

### Troubleshooting

**Newtonsoft.Json Missing?**
```
Package Manager > + > Add package by name
Enter: com.unity.nuget.newtonsoft-json
```

**Connection Issues?**
1. Check Node.js: `node --version`
2. Verify server status in Unity console
3. Restart: `Synaptic Pro > Restart Server`

### Links

- **Discord Community**: [Join us](https://discord.com/invite/Y2nUyWvqR3)

---

<a name="japanese"></a>
## 日本語

**354のプロフェッショナルツール**で、AIパワーによる自動化と自然言語制御でUnity開発を革新します。Synaptic ProはAI統合によるゲーム制作の新しい形を提供します。

### 主な機能

#### **AI統合**
- **マルチプラットフォーム対応**: Claude Desktop、ChatGPT Desktop、Claude Code CLI、Gemini CLI、Codex CLI、Windsurf、Cline
- **自然言語制御**: 日本語や英語の普通の文章でUnityを完全制御
- **リアルタイム同期**: UnityとAIクライアント間の即時フィードバック
- **ワンクリック設定**: 対応AI全プラットフォームの自動設定

#### **Token SuperSave Mode**（推奨）
- **99%コンテキスト削減**: 354ツールの代わりに3つのメタツールのみ
- `list_categories()` - 利用可能なカテゴリを確認
- `list_tools(category)` - カテゴリ内のツールとパラメータを表示
- `execute(tool, params)` - ツール名で任意のツールを実行
- 長いAIセッションに最適 - 会話により多くのコンテキストを使用可能

#### **HTTP Serverモード**
- **直接REST API**: MCPクライアント不要
- ポート8086（設定可能）
- エンドポイント: `/health`, `/tools`, `/execute`, `/batch`, `/prompt`
- カスタム統合や自動化スクリプトに最適

#### **354のプロフェッショナルツール**（34カテゴリ）
- **VFX** (47): ビジュアルエフェクト、パーティクル、VFX Graph編集
- **Utility** (26): バッチ操作、プロジェクト分析
- **UI** (16): 説明文から完全なUIシステムを生成
- **Animation** (16): アニメーター、クリップ、ステートマシン
- **Asset Management** (16): アセットのインポート、整理、最適化
- **Material** (14): PBRマテリアル、シェーダープロパティ
- **Audio** (14): 3D空間音響、アダプティブ音楽
- **Monitoring** (14): パフォーマンス追跡、デバッグ
- **Camera** (13): Cinemachine、仮想カメラ、ドリートラック
- **Input** (13): カスタムマッピング、ジェスチャー、アクセシビリティ
- **Shader** (13): URP/HDRP/Built-in対応シェーダー
- **GameObject** (12): オブジェクトの作成、変形、管理
- **Lighting** (10): リアルタイム/ベイクドライティング、プローブ
- **GOAP** (10): ゴール指向行動計画AI
- **+ 他20カテゴリ...**

#### **Dynamic Meta-Tools**
- `unity_dynamic_inspect` - 任意のGameObject、コンポーネント、アセットを調査
- `unity_dynamic_modify` - プロパティパスで任意のプロパティを変更
- `unity_dynamic_create` - 汎用作成（オブジェクト、プレハブ、シーン、コンポーネント）

### 高度なAIシステム

#### **GOAP（ゴール指向行動計画）**
C#実装による完全なランタイムエンジン：
```
"巡回して、物音を調査し、体力が30％以下になったら
援軍を呼ぶ警備AIを作成して"
```

組み込みテンプレート：
- ガードAI（巡回、調査、交戦）
- コレクターAI（リソース収集、配送）
- ハンターAI（追跡と追撃）
- コンパニオンAI（プレイヤーの追従と支援）
- 商人AI（取引と経済）

#### **Behavior Tree Runtime**
- Selector、Sequence、Parallelコンポジット
- デコレーター: Inverter、Repeater、Cooldown
- アクションノード: Wait、MoveTo、Log、SetBlackboard

#### **内蔵シェーダー**（URP/HDRP/Built-in対応）
- **SynapticWaterPro**: Gerstner波、泡、コースティクス付きオーシャン
- **SynapticSkyPro**: ボリュメトリッククラウド付きプロシージャルスカイ
- **SynapticToonPro**: アニメ調セルシェーディング
- **SynapticGrassPro**: 風アニメーション付きGPUインスタンス草

#### **VFXテクスチャ**
- 150以上のCC0テクスチャ（Kenneyアセット）
- 炎、煙、爆発、スパーク、魔法エフェクト

### 技術アーキテクチャ

- **WebSocketサーバー**: リアルタイム双方向通信（ポート8090）
- **HTTPサーバー**: 直接アクセス用REST API（ポート8086）
- **メインスレッドディスパッチャー**: 非同期操作からの安全なUnity API呼び出し
- **自動リトライシステム**: Unityリコンパイルを適切に処理（30回リトライ、最大5分）
- **自動再接続**: 接続の安定性を維持

### 必要環境

- Unity 2022.3 LTS以上
- Windows 10/11、macOS 10.15以上、またはLinux Ubuntu 20.04以上
- Node.js 16以上（MCPサーバー用）
- 最小4GB RAM（推奨8GB）

### クイックスタート

1. **インストール**
   ```
   1. Unity Asset Storeからインポート
   2. メニュー：Synaptic Pro > Setup
   3. モード選択：SuperSave（推奨）またはFull
   4. 「Complete MCP Setup」をクリック
   5. 「Start AI Connection」をクリック
   ```

2. **基本的な使い方**
   ```
   "位置0,5,0に赤いキューブを作成"
   "選択中のオブジェクトにRigidbodyを追加"
   "雲のある晴天を作成"
   "左上にUIヘルスバーを生成"
   ```

3. **高度な例**
   ```
   "アニメーション付きの完全な三人称コントローラーを作成"
   "5x5グリッドのインベントリシステムをセットアップ"
   "10分間続く昼夜サイクルを作成"
   "時間とともに難易度が上がる敵スポナーを生成"
   ```

### トラブルシューティング

**Newtonsoft.Jsonが見つからない？**
```
Package Manager > + > Add package by name
入力: com.unity.nuget.newtonsoft-json
```

**接続の問題？**
1. Node.jsを確認: `node --version`
2. Unityコンソールでサーバーステータスを確認
3. 再起動: `Synaptic Pro > Restart Server`

### リンク

- **Discordコミュニティ**: [参加する](https://discord.com/invite/Y2nUyWvqR3)

---

## Why Choose Synaptic Pro?

- **Save 80% Development Time**: Automate repetitive tasks
- **No Coding Required**: Natural language commands
- **Enterprise-Ready**: Production-quality code generation
- **Active Community**: Regular updates and support
- **Future-Proof**: AI-powered development is the future

---

**Transform your Unity workflow today with Synaptic Pro!**

© 2025 Synaptic Team. All rights reserved.
