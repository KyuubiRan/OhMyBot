namespace OhMyBot.Core.Infrastructure.ScheduledTasks;

public sealed class ManagedTaskControlService(
    ManagedTaskRegistry registry,
    ILogger<ManagedTaskControlService> logger,
    IHostApplicationLifetime? applicationLifetime = null)
{
    public bool TryStart(ManagedTaskRegistration registration)
    {
        Task execution = Task.CompletedTask;
        if (!registry.TryControl(registration, task =>
                task.TryStart(out execution, applicationLifetime?.ApplicationStopping ?? CancellationToken.None)))
        {
            return false;
        }

        _ = ObserveAsync(registration.Task, execution);
        return true;
    }

    private async Task ObserveAsync(IManagedTask task, Task execution)
    {
        try
        {
            await execution;
            if (task.Status.Outcome == ManagedTaskOutcome.Failed)
            {
                logger.LogWarning("Manual task {TaskName} finished with {Count} failures. errorId={ErrorId}",
                    task.Name, task.Status.FailureCount, task.Status.LastErrorId);
            }
        }
        catch (OperationCanceledException) when (task.Status.Outcome == ManagedTaskOutcome.Canceled)
        {
            logger.LogInformation("Manual task {TaskName} was canceled.", task.Name);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Manual task {TaskName} failed. errorId={ErrorId}", task.Name, task.Status.LastErrorId);
        }
    }
}
