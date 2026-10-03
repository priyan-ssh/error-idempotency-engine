using DbUp;
using IdempotencyEngine.Domain.Interfaces;

namespace IdempotencyEngine.Data.Services;

/// <summary>
/// Executes PostgreSQL database schema migrations using DbUp.
/// </summary>
public class MigrationRunner : IMigrationRunner
{
    /// <summary>
    /// Executes all unapplied SQL migration scripts from the migrations directory against PostgreSQL.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>True if all migrations succeeded; otherwise, false.</returns>
    public bool RunMigrations(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be null or whitespace.", nameof(connectionString));
        }

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
        return result.Successful;
    }
}
