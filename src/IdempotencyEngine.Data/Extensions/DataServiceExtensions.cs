using System;
using IdempotencyEngine.Data.Services.Ingest;
using IdempotencyEngine.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IdempotencyEngine.Data.Extensions;

/// <summary>
/// Extension methods for registering data and write repositories in DI.
/// </summary>
public static class DataServiceExtensions
{
    /// <summary>
    /// Registers data services and repositories in the dependency injection container.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    /// <returns>The updated service collection</returns>
    public static IServiceCollection AddDataServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? "Host=localhost;Database=idempotency_engine;Username=postgres;Password=postgres";

        services.AddScoped<ITransactionHelper>(sp => new PostgresTransactionManager(connectionString));

        return services;
    }
}
