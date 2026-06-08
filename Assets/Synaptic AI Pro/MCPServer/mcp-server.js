const { Server } = require('@modelcontextprotocol/sdk/server/index.js');
const { StdioServerTransport } = require('@modelcontextprotocol/sdk/server/stdio.js');
const { 
    ListToolsRequestSchema, 
    CallToolRequestSchema,
    ListResourcesRequestSchema,
    ReadResourceRequestSchema
} = require('@modelcontextprotocol/sdk/types.js');

// Zodスキーマを MCP JSON Schema に変換
function convertZodSchemaToMCP(zodSchema) {
    if (!zodSchema || !zodSchema._def) return {};
    
    const def = zodSchema._def;
    
    if (def.typeName === 'ZodObject') {
        const properties = {};
        const required = [];
        
        for (const [key, value] of Object.entries(def.shape())) {
            properties[key] = convertZodSchemaToMCP(value);
            if (!value.isOptional()) {
                required.push(key);
            }
        }
        
        return {
            type: 'object',
            properties,
            required: required.length > 0 ? required : undefined
        };
    }
    
    if (def.typeName === 'ZodString') {
        return { type: 'string' };
    }
    
    if (def.typeName === 'ZodNumber') {
        return { type: 'number' };
    }
    
    if (def.typeName === 'ZodBoolean') {
        return { type: 'boolean' };
    }
    
    if (def.typeName === 'ZodArray') {
        return {
            type: 'array',
            items: convertZodSchemaToMCP(def.type)
        };
    }
    
    if (def.typeName === 'ZodEnum') {
        return {
            type: 'string',
            enum: def.values
        };
    }
    
    if (def.typeName === 'ZodOptional') {
        return convertZodSchemaToMCP(def.innerType);
    }
    
    if (def.typeName === 'ZodDefault') {
        const schema = convertZodSchemaToMCP(def.innerType);
        schema.default = def.defaultValue();
        return schema;
    }

    if (def.typeName === 'ZodAny') {
        return {}; // Any type - no restrictions
    }

    if (def.typeName === 'ZodRecord') {
        return { type: 'object' };
    }

    return { type: 'string' };
}

function createServer() {
    const registeredTools = {};
    const registeredResources = {};
    
    const server = new Server(
        {
            name: 'unity-mcp',
            version: '1.1.0',
        },
        {
            capabilities: {
                resources: {},
                tools: {},
            },
        }
    );
    

    // ツール一覧を返すハンドラー
    server.setRequestHandler(ListToolsRequestSchema, async () => {
        return {
            tools: Object.entries(registeredTools).map(([name, tool]) => ({
                name,
                description: tool.description,
                inputSchema: tool.inputSchema
            }))
        };
    });

    // ツール実行ハンドラー
    server.setRequestHandler(CallToolRequestSchema, async (request) => {
        const { name, arguments: args } = request.params;
        
        if (!registeredTools[name]) {
            throw new Error(`Unknown tool: ${name}`);
        }
        
        const tool = registeredTools[name];
        
        try {
            // Zodでバリデーション
            if (tool.zodSchema) {
                tool.zodSchema.parse(args);
            }
            
            const result = await tool.handler(args);
            return result;
        } catch (error) {
            // エラーログを標準エラー出力に出さない（JSON-RPC通信を妨害するため）
            throw error;
        }
    });

    // リソース一覧を返すハンドラー
    server.setRequestHandler(ListResourcesRequestSchema, async () => {
        return {
            resources: Object.entries(registeredResources).map(([uri, resource]) => ({
                uri,
                name: resource.name,
                description: resource.description,
                mimeType: resource.mimeType
            }))
        };
    });

    // リソース読み取りハンドラー
    server.setRequestHandler(ReadResourceRequestSchema, async (request) => {
        const { uri } = request.params;
        
        if (!registeredResources[uri]) {
            throw new Error(`Unknown resource: ${uri}`);
        }
        
        const resource = registeredResources[uri];
        const result = await resource.handler();
        return result;
    });

    // ツール登録メソッド
    server.registerTool = function(name, config, handler) {
        const inputSchema = convertZodSchemaToMCP(config.inputSchema);
        
        registeredTools[name] = {
            description: config.description || config.title,
            inputSchema,
            zodSchema: config.inputSchema,
            handler
        };
        
        // ログを標準エラー出力に出さない
    };

    // リソース登録メソッド
    server.registerResource = function(uri, config, handler) {
        registeredResources[uri] = {
            name: config.title,
            description: config.description,
            mimeType: config.mimeType,
            handler
        };
        
        // ログを標準エラー出力に出さない
    };

    // サーバー起動メソッド
    server.start = async function() {
        const transport = new StdioServerTransport();
        await this.connect(transport);
        // ログを標準エラー出力に出さない
    };

    return server;
}

module.exports = { createServer };