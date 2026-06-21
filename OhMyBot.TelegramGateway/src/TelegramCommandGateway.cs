using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Grpc;

namespace OhMyBot.TelegramGateway;

public sealed class TelegramCommandGateway(
    ICommandRouterClient commandRouterClient,
    IOptions<TelegramGatewayOptions>? options = null,
    ILogger<TelegramCommandGateway>? logger = null)
{
    private const int TelegramPlatformFlag = 1;
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
        var (command, args) = GatewayCommandParser.Parse(
            gatewayRequest.Text,
            _commandPrefixes,
            stripBotMention: true);
        if (!string.IsNullOrWhiteSpace(command))
        {
            logger?.LogInformation(
                "Received command senderId={SenderId} chatId={ChatId} command={Command}.",
                gatewayRequest.UserId,
                gatewayRequest.ChatId,
                command);
        }

        if (command == "info" && !string.IsNullOrWhiteSpace(gatewayRequest.TextMentionUserId))
        {
            args = [gatewayRequest.TextMentionUserId];
        }

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
                ReplyToUserId = gatewayRequest.ReplyToUserId ?? string.Empty,
                Args = { args }
            }, cancellationToken);
        }
        catch (RpcException exception)
        {
            return ResponseError("CoreUnavailable", exception.Status.Detail);
        }
    }

    public async Task<CommandResponse> ExecuteCallbackAsync(
        CallbackRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await commandRouterClient.ExecuteCallbackAsync(request, cancellationToken);
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
