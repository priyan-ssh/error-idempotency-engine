---
name: dapper-conventions
description: Repository patterns, CommandDefinition, ON CONFLICT patterns, idempotency gate, pgvector binding, deterministic ordering.
---

# Dapper Conventions for the Idempotency Engine

## The Fundamental Split

| Layer | Tool | Location |
|---|---|---|
| Reads | EF Core (`AsNoTracking()`) | `ReadRepositories/` |
| Critical section writes | Dapper / `NpgsqlCommand` | `WriteRepositories/` |
| Schema migrations | DbUp plain `.sql` files | `migrations/` |

Never use EF Core for writes. Never use EF Core for migrations.

## CommandDefinition Pattern

Always wrap Dapper calls with explicit timeout and cancellation support:

```csharp
var cmd = new CommandDefinition(
    sql,
    parameters: new { eventId, variantId, count },
    commandTimeout: 5,          // seconds
    cancellationToken: ct
);
var result = await connection.QuerySingleOrDefaultAsync<Guid?>(cmd);
```

## ON CONFLICT … DO NOTHING RETURNING

The idempotency gate and anchor insert both use this pattern:

```sql
-- Idempotency gate (ProcessedIngests)
INSERT INTO ProcessedIngests (event_id)
VALUES (@EventId)
ON CONFLICT (event_id) DO NOTHING
RETURNING event_id;

-- Anchor insert (ProcessedIncidents)
INSERT INTO ProcessedIncidents (id, kind, domain, ...)
VALUES (@Id, @Kind, @Domain, ...)
ON CONFLICT (kind, domain, shape_hash, shape_version) DO NOTHING
RETURNING id;
```

If `RETURNING` yields no row, the conflict fired — a concurrent writer already inserted it. This is correct and expected.

## Idempotency Gate Pattern

Always pair a count increment with a `ProcessedIngests` insert **in the same transaction**:

```csharp
// In one transaction:
var claimed = await conn.ExecuteScalarAsync<string>(insertIntoProcessedIngests, ...);
if (claimed != null)
{
    await conn.ExecuteAsync(updateOccurrenceCount, ...);
}
```

If `ProcessedIngests` insert returns nothing (conflict), skip the count update — this observation was already processed.

## Deterministic Batch Ordering

Before processing a batch of samples, always sort by `context_fingerprint` ascending:

```csharp
samples = samples.OrderBy(s => s.ContextFingerprint).ToList();
```

This guarantees identical concurrent writers process samples in the same order, preventing deadlocks under the advisory lock.

## pgvector Parameter Binding (Npgsql)

Pass float arrays using `NpgsqlDbType.Vector`:

```csharp
cmd.Parameters.Add(new NpgsqlParameter("embedding", NpgsqlDbType.Vector) {
    Value = embeddingFloatArray
});
```

Or use the Npgsql pgvector type handler if available. Never pass vectors as plain strings.

## Partial Unique Index Awareness

`one_current_solution_per_variant` is a partial unique index (`WHERE superseded_at IS NULL`). If an `INSERT INTO VariantSolutions` violates it, another writer concurrently inserted a solution between your check and your insert. Catch the Postgres unique violation (`23505`) and translate to HTTP 409 Conflict.

## ILIKE Safety

Escape `%` and `_` in user-supplied search strings before using in `ILIKE` clauses:

```csharp
var escaped = userInput.Replace("%", "\\%").Replace("_", "\\_");
// then: WHERE shape_template ILIKE @Pattern ESCAPE '\'
```