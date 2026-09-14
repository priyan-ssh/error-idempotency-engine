---
name: pgvector-semantic-search
description: pgvector setup, HNSW tuning, cosine distance queries, KNN thresholds, cross-model suppression.
trigger: model_decision
---

# pgvector Semantic Search

## Dimension and Model

- All embeddings are **384-dimensional**: `VECTOR(384)`
- Default model: `all-MiniLM-L6-v2` via ONNX Runtime (CPU-only, local)
- Model name is stored in every anchor (`ProcessedIncidents.embedding_model`) and every variant (`IncidentVariants.embedding_model`)
- `EmbeddingProvider` interface:
  ```csharp
  interface IEmbeddingProvider {
      string ModelId { get; }        // "all-MiniLM-L6-v2"
      int Dimensions { get; }        // 384
      Task<float[]> EmbedAsync(string text, CancellationToken ct);
      Task<float[][]> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct);
  }
  ```

## Cosine Distance Operator

Use `<=>` for cosine distance: **0 = identical, 2 = opposite** (this is distance, not similarity).

```sql
-- Distance query
SELECT id, (context_embedding <=> $vec) AS dist
FROM IncidentVariants
WHERE incident_id = $anchor_id
  AND embedding_model = $active_model
ORDER BY context_embedding <=> $vec ASC
LIMIT 1;
```

## Index Setup

```sql
-- On ProcessedIncidents (for cross-anchor linking ANN)
CREATE INDEX idx_incidents_embedding
    ON ProcessedIncidents
    USING hnsw (error_embedding vector_cosine_ops);

-- On IncidentVariants (for future global queries; NOT used in per-incident KNN)
CREATE INDEX idx_variants_embedding
    ON IncidentVariants
    USING hnsw (context_embedding vector_cosine_ops);
```

Always use `vector_cosine_ops` when the operator is `<=>`.

## Per-Incident KNN — Does NOT Route Through HNSW

When finding a matching variant for a new fingerprint, the search is **per-incident** (WHERE incident_id = $anchor_id). The candidate set is small. The query planner uses `idx_variants_incident` and performs an **exact sort** — the HNSW index on `context_embedding` is NOT used on this path. Do not expect HNSW to accelerate per-incident lookups.

## KNN Classification Thresholds

Applied after the per-incident exact KNN query:

| Distance (`<=>`) | Action |
|---|---|
| `< 0.10` | Same variant → increment `occurrence_count`, set `merged_by_embedding = true` |
| `0.10 ≤ dist < 0.20` | Borderline → insert new variant with `status = 'needs_review'` |
| `≥ 0.20` or no match | Novel cause → insert new variant with `status = 'active'` |

These are **heuristics**, not correctness rules. Fingerprint hits are authoritative. Embedding similarity is a candidate signal only.

## Cross-Model Suppression

**Never** compare vectors produced by different embedding models. Always include:
```sql
AND embedding_model = $active_model
```
in any KNN or linking query. During a model swap, some variants are invisible to KNN until re-embedded — this is intentional (a duplicate is safer than a misclassification).

## Linker ANN Threshold

For cross-anchor linking (the Linker service):
- Similarity threshold: **> 0.90** (equivalently, distance < 0.10)
- Scope: same `kind`, cross-`domain` allowed
- Only `embedding_model = $active_model`
- Rate cap: 100 ANN queries per domain per minute

## When Embedding Is Called

| Case | Shape embed | Context embed |
|---|---|---|
| Case A (all fingerprints hit) | No | No |
| Case B (some fingerprints miss) | No | Missing contexts only |
| Case C (anchor missing) | Yes | All contexts |
| Lookup, anchor found | No | Fingerprint misses only |
| Lookup, anchor missing | No | No |

## Peek Before Embed

Always check fingerprint in DB before computing any embedding:
```sql
SELECT id FROM IncidentVariants
WHERE incident_id = $anchor_id AND context_fingerprint = $fingerprint;
```
If found → no embedding needed. Only compute embedding for genuine misses.