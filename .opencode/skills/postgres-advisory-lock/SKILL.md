---
name: postgres-advisory-lock
description: pg_try_advisory_xact_lock semantics, transaction-scoped auto-release, lock key naming.
---

# Postgres Advisory Lock Discipline

## When to Acquire a Lock

Only acquire an advisory lock in **Case B** (anchor exists, some fingerprints miss) and **Case C** (anchor missing). **Case A** (all fingerprints hit) must NEVER acquire a lock — it issues only atomic `UPDATE occurrence_count = occurrence_count + N` and returns.

## Lock Key Format

Use `hashtextextended` (64-bit), NOT `hashtext` (32-bit):

```sql
pg_try_advisory_xact_lock(
    hashtextextended(
        $kind || ':' || $domain || ':' || $shape_hash || ':' || $shape_version,
        0
    )
)
```

The key is scoped to `(kind, domain, shape_hash, shape_version)` — exactly one advisory lock per anchor identity.

## Retry Loop (Pseudocode)

```
deadline = now() + lock_budget_ms   -- lock_budget_ms from SystemConfig, default 2000
acquired = false

WHILE now() < deadline AND NOT acquired:
    BEGIN TRANSACTION
    acquired = pg_try_advisory_xact_lock(<key>)
    IF acquired: BREAK           -- stay in this transaction, proceed to work
    ROLLBACK
    IF now() >= deadline: BREAK
    sleep(50ms + jitter(0..25ms))

IF NOT acquired:
    RETURN 503 Service Unavailable, Retry-After: 1
```

## Transaction Rules

- Lock is **`xact`-scoped**: it is automatically released on COMMIT or ROLLBACK. Never call a manual unlock.
- The transaction that successfully acquires the lock **must carry through to COMMIT** — do not ROLLBACK after acquiring unless there is an unrecoverable error.
- Keep the locked transaction as short as possible: all reads and all embedding calls must happen **before** lock acquisition.

## Embedding Before Lock

All vector embeddings MUST be computed **outside and before** the lock:
- Case B: embed missing context strings before the retry loop.
- Case C: embed `shape_template` and all context strings before the retry loop.

Never call `embed()` while holding the advisory lock.

## Re-Check Under Lock

After acquiring the lock in Cases B/C, always re-check whether the fingerprint or anchor was inserted by a concurrent writer during embedding:
```sql
SELECT id FROM IncidentVariants
WHERE incident_id = $anchor_id AND context_fingerprint = $fingerprint;
```
If found, skip the insert and update count instead. This prevents duplicate variant rows.

## 503 Handling

On timeout (lock not acquired within budget), return:
```
HTTP 503 Service Unavailable
Retry-After: 1
```
Callers (watcher, MCP) should respect the `Retry-After` header and retry with their stable `event_id` — idempotency keys make the retry safe.