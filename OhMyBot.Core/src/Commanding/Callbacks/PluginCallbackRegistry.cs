namespace OhMyBot.Core.Commanding.Callbacks;

public sealed class PluginCallbackRegistry
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, IReadOnlyList<IPluginCallbackHandler>> _handlersByOwner =
        new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, IPluginCallbackHandler> _handlers =
        new Dictionary<string, IPluginCallbackHandler>(StringComparer.OrdinalIgnoreCase);

    public void RegisterPlugin(string pluginId, IEnumerable<IPluginCallbackHandler> handlers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        var materialized = handlers.ToArray();
        lock (_lock)
        {
            if (_handlersByOwner.ContainsKey(pluginId))
            {
                throw new InvalidOperationException($"Callback handlers are already registered for plugin '{pluginId}'.");
            }

            _handlersByOwner[pluginId] = materialized;
            try
            {
                RebuildSnapshot();
            }
            catch
            {
                _handlersByOwner.Remove(pluginId);
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
            if (!_handlersByOwner.Remove(pluginId))
            {
                return false;
            }

            RebuildSnapshot();
            return true;
        }
    }

    public bool TryGet(string actionType, out IPluginCallbackHandler handler)
    {
        lock (_lock)
        {
            return _handlers.TryGetValue(actionType, out handler!);
        }
    }

    private void RebuildSnapshot()
    {
        _handlers = _handlersByOwner.Values
            .SelectMany(handlers => handlers)
            .SelectMany(handler => handler.ActionTypes.Select(actionType => (ActionType: actionType, Handler: handler)))
            .ToDictionary(pair => pair.ActionType, pair => pair.Handler, StringComparer.OrdinalIgnoreCase);
    }
}
