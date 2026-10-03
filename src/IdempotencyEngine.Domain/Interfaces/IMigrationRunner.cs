namespace IdempotencyEngine.Domain.Interfaces;

/// <summary>
/// Contract for executing database migrations against PostgreSQL using DbUp.
/// </summary>
public interface IMigrationRunner
{
    /// <summary>
    /// Executes all unapplied SQL migration scripts from the migrations directory.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>True if all migrations succeeded; otherwise, false.</returns>
    bool RunMigrations(string connectionString);
}
