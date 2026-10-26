using System.Text.Json;

namespace OhMyBot.Core.Infrastructure.ScheduledTasks;

public sealed record ManagedTaskSavedState(bool Enabled, ManagedTaskStatus Status);

public sealed class ManagedTaskStateStore
{
    public const string FileName = "managed-task-state.json";
    private readonly Lock _lock = new();
    private readonly string _path;
    private readonly ILogger<ManagedTaskStateStore> _logger;
    private readonly Dictionary<string, ManagedTaskSavedState> _states;
    private readonly Dictionary<string, string> _activeGenerations = new(StringComparer.Ordinal);
    private string? _lastWriteError;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public ManagedTaskStateStore(ILogger<ManagedTaskStateStore> logger, IHostEnvironment? environment = null)
    {
        _path = Path.Combine(environment?.ContentRootPath ?? AppContext.BaseDirectory, FileName);
        _logger = logger;
        try
        {
            _states = File.Exists(_path)
                ? JsonSerializer.Deserialize<Dictionary<string, ManagedTaskSavedState>>(File.ReadAllText(_path), JsonOptions) ?? []
                : [];
            foreach (var (name, state) in _states.ToArray())
            {
                if (state.Status.Outcome == ManagedTaskOutcome.Running)
                    _states[name] = state with { Status = state.Status with
                    {
                        Outcome = ManagedTaskOutcome.Interrupted,
                        CancellationRequested = false,
                        LastFinishedAt = null
                    } };
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogError(exception, "Could not read managed task runtime state; using plugin defaults.");
            _states = [];
        }
    }

    public ManagedTaskSavedState? Get(string name)
    {
        lock (_lock) return _states.GetValueOrDefault(name);
    }

    public string? LastWriteError { get { lock (_lock) return _lastWriteError; } }

    internal (ManagedTaskSavedState? State, string Generation) Attach(string name)
    {
        lock (_lock)
        {
            var generation = Guid.NewGuid().ToString("N");
            _activeGenerations[name] = generation;
            var state = _states.GetValueOrDefault(name);
            if (state?.Status.Outcome == ManagedTaskOutcome.Running)
                state = state with { Status = state.Status with
                {
                    Outcome = ManagedTaskOutcome.Interrupted,
                    CancellationRequested = false,
                    LastFinishedAt = null
                } };
            return (state, generation);
        }
    }

    public void Save(string name, bool enabled, ManagedTaskStatus status, string? generation = null)
    {
        lock (_lock)
        {
            if (generation is not null && _activeGenerations.GetValueOrDefault(name) != generation) return;
            _states[name] = new ManagedTaskSavedState(enabled, status with
            {
                LastError = status.LastError is null ? null : ManagedTaskErrorRedactor.Redact(status.LastError),
                LastErrorDetails = status.LastErrorDetails is null ? null : ManagedTaskErrorRedactor.Redact(status.LastErrorDetails)
            });
            var temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(_states, JsonOptions));
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                File.Move(temporary, _path, overwrite: true);
                _lastWriteError = null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                var errorId = "task-state-" + Guid.NewGuid().ToString("N")[..8];
                _lastWriteError = $"状态保存失败（错误 id: {errorId}），重启后可能丢失当前变更。";
                _logger.LogError(exception, "Could not persist managed task runtime state for {TaskName}. errorId={ErrorId}", name, errorId);
            }
            finally
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}

internal sealed class PersistedManagedTask : IManagedTask
{
    private readonly IManagedTask _inner;
    private readonly ManagedTaskStateStore _store;
    private readonly Lock _persistenceLock = new();
    private readonly string _generation;
    private ManagedTaskStatus? _restoredStatus;

    public PersistedManagedTask(IManagedTask inner, ManagedTaskStateStore store)
    {
        _inner = inner;
        _store = store;
        var attached = store.Attach(inner.Name);
        _generation = attached.Generation;
        if (attached.State is { } saved)
        {
            inner.Enabled = saved.Enabled;
            _restoredStatus = saved.Status;
        }
    }

    public string Name => _inner.Name;
    public string Description => _inner.Description;
    public string Cron => _inner.Cron;
    public bool IsRunning => _inner.IsRunning;
    public bool Enabled
    {
        get => _inner.Enabled;
        set
        {
            lock (_persistenceLock)
            {
                _inner.Enabled = value;
                SaveCore();
            }
        }
    }
    public ManagedTaskStatus Status
    {
        get
        {
            var current = _inner.Status;
            var previous = Volatile.Read(ref _restoredStatus);
            if (previous is null) return current;
            if (current.Outcome == ManagedTaskOutcome.NeverStarted) return previous;
            return current with
            {
                LastSucceededAt = current.LastSucceededAt ?? previous.LastSucceededAt,
                LastFailedAt = current.LastFailedAt ?? previous.LastFailedAt,
                LastErrorId = current.LastErrorId ?? previous.LastErrorId,
                LastError = current.LastError ?? previous.LastError,
                LastErrorDetails = current.LastErrorDetails ?? previous.LastErrorDetails
            };
        }
    }
    public string? StatePersistenceError => _store.LastWriteError;
    public DateTimeOffset? LastStartedAt => Status.LastStartedAt;
    public DateTimeOffset? LastCompletedAt => Status.LastSucceededAt;
    public string? LastError => Status.LastError;

    public bool TryStart(out Task execution, CancellationToken cancellationToken = default)
    {
        if (!_inner.TryStart(out var innerExecution, cancellationToken))
        {
            execution = Task.CompletedTask;
            return false;
        }

        Save();
        execution = ObserveAsync(innerExecution);
        return true;
    }

    public Task ExecuteAsync(CancellationToken cancellationToken = default)
        => TryStart(out var execution, cancellationToken)
            ? execution
            : Task.FromException(new InvalidOperationException($"Task is already running: {Name}."));

    private async Task ObserveAsync(Task execution)
    {
        try { await execution; }
        finally { Save(); }
    }

    public bool Cancel()
    {
        var canceled = _inner.Cancel();
        Save();
        return canceled;
    }

    private void Save()
    {
        lock (_persistenceLock) SaveCore();
    }

    private void SaveCore() => _store.Save(Name, _inner.Enabled, Status, _generation);
}
