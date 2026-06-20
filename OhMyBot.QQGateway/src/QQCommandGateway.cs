using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Grpc;

namespace OhMyBot.QQGateway;

public sealed class QQCommandGateway(
    ICommandRouterClient commandRouterClient,
    IOptions<QQGatewayOptions>? options = null,
    ILogger<QQCommandGateway>? logger = null)
{
    private const int QQPlatformFlag = 2;
    private readonly string[] _commandPrefixes = GatewayCommandParser.NormalizePrefixes(options?.Value.CommandPrefixes);
    private readonly Lock _cacheLock = new();
    private IReadOnlyDictionary<string, RouteDescriptor> _routes = new Dictionary<string, RouteDescriptor>(StringComparer.OrdinalIgnoreCase);
    private long _version;

    public IReadOnlyList<RouteDescriptor> Routes
    {
        get
        {
            lock (_cacheLock)
            {
                return _routes.Values
                    .DistinctBy(route => route.Command, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    public long Version
    {
        get
        {
            lock (_cacheLock)
            {
                return _version;
            }
        }
    }

    public async Task<IReadOnlyList<RouteDescriptor>> ReloadAsync(string botInstanceId, CancellationToken cancellationToken = default)
    {
        var currentVersion = Version;
        var response = await commandRouterClient.GetRoutesAsync(new GetRoutesRequest
        {
            Platform = BotPlatform.Qq,
            BotInstanceId = botInstanceId,
            CurrentVersion = currentVersion
        }, cancellationToken);

        if (response.NotModified)
        {
            return Routes;
        }

        var routes = response.Routes
            .Where(route => (route.SupportPlatforms & QQPlatformFlag) != 0)
            .OrderBy(route => route.Command, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        lock (_cacheLock)
        {
            _routes = BuildRouteLookup(routes, _commandPrefixes);
            _version = response.Version;
        }

        return routes;
    }

    public async Task<CommandResponse> ExecuteAsync(
        GatewayCommandRequest gatewayRequest,
        string botInstanceId,
        CancellationToken cancellationToken = default)
    {
        var (command, args) = GatewayCommandParser.Parse(gatewayRequest.Text, _commandPrefixes);
        if (!string.IsNullOrWhiteSpace(command))
        {
            logger?.LogInformation(
                "Received command senderId={SenderId} chatId={ChatId} command={Command}.",
                gatewayRequest.UserId,
                gatewayRequest.ChatId,
                command);
        }

        if (command == "reload")
        {
            var routes = await ReloadAsync(botInstanceId, cancellationToken);
            return ResponseText($"Reloaded {routes.Count} routes.");
        }

        if (!TryGetRoute(command, out var route))
        {
            return EmptyResponse();
        }

        if (!route.Enabled)
        {
            return ResponseError("RouteDisabled", "This route is disabled.");
        }

        return await commandRouterClient.ExecuteCommandAsync(new CommandRequest
        {
            Platform = BotPlatform.Qq,
            BotInstanceId = botInstanceId,
            ChatId = gatewayRequest.ChatId,
            UserId = gatewayRequest.UserId,
            MessageId = gatewayRequest.MessageId,
            Command = route.Command,
            ChatType = gatewayRequest.ChatType,
            DisplayName = gatewayRequest.DisplayName ?? string.Empty,
            Username = gatewayRequest.Username ?? string.Empty,
            FirstName = gatewayRequest.FirstName ?? string.Empty,
            LastName = gatewayRequest.LastName ?? string.Empty,
            Nickname = gatewayRequest.Nickname ?? string.Empty,
            ReplyToUserId = gatewayRequest.ReplyToUserId ?? string.Empty,
            Args = { args }
        }, cancellationToken);
    }

    public async Task<bool> RecordUserProfileAsync(
        GatewayCommandRequest gatewayRequest,
        string botInstanceId,
        CancellationToken cancellationToken = default)
    {
        await commandRouterClient.RecordUserProfileAsync(new UserProfileRequest
        {
            Platform = BotPlatform.Qq,
            BotInstanceId = botInstanceId,
            Uid = gatewayRequest.UserId,
            Username = gatewayRequest.Username ?? string.Empty,
            FirstName = gatewayRequest.FirstName ?? string.Empty,
            LastName = gatewayRequest.LastName ?? string.Empty,
            Nickname = gatewayRequest.Nickname ?? gatewayRequest.DisplayName ?? string.Empty
        }, cancellationToken);
        return true;
    }

    private bool TryGetRoute(string command, out RouteDescriptor route)
    {
        lock (_cacheLock)
        {
            return _routes.TryGetValue(command, out route!);
        }
    }

    private static IReadOnlyDictionary<string, RouteDescriptor> BuildRouteLookup(
        IEnumerable<RouteDescriptor> routes,
        IReadOnlyCollection<string> commandPrefixes)
    {
        var lookup = new Dictionary<string, RouteDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var route in routes)
        {
            lookup[route.Command] = route;
            foreach (var alias in route.Aliases.Where(alias => !string.IsNullOrWhiteSpace(alias)))
            {
                lookup[GatewayCommandParser.NormalizeRouteKey(alias, commandPrefixes)] = route;
            }
        }

        return lookup;
    }

    private static CommandResponse ResponseText(string text)
    {
        return new CommandResponse
        {
            Code = 0,
            DataKind = CommandResponseDataKind.Text,
            Message = text,
            Text = new TextData { Text = text }
        };
    }

    private static CommandResponse EmptyResponse()
    {
        return new CommandResponse();
    }

    private static CommandResponse ResponseError(string code, string message)
    {
        return new CommandResponse
        {
            Code = 1,
            ErrorCode = code,
            Message = message
        };
    }
}
