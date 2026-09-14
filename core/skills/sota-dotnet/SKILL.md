---
name: sota-dotnet
description: .NET 9 idioms, project structure, EF Core read-only, DbUp migrations, IEmbeddingProvider interface, async correctness.
trigger: model_decision
---

# .NET 9 Idioms & Best Practices — Idempotency Engine

## Project Structure

```
src/
  IdempotencyEngine.Domain/
    Models/           ← C# record types (pure data, no behavior)
    Interfaces/       ← IEmbeddingProvider, IVariantRepository, etc.

  IdempotencyEngine.Data/
    Migrations/       ← DbUp .sql files (001_create_extensions.sql, ...)
    DbContext.cs      ← EF Core, NoTracking by default, reads only
    ReadRepositories/ ← EF Core LINQ queries
    WriteRepositories/ ← Dapper / NpgsqlCommand raw SQL

  IdempotencyEngine.Api/
    Controllers/      ← ASP.NET Core minimal API or controllers
    Ingestion/        ← Critical section service (Cases A/B/C)
    Services/         ← Linker, EmbeddingService, etc.
```

## Domain Models — C# Records

```csharp
// Pure data. No EF navigation properties. No change tracking.
public record ProcessedIncident(
    Guid Id,
    string Kind,
    string Domain,
    string ShapeHash,
    string ShapeVersion,
    string ShapeTemplate,
    string Origin,
    float[] ErrorEmbedding,
    string EmbeddingModel,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
```

Use `record` types for all domain models and DTOs. Use primary constructors to reduce boilerplate.

## Nullable Reference Types

Enable globally in all projects:
```xml
<Nullable>enable</Nullable>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
```

## Async Correctness

- Always `await` — never `.Result`, `.GetAwaiter().GetResult()`, or `.Wait()`
- Use `ConfigureAwait(false)` in library/infrastructure code (Data, Domain projects)
- Accept `CancellationToken` on all async methods and pass it through

## EF Core — Reads Only

```csharp
// In DbContext configuration:
optionsBuilder.UseNpgsql(connectionString)
              .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

// All EF queries must be NoTracking — no SaveChanges, no tracked entities
var incident = await _db.ProcessedIncidents
    .Where(i => i.Kind == kind && i.Domain == domain)
    .AsNoTracking()
    .FirstOrDefaultAsync(ct);
```

**Never** use EF Core for writes. **Never** call `SaveChanges()`.

## DbUp Migrations — Never EF Core Migrations

```csharp
// In Program.cs or a startup service:
var upgrader = DeployChanges.To
    .PostgresqlDatabase(connectionString)
    .WithScriptsFromFileSystem("migrations/")
    .WithTransactionPerScript()
    .LogToConsole()
    .Build();

var result = upgrader.PerformUpgrade();
if (!result.Successful) throw new Exception("Migration failed", result.Error);
```

Migration file naming convention:
```
migrations/
  001_create_extensions.sql
  002_create_domains.sql
  003_create_incidents.sql
  004_create_variants.sql
  005_create_solutions.sql
  006_create_links.sql
  007_create_ingests.sql
  008_create_indexes.sql
```

## IEmbeddingProvider Interface

```csharp
public interface IEmbeddingProvider
{
    string ModelId { get; }       // "all-MiniLM-L6-v2"
    int Dimensions { get; }       // 384
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
    Task<float[][]> EmbedBatchAsync(
        IEnumerable<string> texts,
        CancellationToken ct = default);
}
```

Register as singleton — ONNX model loading is expensive:
```csharp
services.AddSingleton<IEmbeddingProvider, OnnxEmbeddingProvider>();
```

## DI Registration Pattern

```csharp
// Extension methods per project:
public static class DataServiceExtensions
{
    public static IServiceCollection AddDataServices(
        this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<IdempotencyDbContext>(...);
        services.AddScoped<IIncidentReadRepository, IncidentReadRepository>();
        services.AddScoped<IIngestionWriteRepository, IngestionWriteRepository>();
        return services;
    }
}
```

## Health Checks

```csharp
services.AddHealthChecks()
    .AddNpgsql(connectionString, name: "postgres");

app.MapHealthChecks("/health");
```