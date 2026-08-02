namespace OhMyBot.Core.Commanding.Notifications;

public sealed class PluginNotificationSourceRegistry
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, IReadOnlyList<IPluginNotificationSource>> _sourcesByOwner =
        new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, IPluginNotificationSource> _sources =
        new Dictionary<string, IPluginNotificationSource>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<IPluginNotificationSource> Sources
    {
        get
        {
            lock (_lock)
            {
                return _sources.Values
                    .OrderBy(source => source.Order)
                    .ThenBy(source => source.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    public bool TryGet(string type, out IPluginNotificationSource source)
    {
        lock (_lock)
        {
            return _sources.TryGetValue(type, out source!);
        }
    }

    public void RegisterPlugin(string pluginId, IEnumerable<IPluginNotificationSource> sources)
    {
        lock (_lock)
        {
            if (_sourcesByOwner.ContainsKey(pluginId))
            {
                throw new InvalidOperationException($"Notification sources are already registered for plugin '{pluginId}'.");
            }

            _sourcesByOwner[pluginId] = sources.ToArray();
            try
            {
                RebuildSnapshot();
            }
            catch
            {
                _sourcesByOwner.Remove(pluginId);
                RebuildSnapshot();
                throw;
            }
        }
    }

    public bool UnregisterPlugin(string pluginId)
    {
        lock (_lock)
        {
            if (!_sourcesByOwner.Remove(pluginId))
            {
                return false;
            }

            RebuildSnapshot();
            return true;
        }
    }

    private void RebuildSnapshot()
    {
        _sources = _sourcesByOwner.Values.SelectMany(items => items)
            .ToDictionary(source => source.Type, StringComparer.OrdinalIgnoreCase);
    }
}
