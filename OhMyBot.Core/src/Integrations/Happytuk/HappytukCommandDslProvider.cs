using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commanding.Commands;
using OhMyBot.Core.Commanding.Notifications;
using OhMyBot.Core.Infrastructure.Messaging;

namespace OhMyBot.Core.Integrations.Happytuk;

public sealed class HappytukCommandDslProvider(IServiceScopeFactory scopeFactory) : IPlatformCommandDslProvider
{
    public IEnumerable<CommandDslNode> GetNodes()
    {
        return
        [
            new CommandDslNode
            {
                Name = "happytuk",
                Description = "HappyTuk 兑换码相关指令",
                Usage = "/happytuk <bind|redeem> [参数]",
                RequiredPrivilege = UserPrivilege.VerifiedUser,
                SupportPlatforms = SupportedPlatforms.All,
                SupportChatTypes = SupportedChatTypes.All,
                Children =
                [
                    Node("bind", "绑定 HappyTuk 账号", "/happytuk bind <账号> <密码>", BindAsync),
                    Node("redeem", "手动兑换", "/happytuk redeem <兑换码>", RedeemAsync, SupportedChatTypes.All),
                    Node("delete", "删除绑定账号", "/happytuk delete", DeleteAsync)
                ]
            }
        ];
    }

    private static CommandDslNode Node(
        string name,
        string description,
        string usage,
        CommandDslHandler handler,
        SupportedChatTypes chatTypes = SupportedChatTypes.Private) => new()
    {
        Name = name,
        Description = description,
        Usage = usage,
        RequiredPrivilege = UserPrivilege.VerifiedUser,
        SupportPlatforms = SupportedPlatforms.All,
        SupportChatTypes = chatTypes,
        Handler = handler
    };

    private async Task<CommandResponse> BindAsync(CommandContext context)
    {
        if (context.Request.Args.Count < 2)
        {
            return CommandResponses.Error("Usage", "用法：/happytuk bind <账号> <密码>", context);
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<HappytukAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<HappytukResponseBuilder>();
        var publisher = scope.ServiceProvider.GetRequiredService<INotificationPublisher>();
        var subscriptionService = scope.ServiceProvider.GetRequiredService<NotificationSubscriptionService>();
        var account = await accountService.BindAsync(
            context.Identity.CoreUserId,
            context.Request.Args[0],
            string.Join(' ', context.Request.Args.Skip(1)),
            cancellationToken => publisher.PublishAsync(
                context.Request.Platform,
                context.Request.BotInstanceId,
                context.Request.ChatId,
                ["检测到 Cloudflare 验证，正在启动浏览器处理，请稍候……"],
                cancellationToken),
            context.CancellationToken);
        await subscriptionService.EnableAsync(
            context.Identity.CoreUserId,
            context.Request.Platform,
            context.Request.BotInstanceId,
            context.Request.ChatId,
            NotificationTypes.HappytukAutoRedeem,
            account.Id,
            context.CancellationToken);
        return builder.BuildBindResult(context, account);
    }

    private async Task<CommandResponse> RedeemAsync(CommandContext context)
    {
        if (context.Request.Args.Count != 1)
        {
            return CommandResponses.Error("Usage", "用法：/happytuk redeem <兑换码>", context);
        }

        var couponCode = context.Request.Args[0];
        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<HappytukAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<HappytukResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, context.CancellationToken);
        if (accounts.Count == 0)
        {
            return CommandResponses.Error("HappytukAccountMissing", "请先使用 /happytuk bind <账号> <密码> 绑定账号", context);
        }

        var maskNames = context.Request.ChatType != BotChatType.Private;
        // Some coupons are account-specific, so with more than one account let the user pick which
        // account (or all) via a selection panel; the redeem then runs from that callback.
        if (accounts.Count > 1)
        {
            return await builder.BuildRedeemSelectPanelAsync(context, accounts, couponCode, maskNames, context.CancellationToken);
        }

        var redeemService = scope.ServiceProvider.GetRequiredService<HappytukRedeemService>();
        var results = await HappytukRedeemExecutor.TryRunAsync(
            context.Identity.CoreUserId, redeemService, accounts, couponCode, maskNames, context.CancellationToken);
        return results is null
            ? CommandResponses.Silent(context)
            : builder.BuildRedeemResult(context, couponCode, results);
    }

    private async Task<CommandResponse> DeleteAsync(CommandContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<HappytukAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<HappytukResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, context.CancellationToken);
        if (accounts.Count == 0)
        {
            return CommandResponses.Error("HappytukAccountMissing", "尚未绑定 HappyTuk 账号", context);
        }

        return await builder.BuildDeletePanelAsync(context, accounts, context.CancellationToken);
    }
}
