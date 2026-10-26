namespace OhMyBot.Core.Infrastructure.ScheduledTasks;

public sealed class ManagedTaskRegistry
{
    private const string CoreOwner = "core";
    private readonly Lock _lock = new();
    private readonly Dictionary<string, IReadOnlyList<IManagedTask>> _tasksByOwner = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<IManagedTask> _tasks = [];
    private IReadOnlyDictionary<string, IManagedTask> _map = new Dictionary<string, IManagedTask>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, ManagedTaskRegistration> _registrations =
        new Dictionary<string, ManagedTaskRegistration>(StringComparer.OrdinalIgnoreCase);
    private long _version;
    private readonly ManagedTaskStateStore? _stateStore;

    public ManagedTaskRegistry(IEnumerable<IManagedTask> tasks, ManagedTaskStateStore? stateStore = null)
    {
        _stateStore = stateStore;
        _tasksByOwner[CoreOwner] = tasks.ToArray();
        RebuildSnapshot();
    }

    public long Version
    {
        get
        {
            lock (_lock)
            {
                return _version;
            }
        }
    }

    public IReadOnlyList<IManagedTask> Tasks
    {
        get
        {
            lock (_lock)
            {
                return _tasks;
            }
        }
    }

    public bool TryGet(string name, out IManagedTask task)
    {
        lock (_lock)
        {
            return _map.TryGetValue(name, out task!);
        }
    }

    public IReadOnlyList<ManagedTaskRegistration> Registrations
    {
        get { lock (_lock) return _registrations.Values.OrderBy(item => item.Task.Name, StringComparer.OrdinalIgnoreCase).ToArray(); }
    }

    public bool TryGetRegistration(string name, out ManagedTaskRegistration registration)
    {
        lock (_lock) return _registrations.TryGetValue(name, out registration!);
    }

    public bool TryControl(ManagedTaskRegistration registration, Func<IManagedTask, bool> action)
    {
        lock (_lock)
        {
            return _registrations.TryGetValue(registration.Task.Name, out var current)
                && current.RegistrationId == registration.RegistrationId
                && action(current.Task);
        }
    }

    public void RegisterPlugin(string pluginId, IEnumerable<IManagedTask> tasks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        var materialized = tasks.ToArray();
        lock (_lock)
        {
            if (_tasksByOwner.ContainsKey(pluginId))
            {
                throw new InvalidOperationException($"Managed tasks are already registered for plugin '{pluginId}'.");
            }

            _tasksByOwner[pluginId] = materialized;
            try
            {
                RebuildSnapshot();
            }
            catch
            {
                _tasksByOwner.Remove(pluginId);
                RebuildSnapshot();
                throw;
            }
        }
    }

    public bool UnregisterPlugin(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        lock (_lock)
        {
            if (!_tasksByOwner.Remove(pluginId))
            {
                return false;
            }

            RebuildSnapshot();
            return true;
        }
    }

    private void RebuildSnapshot()
    {
        var tasks = _tasksByOwner.Values
            .SelectMany(ownerTasks => ownerTasks)
            .OrderBy(task => task.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _ = tasks.ToDictionary(task => task.Name, StringComparer.OrdinalIgnoreCase);
        _registrations = _tasksByOwner.SelectMany(owner => owner.Value.Select(task =>
            _registrations.TryGetValue(task.Name, out var existing) && ReferenceEquals(existing.SourceTask, task)
                ? existing
                : new ManagedTaskRegistration(owner.Key, Guid.NewGuid().ToString("N"),
                    _stateStore is null ? task : new PersistedManagedTask(task, _stateStore)) { SourceTask = task }))
            .ToDictionary(item => item.Task.Name, StringComparer.OrdinalIgnoreCase);
        _tasks = _registrations.Values.Select(item => item.Task).OrderBy(task => task.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        _map = _tasks.ToDictionary(task => task.Name, StringComparer.OrdinalIgnoreCase);
        _version++;
    }
}

public sealed record ManagedTaskRegistration(string OwnerId, string RegistrationId, IManagedTask Task)
{
    internal IManagedTask SourceTask { get; init; } = Task;
}
