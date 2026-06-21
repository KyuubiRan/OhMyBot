namespace OhMyBot.Core.ScheduledTasks;

public abstract class ManagedTaskBase : IManagedTask
{
    private readonly Lock _lock = new();
    private CancellationTokenSource? _runningCts;

    public abstract string Name { get; }

    public abstract string Description { get; }

    public bool Enabled { get; set; }

    public string Cron { get; }

    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _runningCts is not null;
            }
        }
    }

    public DateTimeOffset? LastStartedAt { get; private set; }

    public DateTimeOffset? LastCompletedAt { get; private set; }

    public string? LastError { get; private set; }

    protected TimeProvider TimeProvider { get; }

    protected ManagedTaskBase(bool enabled, string cron, TimeProvider timeProvider)
    {
        Enabled = enabled;
        Cron = cron;
        TimeProvider = timeProvider;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource runningCts;
        lock (_lock)
        {
            if (_runningCts is not null)
            {
                throw new InvalidOperationException($"Task is already running: {Name}.");
            }

            _runningCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            runningCts = _runningCts;
            LastStartedAt = TimeProvider.GetUtcNow();
            LastError = null;
        }

        try
        {
            await ExecuteCoreAsync(runningCts.Token);
            LastCompletedAt = TimeProvider.GetUtcNow();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LastError = exception.GetBaseException().Message;
            throw;
        }
        finally
        {
            lock (_lock)
            {
                if (ReferenceEquals(_runningCts, runningCts))
                {
                    _runningCts = null;
                }
            }

            runningCts.Dispose();
        }
    }

    public bool Cancel()
    {
        lock (_lock)
        {
            if (_runningCts is null)
            {
                return false;
            }

            _runningCts.Cancel();
            return true;
        }
    }

    protected abstract Task ExecuteCoreAsync(CancellationToken cancellationToken);
}
