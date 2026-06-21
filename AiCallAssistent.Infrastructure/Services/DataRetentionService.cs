using AiCallAssistent.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiCallAssistent.Infrastructure.Services;

public class DataRetentionService(
    IServiceScopeFactory scopeFactory,
    ILogger<DataRetentionService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private const int RetentionDays = 90;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await PurgeOldSessionsAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "DataRetentionService purge failed"); }
        }
    }

    private async Task PurgeOldSessionsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cutoff = DateTimeOffset.UtcNow.AddDays(-RetentionDays);
        var deleted = await db.CallSessions
            .Where(s => s.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
            logger.LogInformation("DataRetention: deleted {Count} call sessions older than {Days} days", deleted, RetentionDays);
    }
}
