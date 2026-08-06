using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commanding.Routing;
using OhMyBot.Core.Infrastructure.UserProfiles;

namespace OhMyBot.Core.Commanding.Commands;

public sealed class CommandExecutionService(
    CoreIdentityService identityService,
    PlatformUserProfileService userProfileService,
    RouteStore routeStore,
    PlatformCommandDslExecutor dslExecutor,
    ILogger<CommandExecutionService> logger,
    TimeProvider timeProvider)
{
    public async Task<CommandResponse> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken = default)
    {
        var started = timeProvider.GetTimestamp();
        await userProfileService.RecordAsync(request, cancellationToken);
        var identity = await identityService.ResolveIdentityAsync(request, cancellationToken);

        if (!routeStore.TryGet(request.Command, out var route))
        {
            return CommandResponses.Error("RouteNotFound", "Unknown route.", identity, request.MessageId);
        }

        if (!route.Enabled)
        {
            return CommandResponses.Error("RouteDisabled", "This route is disabled.", identity, request.MessageId);
        }

        if (!route.TargetExists)
        {
            return CommandResponses.Error("RouteTargetMissing", "The route target command is not available.", identity, request.MessageId);
        }

        var platformFlag = CommandDsl.ToSupportedPlatform(request.Platform);
        if (platformFlag is SupportedPlatforms.None || !route.SupportPlatforms.HasFlag(platformFlag))
        {
            return CommandResponses.Error("UnsupportedPlatform", "This command is not available on this platform.", identity, request.MessageId);
        }

        var chatTypeFlag = CommandDsl.ToSupportedChatType(request.ChatType);
        if (chatTypeFlag is SupportedChatTypes.None || !route.EffectiveSupportChatTypes.HasFlag(chatTypeFlag))
        {
            return CommandResponses.Error("UnsupportedChatType", DescribeChatTypeRestriction(route.EffectiveSupportChatTypes), identity, request.MessageId);
        }

        if ((int)identity.Privilege < (int)route.EffectiveRequiredPrivilege)
        {
            return CommandResponses.Error("PrivilegeDenied", "Insufficient privilege.", identity, request.MessageId);
        }

        var canonicalRequest = request.Clone();
        canonicalRequest.Command = route.Command;
        try
        {
            return await dslExecutor.ExecuteAsync(new CommandContext(canonicalRequest, identity, started, cancellationToken));
        }
        catch (CommandUserException exception)
        {
            // 业务失败：消息是处理器专门写给用户看的自助提示，原样回。
            // 不生成 errorId——这类失败不需要谁去查日志，折叠成「请稍后重试」只会让用户反复重试同一个错密码。
            logger.LogInformation(
                "Command rejected. errorCode={ErrorCode}, command={Command}, user={UserId}, platform={Platform}, chat={ChatId}, reason={Reason}",
                exception.ErrorCode,
                canonicalRequest.Command,
                canonicalRequest.UserId,
                canonicalRequest.Platform,
                canonicalRequest.ChatId,
                exception.Message);
            return CommandResponses.Error(
                exception.ErrorCode,
                exception.Message,
                identity,
                canonicalRequest.MessageId);
        }
        catch (Exception exception)
        {
            // 异常原文可能带表名/约束名/内网地址/上游 API 地址，不能进聊天窗口。
            // 只回一个关联 id，细节留在日志里按 id 查。
            var errorId = Guid.NewGuid().ToString("N")[..6];
            logger.LogError(
                exception,
                "Command handler failed. errorId={ErrorId}, command={Command}, user={UserId}, platform={Platform}, chat={ChatId}.",
                errorId,
                canonicalRequest.Command,
                canonicalRequest.UserId,
                canonicalRequest.Platform,
                canonicalRequest.ChatId);
            return CommandResponses.Error(
                "CommandHandlerFailed",
                $"命令执行失败，请稍后重试。（错误 id: {errorId}）",
                identity,
                canonicalRequest.MessageId);
        }
    }

    public Task<GetRoutesResponse> GetRoutesAsync(GetRoutesRequest request, CancellationToken cancellationToken = default)
    {
        var currentVersion = routeStore.Version;
        var response = new GetRoutesResponse
        {
            Version = currentVersion,
            NotModified = request.CurrentVersion > 0 && request.CurrentVersion == currentVersion
        };

        if (response.NotModified)
        {
            return Task.FromResult(response);
        }

        response.Routes.AddRange(routeStore.GetRoutes(request.Platform).Select(route => route.ToDescriptor()));
        return Task.FromResult(response);
    }

    private static string DescribeChatTypeRestriction(SupportedChatTypes supported)
    {
        return supported switch
        {
            SupportedChatTypes.Private => "此命令只能在私聊中使用。",
            SupportedChatTypes.Group => "此命令只能在群聊中使用。",
            _ => "此命令不支持在当前会话类型中使用。"
        };
    }

}
