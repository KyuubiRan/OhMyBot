namespace OhMyBot.Core.Commands;

public sealed class PlatformCommandDslRegistry
{
    private readonly IReadOnlyList<CommandDslNode> _roots;
    private readonly IReadOnlyDictionary<string, CommandDslNode> _rootLookup;

    public PlatformCommandDslRegistry(IEnumerable<IPlatformCommandDslProvider> providers)
    {
        _roots = providers
            .SelectMany(provider => provider.GetNodes())
            .Select(NormalizeTree)
            .GroupBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => Merge(group.ToArray()))
            .OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _rootLookup = _roots.ToDictionary(node => node.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<CommandDslNode> Roots => _roots;

    public bool TryGet(IReadOnlyList<string> path, out CommandDslNode node)
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
            Enabled = node.Enabled,
            Handler = node.Handler,
            Children = node.Children.Select(NormalizeTree).ToArray()
        };
    }

    private static CommandDslNode Merge(IReadOnlyList<CommandDslNode> nodes)
    {
        var first = nodes[0];
        var children = nodes
            .SelectMany(node => node.Children)
            .GroupBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => Merge(group.ToArray()))
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
            Enabled = first.Enabled,
            Handler = nodes.LastOrDefault(node => node.Handler is not null)?.Handler ?? first.Handler,
            Children = children
        };
    }
}

