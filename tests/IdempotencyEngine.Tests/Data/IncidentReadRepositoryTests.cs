using FluentAssertions;
using IdempotencyEngine.Data;
using IdempotencyEngine.Domain.Entities;
using IdempotencyEngine.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IdempotencyEngine.Tests.Data;

public class IncidentReadRepositoryTests
{
    private IdempotencyDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<IdempotencyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new IdempotencyDbContext(options);
    }

    [Fact]
    public async Task GetIncidentByIdAsync_WhenIncidentExists_ReturnsIncident()
    {
        using var dbContext = CreateInMemoryDbContext();
        var repository = new IncidentReadRepository(dbContext);

        var incidentId = Guid.NewGuid();
        var incident = new ProcessedIncident(
            incidentId,
            "log_error",
            "postgres",
            "shape_hash_123",
            "v1",
            "connection refused",
            "watcher",
            new float[384],
            "all-MiniLM-L6-v2",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow
        );

        dbContext.ProcessedIncidents.Add(incident);
        await dbContext.SaveChangesAsync();

        var result = await repository.GetIncidentByIdAsync(incidentId);

        result.Should().NotBeNull();
        result!.Id.Should().Be(incidentId);
        result.Kind.Should().Be("log_error");
        result.Domain.Should().Be("postgres");
    }

    [Fact]
    public async Task GetIncidentByShapeAsync_WhenIncidentExists_ReturnsIncident()
    {
        using var dbContext = CreateInMemoryDbContext();
        var repository = new IncidentReadRepository(dbContext);

        var incidentId = Guid.NewGuid();
        var incident = new ProcessedIncident(
            incidentId,
            "log_error",
            "postgres",
            "shape_hash_456",
            "v1",
            "connection refused",
            "watcher",
            new float[384],
            "all-MiniLM-L6-v2",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow
        );

        dbContext.ProcessedIncidents.Add(incident);
        await dbContext.SaveChangesAsync();

        var result = await repository.GetIncidentByShapeAsync("log_error", "postgres", "shape_hash_456", "v1");

        result.Should().NotBeNull();
        result!.Id.Should().Be(incidentId);
    }

    [Fact]
    public async Task GetIncidentByIdAsync_WhenIncidentDoesNotExist_ReturnsNull()
    {
        using var dbContext = CreateInMemoryDbContext();
        var repository = new IncidentReadRepository(dbContext);

        var result = await repository.GetIncidentByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }
}
