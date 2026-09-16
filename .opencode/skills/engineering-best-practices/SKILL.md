---
name: engineering-best-practices
description: All 18 system invariants, idempotency gate, concurrency cases, solution versioning, linker rules.
---

# Engineering Best Practices — Idempotency Engine

## The 18 System Invariants

Every implementation decision must be consistent with these invariants (from spec §14):

1. **HTTP Idempotency** — Every observation has a stable `event_id`. Count increments are paired with a `ProcessedIngests` INSERT in the same transaction.
2. **Determinism** — `shape_hash` is a pure function of `(shape_string, shape_version)`. Same input + same rules → same hash, always.
3. **Deterministic Batch Order** — Samples are sorted by `context_fingerprint ASC` before processing. Identical input → identical grouping, no deadlocks.
4. **Anchor Uniqueness** — One anchor per `(kind, domain, shape_hash, shape_version)`. Enforced by unique constraint.
5. **Variant Uniqueness** — One row per `(incident_id, context_fingerprint)`. Enforced by unique constraint.
6. **One Current Solution** — Partial unique index `WHERE superseded_at IS NULL` per variant.
7. **Solution Concurrency** — Conditional supersede `WHERE id = $expected_solution_id AND superseded_at IS NULL`. Unique violation → 409. No advisory lock needed.
8. **Bounded Critical Section** — Advisory lock budget 2,000ms. Timeout → 503 with `Retry-After: 1`.
9. **Lock Only on Insert** — Case A (all fingerprints hit) takes NO advisory lock. Pure atomic `UPDATE occurrence_count`.
10. **Single Transaction** — The winning lock attempt carries all inserts/updates through to COMMIT.
11. **Post-Commit Notify** — `NOTIFY anchor_created` fires only after COMMIT, only if the anchor is truly new (`is_new = true`).
12. **Linker Backstop** — The 5-minute periodic sweep processes anchors that the NOTIFY missed.
13. **Linking Outside Lock** — The Linker never blocks or participates in the ingest critical section.
14. **Embedding Outside Lock** — All `embed()` calls happen before lock acquisition. Never embed while holding the advisory lock.
15. **Peek Before Embed** — Check fingerprint (and anchor) in DB before computing any embedding. Known errors pay zero embedding cost.
16. **Cross-Model Suppression** — KNN and linking compare only same-model vectors. Always filter `AND embedding_model = $active_model`.
17. **One API, Many Clients** — MCP is an agent adapter. CLI, Watcher, and Dashboard call the C# API directly. MCP is not in the path of non-agent clients.
18. **Embedding is a Candidate Signal** — Fingerprint hits are authoritative. Auto-merges via KNN are auditable (`merged_by_embedding = true`).

## Idempotency Gate Pattern

```
INSERT INTO ProcessedIngests (event_id)
ON CONFLICT (event_id) DO NOTHING RETURNING event_id;
```

If `RETURNING` yields no row → already processed → skip the count update. Always in the same transaction as the associated write.

## Concurrency Case Summary

| Case | Lock? | Embed? | Latency |
|---|---|---|---|
| A — all fingerprints hit | No | No | ~5ms |
| B — some fingerprints miss | Yes (Cases B/C only) | Context only | ~35ms |
| C — anchor missing | Yes | Shape + contexts | ~70ms |

## Solution Versioning

Optimistic concurrency, no advisory lock:
1. `UPDATE VariantSolutions SET superseded_at = now() WHERE id = $expected_solution_id AND superseded_at IS NULL`
2. If 0 rows updated and `expected_solution_id` was provided → ROLLBACK → 409
3. `INSERT INTO VariantSolutions (variant_id, text, recorded_by, verified=false)`
4. If unique violation on `one_current_solution_per_variant` → another writer raced → 409

`version` on `IncidentVariants` increments only on user-facing writes. Ingest count increments do NOT bump `version`.
`occurrence_count` counts observations assigned to the semantic variant — not unique fingerprints.

## Linker Behavior

- Triggered by `NOTIFY anchor_created` (post-commit, at-most-once)
- Backstop: 5-minute sweep reading `created_at > linker_last_checkpoint`
- Rate cap: 100 ANN queries per domain per minute — drop if exceeded
- Links within same `kind` only (auto). Cross-kind requires explicit `POST /links`.
- Cross-domain is allowed.
- Only `embedding_model = $active_model` vectors are compared.
- Inserts `IncidentLinks` with `kind = 'suggested'`, ON CONFLICT DO NOTHING.