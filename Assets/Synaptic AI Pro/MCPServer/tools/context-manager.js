// ===== AIコンテキスト管理システム =====

class ContextManager {
    constructor() {
        this.conversationHistory = [];
        this.projectState = {
            currentProject: null,
            tasks: [],
            completedTasks: [],
            pendingTasks: [],
            unityState: {},
            createdObjects: [],
            modifiedObjects: []
        };
        this.conditionalFlows = new Map();
        this.userPreferences = this.loadUserPreferences();
        this.lastActions = [];
        this.dependencies = new Map();
        this.maxHistorySize = 100;
    }

    // 会話履歴の追加
    addToHistory(entry) {
        this.conversationHistory.push({
            timestamp: new Date(),
            ...entry
        });
        
        // 最大サイズを超えたら古いものを削除
        if (this.conversationHistory.length > this.maxHistorySize) {
            this.conversationHistory = this.conversationHistory.slice(-this.maxHistorySize);
        }
    }

    // コンテキストから意図を推測
    inferIntent(message) {
        const lowerMessage = message.toLowerCase();
        const lastAction = this.lastActions[this.lastActions.length - 1];
        
        // 条件分岐パターン
        const conditionalPatterns = [
            { pattern: /もし(.+)なら(.+)/i, type: 'if_then' },
            { pattern: /(.+)の場合は(.+)/i, type: 'when_then' },
            { pattern: /(.+)かどうか/i, type: 'check_condition' },
            { pattern: /(.+)または(.+)/i, type: 'or_condition' },
            { pattern: /(.+)かつ(.+)/i, type: 'and_condition' }
        ];
        
        // 続きの操作パターン
        const continuationPatterns = [
            { pattern: /次に|それから|その後/i, type: 'sequence' },
            { pattern: /同じ|もう一つ|さらに/i, type: 'repeat' },
            { pattern: /代わりに|ではなく/i, type: 'alternative' },
            { pattern: /全部|すべて|まとめて/i, type: 'batch' }
        ];
        
        // パターンマッチング
        for (const { pattern, type } of conditionalPatterns) {
            const match = message.match(pattern);
            if (match) {
                return { type, match, context: this.getRelevantContext() };
            }
        }
        
        for (const { pattern, type } of continuationPatterns) {
            if (pattern.test(message)) {
                return { type, lastAction, context: this.getRelevantContext() };
            }
        }
        
        // デフォルトの意図推測
        return this.inferFromContext(message);
    }

    // コンテキストベースの意図推測
    inferFromContext(message) {
        const intent = {
            type: 'direct',
            confidence: 0.5,
            suggestedActions: []
        };
        
        // 現在のプロジェクト状態を考慮
        if (this.projectState.pendingTasks.length > 0) {
            intent.suggestedActions.push({
                action: 'continue_tasks',
                tasks: this.projectState.pendingTasks.slice(0, 3)
            });
            intent.confidence += 0.2;
        }
        
        // 最近の操作パターンを考慮
        const recentPatterns = this.analyzeRecentActions();
        if (recentPatterns.repeatingPattern) {
            intent.suggestedActions.push({
                action: 'repeat_pattern',
                pattern: recentPatterns.pattern
            });
            intent.confidence += 0.15;
        }
        
        // ユーザーの好みを考慮
        const preferences = this.matchUserPreferences(message);
        if (preferences.length > 0) {
            intent.preferences = preferences;
            intent.confidence += 0.15;
        }
        
        return intent;
    }

    // 条件分岐フローの作成
    createConditionalFlow(condition, trueActions, falseActions = []) {
        const flowId = `flow_${Date.now()}`;
        this.conditionalFlows.set(flowId, {
            condition,
            trueActions,
            falseActions,
            executed: false,
            result: null
        });
        return flowId;
    }

    // 条件の評価
    async evaluateCondition(condition, unityState) {
        // シンプルな条件評価
        if (condition.type === 'object_exists') {
            return unityState.gameObjects?.some(obj => obj.name === condition.objectName);
        }
        
        if (condition.type === 'property_check') {
            const obj = unityState.gameObjects?.find(o => o.name === condition.objectName);
            if (obj && condition.property in obj) {
                return obj[condition.property] === condition.value;
            }
        }
        
        if (condition.type === 'scene_check') {
            return unityState.activeScene === condition.sceneName;
        }
        
        // より複雑な条件は Unity 側で評価
        return false;
    }

    // 最近の操作パターン分析
    analyzeRecentActions() {
        if (this.lastActions.length < 3) {
            return { repeatingPattern: false };
        }
        
        // 繰り返しパターンの検出
        const recentActions = this.lastActions.slice(-5);
        const patterns = {};
        
        for (let i = 0; i < recentActions.length - 1; i++) {
            const pattern = `${recentActions[i].type}_${recentActions[i + 1].type}`;
            patterns[pattern] = (patterns[pattern] || 0) + 1;
        }
        
        // 最も頻繁なパターンを見つける
        const mostFrequent = Object.entries(patterns)
            .sort(([, a], [, b]) => b - a)[0];
        
        if (mostFrequent && mostFrequent[1] >= 2) {
            return {
                repeatingPattern: true,
                pattern: mostFrequent[0],
                frequency: mostFrequent[1]
            };
        }
        
        return { repeatingPattern: false };
    }

    // ユーザーの好みをマッチング
    matchUserPreferences(message) {
        const preferences = [];
        const lowerMessage = message.toLowerCase();
        
        // 色の好み
        if (this.userPreferences.favoriteColors) {
            for (const color of this.userPreferences.favoriteColors) {
                if (lowerMessage.includes(color)) {
                    preferences.push({ type: 'color', value: color });
                }
            }
        }
        
        // UIレイアウトの好み
        if (this.userPreferences.uiLayout && lowerMessage.includes('ui')) {
            preferences.push({ 
                type: 'ui_layout', 
                value: this.userPreferences.uiLayout 
            });
        }
        
        // 命名規則の好み
        if (this.userPreferences.namingConvention) {
            preferences.push({ 
                type: 'naming', 
                value: this.userPreferences.namingConvention 
            });
        }
        
        return preferences;
    }

    // タスク間の依存関係を設定
    setDependency(taskId, dependsOn) {
        if (!this.dependencies.has(taskId)) {
            this.dependencies.set(taskId, []);
        }
        this.dependencies.get(taskId).push(dependsOn);
    }

    // 実行可能なタスクを取得
    getExecutableTasks() {
        const completed = new Set(this.projectState.completedTasks.map(t => t.id));
        const executable = [];
        
        for (const task of this.projectState.pendingTasks) {
            const deps = this.dependencies.get(task.id) || [];
            if (deps.every(dep => completed.has(dep))) {
                executable.push(task);
            }
        }
        
        return executable;
    }

    // プロジェクト状態の更新
    updateProjectState(update) {
        Object.assign(this.projectState, update);
        
        // Unity状態の同期
        if (update.unityState) {
            this.projectState.unityState = {
                ...this.projectState.unityState,
                ...update.unityState
            };
        }
    }

    // アクションの記録
    recordAction(action) {
        this.lastActions.push({
            ...action,
            timestamp: new Date()
        });
        
        // 最大20アクションまで保持
        if (this.lastActions.length > 20) {
            this.lastActions = this.lastActions.slice(-20);
        }
    }

    // ユーザー設定の読み込み
    loadUserPreferences() {
        // 実際にはファイルやDBから読み込む
        return {
            favoriteColors: ['blue', 'green', 'purple'],
            uiLayout: 'center',
            namingConvention: 'camelCase',
            defaultMaterial: 'Standard',
            preferredUnits: 'meters'
        };
    }

    // 関連するコンテキストを取得
    getRelevantContext() {
        return {
            recentObjects: this.projectState.createdObjects.slice(-5),
            pendingTasks: this.projectState.pendingTasks.slice(0, 5),
            lastActions: this.lastActions.slice(-3),
            currentScene: this.projectState.unityState.activeScene
        };
    }

    // コンテキストのサマリーを生成
    generateContextSummary() {
        const summary = {
            projectName: this.projectState.currentProject?.name || 'Unnamed Project',
            totalTasks: this.projectState.tasks.length,
            completedTasks: this.projectState.completedTasks.length,
            pendingTasks: this.projectState.pendingTasks.length,
            createdObjects: this.projectState.createdObjects.length,
            recentActions: this.lastActions.slice(-5).map(a => a.type),
            activeFlows: this.conditionalFlows.size
        };
        
        return summary;
    }

    // コンテキストのクリア
    clearContext() {
        this.conversationHistory = [];
        this.projectState = {
            currentProject: null,
            tasks: [],
            completedTasks: [],
            pendingTasks: [],
            unityState: {},
            createdObjects: [],
            modifiedObjects: []
        };
        this.conditionalFlows.clear();
        this.lastActions = [];
        this.dependencies.clear();
    }
}

// シングルトンインスタンス
export const contextManager = new ContextManager();

// ヘルパー関数
export function analyzeUserIntent(message, sendUnityCommand) {
    const intent = contextManager.inferIntent(message);
    
    // 条件分岐の処理
    if (intent.type === 'if_then') {
        const [, condition, action] = intent.match;
        return {
            type: 'conditional',
            condition: parseCondition(condition),
            action: parseAction(action),
            execute: async () => {
                const state = await sendUnityCommand('get_scene_info', {});
                const result = await contextManager.evaluateCondition(
                    parseCondition(condition), 
                    state
                );
                
                if (result) {
                    return await executeAction(parseAction(action), sendUnityCommand);
                }
                
                return 'Condition not met';
            }
        };
    }
    
    // 連続操作の処理
    if (intent.type === 'sequence') {
        return {
            type: 'sequence',
            previousAction: intent.lastAction,
            suggestedNext: getNextInSequence(intent.lastAction),
            execute: async () => {
                const next = getNextInSequence(intent.lastAction);
                if (next) {
                    return await executeAction(next, sendUnityCommand);
                }
                return 'No next action in sequence';
            }
        };
    }
    
    // デフォルト処理
    return {
        type: 'direct',
        intent,
        execute: async () => {
            return await processDirectCommand(message, sendUnityCommand);
        }
    };
}

// 条件のパース
function parseCondition(conditionText) {
    // シンプルな条件パース実装
    if (conditionText.includes('存在')) {
        const match = conditionText.match(/(.+)が存在/);
        if (match) {
            return {
                type: 'object_exists',
                objectName: match[1].trim()
            };
        }
    }
    
    return { type: 'unknown', text: conditionText };
}

// アクションのパース
function parseAction(actionText) {
    // シンプルなアクションパース実装
    return {
        type: 'parsed_action',
        text: actionText,
        commands: extractCommands(actionText)
    };
}

// コマンド抽出
function extractCommands(text) {
    const commands = [];
    
    // 作成系コマンド
    if (text.includes('作成') || text.includes('作る')) {
        commands.push({ type: 'create', text });
    }
    
    // 移動系コマンド
    if (text.includes('移動') || text.includes('動かす')) {
        commands.push({ type: 'move', text });
    }
    
    // 変更系コマンド
    if (text.includes('変更') || text.includes('変える')) {
        commands.push({ type: 'modify', text });
    }
    
    return commands;
}

// アクション実行
async function executeAction(action, sendUnityCommand) {
    // アクションタイプに基づいて適切なUnityコマンドを実行
    for (const command of action.commands) {
        switch (command.type) {
            case 'create':
                await sendUnityCommand('create_gameobject', { 
                    name: 'NewObject',
                    description: command.text 
                });
                break;
            case 'move':
                // 移動コマンドの実装
                break;
            case 'modify':
                // 変更コマンドの実装
                break;
        }
    }
    
    return `Executed ${action.commands.length} commands`;
}

// 直接コマンドの処理
async function processDirectCommand(message, sendUnityCommand) {
    // 既存のコマンド処理ロジック
    return 'Direct command processed';
}

// シーケンスの次のアクション取得
function getNextInSequence(lastAction) {
    // シーケンスパターンの定義
    const sequences = {
        'create_ui_button': { next: 'add_button_script', type: 'enhance' },
        'create_gameobject': { next: 'add_component', type: 'enhance' },
        'create_material': { next: 'assign_material', type: 'apply' }
    };
    
    if (lastAction && sequences[lastAction.type]) {
        return sequences[lastAction.type];
    }
    
    return null;
}