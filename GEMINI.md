# Idempotency Engine
A dev-time memory for errors, problems, and decisions to track solutions across the development lifecycle.

## Authoritative Specification
For detailed invariants, schema definitions, and thresholds, refer to `resources/projectplan.md`.

## Tech Stack
- C# .NET 9 (ASP.NET Core API)
- Go (journalctl watcher)
- Node.js/TypeScript (MCP)
- PostgreSQL 16 + pgvector
- Docker (Postgres ONLY)

## Project Structure
```
src/
  IdempotencyEngine.Domain/
    Models/                      ← C# records
    Interfaces/

  IdempotencyEngine.Data/
    Migrations/                  ← DbUp SQL files
    DbContext.cs                 ← EF Core, read-only use
    ReadRepositories/            ← EF Core reads
    WriteRepositories/           ← Dapper + raw SQL

  IdempotencyEngine.Api/
    Controllers/
    Ingestion/                   ← critical section

  IdempotencyEngine.Watcher/     ← Go, per-endpoint

  IdempotencyEngine.Mcp/         ← Node.js MCP adapter

  IdempotencyEngine.Cli/         ← thin HTTP wrapper

migrations/                      ← plain .sql DbUp files (001_create_extensions.sql, ...)
resources/                       ← authoritative project spec (read projectplan.md first)
core/                            ← SOURCE OF TRUTH for agents/skills/rules (edit here)
.agents/                         ← agy harness (DO NOT edit directly)
```

## Core Principles
### Two Primary Axes
- `kind`: error, problem, decision
- `domain`: e.g. postgres, hibernation

### Three Resolution Cases
1. **Discriminate**: Same text, different cause → different fixes
2. **Relate**: Different text, same fix
3. **Version**: Same text, same context, fix changes over time

### Key Rules Always in Context
- **Lock only on INSERT** (Cases B and C). Update in Case A takes NO lock.
- **Peek before embed**: Known errors pay no embedding cost.
- **EF Core reads only**: AsNoTracking for reads.
- **Dapper for writes**: NpgsqlCommand + Dapper for critical section writes.
- **DbUp for migrations**: Never use EF Core migrations.

## AIDLC Workflow Overview
This project uses a multi-agent system.
Note: **Do NOT edit `.agents/` directly**. Always edit harnesses in `core/` and run `python3 scripts/sync-harnesses.py` to propagate changes to `.agents/`.
