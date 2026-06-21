using Cronos;

namespace OhMyBot.Core.ScheduledTasks;

public sealed class ManagedTaskHostedService(
    ManagedTaskRegistry registry,
    ILogger<ManagedTaskHostedService> logger,
    TimeProvider timeProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var loops = registry.Tasks.Select(task => RunLoopAsync(task, stoppingToken)).ToArray();
        await Task.WhenAll(loops);
    }

    private async Task RunLoopAsync(IManagedTask task, CancellationToken stoppingToken)
    {
        CronExpression expression;
        try
        {
            expression = CronExpression.Parse(task.Cron);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Invalid cron expression for task {TaskName}: {Cron}", task.Name, task.Cron);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = timeProvider.GetUtcNow();
            var next = expression.GetNextOccurrence(now, TimeZoneInfo.Local);
            if (next is null)
            {
                logger.LogWarning("No next occurrence for task {TaskName}.", task.Name);
                return;
            }

            var delay = next.Value - now;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken);
            }

            if (!task.Enabled)
            {
                logger.LogInformation("Scheduled task {TaskName} is disabled; skipping.", task.Name);
                continue;
            }

            try
            {
                await task.ExecuteAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Scheduled task {TaskName} failed.", task.Name);
            }
        }
    }
}
