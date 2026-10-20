using IdempotencyEngine.Api.Logging;
using IdempotencyEngine.Api.Middleware;
using IdempotencyEngine.Data.Abstractions;
using IdempotencyEngine.Data.DbContexts;
using IdempotencyEngine.Data.Extensions;
using IdempotencyEngine.Data.Services.Incident;
using IdempotencyEngine.Data.Services.Migrations;
using IdempotencyEngine.Data.Services.Onnx;
using IdempotencyEngine.Domain.Interfaces;
using IdempotencyEngine.Domain.Options;
using IdempotencyEngine.Domain.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Configure Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.AddEventSourceLogger();
builder.Logging.AddFileLog("logs/idempotency-engine.log");

// Configure Options from appsettings.json
builder.Services.Configure<EmbeddingOptions>(
    builder.Configuration.GetSection("EmbeddingOptions"));

builder.Services.Configure<OnnxHardwareOptions>(
    builder.Configuration.GetSection("OnnxHardwareOptions"));

builder.Services.Configure<IngestionOptions>(
    builder.Configuration.GetSection("Ingestion"));

// Register Domain Services
builder.Services.AddSingleton<IHasher, Sha256Hasher>();
builder.Services.AddSingleton<ITextNormalizer, TextNormalizer>();
builder.Services.AddSingleton<IFingerprintGenerator, FingerprintGenerator>();

// Register Data Services & Embedding Infrastructure
builder.Services.AddTransient<IMigrationRunner, MigrationRunner>();
builder.Services.AddSingleton<IOnnxSessionOptionsFactory, ConfigurableOnnxSessionOptionsFactory>();
builder.Services.AddSingleton<IEmbeddingProvider, OnnxEmbeddingProvider>();

// Register Read & Write Repositories / Ingestion Service
builder.Services.AddDbContext<IdempotencyDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection") ?? string.Empty));
builder.Services.AddScoped<IIncidentReadRepository, IncidentReadRepository>();
builder.Services.AddScoped<IVariantReadRepository, VariantReadRepository>();
builder.Services.AddDataServices(builder.Configuration);
builder.Services.AddScoped<IIngestionService, IngestionService>();

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

app.UseMiddleware<GlobalExceptionMiddleware>();

app.MapControllers();

app.Run();
