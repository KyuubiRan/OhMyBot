using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace OhMyBot.Core.Data;

public sealed class DatabaseMigrationHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseMigrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        logger.LogInformation("Applying database migrations.");

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OhMyBotV2DbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);

        logger.LogInformation(
            "Database migrations applied in {ElapsedMilliseconds:F0} ms.",
            Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
