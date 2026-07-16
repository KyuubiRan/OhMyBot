namespace OhMyBot.Core.Commanding.Admin;

public sealed class PluginAdminCommandRegistry
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, IReadOnlyList<IAdminCommand>> _commandsByOwner =
        new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<IAdminCommand> _commands = [];

    public IReadOnlyList<IAdminCommand> Commands
    {
        get
        {
            lock (_lock)
            {
                return _commands;
            }
        }
    }

    public void RegisterPlugin(string pluginId, IEnumerable<IAdminCommand> commands)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        lock (_lock)
        {
            if (_commandsByOwner.ContainsKey(pluginId))
            {
                throw new InvalidOperationException($"Admin commands are already registered for plugin '{pluginId}'.");
            }

            _commandsByOwner[pluginId] = commands.ToArray();
            try
            {
                RebuildSnapshot();
            }
            catch
            {
                _commandsByOwner.Remove(pluginId);
                RebuildSnapshot();
                throw;
            }
        }
    }

    public bool UnregisterPlugin(string pluginId)
    {
        lock (_lock)
        {
            if (!_commandsByOwner.Remove(pluginId))
            {
                return false;
            }

            RebuildSnapshot();
            return true;
        }
    }

    private void RebuildSnapshot()
    {
        var commands = _commandsByOwner.Values.SelectMany(items => items).ToArray();
        _ = commands.SelectMany(command => command.Definition.Aliases.Append(command.Definition.Name))
            .ToDictionary(name => name, StringComparer.OrdinalIgnoreCase);
        _commands = commands;
    }
}
