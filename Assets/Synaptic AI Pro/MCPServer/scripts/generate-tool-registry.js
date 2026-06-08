#!/usr/bin/env node

/**
 * generate-tool-registry.js
 *
 * Generates tool-registry.json with embeddings for all Unity MCP tools
 *
 * Usage:
 *   OPENAI_API_KEY=sk-xxx node scripts/generate-tool-registry.js
 *
 * Output: tool-registry.json in MCPServer directory
 */

import { getEmbedding } from '../utils/embedding.js';
import { detectCategory } from '../utils/tool-loader.js';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Load existing index.js to extract tool definitions
async function extractToolsFromIndexJs() {
    const indexPath = path.join(__dirname, '..', 'index.js');
    const content = fs.readFileSync(indexPath, 'utf-8');

    // Simple regex to find mcpServer.registerTool calls
    const toolRegex = /mcpServer\.registerTool\('([^']+)',\s*{[^}]*title:\s*'([^']+)',\s*description:\s*'([^']+)'/g;

    const tools = [];
    let match;

    while ((match = toolRegex.exec(content)) !== null) {
        const [, name, title, description] = match;
        tools.push({
            name,
            title,
            description
        });
    }

    console.log(`[Generator] Found ${tools.length} tools in index.js`);
    return tools;
}

async function generateRegistry() {
    console.log('[Generator] Starting tool registry generation...');

    if (!process.env.OPENAI_API_KEY) {
        console.error('[Generator] ERROR: OPENAI_API_KEY environment variable not set');
        console.error('[Generator] Usage: OPENAI_API_KEY=sk-xxx node scripts/generate-tool-registry.js');
        process.exit(1);
    }

    const tools = await extractToolsFromIndexJs();

    if (tools.length === 0) {
        console.error('[Generator] ERROR: No tools found in index.js');
        process.exit(1);
    }

    const registry = {};
    let completed = 0;

    console.log('[Generator] Generating embeddings (this may take 1-2 minutes)...');

    for (const tool of tools) {
        try {
            // Create embedding text from name and description
            const embeddingText = `${tool.name} ${tool.description}`;
            const embedding = await getEmbedding(embeddingText);

            // Detect category
            const category = detectCategory(tool.name);

            registry[tool.name] = {
                title: tool.title,
                description: tool.description,
                category: category,
                embedding: embedding
            };

            completed++;

            // Progress indicator
            if (completed % 10 === 0) {
                console.log(`[Generator] Progress: ${completed}/${tools.length} (${Math.round(completed / tools.length * 100)}%)`);
            }

            // Rate limiting: 50ms delay between requests
            await new Promise(resolve => setTimeout(resolve, 50));

        } catch (error) {
            console.error(`[Generator] Error processing ${tool.name}:`, error.message);
        }
    }

    // Write to file
    const outputPath = path.join(__dirname, '..', 'tool-registry.json');
    fs.writeFileSync(outputPath, JSON.stringify(registry, null, 2));

    console.log(`[Generator] ✅ Successfully generated tool-registry.json`);
    console.log(`[Generator] Location: ${outputPath}`);
    console.log(`[Generator] Total tools: ${Object.keys(registry).length}`);

    // Category breakdown
    const categoryCount = {};
    for (const meta of Object.values(registry)) {
        categoryCount[meta.category] = (categoryCount[meta.category] || 0) + 1;
    }

    console.log('[Generator] Category breakdown:');
    for (const [category, count] of Object.entries(categoryCount).sort((a, b) => b[1] - a[1])) {
        console.log(`  ${category}: ${count} tools`);
    }

    // Estimate cost
    const textLength = tools.reduce((sum, tool) => sum + tool.name.length + tool.description.length, 0);
    const estimatedTokens = Math.ceil(textLength / 4);
    const estimatedCost = (estimatedTokens / 1000000) * 0.02; // $0.02 per 1M tokens
    console.log(`[Generator] Estimated cost: $${estimatedCost.toFixed(4)}`);
}

generateRegistry().catch(error => {
    console.error('[Generator] Fatal error:', error);
    process.exit(1);
});
