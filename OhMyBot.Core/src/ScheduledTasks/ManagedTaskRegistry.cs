namespace OhMyBot.Core.ScheduledTasks;

public sealed class ManagedTaskRegistry(IEnumerable<IManagedTask> tasks)
{
    private readonly IReadOnlyList<IManagedTask> _tasks = tasks.OrderBy(task => task.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    private readonly Dictionary<string, IManagedTask> _map = tasks.ToDictionary(task => task.Name, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<IManagedTask> Tasks => _tasks;

    public bool TryGet(string name, out IManagedTask task)
    {
        return _map.TryGetValue(name, out task!);
    }
}
