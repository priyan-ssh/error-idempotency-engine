using System;
using System.Collections.Generic;
using FluentAssertions;
using IdempotencyEngine.Data.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace IdempotencyEngine.Tests.Data;

public class TestLogger<T> : ILogger<T>
{
    public List<string> LoggedMessages { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (formatter != null)
        {
            LoggedMessages.Add(formatter(state, exception));
        }
    }
}

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

    [Fact]
    public void RunMigrations_WithLogger_LogsInformationMessages()
    {
        var logger = new TestLogger<MigrationRunner>();
        var runner = new MigrationRunner(logger);

        Action act = () => runner.RunMigrations("Host=localhost;Port=5999;Database=invalid;Username=invalid;Password=invalid;");
        act.Should().Throw<Exception>();

        logger.LoggedMessages.Should().NotBeEmpty();
    }
}
