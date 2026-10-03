using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdempotencyEngine.Domain.Services;

public class MigrationRunner
{
    private readonly ILogger<MigrationRunner> _logger;

    public MigrationRunner(ILogger<MigrationRunner>? logger = null)
    {
        _logger = logger ?? NullLogger<MigrationRunner>.Instance;
    }
}

