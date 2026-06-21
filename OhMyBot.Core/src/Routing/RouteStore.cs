using System.Text.Json;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commands;

namespace OhMyBot.Core.Routing;

public sealed class RouteStore(
    PlatformCommandDslRegistry commandRegistry,
    IOptions<RouteOptions> options,
    ILogger<RouteStore> logger)
{
    private readonly Lock _lock = new();
    private readonly RouteOptions _options = options.Value;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private IReadOnlyDictionary<string, RouteEntry> _routes = new Dictionary<string, RouteEntry>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, RouteEntry> _routeLookup = new Dictionary<string, RouteEntry>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<CommandDslNode> _nodes = [];
    private long _version;

    public string RouteFilePath => Path.GetFullPath(_options.Path);

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

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return ReloadAsync(writeMergedFile: true, cancellationToken);
    }

    public async Task<bool> ReloadAsync(bool writeMergedFile, CancellationToken cancellationToken = default)
    {
        try
        {
            var routeFilePath = RouteFilePath;
            var document = await LoadOrCreateDocumentAsync(routeFilePath, cancellationToken);
            var merged = MergeDefaults(document);
            var nodes = BuildNodes(document.Routes, commandRegistry.Roots);
            var routes = BuildRoutes(nodes);
            var routeLookup = BuildRouteLookup(routes);

            if (writeMergedFile && merged)
            {
                await WriteDocumentAsync(routeFilePath, document, cancellationToken);
            }

            lock (_lock)
            {
                _routes = routes;
                _routeLookup = routeLookup;
                _nodes = nodes;
                _version++;
            }

            logger.LogInformation("Loaded {Count} command routes from {RouteFilePath}.", routes.Count, routeFilePath);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to reload route file. Keeping the previous route snapshot.");
            return false;
        }
    }

    public IReadOnlyList<RouteEntry> GetRoutes(BotPlatform platform)
    {
        var platformFlag = CommandDsl.ToSupportedPlatform(platform);
        lock (_lock)
        {
            return _routes.Values
                .Where(route => route.SupportPlatforms.HasFlag(platformFlag))
                .OrderBy(route => route.Command, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public bool TryGet(string command, out RouteEntry route)
    {
        lock (_lock)
        {
            return _routeLookup.TryGetValue(CommandDsl.Normalize(command), out route!);
        }
    }

    public IReadOnlyList<CommandDslNode> GetNodes()
    {
        lock (_lock)
        {
            return _nodes;
        }
    }

    public bool TryGetNode(IReadOnlyList<string> path, out CommandDslNode node)
    {
        lock (_lock)
        {
            return TryFindNode(_nodes, path, out node);
        }
    }

    private async Task<RouteDocument> LoadOrCreateDocumentAsync(string routeFilePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(routeFilePath))
        {
            var document = new RouteDocument();
            MergeDefaults(document);
            await WriteDocumentAsync(routeFilePath, document, cancellationToken);
            return document;
        }

        await using var stream = File.OpenRead(routeFilePath);
        var loaded = await JsonSerializer.DeserializeAsync<RouteDocument>(stream, _jsonOptions, cancellationToken);
        return loaded ?? new RouteDocument();
    }

    private bool MergeDefaults(RouteDocument document)
    {
        var changed = false;
        var existing = document.Routes
            .Select(route => CommandDsl.Normalize(route.Command))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var command in commandRegistry.Roots)
        {
            if (existing.Contains(command.Name))
            {
                var definition = document.Routes.First(route => string.Equals(CommandDsl.Normalize(route.Command), command.Name, StringComparison.OrdinalIgnoreCase));
                changed |= MergeChildren(definition, command);
                continue;
            }

            document.Routes.Add(ToDefinition(command));
            changed = true;
        }

        return changed;
    }

    private static bool MergeChildren(RouteDefinition definition, CommandDslNode node)
    {
        var changed = false;
        var existing = definition.Children
            .Select(child => CommandDsl.Normalize(child.Command))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var child in node.Children)
        {
            if (existing.Contains(child.Name))
            {
                var childDefinition = definition.Children.First(item => string.Equals(CommandDsl.Normalize(item.Command), child.Name, StringComparison.OrdinalIgnoreCase));
                changed |= MergeChildren(childDefinition, child);
                continue;
            }

            definition.Children.Add(ToDefinition(child));
            changed = true;
        }

        return changed;
    }

    private static RouteDefinition ToDefinition(CommandDslNode command)
    {
        return new RouteDefinition
        {
            Command = command.Name,
            CoreCommand = command.Name,
            Description = command.Description,
            Usage = command.Usage,
            Aliases = command.Aliases.ToArray(),
            RequiredPrivilege = command.RequiredPrivilege.ToString(),
            SupportPlatforms = ToPlatformNames(command.SupportPlatforms),
            SupportChatTypes = ToChatTypeNames(command.SupportChatTypes),
            Enabled = command.Enabled,
            Children = command.Children.Select(ToDefinition).ToList()
        };
    }

    private static IReadOnlyList<CommandDslNode> BuildNodes(
        IReadOnlyList<RouteDefinition> definitions,
        IReadOnlyList<CommandDslNode> defaults)
    {
        var definitionLookup = definitions.ToDictionary(
            definition => CommandDsl.Normalize(definition.Command),
            StringComparer.OrdinalIgnoreCase);

        return defaults
            .Select(defaultNode => definitionLookup.TryGetValue(defaultNode.Name, out var definition)
                ? ApplyDefinition(defaultNode, definition)
                : defaultNode)
            .ToArray();
    }

    private static CommandDslNode ApplyDefinition(CommandDslNode defaultNode, RouteDefinition definition)
    {
        var children = defaultNode.Children
            .Select(child => definition.Children.FirstOrDefault(item => string.Equals(CommandDsl.Normalize(item.Command), child.Name, StringComparison.OrdinalIgnoreCase)) is { } childDefinition
                ? ApplyDefinition(child, childDefinition)
                : child)
            .ToArray();

        return new CommandDslNode
        {
            Name = defaultNode.Name,
            Description = string.IsNullOrWhiteSpace(definition.Description) ? defaultNode.Description : definition.Description,
            Usage = string.IsNullOrWhiteSpace(definition.Usage) ? defaultNode.Usage : definition.Usage,
            Aliases = CommandDsl.NormalizeAliases(defaultNode.Name, definition.Aliases.Length == 0 ? defaultNode.Aliases : definition.Aliases),
            RequiredPrivilege = (UserPrivilege)Math.Max((int)ParsePrivilege(definition.RequiredPrivilege), (int)defaultNode.RequiredPrivilege),
            SupportPlatforms = ParsePlatforms(definition.SupportPlatforms),
            SupportChatTypes = ParseChatTypes(definition.SupportChatTypes) & defaultNode.SupportChatTypes,
            Enabled = definition.Enabled,
            Handler = defaultNode.Handler,
            Children = children
        };
    }

    private IReadOnlyDictionary<string, RouteEntry> BuildRoutes(IReadOnlyList<CommandDslNode> roots)
    {
        var routes = new Dictionary<string, RouteEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in roots)
        {
            if (string.IsNullOrWhiteSpace(node.Name))
            {
                continue;
            }

            routes[node.Name] = new RouteEntry(
                node.Name,
                node.Name,
                node.Description,
                node.Usage,
                node.Aliases,
                node.RequiredPrivilege,
                node.SupportPlatforms,
                node.SupportChatTypes,
                node.Enabled,
                node.Handler is not null || node.Children.Count > 0,
                node.RequiredPrivilege,
                node.SupportChatTypes);
        }

        return routes;
    }

    private static IReadOnlyDictionary<string, RouteEntry> BuildRouteLookup(IReadOnlyDictionary<string, RouteEntry> routes)
    {
        var lookup = new Dictionary<string, RouteEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var route in routes.Values)
        {
            lookup[route.Command] = route;
            foreach (var alias in route.Aliases)
            {
                lookup[alias] = route;
            }
        }

        return lookup;
    }

    private async Task WriteDocumentAsync(string routeFilePath, RouteDocument document, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(routeFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempFilePath = routeFilePath + ".tmp";
        await using (var stream = File.Create(tempFilePath))
        {
            await JsonSerializer.SerializeAsync(stream, document, _jsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(tempFilePath, routeFilePath, overwrite: true);
    }

    private static UserPrivilege ParsePrivilege(string value)
    {
        return UserPrivilegeNames.TryParse(value, out var privilege)
            || Enum.TryParse<UserPrivilege>(value, ignoreCase: true, out privilege)
            ? privilege
            : UserPrivilege.User;
    }

    private static SupportedPlatforms ParsePlatforms(IEnumerable<string> values)
    {
        var result = SupportedPlatforms.None;
        foreach (var value in values)
        {
            if (Enum.TryParse<SupportedPlatforms>(value, ignoreCase: true, out var platform))
            {
                result |= platform;
            }
        }

        return result is SupportedPlatforms.None ? SupportedPlatforms.All : result;
    }

    private static string[] ToPlatformNames(SupportedPlatforms platforms)
    {
        if (platforms is SupportedPlatforms.All)
        {
            return ["Telegram", "QQ"];
        }

        var names = new List<string>();
        if (platforms.HasFlag(SupportedPlatforms.Telegram))
        {
            names.Add("Telegram");
        }

        if (platforms.HasFlag(SupportedPlatforms.QQ))
        {
            names.Add("QQ");
        }

        return names.ToArray();
    }

    private static SupportedChatTypes ParseChatTypes(IEnumerable<string> values)
    {
        var result = SupportedChatTypes.None;
        foreach (var value in values)
        {
            if (Enum.TryParse<SupportedChatTypes>(value, ignoreCase: true, out var chatType))
            {
                result |= chatType;
            }
        }

        return result is SupportedChatTypes.None ? SupportedChatTypes.All : result;
    }

    private static string[] ToChatTypeNames(SupportedChatTypes chatTypes)
    {
        if (chatTypes is SupportedChatTypes.All)
        {
            return ["Private", "Group"];
        }

        var names = new List<string>();
        if (chatTypes.HasFlag(SupportedChatTypes.Private))
        {
            names.Add("Private");
        }

        if (chatTypes.HasFlag(SupportedChatTypes.Group))
        {
            names.Add("Group");
        }

        return names.ToArray();
    }

    private static bool TryFindNode(
        IReadOnlyList<CommandDslNode> nodes,
        IReadOnlyList<string> path,
        out CommandDslNode node)
    {
        node = null!;
        if (path.Count == 0)
        {
            return false;
        }

        var current = nodes.FirstOrDefault(item => string.Equals(item.Name, CommandDsl.Normalize(path[0]), StringComparison.OrdinalIgnoreCase));
        if (current is null)
        {
            return false;
        }

        for (var i = 1; i < path.Count; i++)
        {
            var segment = CommandDsl.Normalize(path[i]);
            current = current.Children.FirstOrDefault(item => string.Equals(item.Name, segment, StringComparison.OrdinalIgnoreCase));
            if (current is null)
            {
                return false;
            }
        }

        node = current;
        return true;
    }
}
