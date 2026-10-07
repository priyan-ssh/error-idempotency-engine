using System;
using System.Threading.Tasks;
using FluentAssertions;
using IdempotencyEngine.Data;
using IdempotencyEngine.Data.Services;
using IdempotencyEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IdempotencyEngine.Tests.Data;

public class VariantReadRepositoryTests
{
    private IdempotencyDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<IdempotencyDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new IdempotencyDbContext(options);
    }

    [Fact]
    public async Task GetVariantByIdAsync_WhenVariantExists_ReturnsVariant()
    {
        using var dbContext = CreateInMemoryDbContext();
        var repository = new VariantReadRepository(dbContext);

        var variantId = Guid.NewGuid();
        var variant = new IncidentVariant(
            variantId,
            Guid.NewGuid(),
            "fp_id_test",
            new float[384],
            "all-MiniLM-L6-v2",
            "{}",
            "sample log",
            "active",
            false,
            false,
            1,
            1,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow
        );

        dbContext.IncidentVariants.Add(variant);
        await dbContext.SaveChangesAsync();

        var result = await repository.GetVariantByIdAsync(variantId);

        result.Should().NotBeNull();
        result!.Id.Should().Be(variantId);
    }

    [Fact]
    public async Task GetVariantByIdAsync_WhenVariantDoesNotExist_ReturnsNull()
    {
        using var dbContext = CreateInMemoryDbContext();
        var repository = new VariantReadRepository(dbContext);

        var result = await repository.GetVariantByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }
}
