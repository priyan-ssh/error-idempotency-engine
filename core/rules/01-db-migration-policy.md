---
trigger: always_on
description: Database migration policy — DbUp only, EF Core for reads only, no EF migrations.
---

# Rule: Database Migration Policy
- Do NOT use Entity Framework (EF) Core for migrations. Ever.
- Use DbUp and plain `.sql` files in the `migrations/` folder (e.g. `001_create_extensions.sql`).
- Use EF Core strictly for read-only queries (`AsNoTracking()`). Never call `SaveChanges()`.
- Write repositories (`WriteRepositories/`) use Dapper or `NpgsqlCommand` — never EF Core.