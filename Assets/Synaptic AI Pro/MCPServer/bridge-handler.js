// Unity内チャットとデスクトップアプリのブリッジ処理

class BridgeHandler {
    constructor() {
        this.unitySocket = null;
        this.desktopSocket = null;
        this.conversationHistory = [];
        this.pendingToolExecutions = new Map();
    }

    // Unity接続を設定
    setUnityConnection(socket) {
        this.unitySocket = socket;
        // console.log('[Bridge] Unity connected');
        
        // 既存の会話履歴を送信
        if (this.conversationHistory.length > 0) {
            socket.send(JSON.stringify({
                type: 'conversation_history',
                history: this.conversationHistory
            }));
        }
    }

    // デスクトップアプリ接続を設定
    setDesktopConnection(socket) {
        this.desktopSocket = socket;
        // console.log('[Bridge] Desktop app connected');
    }

    // Unity → デスクトップアプリへのメッセージ転送
    forwardToDesktop(message) {
        if (!this.desktopSocket) {
            // デスクトップアプリ未接続の場合はエラー
            if (this.unitySocket) {
                this.unitySocket.send(JSON.stringify({
                    type: 'error',
                    message: 'デスクトップアプリが接続されていません。Claude DesktopまたはGPT Desktopを起動してください。'
                }));
            }
            return;
        }

        // 会話履歴に追加
        this.conversationHistory.push({
            role: 'user',
            content: message.content,
            timestamp: new Date()
        });

        // デスクトップアプリに転送
        this.desktopSocket.send(JSON.stringify({
            type: 'user_message',
            content: message.content,
            context: {
                source: 'unity',
                projectName: message.projectName || 'Unity Project',
                history: this.conversationHistory.slice(-10) // 直近10件の履歴を含める
            }
        }));
    }

    // デスクトップアプリ → Unityへのメッセージ転送
    forwardToUnity(message) {
        if (!this.unitySocket) {
            console.error('[Bridge] Unity not connected');
            return;
        }

        // AIの応答を会話履歴に追加
        if (message.type === 'assistant_response') {
            this.conversationHistory.push({
                role: 'assistant',
                content: message.content,
                timestamp: new Date()
            });
        }

        // Unityに転送
        this.unitySocket.send(JSON.stringify(message));
    }

    // ツール実行結果の処理
    handleToolResult(toolId, result) {
        if (this.pendingToolExecutions.has(toolId)) {
            const { desktopRequestId } = this.pendingToolExecutions.get(toolId);
            
            // デスクトップアプリに結果を送信
            if (this.desktopSocket) {
                this.desktopSocket.send(JSON.stringify({
                    type: 'tool_result',
                    requestId: desktopRequestId,
                    result: result
                }));
            }
            
            this.pendingToolExecutions.delete(toolId);
        }
    }

    // 接続切断の処理
    handleDisconnect(type) {
        if (type === 'unity') {
            this.unitySocket = null;
            // console.log('[Bridge] Unity disconnected');
        } else if (type === 'desktop') {
            this.desktopSocket = null;
            // console.log('[Bridge] Desktop app disconnected');
            
            // Unityに通知
            if (this.unitySocket) {
                this.unitySocket.send(JSON.stringify({
                    type: 'desktop_disconnected',
                    message: 'デスクトップアプリとの接続が切断されました'
                }));
            }
        }
    }

    // 状態を取得
    getStatus() {
        return {
            unityConnected: this.unitySocket !== null,
            desktopConnected: this.desktopSocket !== null,
            historyLength: this.conversationHistory.length,
            pendingTools: this.pendingToolExecutions.size
        };
    }
}

module.exports = BridgeHandler;