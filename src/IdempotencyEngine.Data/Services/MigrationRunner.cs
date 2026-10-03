using DbUp;
using IdempotencyEngine.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdempotencyEngine.Data.Services;

/// <summary>
/// Executes PostgreSQL database schema migrations using DbUp.
/// </summary>
public class MigrationRunner : IMigrationRunner
{
    private readonly ILogger<MigrationRunner> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MigrationRunner"/> class with the specified logger.
    /// </summary>
    /// <param name="logger"></param>
    public MigrationRunner(ILogger<MigrationRunner>? logger = null)
    {
        _logger = logger ?? NullLogger<MigrationRunner>.Instance;
    }

    /// <summary>
    /// Executes all unapplied SQL migration scripts from the migrations directory against PostgreSQL.
    /// </summary>
    /// <param name="connectionString"></param>
    /// <returns>True if all migrations succeeded; otherwise, false.</returns>
    /// <exception cref="ArgumentException"></exception>
    public bool RunMigrations(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogWarning("Connection string cannot be null or whitespace.");
            throw new ArgumentException("Connection string cannot be null or whitespace.", nameof(connectionString));
        }

        _logger.LogInformation("Starting database migrations.");
        try
        {
            var scriptsPath = Path.Combine(AppContext.BaseDirectory, "migrations");
            if (!Directory.Exists(scriptsPath))
            {
                scriptsPath = Path.Combine(Directory.GetCurrentDirectory(), "migrations");
            }

            EnsureDatabase.For.PostgresqlDatabase(connectionString);

            var upgrader = DeployChanges.To
                .PostgresqlDatabase(connectionString)
                .WithScriptsFromFileSystem(scriptsPath)
                .LogToConsole()
                .Build();

            var result = upgrader.PerformUpgrade();
            if (result.Successful)
            {
                _logger.LogInformation("Database migrations executed successfully.");
            }
            else
            {
                _logger.LogError(result.Error, "Database migration failed.");
            }
            return result.Successful;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception encountered during database migrations.");
            throw;
        }
    }
}
