using Grpc.Core;
using OhMyBot.Contracts.Grpc;

namespace OhMyBot.TelegramGateway;

public sealed class TelegramCommandGateway(ICommandRouterClient commandRouterClient)
{
    private const int TelegramPlatformFlag = 1;
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
            Platform = BotPlatform.Telegram,
            BotInstanceId = botInstanceId,
            CurrentVersion = currentVersion
        }, cancellationToken);

        if (response.NotModified)
        {
            return Routes;
        }

        var routes = response.Routes
            .Where(route => (route.SupportPlatforms & TelegramPlatformFlag) != 0)
            .OrderBy(route => route.Command, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        lock (_cacheLock)
        {
            _routes = BuildRouteLookup(routes);
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
            try
            {
                var routes = await ReloadAsync(botInstanceId, cancellationToken);
                return ResponseText($"Reloaded {routes.Count} routes.");
            }
            catch (RpcException exception)
            {
                return ResponseError("CoreUnavailable", exception.Status.Detail);
            }
        }

        if (!TryGetRoute(command, out var route))
        {
            return EmptyResponse();
        }

        if (!route.Enabled)
        {
            return ResponseError("RouteDisabled", "This route is disabled.");
        }

        try
        {
            return await commandRouterClient.ExecuteCommandAsync(new CommandRequest
            {
                Platform = BotPlatform.Telegram,
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
                Args = { args }
            }, cancellationToken);
        }
        catch (RpcException exception)
        {
            return ResponseError("CoreUnavailable", exception.Status.Detail);
        }
    }

    public async Task<bool> RecordUserProfileAsync(
        GatewayCommandRequest gatewayRequest,
        string botInstanceId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await commandRouterClient.RecordUserProfileAsync(new UserProfileRequest
            {
                Platform = BotPlatform.Telegram,
                BotInstanceId = botInstanceId,
                Uid = gatewayRequest.UserId,
                Username = gatewayRequest.Username ?? string.Empty,
                FirstName = gatewayRequest.FirstName ?? string.Empty,
                LastName = gatewayRequest.LastName ?? string.Empty,
                Nickname = gatewayRequest.Nickname ?? string.Empty
            }, cancellationToken);
            return true;
        }
        catch (RpcException)
        {
            return false;
        }
    }

    private bool TryGetRoute(string command, out RouteDescriptor route)
    {
        lock (_cacheLock)
        {
            return _routes.TryGetValue(command, out route!);
        }
    }

    private static IReadOnlyDictionary<string, RouteDescriptor> BuildRouteLookup(IEnumerable<RouteDescriptor> routes)
    {
        var lookup = new Dictionary<string, RouteDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var route in routes)
        {
            lookup[route.Command] = route;
            foreach (var alias in route.Aliases.Where(alias => !string.IsNullOrWhiteSpace(alias)))
            {
                lookup[alias.Trim().TrimStart('/').ToLowerInvariant()] = route;
            }
        }

        return lookup;
    }

    private static (string Command, string[] Args) Parse(string text)
    {
        var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return (string.Empty, []);
        }

        var command = parts[0].TrimStart('/').Split('@', 2)[0].ToLowerInvariant();
        return (command, parts.Skip(1).ToArray());
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
