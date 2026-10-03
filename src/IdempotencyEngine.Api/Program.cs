using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using IdempotencyEngine.Data.Services;
using IdempotencyEngine.Domain.Interfaces;
using IdempotencyEngine.Domain.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Register Domain Services
builder.Services.AddSingleton<IHasher, Sha256Hasher>();
builder.Services.AddSingleton<ITextNormalizer, TextNormalizer>();
builder.Services.AddSingleton<IFingerprintGenerator, FingerprintGenerator>();

// Register Data Services
builder.Services.AddTransient<IMigrationRunner, IdempotencyEngine.Data.Services.MigrationRunner>();

// Add Controllers
builder.Services.AddControllers();

var app = builder.Build();

// Run Database Migrations on Startup
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var migrationRunner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

    if (!string.IsNullOrEmpty(connectionString))
    {
        logger.LogInformation("Executing startup database migrations...");
        bool migrationSuccess = migrationRunner.RunMigrations(connectionString);
        if (!migrationSuccess)
        {
            logger.LogError("Database migrations failed!");
        }
    }
}

app.MapControllers();

app.Run();