using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Data;
using OhMyBot.Core.Data.Entities;
using OhMyBot.Core.Linking;

namespace OhMyBot.Core.Commands;

public sealed class CoreCommandDslProvider(
    IServiceScopeFactory scopeFactory,
    IOptions<LinkTokenOptions> linkTokenOptions,
    TimeProvider timeProvider) : IPlatformCommandDslProvider
{
    private readonly LinkTokenOptions _linkTokenOptions = linkTokenOptions.Value;

    public IEnumerable<CommandDslNode> GetNodes()
    {
        return
        [
            new CommandDslNode
            {
                Name = "ping",
                Description = "检查 Core 连接状态。",
                Usage = "/ping",
                Handler = PingAsync
            },
            new CommandDslNode
            {
                Name = "link",
                Description = "跨平台绑定身份。",
                Usage = "/link [token]",
                Handler = LinkAsync
            },
            new CommandDslNode
            {
                Name = "info",
                Description = "查看用户信息。",
                Usage = "/info [uid]",
                Handler = InfoAsync
            },
            new CommandDslNode
            {
                Name = "help",
                Description = "显示可用指令",
                Usage = "/help [子命令]",
                Handler = _ => Task.FromResult(CommandResponses.Text(string.Empty))
            }
        ];
    }

    private Task<CommandResponse> PingAsync(CommandContext context)
    {
        var elapsed = timeProvider.GetElapsedTime(context.StartedAt);
        var elapsedMs = elapsed.Ticks / TimeSpan.TicksPerMillisecond;
        var response = CommandResponses.Ok(CommandResponseDataKind.Ping, context);
        response.Ping = new PingData { ElapsedMs = elapsedMs };
        return Task.FromResult(response);
    }

    private async Task<CommandResponse> LinkAsync(CommandContext context)
    {
        var request = context.Request;
        var currentIdentity = context.Identity;
        var cancellationToken = context.CancellationToken;
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OhMyBotV2DbContext>();
        var identityService = scope.ServiceProvider.GetRequiredService<CoreIdentityService>();
        var linkTokenStore = scope.ServiceProvider.GetRequiredService<ILinkTokenStore>();

        if (request.Args.Count == 0)
        {
            var token = GenerateToken();
            var payload = new LinkTokenPayload(
                currentIdentity.CoreUserId,
                currentIdentity.Platform,
                currentIdentity.PlatformUserId,
                timeProvider.GetUtcNow());

            await linkTokenStore.SetAsync(token, payload, _linkTokenOptions.TokenTtl, cancellationToken);
            var response = CommandResponses.Ok(CommandResponseDataKind.LinkToken, context);
            response.LinkToken = new LinkTokenData
            {
                Token = token,
                TtlSeconds = (int)_linkTokenOptions.TokenTtl.TotalSeconds
            };
            return response;
        }

        var incomingToken = request.Args[0].Trim();
        if (string.IsNullOrWhiteSpace(incomingToken))
        {
            return CommandResponses.Error("LinkTokenInvalid", "绑定令牌为空。", context);
        }

        var tokenPayload = await linkTokenStore.GetAsync(incomingToken, cancellationToken);
        if (tokenPayload is null)
        {
            return CommandResponses.Error("LinkTokenInvalid", "绑定令牌不存在、已过期或已被使用，请重新获取。", context);
        }

        if (tokenPayload.CreatedFromPlatform == request.Platform)
        {
            return CommandResponses.Error("LinkPlatformNotAllowed", "绑定令牌只能用于不同平台账号绑定。", context);
        }

        var targetUser = await dbContext.CoreUsers
            .Include(user => user.Identities)
            .FirstOrDefaultAsync(user => user.Id == tokenPayload.OwnerCoreUserId, cancellationToken);

        if (targetUser is null)
        {
            await linkTokenStore.RemoveAsync(incomingToken, cancellationToken);
            return CommandResponses.Error("LinkTargetMissing", "绑定令牌所属账号不存在，请重新获取。", context);
        }

        var sourceUser = await dbContext.CoreUsers
            .Include(user => user.Identities)
            .FirstAsync(user => user.Id == currentIdentity.CoreUserId, cancellationToken);

        if (sourceUser.Id == targetUser.Id)
        {
            return CommandResponses.Error("LinkAlreadyBound", "当前账号已经绑定到该身份。", context);
        }

        var (retainedUser, mergedUser) = SelectMergeDirection(sourceUser, targetUser);
        await MergeUsersAsync(dbContext, mergedUser, retainedUser, cancellationToken);
        await identityService.CacheUserIdentitiesAsync(retainedUser, cancellationToken);
        await linkTokenStore.RemoveAsync(incomingToken, cancellationToken);
        var linkResponse = CommandResponses.Ok(CommandResponseDataKind.LinkResult, context);
        linkResponse.LinkResult = new LinkResultData
        {
            Status = "linked",
            CoreUserId = retainedUser.Id
        };
        return linkResponse;
    }

    private async Task<CommandResponse> InfoAsync(CommandContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OhMyBotV2DbContext>();
        var callerIsAdmin = (int)context.Identity.Privilege >= (int)UserPrivilege.Admin;
        var requestedUser = context.Request.Args.Count > 0
            ? context.Request.Args[0].Trim()
            : context.Request.ReplyToUserId.Trim();

        CoreUser? targetUser;
        if (callerIsAdmin && !string.IsNullOrWhiteSpace(requestedUser))
        {
            var targetIdentity = await FindIdentityAsync(
                dbContext,
                context.Request.Platform,
                requestedUser,
                context.CancellationToken);

            if (targetIdentity is null)
            {
                return CommandResponses.Error(
                    "UserNotFound",
                    $"User identity not found: {requestedUser}.",
                    context);
            }

            targetUser = await LoadUserAsync(dbContext, targetIdentity.CoreUserId, context.CancellationToken);
        }
        else
        {
            targetUser = await LoadUserAsync(dbContext, context.Identity.CoreUserId, context.CancellationToken);
        }

        if (targetUser is null)
        {
            return CommandResponses.Error("UserNotFound", "Current user was not found.", context);
        }

        var data = new UserInfoData
        {
            Self = targetUser.Id == context.Identity.CoreUserId,
            Privilege = targetUser.Privilege
        };

        data.Identities.AddRange(targetUser.Identities
            .OrderBy(identity => identity.Platform)
            .ThenBy(identity => identity.PlatformUserId, StringComparer.Ordinal)
            .Select(ToIdentityData));

        if (callerIsAdmin)
        {
            data.CoreUserId = targetUser.Id;
        }

        var response = CommandResponses.Ok(CommandResponseDataKind.UserInfo, context);
        response.UserInfo = data;
        return response;
    }

    private static (CoreUser RetainedUser, CoreUser MergedUser) SelectMergeDirection(CoreUser firstUser, CoreUser secondUser)
    {
        return firstUser.Id.CompareTo(secondUser.Id) <= 0
            ? (firstUser, secondUser)
            : (secondUser, firstUser);
    }

    private async Task MergeUsersAsync(
        OhMyBotV2DbContext dbContext,
        CoreUser sourceUser,
        CoreUser targetUser,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        targetUser.Privilege = (UserPrivilege)Math.Max((int)targetUser.Privilege, (int)sourceUser.Privilege);
        targetUser.UpdatedAt = now;

        foreach (var identity in sourceUser.Identities.ToArray())
        {
            identity.CoreUserId = targetUser.Id;
            identity.CoreUser = targetUser;
            identity.UpdatedAt = now;
            targetUser.Identities.Add(identity);
        }

        dbContext.CoreUsers.Remove(sourceUser);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static PlatformIdentityData ToIdentityData(PlatformIdentity identity)
    {
        return new PlatformIdentityData
        {
            Platform = identity.Platform,
            Uid = identity.PlatformUserId,
            DisplayName = identity.DisplayName ?? string.Empty,
            Username = identity.Username ?? string.Empty
        };
    }

    private static Task<CoreUser?> LoadUserAsync(
        OhMyBotV2DbContext dbContext,
        long coreUserId,
        CancellationToken cancellationToken)
    {
        return dbContext.CoreUsers
            .AsNoTracking()
            .Include(user => user.Identities)
            .FirstOrDefaultAsync(user => user.Id == coreUserId, cancellationToken);
    }

    private static Task<PlatformIdentity?> FindIdentityAsync(
        OhMyBotV2DbContext dbContext,
        BotPlatform platform,
        string requestedUser,
        CancellationToken cancellationToken)
    {
        var normalized = requestedUser.Trim();
        var username = normalized.TrimStart('@');
        var normalizedUsername = username.ToLowerInvariant();
        var searchByUsernameOnly = normalized.StartsWith('@');

        return dbContext.PlatformIdentities
            .AsNoTracking()
            .FirstOrDefaultAsync(
                identity => identity.Platform == platform
                    && (searchByUsernameOnly
                        ? identity.Username != null && identity.Username.ToLower() == normalizedUsername
                        : identity.PlatformUserId == normalized
                            || identity.Username != null && identity.Username.ToLower() == normalizedUsername),
                cancellationToken);
    }
}
