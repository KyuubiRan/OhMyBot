using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commands;
using OhMyBot.Core.Data.Entities;
using OhMyBot.Core.Notifications;

namespace OhMyBot.Core.Kuro;

public sealed class KuroCommandDslProvider(IServiceScopeFactory scopeFactory) : IPlatformCommandDslProvider
{
    private static readonly HashSet<string> BbsActionKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "signin",
        "view",
        "like",
        "share"
    };

    public IEnumerable<CommandDslNode> GetNodes()
    {
        return
        [
            new CommandDslNode
            {
                Name = "kuro",
                Description = "库街区相关指令",
                Usage = "/kuro <命令> [参数]",
                RequiredPrivilege = UserPrivilege.VerifiedUser,
                SupportPlatforms = SupportedPlatforms.Telegram,
                SupportChatTypes = SupportedChatTypes.Private,
                Children =
                [
                    Node("bind", "绑定库街区账号", "/kuro bind <token> [devCode] [distinctId]", BindAsync),
                    Node("list", "查看库街区账号", "/kuro list", ListAsync),
                    Node("signin", "执行库街区社区签到任务", "/kuro signin [accountId] [signin|view|like|share ...]", BbsSignAsync),
                    new CommandDslNode
                    {
                        Name = "game",
                        Description = "库街区游戏签到",
                        Usage = "/kuro game <init|signin> [参数]",
                        RequiredPrivilege = UserPrivilege.VerifiedUser,
                        SupportPlatforms = SupportedPlatforms.Telegram,
                        SupportChatTypes = SupportedChatTypes.Private,
                        Children =
                        [
                            Node("init", "同步库街区游戏角色", "/kuro game init [accountId]", GameInitAsync),
                            Node("signin", "执行库街区游戏签到", "/kuro game signin [accountId] [wuwa|pgr|all]", GameSignAsync)
                        ]
                    },
                    Node("autosign", "库街区自动签到管理", "/kuro autosign", AutoSignAsync),
                    Node("delete", "删除库街区绑定", "/kuro delete", DeleteAsync)
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
            SupportPlatforms = SupportedPlatforms.Telegram,
            SupportChatTypes = SupportedChatTypes.Private,
            Handler = handler
        };
    }

    private async Task<CommandResponse> BindAsync(CommandContext context)
    {
        if (context.Request.Args.Count < 1)
        {
            return CommandResponses.Error("Usage", "用法：/kuro bind <token> [devCode] [distinctId]", context);
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<KuroResponseBuilder>();
        var subscriptionService = scope.ServiceProvider.GetRequiredService<NotificationSubscriptionService>();
        var token = context.Request.Args[0];
        var devCode = context.Request.Args.Count > 1 ? context.Request.Args[1] : null;
        var distinctId = context.Request.Args.Count > 2 ? context.Request.Args[2] : null;
        var result = await service.BindAsync(context.Identity.CoreUserId, token, devCode, distinctId, context.CancellationToken);
        await subscriptionService.EnableAsync(
            context.Identity.CoreUserId,
            context.Request.Platform,
            context.Request.BotInstanceId,
            context.Request.ChatId,
            NotificationTypes.KuroAutoSign,
            result.Account.Id,
            context.CancellationToken);
        return builder.BuildBindResult(context, result);
    }

    private async Task<CommandResponse> ListAsync(CommandContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<KuroResponseBuilder>();
        var accounts = await service.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, context.CancellationToken);
        return await builder.BuildAccountListAsync(context, accounts, context.CancellationToken);
    }

    private async Task<CommandResponse> BbsSignAsync(CommandContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
        var signService = scope.ServiceProvider.GetRequiredService<KuroSignService>();
        var builder = scope.ServiceProvider.GetRequiredService<KuroResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, context.CancellationToken);
        if (accounts.Count == 0)
        {
            return CommandResponses.Error("KuroAccountMissing", "请先使用 /kuro bind <token> 绑定库街区账号", context);
        }

        var args = context.Request.Args.ToList();
        var account = ResolveAccount(args, accounts);
        var actions = args.Where(arg => BbsActionKeys.Contains(arg)).ToArray();
        if (args.Any(arg => !BbsActionKeys.Contains(arg)))
        {
            return CommandResponses.Error("KuroInvalidAction", "社区任务类型可选：signin、view、like、share", context);
        }

        if (account is null)
        {
            return await builder.BuildBbsSignSelectionAsync(context, accounts, actions, context.CancellationToken);
        }

        var actionSet = actions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = await signService.ExecuteBbsSignAsync(
            account,
            taskFlags: 0,
            requestedActions: actionSet,
            runAllWhenNoRequestedActions: true,
            cancellationToken: context.CancellationToken);
        return builder.BuildBbsSignResult(context, result);
    }

    private async Task<CommandResponse> GameInitAsync(CommandContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<KuroResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, context.CancellationToken);
        if (accounts.Count == 0)
        {
            return CommandResponses.Error("KuroAccountMissing", "请先使用 /kuro bind <token> 绑定库街区账号", context);
        }

        var args = context.Request.Args.ToList();
        var account = ResolveAccount(args, accounts);
        if (args.Count > 0)
        {
            return CommandResponses.Error("Usage", "用法：/kuro game init [accountId]", context);
        }

        if (account is null)
        {
            return CommandResponses.Text("请指定账号 ID，例如：/kuro game init " + accounts[0].Id, context);
        }

        var updated = await accountService.RefreshRolesAsync(context.Identity.CoreUserId, account.Id, context.CancellationToken);
        return await builder.BuildAccountListAsync(context, [updated], context.CancellationToken);
    }

    private async Task<CommandResponse> GameSignAsync(CommandContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
        var signService = scope.ServiceProvider.GetRequiredService<KuroSignService>();
        var builder = scope.ServiceProvider.GetRequiredService<KuroResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, context.CancellationToken);
        if (accounts.Count == 0)
        {
            return CommandResponses.Error("KuroAccountMissing", "请先使用 /kuro bind <token> 绑定库街区账号", context);
        }

        var args = context.Request.Args.ToList();
        var account = ResolveAccount(args, accounts);
        var gameIds = new List<long>();
        foreach (var arg in args)
        {
            if (string.Equals(arg, "all", StringComparison.OrdinalIgnoreCase))
            {
                gameIds.Clear();
                continue;
            }

            if (!KuroGameNames.TryParse(arg, out var gameId))
            {
                return CommandResponses.Error("KuroInvalidGame", "游戏类型可选：wuwa、pgr、all", context);
            }

            gameIds.Add(gameId);
        }

        if (account is null)
        {
            return await builder.BuildGameSignSelectionAsync(context, accounts, gameIds, context.CancellationToken);
        }

        var result = await signService.ExecuteGameSignAsync(
            account,
            gameIds,
            includeMissingConfigMessage: true,
            cancellationToken: context.CancellationToken);
        return builder.BuildGameSignResult(context, result);
    }

    private async Task<CommandResponse> AutoSignAsync(CommandContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<KuroResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, cancellationToken: context.CancellationToken);
        if (accounts.Count == 0)
        {
            return CommandResponses.Error("KuroAccountMissing", "请先使用 /kuro bind <token> 绑定库街区账号", context);
        }

        return await builder.BuildAutoSignPanelAsync(context, accounts, cancellationToken: context.CancellationToken);
    }

    private async Task<CommandResponse> DeleteAsync(CommandContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<KuroResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, context.CancellationToken);
        if (accounts.Count == 0)
        {
            return CommandResponses.Error("KuroAccountMissing", "尚未绑定库街区账号", context);
        }

        return await builder.BuildDeletePanelAsync(context, accounts, context.CancellationToken);
    }

    private static KuroAccount? ResolveAccount(List<string> args, IReadOnlyList<KuroAccount> accounts)
    {
        if (args.Count > 0 && long.TryParse(args[0], out var accountId))
        {
            args.RemoveAt(0);
            var account = accounts.FirstOrDefault(item => item.Id == accountId);
            if (account is null)
            {
                throw new InvalidOperationException("未找到指定库街区账号");
            }

            return account;
        }

        return accounts.Count == 1 ? accounts[0] : null;
    }
}
