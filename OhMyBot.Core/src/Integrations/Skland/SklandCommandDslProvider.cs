using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commanding.Commands;
using OhMyBot.Core.Commanding.Notifications;
using OhMyBot.Core.Infrastructure.Data.Entities;

namespace OhMyBot.Core.Integrations.Skland;

public sealed class SklandCommandDslProvider(IServiceScopeFactory scopeFactory) : IPlatformCommandDslProvider
{
    public IEnumerable<CommandDslNode> GetNodes()
    {
        return
        [
            new CommandDslNode
            {
                Name = "skland",
                Description = "森空岛相关指令",
                Usage = "/skland <命令> [参数]",
                RequiredPrivilege = UserPrivilege.VerifiedUser,
                SupportPlatforms = SupportedPlatforms.All,
                SupportChatTypes = SupportedChatTypes.Private,
                Children =
                [
                    Node("bind", "绑定森空岛账号（使用鹰角网络 OAuth Token）", "/skland bind <token>", BindAsync),
                    Node("list", "查看绑定的森空岛账号与角色", "/skland list", ListAsync),
                    new CommandDslNode
                    {
                        Name = "game",
                        Description = "森空岛游戏签到",
                        Usage = "/skland game <init|signin> [参数]",
                        RequiredPrivilege = UserPrivilege.VerifiedUser,
                        SupportPlatforms = SupportedPlatforms.All,
                        SupportChatTypes = SupportedChatTypes.Private,
                        Children =
                        [
                            Node("init", "同步游戏角色", "/skland game init [accountId]", GameInitAsync),
                            Node("signin", "执行游戏签到", "/skland game signin [accountId] [arknights|endfield|all]", GameSignAsync)
                        ]
                    },
                    Node("autosign", "自动签到管理", "/skland autosign", AutoSignAsync),
                    Node("delete", "删除绑定", "/skland delete", DeleteAsync)
                ]
            }
        ];
    }

    private static CommandDslNode Node(string name, string description, string usage, CommandDslHandler handler)
    {
        return new CommandDslNode
        {
            Name = name,
            Description = description,
            Usage = usage,
            RequiredPrivilege = UserPrivilege.VerifiedUser,
            SupportPlatforms = SupportedPlatforms.All,
            SupportChatTypes = SupportedChatTypes.Private,
            Handler = handler
        };
    }

    // ---- Handlers ----

    private static readonly string BindUsage = string.Join('\n',
        "用法：/skland bind <鹰角Token>",
        string.Empty,
        "一键获取 Token：",
        "1. 浏览器登录 鹰角网络通行证（登录后保持在该页面）",
        "   · https://user.hypergryph.com/login",
        "2. 在同一浏览器新标签页打开：",
        "   · https://web-api.hypergryph.com/account/info/hg",
        "3. 复制页面中 `content` 字段的值（一长串字符）",
        "4. 回到这里发送 /skland bind 后面粘贴刚复制的 Token",
        string.Empty,
        "注：也可登录森空岛网页版 https://www.skland.com/ 后打开 https://web-api.skland.com/account/info/hg 获取 `content`。");

    private async Task<CommandResponse> BindAsync(CommandContext context)
    {
        if (context.Request.Args.Count < 1)
        {
            return CommandResponses.Text(BindUsage, context);
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<SklandAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<SklandResponseBuilder>();
        var subscriptionService = scope.ServiceProvider.GetRequiredService<NotificationSubscriptionService>();

        var token = context.Request.Args[0];
        var result = await service.BindAsync(context.Identity.CoreUserId, token, context.CancellationToken);
        await subscriptionService.EnableAsync(
            context.Identity.CoreUserId,
            context.Request.Platform,
            context.Request.BotInstanceId,
            context.Request.ChatId,
            NotificationTypes.SklandAutoSign,
            result.Account.Id,
            context.CancellationToken);
        return builder.BuildBindResult(context, result);
    }

    private async Task<CommandResponse> ListAsync(CommandContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<SklandAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<SklandResponseBuilder>();
        var accounts = await service.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, context.CancellationToken);
        return await builder.BuildAccountListAsync(context, accounts, context.CancellationToken);
    }

    private async Task<CommandResponse> GameInitAsync(CommandContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<SklandAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<SklandResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, context.CancellationToken);
        if (accounts.Count == 0)
        {
            return CommandResponses.Error("SklandAccountMissing", "请先使用 /skland bind <token> 绑定森空岛账号", context);
        }

        var args = context.Request.Args.ToList();
        var account = ResolveAccount(args, accounts);
        if (args.Count > 0)
        {
            return CommandResponses.Error("Usage", "用法：/skland game init [accountId]", context);
        }

        if (account is null)
        {
            return CommandResponses.Text("请指定账号 ID，例如：/skland game init " + accounts[0].Id, context);
        }

        var updated = await accountService.RefreshRolesAsync(context.Identity.CoreUserId, account.Id, context.CancellationToken);
        return await builder.BuildAccountListAsync(context, [updated], context.CancellationToken);
    }

    private async Task<CommandResponse> GameSignAsync(CommandContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<SklandAccountService>();
        var signService = scope.ServiceProvider.GetRequiredService<SklandSignService>();
        var builder = scope.ServiceProvider.GetRequiredService<SklandResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, context.CancellationToken);
        if (accounts.Count == 0)
        {
            return CommandResponses.Error("SklandAccountMissing", "请先使用 /skland bind <token> 绑定森空岛账号", context);
        }

        var args = context.Request.Args.ToList();
        var account = ResolveAccount(args, accounts);

        // 若剩余参数指定了游戏类型，收集 gameId 过滤
        var gameIds = new List<int>();
        foreach (var arg in args)
        {
            if (string.Equals(arg, "all", StringComparison.OrdinalIgnoreCase))
            {
                gameIds.Clear();
                continue;
            }

            if (!SklandGameNames.TryParse(arg, out var gameId))
            {
                return CommandResponses.Error("SklandInvalidGame", "游戏类型可选：arknights、endfield、all", context);
            }

            gameIds.Add(gameId);
        }

        if (account is null)
        {
            return await builder.BuildGameSignSelectionAsync(context, accounts, cancellationToken: context.CancellationToken);
        }

        // 显式指定了游戏时直接签到；否则弹出勾选面板由用户选择。
        if (gameIds.Count == 0)
        {
            return await builder.BuildGameSignPanelAsync(
                context, account, SklandResponseBuilder.ResolveGameSignSelection(account), cancellationToken: context.CancellationToken);
        }

        // 按 gameId 过滤角色
        var roleIds = account.Roles
            .Where(r => gameIds.Contains(r.GameId))
            .Select(r => r.Id)
            .ToArray();
        if (roleIds.Length == 0)
        {
            return CommandResponses.Error("SklandRoleMissing", "未找到对应游戏角色，请先使用 /skland game init 同步", context);
        }

        var signResult = await signService.ExecuteGameSignAsync(account, roleIds, includeMissingConfigMessage: true, cancellationToken: context.CancellationToken);
        return builder.BuildGameSignResult(context, signResult);
    }

    private async Task<CommandResponse> AutoSignAsync(CommandContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<SklandAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<SklandResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, cancellationToken: context.CancellationToken);
        if (accounts.Count == 0)
        {
            return CommandResponses.Error("SklandAccountMissing", "请先使用 /skland bind <token> 绑定森空岛账号", context);
        }

        return await builder.BuildAutoSignPanelAsync(context, accounts, cancellationToken: context.CancellationToken);
    }

    private async Task<CommandResponse> DeleteAsync(CommandContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<SklandAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<SklandResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, context.CancellationToken);
        if (accounts.Count == 0)
        {
            return CommandResponses.Error("SklandAccountMissing", "尚未绑定森空岛账号", context);
        }

        return await builder.BuildDeletePanelAsync(context, accounts, context.CancellationToken);
    }

    private static SklandAccount? ResolveAccount(List<string> args, IReadOnlyList<SklandAccount> accounts)
    {
        if (args.Count > 0 && long.TryParse(args[0], out var accountId))
        {
            args.RemoveAt(0);
            var account = accounts.FirstOrDefault(a => a.Id == accountId);
            if (account is null)
            {
                throw new InvalidOperationException("未找到指定森空岛账号");
            }

            return account;
        }

        return accounts.Count == 1 ? accounts[0] : null;
    }
}
