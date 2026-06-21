namespace OhMyBot.Core.ScheduledTasks;

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

    Task ExecuteAsync(CancellationToken cancellationToken = default);

    bool Cancel();
}
