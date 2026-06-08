// utils/embedding.js - OpenAI Embedding API integration for tool similarity search

import OpenAI from 'openai';

const openai = new OpenAI({
    apiKey: process.env.OPENAI_API_KEY || ''
});

// In-memory cache for embeddings
const embeddingCache = new Map();

/**
 * Get embedding vector for text using OpenAI API
 * @param {string} text - Text to embed
 * @returns {Promise<number[]>} - Embedding vector (1536 dimensions)
 */
export async function getEmbedding(text) {
    if (!text || text.trim().length === 0) {
        console.warn('[Embedding] Empty text provided, returning zero vector');
        return new Array(1536).fill(0);
    }

    // Check cache first
    const cacheKey = text.toLowerCase().trim();
    if (embeddingCache.has(cacheKey)) {
        return embeddingCache.get(cacheKey);
    }

    try {
        if (!process.env.OPENAI_API_KEY) {
            console.warn('[Embedding] No OpenAI API key, using zero vector fallback');
            return new Array(1536).fill(0);
        }

        const response = await openai.embeddings.create({
            model: 'text-embedding-3-small',
            input: text,
        });

        const embedding = response.data[0].embedding;

        // Cache the result
        embeddingCache.set(cacheKey, embedding);

        return embedding;
    } catch (error) {
        console.error('[Embedding] Error:', error.message);
        // Fallback: return zero vector
        return new Array(1536).fill(0);
    }
}

/**
 * Calculate cosine similarity between two vectors
 * @param {number[]} vecA - First vector
 * @param {number[]} vecB - Second vector
 * @returns {number} - Similarity score (0-1)
 */
export function cosineSimilarity(vecA, vecB) {
    if (!vecA || !vecB || vecA.length !== vecB.length) {
        return 0;
    }

    let dotProduct = 0;
    let normA = 0;
    let normB = 0;

    for (let i = 0; i < vecA.length; i++) {
        dotProduct += vecA[i] * vecB[i];
        normA += vecA[i] * vecA[i];
        normB += vecB[i] * vecB[i];
    }

    const denominator = Math.sqrt(normA) * Math.sqrt(normB);

    if (denominator === 0) {
        return 0;
    }

    return dotProduct / denominator;
}

/**
 * Clear embedding cache (useful for testing)
 */
export function clearCache() {
    embeddingCache.clear();
}

/**
 * Get cache statistics
 */
export function getCacheStats() {
    return {
        size: embeddingCache.size,
        keys: Array.from(embeddingCache.keys())
    };
}
