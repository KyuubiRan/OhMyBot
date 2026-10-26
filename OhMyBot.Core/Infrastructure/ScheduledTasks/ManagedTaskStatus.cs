namespace OhMyBot.Core.Infrastructure.ScheduledTasks;

public enum ManagedTaskOutcome
{
    NeverStarted,
    Running,
    Succeeded,
    Failed,
    Canceled,
    Interrupted
}

public sealed record ManagedTaskStatus(
    ManagedTaskOutcome Outcome,
    bool CancellationRequested,
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastFinishedAt,
    DateTimeOffset? LastSucceededAt,
    DateTimeOffset? LastFailedAt,
    string? LastErrorId,
    string? LastError,
    string? LastErrorDetails,
    int FailureCount);
