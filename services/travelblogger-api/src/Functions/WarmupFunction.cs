using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TravelBlogger.Infrastructure.Data;

namespace TravelBlogger.Functions;

public sealed class WarmupFunction
{
    private readonly AppDbContext _db;
    private readonly ILogger<WarmupFunction> _logger;

    public WarmupFunction(AppDbContext db, ILogger<WarmupFunction> logger)
    {
        _db = db;
        _logger = logger;
    }

    [Function("Warmup")]
    public async Task Run([WarmupTrigger] object warmupContext)
    {
        _logger.LogInformation("Warmup started — pre-initializing DB connection pool");
        await _db.Database.ExecuteSqlRawAsync("SELECT 1");
        _logger.LogInformation("Warmup complete — instance ready to serve traffic");
    }
}
