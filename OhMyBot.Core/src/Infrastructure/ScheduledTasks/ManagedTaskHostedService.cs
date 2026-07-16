using Cronos;

namespace OhMyBot.Core.Infrastructure.ScheduledTasks;

public sealed class ManagedTaskHostedService(
    ManagedTaskRegistry registry,
    ILogger<ManagedTaskHostedService> logger,
    TimeProvider timeProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var loops = new Dictionary<IManagedTask, (CancellationTokenSource Cancellation, Task Loop)>();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var currentTasks = registry.Tasks.ToHashSet();
                foreach (var removed in loops.Keys.Where(task => !currentTasks.Contains(task)).ToArray())
                {
                    var loop = loops[removed];
                    await loop.Cancellation.CancelAsync();
                    try
                    {
                        await loop.Loop;
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    loop.Cancellation.Dispose();
                    loops.Remove(removed);
                }

                foreach (var task in currentTasks.Where(task => !loops.ContainsKey(task)))
                {
                    var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    loops[task] = (cancellation, RunLoopAsync(task, cancellation.Token));
                }

                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            foreach (var loop in loops.Values)
            {
                await loop.Cancellation.CancelAsync();
            }

            try
            {
                await Task.WhenAll(loops.Values.Select(loop => loop.Loop));
            }
            catch (OperationCanceledException)
            {
            }

            foreach (var loop in loops.Values)
            {
                loop.Cancellation.Dispose();
            }
        }
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
