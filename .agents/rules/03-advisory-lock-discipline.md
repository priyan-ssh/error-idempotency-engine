---
trigger: always_on
description: Advisory lock discipline — pg_try_advisory_xact_lock, 64-bit key, auto-release, embedding before lock.
---

# Rule: Advisory Lock Discipline
- Use `pg_try_advisory_xact_lock(hashtextextended($kind||':'||$domain||':'||$shape_hash||':'||$shape_version, 0))` — **64-bit** (`hashtextextended`), NOT 32-bit (`hashtext`).
- Lock is `xact`-scoped: automatically released on COMMIT or ROLLBACK. Do NOT write manual unlock logic.
- **Embedding must be computed before acquiring the lock.** Never call `embed()` while holding the advisory lock.
- Keep the locked transaction as short as possible — all reads and embedding happen outside it.
- On lock timeout (budget: 2,000ms default), return `503 Service Unavailable` with `Retry-After: 1`.