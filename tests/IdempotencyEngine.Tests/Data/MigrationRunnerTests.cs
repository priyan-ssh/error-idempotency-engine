using System;
using FluentAssertions;
using IdempotencyEngine.Data.Services;
using Xunit;

namespace IdempotencyEngine.Tests.Data;

public class MigrationRunnerTests
{
    private readonly MigrationRunner _runner;

    public MigrationRunnerTests()
    {
        _runner = new MigrationRunner();
    }

    [Fact]
    public void RunMigrations_WithNullOrWhitespaceConnectionString_ThrowsArgumentException()
    {
        Action act = () => _runner.RunMigrations("   ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RunMigrations_WithInvalidConnectionString_ReturnsFalseOrThrows()
    {
        // Invalid connection string should fail gracefully or throw
        Action act = () => _runner.RunMigrations("Host=localhost;Port=5999;Database=invalid;Username=invalid;Password=invalid;");
        act.Should().Throw<Exception>();
    }
}
