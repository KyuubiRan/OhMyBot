using OhMyBot.Contracts.Grpc;

namespace OhMyBot.QQGateway;

public sealed class QQCommandGateway(ICommandRouterClient commandRouterClient)
{
    private const int QQPlatformFlag = 2;
    private readonly Lock _cacheLock = new();
    private IReadOnlyDictionary<string, RouteDescriptor> _routes = new Dictionary<string, RouteDescriptor>(StringComparer.OrdinalIgnoreCase);
    private long _version;

    public IReadOnlyList<RouteDescriptor> Routes
    {
        get
        {
            lock (_cacheLock)
            {
                return _routes.Values.ToArray();
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
            _routes = routes.ToDictionary(route => route.Command, StringComparer.OrdinalIgnoreCase);
            _version = response.Version;
        }

        return routes;
    }

    public async Task<CommandResponse> ExecuteAsync(
        GatewayCommandRequest gatewayRequest,
        string botInstanceId,
        CancellationToken cancellationToken = default)
    {
        var (command, args) = Parse(gatewayRequest.Text);
        if (command == "reload")
        {
            var routes = await ReloadAsync(botInstanceId, cancellationToken);
            return ResponseText($"Reloaded {routes.Count} routes.");
        }

        if (!TryGetRoute(command, out var route))
        {
            return ResponseError("RouteNotFound", "Unknown route.");
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
            DisplayName = gatewayRequest.DisplayName ?? string.Empty,
            Username = gatewayRequest.Username ?? string.Empty,
            Args = { args }
        }, cancellationToken);
    }

    private bool TryGetRoute(string command, out RouteDescriptor route)
    {
        lock (_cacheLock)
        {
            return _routes.TryGetValue(command, out route!);
        }
    }

    private static (string Command, string[] Args) Parse(string text)
    {
        var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return (string.Empty, []);
        }

        var command = parts[0].TrimStart('/').ToLowerInvariant();
        return (command, parts.Skip(1).ToArray());
    }

    private static CommandResponse ResponseText(string text)
    {
        var response = new CommandResponse();
        response.Messages.Add(new ResponseMessage { Text = text });
        return response;
    }

    private static CommandResponse ResponseError(string code, string message)
    {
        return new CommandResponse
        {
            Error = new CommandError
            {
                Code = code,
                Message = message
            }
        };
    }
}
