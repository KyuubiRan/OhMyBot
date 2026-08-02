namespace OhMyBot.Core.Infrastructure.ScheduledTasks;

public sealed class ManagedTaskRegistry
{
    private const string CoreOwner = "core";
    private readonly Lock _lock = new();
    private readonly Dictionary<string, IReadOnlyList<IManagedTask>> _tasksByOwner = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<IManagedTask> _tasks = [];
    private IReadOnlyDictionary<string, IManagedTask> _map = new Dictionary<string, IManagedTask>(StringComparer.OrdinalIgnoreCase);
    private long _version;

    public ManagedTaskRegistry(IEnumerable<IManagedTask> tasks)
    {
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
        _map = tasks.ToDictionary(task => task.Name, StringComparer.OrdinalIgnoreCase);
        _tasks = tasks;
        _version++;
    }
}
