#!/usr/bin/env node

/**
 * generate-simple-registry.js
 *
 * Generates tool-registry.json WITHOUT embeddings (no API key required)
 * Now includes inputSchema for LLM tool usage
 *
 * Usage:
 *   node scripts/generate-simple-registry.js
 */

import { detectCategory } from '../utils/tool-loader.js';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

/**
 * Parse zod schema string into JSON Schema
 */
function parseZodSchema(zodString) {
    if (!zodString) return null;

    const schema = {
        type: 'object',
        properties: {},
        required: []
    };

    // Match property definitions like: name: z.string().describe('...')
    // Handle nested objects and various zod methods
    const lines = zodString.split('\n');
    let braceDepth = 0;

    for (let li = 0; li < lines.length; li++) {
        const line = lines[li];
        const trimmed = line.trim();

        // Track brace depth for nested objects
        braceDepth += (trimmed.match(/\{/g) || []).length;
        braceDepth -= (trimmed.match(/\}/g) || []).length;

        // Match top-level property: propertyName: z.type(...)
        const propMatch = trimmed.match(/^(\w+):\s*z\.(\w+)\(/);
        if (propMatch && braceDepth <= 1) {
            const [, propName, zodType] = propMatch;

            // Skip 'inputSchema' itself - we're parsing its contents
            if (propName === 'inputSchema') continue;

            const prop = {};

            // Build "lookahead window" — if the property continues to next lines
            // (multi-line z.enum/z.union/etc), accumulate until parens balance.
            // This lets us catch .describe() that appears on a later line.
            let window = line;
            let parenDepth = (line.match(/\(/g) || []).length - (line.match(/\)/g) || []).length;
            let extraLines = 0;
            // Continue while: parens unbalanced OR next line is a chain continuation (.method)
            while ((li + extraLines + 1) < lines.length && extraLines < 30) {
                const nextLine = lines[li + extraLines + 1];
                const nextTrim = nextLine.trim();
                const isChainContinuation = nextTrim.startsWith('.');
                if (parenDepth > 0 || isChainContinuation) {
                    extraLines++;
                    window += '\n' + nextLine;
                    parenDepth += (nextLine.match(/\(/g) || []).length;
                    parenDepth -= (nextLine.match(/\)/g) || []).length;
                } else {
                    break;
                }
            }

            const isOptional = /\.optional\(\)/.test(window);

            // Extract description (single quote, double quote, or backtick)
            let descMatch = window.match(/\.describe\(\s*'((?:[^']|\\')*)'\s*\)/);
            if (!descMatch) descMatch = window.match(/\.describe\(\s*"((?:[^"]|\\")*)"\s*\)/);
            if (!descMatch) descMatch = window.match(/\.describe\(\s*`([\s\S]*?)`\s*\)/);
            if (descMatch) prop.description = descMatch[1].split('\n')[0].trim();

            // Extract default value
            const defaultMatch = window.match(/\.default\(([^)]+)\)/);
            if (defaultMatch) {
                try {
                    const defaultVal = defaultMatch[1].trim();
                    if (defaultVal.startsWith("'") || defaultVal.startsWith('"')) {
                        prop.default = defaultVal.slice(1, -1);
                    } else if (defaultVal === 'true' || defaultVal === 'false') {
                        prop.default = defaultVal === 'true';
                    } else if (!isNaN(Number(defaultVal))) {
                        prop.default = Number(defaultVal);
                    } else {
                        prop.default = defaultVal;
                    }
                } catch {}
            }

            // Map zod types to JSON Schema
            switch (zodType) {
                case 'string':
                    prop.type = 'string';
                    break;
                case 'number':
                    prop.type = 'number';
                    break;
                case 'boolean':
                    prop.type = 'boolean';
                    break;
                case 'enum':
                    prop.type = 'string';
                    const enumMatch = window.match(/z\.enum\(\[([\s\S]+?)\]\)/);
                    if (enumMatch) {
                        prop.enum = enumMatch[1].split(',')
                            .map(s => s.trim().replace(/['"`]/g, ''))
                            .filter(s => s.length > 0);
                    }
                    break;
                case 'object':
                    prop.type = 'object';
                    if (window.includes('x: z.number()') || window.includes('x:z.number()')) {
                        prop.properties = {
                            x: { type: 'number' },
                            y: { type: 'number' },
                            z: { type: 'number' }
                        };
                    }
                    break;
                case 'array':
                    prop.type = 'array';
                    break;
                case 'union':
                    prop.type = 'string';
                    break;
                default:
                    prop.type = 'string';
            }

            schema.properties[propName] = prop;

            if (!isOptional && !defaultMatch) {
                schema.required.push(propName);
            }
        }
    }

    // Remove empty required array
    if (schema.required.length === 0) {
        delete schema.required;
    }

    return Object.keys(schema.properties).length > 0 ? schema : null;
}

/**
 * Extract inputSchema block from tool definition
 */
function extractInputSchema(content, toolStartIndex) {
    // Find inputSchema: z.object({ starting from toolStartIndex
    const searchArea = content.substring(toolStartIndex, toolStartIndex + 3000);
    const schemaStart = searchArea.indexOf('inputSchema: z.object({');

    if (schemaStart === -1) return null;

    // Find matching closing brace
    let braceCount = 0;
    let inSchema = false;
    let schemaEnd = schemaStart;

    for (let i = schemaStart; i < searchArea.length; i++) {
        const char = searchArea[i];
        if (char === '{') {
            braceCount++;
            inSchema = true;
        } else if (char === '}') {
            braceCount--;
            if (inSchema && braceCount === 0) {
                schemaEnd = i + 1;
                break;
            }
        }
    }

    const schemaBlock = searchArea.substring(schemaStart, schemaEnd);
    return parseZodSchema(schemaBlock);
}

// Load existing index.js to extract tool definitions
function extractToolsFromIndexJs() {
    const indexPath = path.join(__dirname, '..', 'index.js');
    const content = fs.readFileSync(indexPath, 'utf-8');

    const tools = [];

    // Find all registerTool calls with their positions
    const registerRegex = /mcpServer\.registerTool\('([^']+)',\s*\{/g;
    let match;

    while ((match = registerRegex.exec(content)) !== null) {
        const name = match[1];
        const startIndex = match.index;

        // Extract title and description from the block after this match
        const blockArea = content.substring(startIndex, startIndex + 2000);

        // Title
        const titleMatch = blockArea.match(/title:\s*'([^']+)'/);
        const title = titleMatch ? titleMatch[1] : name;

        // Description (single quote or backtick)
        let description = '';
        const descSingleMatch = blockArea.match(/description:\s*'([^']+)'/);
        const descBacktickMatch = blockArea.match(/description:\s*`([^`]+)`/);

        if (descSingleMatch) {
            description = descSingleMatch[1].substring(0, 500);
        } else if (descBacktickMatch) {
            description = descBacktickMatch[1].split('\n')[0].trim().substring(0, 500);
        }

        // Extract inputSchema
        const inputSchema = extractInputSchema(content, startIndex);

        tools.push({
            name,
            title,
            description,
            inputSchema
        });
    }

    // Remove duplicates
    const uniqueTools = [];
    const seenNames = new Set();
    for (const tool of tools) {
        if (!seenNames.has(tool.name)) {
            seenNames.add(tool.name);
            uniqueTools.push(tool);
        }
    }

    console.log(`[Generator] Found ${uniqueTools.length} tools in index.js`);
    return uniqueTools;
}

function generateRegistry() {
    console.log('[Generator] Starting simple tool registry generation (with inputSchema)...');

    const tools = extractToolsFromIndexJs();

    if (tools.length === 0) {
        console.error('[Generator] ERROR: No tools found in index.js');
        process.exit(1);
    }

    const registry = {};
    let schemaCount = 0;

    for (const tool of tools) {
        // Detect category
        const category = detectCategory(tool.name);

        registry[tool.name] = {
            title: tool.title,
            description: tool.description,
            category: category,
            inputSchema: tool.inputSchema || null,
            embedding: null
        };

        if (tool.inputSchema) {
            schemaCount++;
        }
    }

    // Write to file
    const outputPath = path.join(__dirname, '..', 'tool-registry.json');
    fs.writeFileSync(outputPath, JSON.stringify(registry, null, 2));

    console.log(`[Generator] ✅ Successfully generated tool-registry.json`);
    console.log(`[Generator] Location: ${outputPath}`);
    console.log(`[Generator] Total tools: ${Object.keys(registry).length}`);
    console.log(`[Generator] Tools with inputSchema: ${schemaCount}`);

    // Category breakdown
    const categoryCount = {};
    for (const meta of Object.values(registry)) {
        categoryCount[meta.category] = (categoryCount[meta.category] || 0) + 1;
    }

    console.log('[Generator] Category breakdown:');
    for (const [category, count] of Object.entries(categoryCount).sort((a, b) => b[1] - a[1])) {
        console.log(`  ${category}: ${count} tools`);
    }
}

generateRegistry();
