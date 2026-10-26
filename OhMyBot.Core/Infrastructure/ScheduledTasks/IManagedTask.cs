namespace OhMyBot.Core.Infrastructure.ScheduledTasks;

public interface IManagedTask
{
    string Name { get; }

    string Description { get; }

    bool Enabled { get; set; }

    string Cron { get; }

    bool IsRunning { get; }

    DateTimeOffset? LastStartedAt { get; }

    DateTimeOffset? LastCompletedAt { get; }

    string? LastError { get; }

    string? StatePersistenceError => null;

    ManagedTaskStatus Status => new(
        IsRunning ? ManagedTaskOutcome.Running
            : LastError is not null ? ManagedTaskOutcome.Failed
            : LastCompletedAt is not null ? ManagedTaskOutcome.Succeeded
            : ManagedTaskOutcome.NeverStarted,
        false, LastStartedAt, LastCompletedAt, LastCompletedAt, null, null, LastError, LastError,
        LastError is null ? 0 : 1);

    // Existing implementations keep working; ManagedTaskBase reserves the run atomically.
    bool TryStart(out Task execution, CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            execution = Task.CompletedTask;
            return false;
        }

        execution = ExecuteAsync(cancellationToken);
        return true;
    }

    Task ExecuteAsync(CancellationToken cancellationToken = default);

    bool Cancel();
}
