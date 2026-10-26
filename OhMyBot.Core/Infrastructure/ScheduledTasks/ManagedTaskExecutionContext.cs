namespace OhMyBot.Core.Infrastructure.ScheduledTasks;

/// <summary>Captures isolated module failures only while executing a managed task.</summary>
public static class ManagedTaskExecutionContext
{
    private static readonly AsyncLocal<Action<string>?> FailureSink = new();

    internal static IDisposable Begin(Action<string> report)
    {
        var previous = FailureSink.Value;
        FailureSink.Value = report;
        return new Scope(previous);
    }

    public static void ReportFailure(Exception exception, string? context = null)
        => ReportFailure(context is null ? exception.ToString() : context + "\n" + exception);

    public static void ReportFailure(string details) => FailureSink.Value?.Invoke(details);

    private sealed class Scope(Action<string>? previous) : IDisposable
    {
        public void Dispose() => FailureSink.Value = previous;
    }
}
