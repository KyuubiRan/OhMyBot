using System.Text.Json;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commands;

namespace OhMyBot.Core.Routing;

public sealed class RouteStore(
    CommandRegistry commandRegistry,
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
            var routes = BuildRoutes(document);
            var routeLookup = BuildRouteLookup(routes);

            if (writeMergedFile && merged)
            {
                await WriteDocumentAsync(routeFilePath, document, cancellationToken);
            }

            lock (_lock)
            {
                _routes = routes;
                _routeLookup = routeLookup;
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
        var platformFlag = CommandRegistry.ToSupportedPlatform(platform);
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
            return _routeLookup.TryGetValue(CommandRegistration.Normalize(command), out route!);
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
            .Select(route => CommandRegistration.Normalize(route.Command))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var command in commandRegistry.Commands)
        {
            if (existing.Contains(command.Name))
            {
                continue;
            }

            document.Routes.Add(new RouteDefinition
            {
                Command = command.Name,
                CoreCommand = command.Name,
                Description = command.Description,
                Usage = command.Usage,
                Aliases = command.Aliases.ToArray(),
                RequiredPrivilege = command.RequiredPrivilege.ToString(),
                SupportPlatforms = ToPlatformNames(command.SupportPlatforms),
                Enabled = command.Enabled
            });
            changed = true;
        }

        return changed;
    }

    private IReadOnlyDictionary<string, RouteEntry> BuildRoutes(RouteDocument document)
    {
        var routes = new Dictionary<string, RouteEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in document.Routes)
        {
            var commandName = CommandRegistration.Normalize(definition.Command);
            var coreCommand = CommandRegistration.Normalize(
                string.IsNullOrWhiteSpace(definition.CoreCommand) ? definition.Command : definition.CoreCommand);

            if (string.IsNullOrWhiteSpace(commandName))
            {
                continue;
            }

            var targetExists = commandRegistry.TryGet(coreCommand, out var commandRegistration);
            var routePrivilege = ParsePrivilege(definition.RequiredPrivilege);
            var corePrivilege = commandRegistration?.RequiredPrivilege ?? routePrivilege;
            var aliases = NormalizeAliases(commandName, definition.Aliases);

            routes[commandName] = new RouteEntry(
                commandName,
                coreCommand,
                definition.Description,
                definition.Usage,
                aliases,
                routePrivilege,
                ParsePlatforms(definition.SupportPlatforms),
                definition.Enabled,
                targetExists,
                (UserPrivilege)Math.Max((int)routePrivilege, (int)corePrivilege));
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
        return Enum.TryParse<UserPrivilege>(value, ignoreCase: true, out var privilege)
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

    private static string[] NormalizeAliases(string commandName, IEnumerable<string> aliases)
    {
        return aliases
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Select(CommandRegistration.Normalize)
            .Where(alias => alias.Length > 0 && !string.Equals(alias, commandName, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
