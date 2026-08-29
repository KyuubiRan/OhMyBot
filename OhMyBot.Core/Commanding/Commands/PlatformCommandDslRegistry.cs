namespace OhMyBot.Core.Commanding.Commands;

public sealed class PlatformCommandDslRegistry
{
    private const string CoreOwner = "core";
    private readonly Lock _lock = new();
    private readonly Dictionary<string, IReadOnlyList<IPlatformCommandDslProvider>> _providersByOwner =
        new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<CommandDslNode> _roots = [];
    private IReadOnlyDictionary<string, CommandDslNode> _rootLookup =
        new Dictionary<string, CommandDslNode>(StringComparer.OrdinalIgnoreCase);

    public PlatformCommandDslRegistry(IEnumerable<IPlatformCommandDslProvider> providers)
    {
        _providersByOwner[CoreOwner] = providers.ToArray();
        RebuildSnapshot();
    }

    public IReadOnlyList<CommandDslNode> Roots
    {
        get
        {
            lock (_lock)
            {
                return _roots;
            }
        }
    }

    public void RegisterPlugin(string pluginId, IEnumerable<IPlatformCommandDslProvider> providers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        var materialized = providers.ToArray();

        lock (_lock)
        {
            if (_providersByOwner.ContainsKey(pluginId))
            {
                throw new InvalidOperationException($"Command providers are already registered for plugin '{pluginId}'.");
            }

            _providersByOwner[pluginId] = materialized;
            try
            {
                RebuildSnapshot();
            }
            catch
            {
                _providersByOwner.Remove(pluginId);
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
            if (!_providersByOwner.Remove(pluginId))
            {
                return false;
            }

            RebuildSnapshot();
            return true;
        }
    }

    public bool TryGet(IReadOnlyList<string> path, out CommandDslNode node)
    {
        lock (_lock)
        {
            return TryGetCore(path, out node);
        }
    }

    private void RebuildSnapshot()
    {
        var roots = _providersByOwner
            .OrderBy(pair => string.Equals(pair.Key, CoreOwner, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .SelectMany(pair => pair.Value)
            .SelectMany(provider => provider.GetNodes())
            .Select(NormalizeTree)
            .GroupBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => Merge(group.Key, group.ToArray()))
            .OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _roots = roots;
        _rootLookup = roots.ToDictionary(node => node.Name, StringComparer.OrdinalIgnoreCase);
    }

    private bool TryGetCore(IReadOnlyList<string> path, out CommandDslNode node)
    {
        node = null!;
        if (path.Count == 0 || !_rootLookup.TryGetValue(CommandDsl.Normalize(path[0]), out var current))
        {
            return false;
        }

        for (var i = 1; i < path.Count; i++)
        {
            var segment = CommandDsl.Normalize(path[i]);
            current = current.Children.FirstOrDefault(child => string.Equals(child.Name, segment, StringComparison.OrdinalIgnoreCase));
            if (current is null)
            {
                return false;
            }
        }

        node = current;
        return true;
    }

    private static CommandDslNode NormalizeTree(CommandDslNode node)
    {
        return new CommandDslNode
        {
            Name = CommandDsl.Normalize(node.Name),
            Description = node.Description,
            Usage = node.Usage,
            Aliases = CommandDsl.NormalizeAliases(node.Name, node.Aliases),
            RequiredPrivilege = node.RequiredPrivilege,
            SupportPlatforms = node.SupportPlatforms,
            SupportChatTypes = node.SupportChatTypes,
            AcceptsReplyMedia = node.AcceptsReplyMedia,
            ProgressStyle = node.ProgressStyle,
            Enabled = node.Enabled,
            Handler = node.Handler,
            Children = node.Children.Select(NormalizeTree).ToArray()
        };
    }

    private static CommandDslNode Merge(string path, IReadOnlyList<CommandDslNode> nodes)
    {
        var first = nodes[0];
        var handlers = nodes.Where(node => node.Handler is not null).ToArray();
        if (handlers.Length > 1)
        {
            throw new InvalidOperationException($"Multiple command handlers are registered for path '{path}'.");
        }

        if (nodes.Skip(1).Any(node =>
                !string.Equals(node.Description, first.Description, StringComparison.Ordinal)
                || !string.Equals(node.Usage, first.Usage, StringComparison.Ordinal)
                || node.RequiredPrivilege != first.RequiredPrivilege
                || node.SupportPlatforms != first.SupportPlatforms
                || node.SupportChatTypes != first.SupportChatTypes
                || node.AcceptsReplyMedia != first.AcceptsReplyMedia
                || node.ProgressStyle != first.ProgressStyle))
        {
            throw new InvalidOperationException($"Conflicting command metadata is registered for path '{path}'.");
        }

        var children = nodes
            .SelectMany(node => node.Children)
            .GroupBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => Merge($"{path} {group.Key}", group.ToArray()))
            .OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new CommandDslNode
        {
            Name = first.Name,
            Description = first.Description,
            Usage = first.Usage,
            Aliases = first.Aliases,
            RequiredPrivilege = first.RequiredPrivilege,
            SupportPlatforms = first.SupportPlatforms,
            SupportChatTypes = first.SupportChatTypes,
            AcceptsReplyMedia = first.AcceptsReplyMedia,
            ProgressStyle = first.ProgressStyle,
            Enabled = first.Enabled,
            Handler = handlers.SingleOrDefault()?.Handler,
            Children = children
        };
    }
}
