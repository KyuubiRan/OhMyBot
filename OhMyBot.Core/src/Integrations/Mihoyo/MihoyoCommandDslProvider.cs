using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commanding.Commands;
using OhMyBot.Core.Infrastructure.Data.Entities;
using OhMyBot.Core.Commanding.Notifications;

namespace OhMyBot.Core.Integrations.Mihoyo;

public sealed class MihoyoCommandDslProvider(IServiceScopeFactory scopeFactory) : IPlatformCommandDslProvider
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
                Name = "mihoyo",
                Description = "米游社相关指令",
                Usage = "/mihoyo <命令> [参数]",
                RequiredPrivilege = UserPrivilege.VerifiedUser,
                SupportPlatforms = SupportedPlatforms.All,
                SupportChatTypes = SupportedChatTypes.Private,
                Children =
                [
                    Node("bind", "绑定米游社/HoYoLAB 账号", "/mihoyo bind <cookie>", BindAsync),
                    Node("list", "查看米游社账号", "/mihoyo list", ListAsync),
                    Node("signin", "执行米游社任务（仅国服）", "/mihoyo signin [accountId] [signin|view|like|share ...]", BbsSignAsync),
                    new CommandDslNode
                    {
                        Name = "game",
                        Description = "米游社游戏签到",
                        Usage = "/mihoyo game <init|signin> [参数]",
                        RequiredPrivilege = UserPrivilege.VerifiedUser,
                        SupportPlatforms = SupportedPlatforms.All,
                        SupportChatTypes = SupportedChatTypes.Private,
                        Children =
                        [
                            Node("init", "同步米游社游戏角色", "/mihoyo game init [accountId]", GameInitAsync),
                            Node("signin", "执行米游社游戏签到", "/mihoyo game signin [accountId] [genshin|sr|zzz|honkai3|themis|honkai2|all]", GameSignAsync)
                        ]
                    },
                    Node("autosign", "米游社自动签到管理", "/mihoyo autosign", AutoSignAsync),
                    Node("delete", "删除米游社绑定", "/mihoyo delete", DeleteAsync)
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

    private static readonly Dictionary<string, MihoyoRegion> RegionKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cn"] = MihoyoRegion.Cn,
        ["国服"] = MihoyoRegion.Cn,
        ["mihoyo"] = MihoyoRegion.Cn,
        ["miyoushe"] = MihoyoRegion.Cn,
        ["米游社"] = MihoyoRegion.Cn,
        ["os"] = MihoyoRegion.Os,
        ["国际服"] = MihoyoRegion.Os,
        ["hoyolab"] = MihoyoRegion.Os,
        ["global"] = MihoyoRegion.Os
    };

    private static readonly string BindUsage = string.Join('\n',
        "用法：/mihoyo bind [cn|os] <cookie>（默认国服 cn；国际服请加 os）",
        string.Empty,
        "一键获取 Cookie：",
        "1. 浏览器登录对应网站（登录后保持在该页面）",
        "   · 国服：https://user.miyoushe.com",
        "   · 国际服：https://www.hoyolab.com",
        "2. 按 F12 打开开发者工具，切到 Console（控制台）",
        "3. 粘贴下面这行并回车，Cookie 会自动复制到剪贴板：",
        "`copy(document.cookie)`",
        "4. 回到这里发送 /mihoyo bind 后面粘贴刚复制的 Cookie（国际服写成 /mihoyo bind os 粘贴）",
        string.Empty,
        "注：国服游戏签到只需 cookie_token；若 Cookie 中含 stoken，则可自动续期并执行米游社任务。国际服需含 ltoken。");

    private async Task<CommandResponse> BindAsync(CommandContext context)
    {
        var args = context.Request.Args;
        if (args.Count < 1)
        {
            return CommandResponses.Text(BindUsage, context);
        }

        var region = MihoyoRegion.Cn;
        var cookieStart = 0;
        if (RegionKeywords.TryGetValue(args[0], out var parsedRegion))
        {
            region = parsedRegion;
            cookieStart = 1;
        }

        if (args.Count <= cookieStart)
        {
            return CommandResponses.Text(BindUsage, context);
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<MihoyoResponseBuilder>();
        var subscriptionService = scope.ServiceProvider.GetRequiredService<NotificationSubscriptionService>();
        // Cookie 可能含空格，合并剩余参数
        var cookie = string.Join(' ', args.Skip(cookieStart));
        var result = await service.BindAsync(context.Identity.CoreUserId, cookie, region, context.CancellationToken);
        await subscriptionService.EnableAsync(
            context.Identity.CoreUserId,
            context.Request.Platform,
            context.Request.BotInstanceId,
            context.Request.ChatId,
            NotificationTypes.MihoyoAutoSign,
            result.Account.Id,
            context.CancellationToken);
        return builder.BuildBindResult(context, result);
    }

    private async Task<CommandResponse> ListAsync(CommandContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<MihoyoResponseBuilder>();
        var accounts = await service.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, context.CancellationToken);
        return await builder.BuildAccountListAsync(context, accounts, context.CancellationToken);
    }

    private async Task<CommandResponse> BbsSignAsync(CommandContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
        var signService = scope.ServiceProvider.GetRequiredService<MihoyoSignService>();
        var builder = scope.ServiceProvider.GetRequiredService<MihoyoResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, context.CancellationToken);
        if (accounts.Count == 0)
        {
            return CommandResponses.Error("MihoyoAccountMissing", "请先使用 /mihoyo bind <cookie> 绑定米游社账号", context);
        }

        var args = context.Request.Args.ToList();
        var account = ResolveAccount(args, accounts);
        if (args.Any(arg => !BbsActionKeys.Contains(arg)))
        {
            return CommandResponses.Error("MihoyoInvalidAction", "社区任务类型可选：signin、view、like、share", context);
        }

        var actions = args.Where(arg => BbsActionKeys.Contains(arg)).ToArray();
        if (account is null)
        {
            return await builder.BuildBbsSignSelectionAsync(context, accounts, actions, cancellationToken: context.CancellationToken);
        }

        if (account.Region == MihoyoRegion.Os)
        {
            return CommandResponses.Error("MihoyoOsNoBbs", "国际服(HoYoLAB) 没有米游社任务", context);
        }

        var result = await signService.ExecuteBbsSignAsync(
            account,
            taskFlags: 0,
            requestedActions: actions.ToHashSet(StringComparer.OrdinalIgnoreCase),
            runAllWhenNoRequestedActions: true,
            cancellationToken: context.CancellationToken);
        return builder.BuildBbsSignResult(context, result);
    }

    private async Task<CommandResponse> GameInitAsync(CommandContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<MihoyoResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, context.CancellationToken);
        if (accounts.Count == 0)
        {
            return CommandResponses.Error("MihoyoAccountMissing", "请先使用 /mihoyo bind <cookie> 绑定米游社账号", context);
        }

        var args = context.Request.Args.ToList();
        var account = ResolveAccount(args, accounts);
        if (args.Count > 0)
        {
            return CommandResponses.Error("Usage", "用法：/mihoyo game init [accountId]", context);
        }

        if (account is null)
        {
            return CommandResponses.Text("请指定账号 ID，例如：/mihoyo game init " + accounts[0].Id, context);
        }

        var updated = await accountService.RefreshRolesAsync(context.Identity.CoreUserId, account.Id, context.CancellationToken);
        return await builder.BuildAccountListAsync(context, [updated], context.CancellationToken);
    }

    private async Task<CommandResponse> GameSignAsync(CommandContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
        var signService = scope.ServiceProvider.GetRequiredService<MihoyoSignService>();
        var builder = scope.ServiceProvider.GetRequiredService<MihoyoResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, context.CancellationToken);
        if (accounts.Count == 0)
        {
            return CommandResponses.Error("MihoyoAccountMissing", "请先使用 /mihoyo bind <cookie> 绑定米游社账号", context);
        }

        var args = context.Request.Args.ToList();
        var account = ResolveAccount(args, accounts);
        var gameKeys = new List<string>();
        foreach (var arg in args)
        {
            if (string.Equals(arg, "all", StringComparison.OrdinalIgnoreCase))
            {
                gameKeys.Clear();
                continue;
            }

            if (!MihoyoGameCatalog.TryParse(arg, out var game))
            {
                return CommandResponses.Error("MihoyoInvalidGame", "游戏类型可选：genshin、sr、zzz、honkai3、themis、honkai2、all", context);
            }

            gameKeys.Add(game.Key);
        }

        if (account is null)
        {
            return await builder.BuildGameSignSelectionAsync(context, accounts, cancellationToken: context.CancellationToken);
        }

        // 显式指定了游戏时直接签到；否则弹出勾选面板由用户选择。
        if (gameKeys.Count == 0)
        {
            return await builder.BuildGameSignPanelAsync(
                context, account, MihoyoResponseBuilder.ResolveGameSignSelection(account), cancellationToken: context.CancellationToken);
        }

        var result = await signService.ExecuteGameSignAsync(
            account,
            gameKeys,
            includeMissingConfigMessage: true,
            cancellationToken: context.CancellationToken);
        return builder.BuildGameSignResult(context, result);
    }

    private async Task<CommandResponse> AutoSignAsync(CommandContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<MihoyoResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, cancellationToken: context.CancellationToken);
        if (accounts.Count == 0)
        {
            return CommandResponses.Error("MihoyoAccountMissing", "请先使用 /mihoyo bind <cookie> 绑定米游社账号", context);
        }

        return await builder.BuildAutoSignPanelAsync(context, accounts, cancellationToken: context.CancellationToken);
    }

    private async Task<CommandResponse> DeleteAsync(CommandContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<MihoyoResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, context.CancellationToken);
        if (accounts.Count == 0)
        {
            return CommandResponses.Error("MihoyoAccountMissing", "尚未绑定米游社账号", context);
        }

        return await builder.BuildDeletePanelAsync(context, accounts, context.CancellationToken);
    }

    private static MihoyoAccount? ResolveAccount(List<string> args, IReadOnlyList<MihoyoAccount> accounts)
    {
        if (args.Count > 0 && long.TryParse(args[0], out var accountId))
        {
            args.RemoveAt(0);
            var account = accounts.FirstOrDefault(item => item.Id == accountId);
            if (account is null)
            {
                throw new InvalidOperationException("未找到指定米游社账号");
            }

            return account;
        }

        return accounts.Count == 1 ? accounts[0] : null;
    }
}
