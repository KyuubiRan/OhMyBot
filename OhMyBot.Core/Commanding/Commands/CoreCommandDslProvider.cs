using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commanding.Callbacks;
using OhMyBot.Core.Commanding.Presentation;
using OhMyBot.Core.Infrastructure.Data;
using OhMyBot.Core.Infrastructure.Data.Entities;
using OhMyBot.Core.Infrastructure.Identity;
using OhMyBot.Core.Infrastructure.Linking;
using OhMyBot.Core.Infrastructure.Plugins;

namespace OhMyBot.Core.Commanding.Commands;

public sealed class CoreCommandDslProvider(
    IServiceScopeFactory scopeFactory,
    IOptions<LinkTokenOptions> linkTokenOptions,
    TimeProvider timeProvider,
    Func<IPluginManager>? pluginManagerAccessor = null) : IPlatformCommandDslProvider
{
    private static readonly IPluginManager FallbackPluginManager = new NullPluginManager();
    private readonly LinkTokenOptions _linkTokenOptions = linkTokenOptions.Value;
    private readonly Func<IPluginManager> _pluginManagerAccessor =
        pluginManagerAccessor ?? (() => FallbackPluginManager);

    public IEnumerable<CommandDslNode> GetNodes()
    {
        return
        [
            new CommandDslNode
            {
                Name = "ping",
                Description = "检查 Core 连接状态",
                Usage = "/ping",
                Handler = PingAsync
            },
            new CommandDslNode
            {
                Name = "link",
                Description = "跨平台绑定身份",
                Usage = "/link [token]",
                SupportChatTypes = SupportedChatTypes.Private,
                Handler = LinkAsync
            },
            new CommandDslNode
            {
                Name = "info",
                Description = "查看用户信息",
                Usage = "/info [uid]",
                Handler = InfoAsync
            },
            new CommandDslNode
            {
                Name = "setpriv",
                Description = "设置用户权限",
                Usage = "/setpriv <uid|@user> 或回复消息 /setpriv",
                RequiredPrivilege = UserPrivilege.Admin,
                Handler = SetPrivilegeAsync
            },
            new CommandDslNode
            {
                Name = "help",
                Description = "显示可用指令",
                Usage = "/help [子命令]",
                Handler = context => Task.FromResult(CommandResponses.Silent(context))
            },
            new CommandDslNode
            {
                Name = "plugin",
                Description = "管理 Core 插件",
                Usage = "/plugin <list|status|reload|enable|disable>",
                RequiredPrivilege = UserPrivilege.Owner,
                Children =
                [
                    new CommandDslNode
                    {
                        Name = "list",
                        Description = "列出插件",
                        Usage = "/plugin list",
                        RequiredPrivilege = UserPrivilege.Owner,
                        Handler = PluginListAsync
                    },
                    new CommandDslNode
                    {
                        Name = "status",
                        Description = "查看插件状态",
                        Usage = "/plugin status <id>",
                        RequiredPrivilege = UserPrivilege.Owner,
                        Handler = PluginStatusAsync
                    },
                    new CommandDslNode
                    {
                        Name = "reload",
                        Description = "重载插件",
                        Usage = "/plugin reload <id|all>",
                        RequiredPrivilege = UserPrivilege.Owner,
                        Handler = PluginReloadAsync
                    },
                    new CommandDslNode
                    {
                        Name = "enable",
                        Description = "启用插件",
                        Usage = "/plugin enable <id>",
                        RequiredPrivilege = UserPrivilege.Owner,
                        Handler = PluginEnableAsync
                    },
                    new CommandDslNode
                    {
                        Name = "disable",
                        Description = "禁用插件",
                        Usage = "/plugin disable <id>",
                        RequiredPrivilege = UserPrivilege.Owner,
                        Handler = PluginDisableAsync
                    }
                ]
            }
        ];
    }

    private Task<CommandResponse> PluginListAsync(CommandContext context)
    {
        var plugins = _pluginManagerAccessor().GetPlugins();
        if (plugins.Count == 0)
        {
            return Task.FromResult(CommandResponses.Text("当前没有已发现的插件。", context));
        }

        var lines = plugins.Select(plugin =>
            $"{plugin.Id} {plugin.Version} [{plugin.State}] platforms={plugin.SupportedPlatforms} generation={plugin.Generation}");
        return Task.FromResult(CommandResponses.Text(string.Join('\n', lines), context));
    }

    private Task<CommandResponse> PluginStatusAsync(CommandContext context)
    {
        if (context.Request.Args.Count == 0)
        {
            return Task.FromResult(CommandResponses.Error("PluginIdMissing", "请提供插件 ID。", context));
        }

        var pluginId = context.Request.Args[0].Trim();
        var plugin = _pluginManagerAccessor().GetPlugins()
            .FirstOrDefault(item => string.Equals(item.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        if (plugin is null)
        {
            return Task.FromResult(CommandResponses.Error("PluginNotFound", $"未找到插件：{pluginId}", context));
        }

        var dependencies = plugin.Dependencies.Count == 0
            ? "-"
            : string.Join(", ", plugin.Dependencies.Select(item => $"{item.PluginId} {item.VersionRange}"));
        var text = string.Join('\n',
            $"ID: {plugin.Id}",
            $"名称: {plugin.Name}",
            $"版本: {plugin.Version}",
            $"状态: {plugin.State}",
            $"权重: {plugin.LoadPriority}",
            $"平台: {plugin.SupportedPlatforms}",
            $"Generation: {plugin.Generation}",
            $"依赖: {dependencies}",
            $"错误: {plugin.LastError ?? "-"}");
        return Task.FromResult(CommandResponses.Text(text, context));
    }

    private async Task<CommandResponse> PluginReloadAsync(CommandContext context)
    {
        if (context.Request.Args.Count == 0)
        {
            return CommandResponses.Error("PluginIdMissing", "请提供插件 ID 或 all。", context);
        }

        var result = await _pluginManagerAccessor()
            .ReloadAsync(context.Request.Args[0].Trim(), context.CancellationToken);
        return result.Success
            ? CommandResponses.Text(result.Message, context)
            : CommandResponses.Error("PluginReloadFailed", result.Message, context);
    }

    private async Task<CommandResponse> PluginEnableAsync(CommandContext context)
    {
        if (context.Request.Args.Count == 0)
        {
            return CommandResponses.Error("PluginIdMissing", "请提供插件 ID。", context);
        }

        var result = await _pluginManagerAccessor()
            .EnableAsync(context.Request.Args[0].Trim(), context.CancellationToken);
        return result.Success
            ? CommandResponses.Text(result.Message, context)
            : CommandResponses.Error("PluginEnableFailed", result.Message, context);
    }

    private async Task<CommandResponse> PluginDisableAsync(CommandContext context)
    {
        if (context.Request.Args.Count == 0)
        {
            return CommandResponses.Error("PluginIdMissing", "请提供插件 ID。", context);
        }

        var result = await _pluginManagerAccessor()
            .DisableAsync(context.Request.Args[0].Trim(), context.CancellationToken);
        return result.Success
            ? CommandResponses.Text(result.Message, context)
            : CommandResponses.Error("PluginDisableFailed", result.Message, context);
    }

    private Task<CommandResponse> PingAsync(CommandContext context)
    {
        var elapsed = timeProvider.GetElapsedTime(context.StartedAt);
        var elapsedMs = elapsed.Ticks / TimeSpan.TicksPerMillisecond;
        var response = context.Request.Platform == BotPlatform.Qq
            ? CommandResponses.Qq(context.Identity, $"Pong！Core 响应耗时 {elapsedMs}ms")
            : CommandResponses.TelegramPlain(context.Identity, $"Pong！Core：{elapsedMs}ms", context.Request.MessageId);
        return Task.FromResult(response);
    }

    private async Task<CommandResponse> LinkAsync(CommandContext context)
    {
        var request = context.Request;
        var currentIdentity = context.Identity;
        var cancellationToken = context.CancellationToken;
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var identityService = scope.ServiceProvider.GetRequiredService<CoreIdentityService>();
        var mergeService = scope.ServiceProvider.GetService<CoreUserMergeService>()
            ?? new CoreUserMergeService(dbContext, timeProvider);
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
            var ttlSeconds = (int)_linkTokenOptions.TokenTtl.TotalSeconds;
            return currentIdentity.Platform == BotPlatform.Qq
                ? CommandResponses.Qq(
                    currentIdentity,
                    $"绑定令牌：{token}\n有效期 {ttlSeconds / 60} 分钟。请在另一平台发送 /link {token} 完成绑定。")
                : CommandResponses.TelegramMarkdown(
                    currentIdentity,
                    $"绑定令牌：{MarkdownV2.CodeSpan(token)}\n有效期：{ttlSeconds / 60:F0} 分钟",
                    context.Request.MessageId);
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
        await mergeService.MergeAsync(mergedUser, retainedUser, cancellationToken);
        await identityService.CacheUserIdentitiesAsync(retainedUser, cancellationToken);
        await linkTokenStore.RemoveAsync(incomingToken, cancellationToken);
        return currentIdentity.Platform == BotPlatform.Qq
            ? CommandResponses.Qq(currentIdentity, "绑定成功。")
            : CommandResponses.TelegramPlain(currentIdentity, "账号绑定成功。", context.Request.MessageId);
    }

    private async Task<CommandResponse> InfoAsync(CommandContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var callerIsAdmin = (int)context.Identity.Privilege >= (int)UserPrivilege.Admin;
        var requestedUser = context.Request.Args.Count > 0
            ? context.Request.Args[0].Trim()
            : context.Request.ReplyToUserId.Trim();

        var profile = callerIsAdmin && !string.IsNullOrWhiteSpace(requestedUser)
            ? await FindProfileAsync(dbContext, context.Request.Platform, requestedUser, context.CancellationToken)
            : await FindProfileAsync(dbContext, context.Request.Platform, context.Identity.PlatformUserId, context.CancellationToken);

        var view = await BuildUserInfoViewAsync(dbContext, profile, context.CancellationToken);
        if (view is null)
        {
            return CommandResponses.Error(
                "UserNotFound",
                string.IsNullOrWhiteSpace(requestedUser)
                    ? "Current user was not found."
                    : $"User identity not found: {requestedUser}.",
                context);
        }

        return RenderUserInfo(context, view);
    }

    private static CommandResponse RenderUserInfo(CommandContext context, UserInfoView view)
    {
        if (context.Request.Platform == BotPlatform.Qq)
        {
            var identity = view.Identities.FirstOrDefault(item => item.Platform == BotPlatform.Qq)
                ?? view.Identities.FirstOrDefault();
            var lines = new List<string>();
            if (identity is not null && !string.IsNullOrWhiteSpace(identity.Uid))
            {
                lines.Add($"UID: {identity.Uid}");
            }

            if (identity is not null && !string.IsNullOrWhiteSpace(identity.Username))
            {
                lines.Add($"用户名: {FormatUsername(identity.Username)}");
            }

            if (identity is not null && !string.IsNullOrWhiteSpace(identity.DisplayName))
            {
                lines.Add($"昵称: {identity.DisplayName}");
            }

            lines.Add($"权限: {UserPrivilegeNames.Format(view.Privilege)}");
            return CommandResponses.Qq(context.Identity, string.Join('\n', lines));
        }

        var telegramIdentity = view.Identities.FirstOrDefault(item => item.Platform == BotPlatform.Telegram)
            ?? view.Identities.FirstOrDefault();
        var markdownLines = new List<string>();
        if (telegramIdentity is not null && !string.IsNullOrWhiteSpace(telegramIdentity.Uid))
        {
            markdownLines.Add($"UID: {MarkdownV2.CodeSpan(telegramIdentity.Uid)}");
        }

        if (telegramIdentity is not null && !string.IsNullOrWhiteSpace(telegramIdentity.Username))
        {
            markdownLines.Add($"用户名: {MarkdownV2.CodeSpan(FormatUsername(telegramIdentity.Username))}");
        }

        if (telegramIdentity is not null && !string.IsNullOrWhiteSpace(telegramIdentity.DisplayName))
        {
            markdownLines.Add($"昵称: {MarkdownV2.CodeSpan(telegramIdentity.DisplayName)}");
        }

        markdownLines.Add($"权限: {MarkdownV2.CodeSpan(UserPrivilegeNames.Format(view.Privilege))}");
        return CommandResponses.TelegramMarkdown(context.Identity, string.Join('\n', markdownLines), context.Request.MessageId);
    }

    private static string FormatUsername(string username)
    {
        var normalized = username.Trim();
        return normalized.StartsWith('@') ? normalized : $"@{normalized}";
    }

    private sealed record UserInfoView(UserPrivilege Privilege, IReadOnlyList<IdentityView> Identities);

    private sealed record IdentityView(BotPlatform Platform, string Uid, string DisplayName, string Username);

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
            // uid 不存在：纯数字 uid 允许就地授权（选完权限、apply 时才建档），@用户名无法凭空建档，仍报未找到。
            if (!IsNumericUid(requestedUser))
            {
                return CommandResponses.Error("UserNotFound", $"未找到用户：{requestedUser}", context);
            }

            target = new SetPrivilegeTarget(
                context.Request.Platform,
                requestedUser,
                requestedUser,
                CoreUserId: null,
                CurrentPrivilege: UserPrivilege.User);
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

        // 追加权限选择按钮；QQ 无原生按钮，会在 gRPC 边界由 QqMenuConverter 转成回复序号的编号菜单。
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

            response.AddButtonRow(buttonRow);
        }

        return response;
    }

    private static (CoreUser RetainedUser, CoreUser MergedUser) SelectMergeDirection(CoreUser firstUser, CoreUser secondUser)
    {
        return firstUser.Id.CompareTo(secondUser.Id) <= 0
            ? (firstUser, secondUser)
            : (secondUser, firstUser);
    }

    private static string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // 纯数字 uid（QQ/Telegram 的平台 uid 形态）；@用户名等非数字 token 不允许就地建档。
    private static bool IsNumericUid(string value)
    {
        return value.Length > 0 && value.All(char.IsDigit);
    }

    private static IdentityView ToIdentityView(PlatformUserProfile profile)
    {
        return new IdentityView(
            profile.Platform,
            profile.Uid,
            FirstNonEmpty(FormatProfileDisplayName(profile), profile.Uid),
            profile.Username ?? string.Empty);
    }

    private static async Task<UserInfoView?> BuildUserInfoViewAsync(
        CoreDbContext dbContext,
        PlatformUserProfile? profile,
        CancellationToken cancellationToken)
    {
        if (profile is null)
        {
            return null;
        }

        CoreUser? coreUser = profile.CoreUserId is null
            ? null
            : await LoadUserAsync(dbContext, profile.CoreUserId.Value, cancellationToken);

        return new UserInfoView(
            coreUser?.Privilege ?? UserPrivilege.User,
            [ToIdentityView(profile)]);
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
        CoreDbContext dbContext,
        long coreUserId,
        CancellationToken cancellationToken)
    {
        return dbContext.CoreUsers
            .AsNoTracking()
            .Include(user => user.PlatformProfiles)
            .FirstOrDefaultAsync(user => user.Id == coreUserId, cancellationToken);
    }

    private static Task<PlatformUserProfile?> FindProfileAsync(
        CoreDbContext dbContext,
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
