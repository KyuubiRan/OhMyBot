using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Callbacks;
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
                Name = "setpriv",
                Description = "设置用户权限。",
                Usage = "/setpriv <uid|@user> 或回复消息 /setpriv",
                RequiredPrivilege = UserPrivilege.Admin,
                Handler = SetPrivilegeAsync
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
            .Include(user => user.PlatformProfiles)
            .FirstOrDefaultAsync(user => user.Id == tokenPayload.OwnerCoreUserId, cancellationToken);

        if (targetUser is null)
        {
            await linkTokenStore.RemoveAsync(incomingToken, cancellationToken);
            return CommandResponses.Error("LinkTargetMissing", "绑定令牌所属账号不存在，请重新获取。", context);
        }

        var sourceUser = await dbContext.CoreUsers
            .Include(user => user.PlatformProfiles)
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

        UserInfoData? data;
        if (callerIsAdmin && !string.IsNullOrWhiteSpace(requestedUser))
        {
            var targetProfile = await FindProfileAsync(
                dbContext,
                context.Request.Platform,
                requestedUser,
                context.CancellationToken);
            data = await BuildPlatformUserInfoDataAsync(
                dbContext,
                targetProfile,
                self: false,
                includeCoreUserId: true,
                context.CancellationToken);
        }
        else
        {
            var currentProfile = await FindProfileAsync(
                dbContext,
                context.Request.Platform,
                context.Identity.PlatformUserId,
                context.CancellationToken);
            data = await BuildPlatformUserInfoDataAsync(
                dbContext,
                currentProfile,
                self: true,
                includeCoreUserId: callerIsAdmin,
                context.CancellationToken);
        }

        if (data is null)
        {
            return CommandResponses.Error(
                "UserNotFound",
                string.IsNullOrWhiteSpace(requestedUser)
                    ? "Current user was not found."
                    : $"User identity not found: {requestedUser}.",
                context);
        }

        var response = CommandResponses.Ok(CommandResponseDataKind.UserInfo, context);
        response.UserInfo = data;
        return response;
    }

    private async Task<CommandResponse> SetPrivilegeAsync(CommandContext context)
    {
        var requestedUser = context.Request.Args.Count > 0
            ? context.Request.Args[0].Trim()
            : context.Request.ReplyToUserId.Trim();

        if (string.IsNullOrWhiteSpace(requestedUser))
        {
            return CommandResponses.Error("SetPrivilegeTargetMissing", "请回复用户消息，或提供 uid / @user。", context);
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<SetPrivilegeService>();
        var callbackStore = scope.ServiceProvider.GetRequiredService<CallbackActionStore>();
        var target = await service.FindTargetAsync(context.Request.Platform, requestedUser, context.CancellationToken);
        if (target is null)
        {
            return CommandResponses.Error("UserNotFound", $"未找到用户：{requestedUser}", context);
        }

        if (target.CoreUserId is not null
            && !SetPrivilegeService.CanOperateTarget(context.Identity.CoreUserId, context.Identity.Privilege, target.CoreUserId.Value, target.CurrentPrivilege))
        {
            return CommandResponses.Error("SetPrivilegeForbidden", "不能操作权限高于或等于自己的用户。", context);
        }

        var allowedPrivileges = SetPrivilegeService.GetAllowedTargetPrivileges(context.Identity.Privilege);
        if (allowedPrivileges.Count == 0)
        {
            return CommandResponses.Error("PrivilegeDenied", "Insufficient privilege.", context);
        }

        var response = CommandResponses.Text(
            $"`{target.DisplayName}` 当前权限: `{SetPrivilegeService.FormatPrivilege(target.CurrentPrivilege)}`",
            context);

        foreach (var row in allowedPrivileges.Chunk(2))
        {
            var buttonRow = new ResponseButtonRow();
            foreach (var privilege in row)
            {
                buttonRow.Buttons.Add(new ResponseButton
                {
                    Text = SetPrivilegeService.FormatPrivilege(privilege),
                    Payload = await callbackStore.PutAsync(
                        "setpriv-apply",
                        context.Identity.CoreUserId,
                        context.Request.ChatId,
                        context.Request.UserId,
                        new SetPrivilegeCallbackData(target.Platform, target.Uid, privilege),
                        cancellationToken: context.CancellationToken)
                });
            }

            response.ButtonRows.Add(buttonRow);
        }

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

        foreach (var profile in sourceUser.PlatformProfiles.ToArray())
        {
            profile.CoreUserId = targetUser.Id;
            profile.CoreUser = targetUser;
            profile.UpdatedAt = now;
            targetUser.PlatformProfiles.Add(profile);
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

    private static PlatformIdentityData ToIdentityData(PlatformUserProfile profile)
    {
        return new PlatformIdentityData
        {
            Platform = profile.Platform,
            Uid = profile.Uid,
            DisplayName = FirstNonEmpty(FormatProfileDisplayName(profile), profile.Uid),
            Username = profile.Username ?? string.Empty
        };
    }

    private static async Task<UserInfoData?> BuildPlatformUserInfoDataAsync(
        OhMyBotV2DbContext dbContext,
        PlatformUserProfile? profile,
        bool self,
        bool includeCoreUserId,
        CancellationToken cancellationToken)
    {
        if (profile is null)
        {
            return null;
        }

        CoreUser? coreUser = profile.CoreUserId is null
            ? null
            : await LoadUserAsync(dbContext, profile.CoreUserId.Value, cancellationToken);
        var data = new UserInfoData
        {
            Self = self,
            Privilege = coreUser?.Privilege ?? UserPrivilege.User
        };
        data.Identities.Add(ToIdentityData(profile));

        if (includeCoreUserId && coreUser is not null)
        {
            data.CoreUserId = coreUser.Id;
        }

        return data;
    }

    private static string FormatProfileDisplayName(PlatformUserProfile profile)
    {
        var name = string.Join(' ', new[] { profile.LastName, profile.FirstName }.Where(part => !string.IsNullOrWhiteSpace(part)));
        return FirstNonEmpty(profile.Nickname, name, profile.Username, profile.Uid);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static Task<CoreUser?> LoadUserAsync(
        OhMyBotV2DbContext dbContext,
        long coreUserId,
        CancellationToken cancellationToken)
    {
        return dbContext.CoreUsers
            .AsNoTracking()
            .Include(user => user.PlatformProfiles)
            .FirstOrDefaultAsync(user => user.Id == coreUserId, cancellationToken);
    }

    private static Task<PlatformUserProfile?> FindProfileAsync(
        OhMyBotV2DbContext dbContext,
        BotPlatform platform,
        string requestedUser,
        CancellationToken cancellationToken)
    {
        var normalized = requestedUser.Trim();
        var username = normalized.TrimStart('@');
        var normalizedUsername = username.ToLowerInvariant();
        var searchByUsernameOnly = normalized.StartsWith('@');

        return dbContext.PlatformUserProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(
                profile => profile.Platform == platform
                    && (searchByUsernameOnly
                        ? profile.Username != null && profile.Username.ToLower() == normalizedUsername
                        : profile.Uid == normalized
                            || profile.Username != null && profile.Username.ToLower() == normalizedUsername),
                cancellationToken);
    }
}
