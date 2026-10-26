namespace OhMyBot.Core.Infrastructure.ScheduledTasks;

public abstract class ManagedTaskBase : IManagedTask
{
    private const int MaximumErrorDetailsLength = 32_000;
    private readonly Lock _lock = new();
    private CancellationTokenSource? _runningCts;
    private bool _enabled;
    private ManagedTaskStatus _status = new(
        ManagedTaskOutcome.NeverStarted, false, null, null, null, null, null, null, null, 0);
    private readonly List<string> _currentFailures = [];

    public abstract string Name { get; }
    public abstract string Description { get; }

    public bool Enabled
    {
        get { lock (_lock) return _enabled; }
        set { lock (_lock) _enabled = value; }
    }

    public string Cron { get; }
    public bool IsRunning { get { lock (_lock) return _runningCts is not null; } }
    public ManagedTaskStatus Status { get { lock (_lock) return _status; } }
    public DateTimeOffset? LastStartedAt => Status.LastStartedAt;
    public DateTimeOffset? LastCompletedAt => Status.LastSucceededAt;
    public string? LastError => Status.LastError;
    protected TimeProvider TimeProvider { get; }

    protected ManagedTaskBase(bool enabled, string cron, TimeProvider timeProvider)
    {
        _enabled = enabled;
        Cron = cron;
        TimeProvider = timeProvider;
    }

    public bool TryStart(out Task execution, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CancellationTokenSource runningCts;
        lock (_lock)
        {
            if (_runningCts is not null)
            {
                execution = Task.CompletedTask;
                return false;
            }

            runningCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _runningCts = runningCts;
            _currentFailures.Clear();
            _status = _status with
            {
                Outcome = ManagedTaskOutcome.Running,
                CancellationRequested = false,
                LastStartedAt = TimeProvider.GetUtcNow(),
                LastFinishedAt = null,
                FailureCount = 0
            };
            execution = Task.Run(() => RunExecutionAsync(runningCts), CancellationToken.None);
            return true;
        }
    }

    public Task ExecuteAsync(CancellationToken cancellationToken = default)
        => TryStart(out var execution, cancellationToken)
            ? execution
            : Task.FromException(new InvalidOperationException($"Task is already running: {Name}."));

    private async Task RunExecutionAsync(CancellationTokenSource runningCts)
    {
        using var diagnostics = ManagedTaskExecutionContext.Begin(ReportFailure);
        var outcome = ManagedTaskOutcome.Succeeded;
        try
        {
            runningCts.Token.ThrowIfCancellationRequested();
            await ExecuteCoreAsync(runningCts.Token);
            runningCts.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (runningCts.IsCancellationRequested)
        {
            outcome = ManagedTaskOutcome.Canceled;
            throw;
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
            outcome = ManagedTaskOutcome.Failed;
            throw;
        }
        finally
        {
            lock (_lock)
            {
                var finished = TimeProvider.GetUtcNow();
                if (outcome == ManagedTaskOutcome.Succeeded && _status.FailureCount > 0)
                {
                    outcome = ManagedTaskOutcome.Failed;
                }

                _status = _status with
                {
                    Outcome = outcome,
                    LastFinishedAt = finished,
                    LastSucceededAt = outcome == ManagedTaskOutcome.Succeeded ? finished : _status.LastSucceededAt
                };
                _runningCts = null;
            }

            runningCts.Dispose();
        }
    }

    // Batch tasks may continue after an account fails, but must retain that failed result.
    protected void ReportFailure(Exception exception, string? context = null)
        => ReportFailure(string.IsNullOrWhiteSpace(context) ? exception.ToString() : context + "\n" + exception);

    protected void ReportFailure(string details)
    {
        lock (_lock)
        {
            if (_runningCts is null || string.IsNullOrWhiteSpace(details)) return;
            if (_currentFailures.Sum(item => item.Length) < MaximumErrorDetailsLength)
            {
                _currentFailures.Add(details[..Math.Min(details.Length, MaximumErrorDetailsLength)]);
            }

            var summary = details.Split('\n', 2)[0];
            var combined = string.Join("\n\n", _currentFailures);
            _status = _status with
            {
                LastFailedAt = TimeProvider.GetUtcNow(),
                LastErrorId = _status.FailureCount == 0 ? "task-" + Guid.NewGuid().ToString("N")[..8] : _status.LastErrorId,
                LastError = summary[..Math.Min(summary.Length, 500)],
                LastErrorDetails = combined[..Math.Min(combined.Length, MaximumErrorDetailsLength)],
                FailureCount = _status.FailureCount + 1
            };
        }
    }

    public bool Cancel()
    {
        CancellationTokenSource runningCts;
        lock (_lock)
        {
            if (_runningCts is null) return false;
            runningCts = _runningCts;
            _status = _status with { CancellationRequested = true };
        }

        try { runningCts.Cancel(); return true; }
        catch (ObjectDisposedException) { return false; }
    }

    protected abstract Task ExecuteCoreAsync(CancellationToken cancellationToken);
}
