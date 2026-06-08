import { z } from 'zod';
import { contextManager, analyzeUserIntent } from './context-manager.js';

// ===== 高度な対話・計画ツール =====
export function registerAdvancedTools(mcpServer, sendUnityCommand) {
    
    // プロジェクト計画ツール
    mcpServer.registerTool('unity_plan_project', {
        title: 'Plan Unity Project',
        description: 'Create a detailed plan and todo list for a Unity project based on natural language description',
        inputSchema: {
            description: z.string().describe('Natural language description of what to create'),
            projectType: z.enum(['game', 'tool', 'simulation', 'visualization', 'prototype']).optional(),
            complexity: z.enum(['simple', 'medium', 'complex']).optional().default('medium')
        }
    }, async (params) => {
        const plan = await analyzeAndPlanProject(params.description);
        await sendUnityCommand('create_project_plan', plan);
        
        return {
            content: [{
                type: 'text',
                text: `プロジェクト計画を作成しました:\n${formatProjectPlan(plan)}`
            }]
        };
    });
    
    // タスク分解ツール
    mcpServer.registerTool('unity_decompose_task', {
        title: 'Decompose Unity Task',
        description: 'Break down a complex task into smaller, manageable subtasks',
        inputSchema: {
            task: z.string().describe('Task to decompose'),
            context: z.string().optional().describe('Additional context or constraints'),
            maxDepth: z.number().optional().default(3)
        }
    }, async (params) => {
        const subtasks = await decomposeTask(params.task, params.context);
        await sendUnityCommand('create_task_list', { tasks: subtasks });
        
        return {
            content: [{
                type: 'text',
                text: `タスクを${subtasks.length}個のサブタスクに分解しました:\n${formatTaskList(subtasks)}`
            }]
        };
    });
    
    // バッチ実行ツール  
    mcpServer.registerTool('unity_execute_batch', {
        title: 'Execute Multiple Unity Operations',
        description: 'Execute a series of Unity operations in sequence with progress feedback',
        inputSchema: {
            tasks: z.array(z.object({
                tool: z.string().describe('Tool name to execute'),
                parameters: z.record(z.any()).describe('Parameters for the tool'),
                description: z.string().describe('Human readable description')
            })),
            progressFeedback: z.boolean().default(true).describe('Send progress updates')
        }
    }, async (params) => {
        const results = [];
        const totalTasks = params.tasks.length;
        
        for (let i = 0; i < params.tasks.length; i++) {
            const task = params.tasks[i];
            
            try {
                // 進捗フィードバック
                if (params.progressFeedback) {
                    console.log(`[Batch ${i+1}/${totalTasks}] ${task.description}`);
                }
                
                // ツール実行
                const result = await sendUnityCommand(task.tool, task.parameters);
                
                results.push({
                    task: task.description,
                    success: result.success,
                    result: result.result || result.error,
                    index: i + 1
                });
                
                // エラーの場合は継続するかどうか判定
                if (!result.success) {
                    console.error(`Task ${i+1} failed: ${result.error}`);
                    // 重要でないエラーは継続、重大なエラーは停止
                    if (result.error?.includes('not found') || result.error?.includes('Unknown operation')) {
                        break;
                    }
                }
                
                // 短い間隔をあける（Unity側の処理待ち）
                await new Promise(resolve => setTimeout(resolve, 200));
                
            } catch (error) {
                results.push({
                    task: task.description,
                    success: false,
                    result: error.message,
                    index: i + 1
                });
                console.error(`Batch execution error: ${error.message}`);
            }
        }
        
        const successCount = results.filter(r => r.success).length;
        const summary = `バッチ実行完了: ${successCount}/${totalTasks}個のタスクが成功\n\n` +
            results.map(r => `${r.index}. ${r.task}: ${r.success ? '✅' : '❌'} ${r.result}`).join('\n');
        
        return {
            content: [{
                type: 'text',
                text: summary
            }]
        };
    });
    
    // インテリジェント実装ツール
    mcpServer.registerTool('unity_implement_feature', {
        title: 'Implement Unity Feature',
        description: 'Intelligently implement a feature based on description and context',
        inputSchema: {
            feature: z.string().describe('Feature description'),
            requirements: z.array(z.string()).optional().describe('Specific requirements'),
            constraints: z.array(z.string()).optional().describe('Constraints or limitations'),
            style: z.enum(['minimal', 'standard', 'detailed']).optional().default('standard')
        }
    }, async (params) => {
        const implementation = await planFeatureImplementation(params);
        const steps = implementation.steps;
        
        // 各ステップを順番に実行
        for (const step of steps) {
            await executeImplementationStep(step, sendUnityCommand);
        }
        
        return {
            content: [{
                type: 'text',
                text: `機能「${params.feature}」を実装しました。\n実行したステップ:\n${steps.map((s, i) => `${i+1}. ${s.description}`).join('\n')}`
            }]
        };
    });
    
    // コンテキスト保持ツール
    mcpServer.registerTool('unity_set_context', {
        title: 'Set Project Context',
        description: 'Set or update the current project context for more intelligent responses',
        inputSchema: {
            projectName: z.string().optional(),
            projectType: z.string().optional(),
            currentPhase: z.enum(['planning', 'prototyping', 'development', 'testing', 'polish']).optional(),
            technologies: z.array(z.string()).optional(),
            goals: z.array(z.string()).optional()
        }
    }, async (params) => {
        await updateProjectContext(params);
        
        return {
            content: [{
                type: 'text',
                text: `プロジェクトコンテキストを更新しました:\n${JSON.stringify(params, null, 2)}`
            }]
        };
    });
    
    // 進捗確認ツール
    mcpServer.registerTool('unity_check_progress', {
        title: 'Check Project Progress',
        description: 'Check the current progress of tasks and implementations',
        inputSchema: {
            scope: z.enum(['all', 'current', 'completed', 'pending']).optional().default('current'),
            detailed: z.boolean().optional().default(false)
        }
    }, async (params) => {
        const progress = await getProjectProgress(params.scope);
        
        return {
            content: [{
                type: 'text',
                text: formatProgressReport(progress, params.detailed)
            }]
        };
    });
}

// ===== ヘルパー関数 =====

async function analyzeAndPlanProject(description) {
    // 自然言語の説明を分析してプロジェクト計画を生成
    const plan = {
        title: extractProjectTitle(description),
        overview: description,
        phases: [],
        tasks: [],
        components: [],
        assets: []
    };
    
    // キーワード分析
    const keywords = extractKeywords(description);
    
    // ゲームプロジェクトの例
    if (keywords.includes('ゲーム') || keywords.includes('game')) {
        plan.phases = [
            { name: 'セットアップ', tasks: ['シーン作成', 'フォルダ構造作成'] },
            { name: 'プロトタイプ', tasks: ['基本操作実装', 'コアメカニクス'] },
            { name: '本実装', tasks: ['UI作成', 'ゲームロジック', 'エフェクト'] },
            { name: '仕上げ', tasks: ['バランス調整', 'パフォーマンス最適化'] }
        ];
    }
    
    // 具体的なタスクを生成
    plan.tasks = generateTasksFromDescription(description, keywords);
    
    return plan;
}

async function decomposeTask(task, context) {
    const subtasks = [];
    
    // タスクの種類を判定
    const taskType = identifyTaskType(task);
    
    switch (taskType) {
        case 'ui_creation':
            subtasks.push(
                { name: 'UIレイアウト設計', priority: 'high' },
                { name: 'Canvas作成', priority: 'high' },
                { name: 'UI要素配置', priority: 'medium' },
                { name: 'スタイル適用', priority: 'low' },
                { name: 'インタラクション実装', priority: 'medium' }
            );
            break;
            
        case 'character_creation':
            subtasks.push(
                { name: 'キャラクターGameObject作成', priority: 'high' },
                { name: 'モデル/スプライト設定', priority: 'high' },
                { name: 'Collider追加', priority: 'medium' },
                { name: '移動スクリプト作成', priority: 'high' },
                { name: 'アニメーション設定', priority: 'medium' }
            );
            break;
            
        case 'system_creation':
            subtasks.push(
                { name: 'システム設計', priority: 'high' },
                { name: 'コアクラス作成', priority: 'high' },
                { name: 'インターフェース定義', priority: 'medium' },
                { name: 'テスト実装', priority: 'medium' },
                { name: 'ドキュメント作成', priority: 'low' }
            );
            break;
            
        default:
            // 一般的なタスク分解
            subtasks.push(
                { name: '要件分析', priority: 'high' },
                { name: '設計', priority: 'high' },
                { name: '実装', priority: 'high' },
                { name: 'テスト', priority: 'medium' },
                { name: '最適化', priority: 'low' }
            );
    }
    
    return subtasks;
}

async function planFeatureImplementation(params) {
    const { feature, requirements, constraints } = params;
    const steps = [];
    
    // 機能の種類を分析
    const featureAnalysis = analyzeFeature(feature);
    
    // 実装ステップを生成
    if (featureAnalysis.needsUI) {
        steps.push({
            type: 'create_ui',
            description: 'UI要素の作成',
            params: {
                elementType: featureAnalysis.uiType || 'Panel',
                name: featureAnalysis.name + '_UI'
            }
        });
    }
    
    if (featureAnalysis.needsScript) {
        steps.push({
            type: 'create_script',
            description: 'スクリプトの作成',
            params: {
                name: featureAnalysis.name + 'Controller',
                template: featureAnalysis.scriptTemplate || 'MonoBehaviour'
            }
        });
    }
    
    if (featureAnalysis.needsGameObject) {
        steps.push({
            type: 'create_gameobject',
            description: 'GameObjectの作成',
            params: {
                objectType: featureAnalysis.objectType || 'Empty',
                name: featureAnalysis.name
            }
        });
    }
    
    // 要件に基づいて追加ステップ
    if (requirements) {
        requirements.forEach(req => {
            const additionalSteps = generateStepsFromRequirement(req);
            steps.push(...additionalSteps);
        });
    }
    
    return { steps };
}

async function executeImplementationStep(step, sendUnityCommand) {
    console.error(`Executing step: ${step.description}`);
    await sendUnityCommand(step.type, step.params);
    
    // ステップ間の待機
    await new Promise(resolve => setTimeout(resolve, 500));
}

// プロジェクトコンテキスト管理
const projectContext = {
    projectName: '',
    projectType: '',
    currentPhase: 'planning',
    technologies: [],
    goals: [],
    completedTasks: [],
    pendingTasks: []
};

async function updateProjectContext(params) {
    Object.assign(projectContext, params);
}

async function getProjectProgress(scope) {
    return {
        total: projectContext.pendingTasks.length + projectContext.completedTasks.length,
        completed: projectContext.completedTasks.length,
        pending: projectContext.pendingTasks.length,
        tasks: scope === 'all' ? 
            [...projectContext.completedTasks, ...projectContext.pendingTasks] :
            scope === 'completed' ? projectContext.completedTasks :
            scope === 'pending' ? projectContext.pendingTasks :
            projectContext.pendingTasks.slice(0, 5)
    };
}

// ===== ユーティリティ関数 =====

function extractProjectTitle(description) {
    // 「〜を作りたい」「〜のような」などのパターンから抽出
    const patterns = [
        /「(.+?)」/,
        /(.+?)を作りたい/,
        /(.+?)のような/,
        /(.+?)みたいな/
    ];
    
    for (const pattern of patterns) {
        const match = description.match(pattern);
        if (match) return match[1];
    }
    
    return 'Unityプロジェクト';
}

function extractKeywords(text) {
    const keywords = [];
    const patterns = {
        game: ['ゲーム', 'game', 'プレイ', 'play'],
        ui: ['UI', 'ボタン', 'メニュー', 'button', 'menu'],
        character: ['キャラ', 'プレイヤー', 'character', 'player'],
        system: ['システム', 'system', '機能', 'feature']
    };
    
    for (const [category, words] of Object.entries(patterns)) {
        if (words.some(word => text.toLowerCase().includes(word))) {
            keywords.push(category);
        }
    }
    
    return keywords;
}

function identifyTaskType(task) {
    const taskLower = task.toLowerCase();
    
    if (taskLower.includes('ui') || taskLower.includes('ボタン') || taskLower.includes('メニュー')) {
        return 'ui_creation';
    } else if (taskLower.includes('キャラ') || taskLower.includes('プレイヤー')) {
        return 'character_creation';
    } else if (taskLower.includes('システム') || taskLower.includes('機能')) {
        return 'system_creation';
    }
    
    return 'general';
}

function analyzeFeature(feature) {
    const analysis = {
        name: feature.split(/[\s　]+/)[0],
        needsUI: false,
        needsScript: false,
        needsGameObject: false,
        uiType: null,
        scriptTemplate: null,
        objectType: null
    };
    
    const featureLower = feature.toLowerCase();
    
    // UI関連
    if (featureLower.includes('ボタン') || featureLower.includes('button')) {
        analysis.needsUI = true;
        analysis.uiType = 'Button';
    } else if (featureLower.includes('メニュー') || featureLower.includes('menu')) {
        analysis.needsUI = true;
        analysis.uiType = 'Panel';
    }
    
    // スクリプト関連
    if (featureLower.includes('動') || featureLower.includes('制御') || featureLower.includes('システム')) {
        analysis.needsScript = true;
    }
    
    // GameObject関連
    if (featureLower.includes('オブジェクト') || featureLower.includes('キャラ')) {
        analysis.needsGameObject = true;
    }
    
    return analysis;
}

function generateTasksFromDescription(description, keywords) {
    const tasks = [];
    let taskId = 1;
    
    // 基本タスク
    tasks.push({
        id: taskId++,
        name: 'プロジェクトセットアップ',
        status: 'pending',
        priority: 'high'
    });
    
    // キーワードに基づくタスク生成
    if (keywords.includes('game')) {
        tasks.push(
            { id: taskId++, name: 'ゲームマネージャー作成', status: 'pending', priority: 'high' },
            { id: taskId++, name: 'プレイヤーコントローラー実装', status: 'pending', priority: 'high' },
            { id: taskId++, name: 'ゲームループ実装', status: 'pending', priority: 'medium' }
        );
    }
    
    if (keywords.includes('ui')) {
        tasks.push(
            { id: taskId++, name: 'UIシステム構築', status: 'pending', priority: 'high' },
            { id: taskId++, name: 'メインメニュー作成', status: 'pending', priority: 'medium' }
        );
    }
    
    return tasks;
}

function generateStepsFromRequirement(requirement) {
    const steps = [];
    const reqLower = requirement.toLowerCase();
    
    if (reqLower.includes('アニメーション') || reqLower.includes('animation')) {
        steps.push({
            type: 'create_animation',
            description: 'アニメーション設定',
            params: { animationName: 'DefaultAnimation' }
        });
    }
    
    if (reqLower.includes('物理') || reqLower.includes('physics')) {
        steps.push({
            type: 'setup_physics',
            description: '物理演算設定',
            params: { addRigidbody: true }
        });
    }
    
    return steps;
}

function formatProjectPlan(plan) {
    let output = `📋 ${plan.title}\n\n`;
    output += `概要: ${plan.overview}\n\n`;
    
    if (plan.phases.length > 0) {
        output += '📅 フェーズ:\n';
        plan.phases.forEach((phase, i) => {
            output += `${i+1}. ${phase.name}\n`;
            phase.tasks.forEach(task => {
                output += `   - ${task}\n`;
            });
        });
    }
    
    if (plan.tasks.length > 0) {
        output += '\n✅ タスク一覧:\n';
        plan.tasks.forEach(task => {
            output += `- [${task.status === 'completed' ? 'x' : ' '}] ${task.name} (${task.priority})\n`;
        });
    }
    
    return output;
}

function formatTaskList(tasks) {
    return tasks.map((task, i) => 
        `${i+1}. ${task.name} [${task.priority}]`
    ).join('\n');
}

function formatProgressReport(progress, detailed) {
    let report = `📊 プロジェクト進捗\n`;
    report += `完了: ${progress.completed}/${progress.total} (${Math.round(progress.completed/progress.total*100)}%)\n\n`;
    
    if (detailed && progress.tasks.length > 0) {
        report += '📋 タスク詳細:\n';
        progress.tasks.forEach(task => {
            const status = task.status === 'completed' ? '✅' : '⏳';
            report += `${status} ${task.name}\n`;
        });
    }
    
    return report;
}