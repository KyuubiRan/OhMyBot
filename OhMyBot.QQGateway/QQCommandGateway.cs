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
    private readonly RecordedProfileCache _recordedProfiles =
        new(TimeProvider.System, options?.Value.ProfileRecordDedupTtl ?? TimeSpan.FromMinutes(30));
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
        var request = new UserProfileRequest
        {
            Platform = BotPlatform.Qq,
            BotInstanceId = botInstanceId,
            Uid = gatewayRequest.UserId,
            Username = gatewayRequest.Username ?? string.Empty,
            FirstName = gatewayRequest.FirstName ?? string.Empty,
            LastName = gatewayRequest.LastName ?? string.Empty,
            Nickname = gatewayRequest.Nickname ?? gatewayRequest.DisplayName ?? string.Empty
        };

        // 本地去重：同一 uid 且档案未变、TTL 内则跳过对 Core 的 gRPC（活跃群防刷屏）。成功后才落表。
        var signature = ProfileSignature(request);
        if (!_recordedProfiles.ShouldRecord(request.Uid, signature))
        {
            return true;
        }

        await commandRouterClient.RecordUserProfileAsync(request, cancellationToken);
        _recordedProfiles.MarkRecorded(request.Uid, signature);
        return true;
    }

    private static string ProfileSignature(UserProfileRequest request)
    {
        return string.Join('\u001f', request.Username, request.FirstName, request.LastName, request.Nickname);
    }

    /// <summary>用户回复/发送序号选择菜单项。<paramref name="replyToMessageId"/> 为被回复的菜单消息 id（私聊裸数字可空）。</summary>
    public Task<CommandResponse> ExecuteMenuSelectionAsync(
        GatewayCommandRequest gatewayRequest,
        string? replyToMessageId,
        string selection,
        string botInstanceId,
        CancellationToken cancellationToken = default)
    {
        return commandRouterClient.ExecuteQqMenuSelectionAsync(new QqMenuSelectionRequest
        {
            Platform = BotPlatform.Qq,
            BotInstanceId = botInstanceId,
            ChatId = gatewayRequest.ChatId,
            UserId = gatewayRequest.UserId,
            MessageId = gatewayRequest.MessageId,
            ReplyToMessageId = replyToMessageId ?? string.Empty,
            ChatType = gatewayRequest.ChatType,
            Selection = selection
        }, cancellationToken);
    }

    /// <summary>把已发出的菜单消息绑定到其选项列表（存 Core Redis）。</summary>
    public async Task BindMenuAsync(
        string chatId,
        string messageId,
        string senderId,
        BotChatType chatType,
        string menuToken,
        string botInstanceId,
        CancellationToken cancellationToken = default)
    {
        await commandRouterClient.BindQqMenuAsync(new BindQqMenuRequest
        {
            BotInstanceId = botInstanceId,
            ChatId = chatId,
            MessageId = messageId,
            SenderId = senderId,
            ChatType = chatType,
            MenuToken = menuToken
        }, cancellationToken);
    }

    /// <summary>把平台待审批请求上报给 Core。返回是否有插件受理。</summary>
    public async Task<bool> ReportPlatformRequestAsync(
        PlatformRequestReport report,
        CancellationToken cancellationToken = default)
    {
        var ack = await commandRouterClient.ReportPlatformRequestAsync(report, cancellationToken);
        return ack.Accepted;
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
            Qq = new QqResponse { Messages = { new QqMessage { Text = text } } }
        };
    }

    private static CommandResponse EmptyResponse()
    {
        return new CommandResponse { Qq = new QqResponse() };
    }

    private static CommandResponse ResponseError(string code, string message)
    {
        return new CommandResponse
        {
            Code = 1,
            ErrorCode = code,
            Qq = new QqResponse { Messages = { new QqMessage { Text = message } } }
        };
    }
}
