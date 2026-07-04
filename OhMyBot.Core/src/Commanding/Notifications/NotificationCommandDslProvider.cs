using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Integrations.AiRouter;
using OhMyBot.Core.Commanding.Callbacks;
using OhMyBot.Core.Commanding.Commands;
using OhMyBot.Core.Commanding.Presentation;
using OhMyBot.Core.Integrations.Kuro;
using OhMyBot.Core.Integrations.Mihoyo;

namespace OhMyBot.Core.Commanding.Notifications;

public sealed class NotificationCommandDslProvider(IServiceScopeFactory scopeFactory) : IPlatformCommandDslProvider
{
    public IEnumerable<CommandDslNode> GetNodes()
    {
        return
        [
            new CommandDslNode
            {
                Name = "notify",
                Description = "管理消息订阅",
                Usage = "/notify",
                RequiredPrivilege = UserPrivilege.VerifiedUser,
                SupportPlatforms = SupportedPlatforms.All,
                SupportChatTypes = SupportedChatTypes.Private,
                Handler = NotifyAsync
            }
        ];
    }

    private async Task<CommandResponse> NotifyAsync(CommandContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var aiAccountService = scope.ServiceProvider.GetRequiredService<AiRouterAccountService>();
        var kuroAccountService = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
        var mihoyoAccountService = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
        var callbackStore = scope.ServiceProvider.GetRequiredService<CallbackActionStore>();
        var subscriptionService = scope.ServiceProvider.GetRequiredService<NotificationSubscriptionService>();
        var aiAccounts = await aiAccountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, context.CancellationToken);
        var kuroAccounts = await kuroAccountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, context.CancellationToken);
        var mihoyoAccounts = await mihoyoAccountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, context.CancellationToken);
        var aiEnabled = await subscriptionService.GetEnabledTargetIdsAsync(
            context.Identity.CoreUserId,
            context.Request.Platform,
            NotificationTypes.AiRouterAutoSign,
            aiAccounts.Select(account => account.Id).ToArray(),
            context.CancellationToken);
        var kuroEnabled = await subscriptionService.GetEnabledTargetIdsAsync(
            context.Identity.CoreUserId,
            context.Request.Platform,
            NotificationTypes.KuroAutoSign,
            kuroAccounts.Select(account => account.Id).ToArray(),
            context.CancellationToken);
        var mihoyoEnabled = await subscriptionService.GetEnabledTargetIdsAsync(
            context.Identity.CoreUserId,
            context.Request.Platform,
            NotificationTypes.MihoyoAutoSign,
            mihoyoAccounts.Select(account => account.Id).ToArray(),
            context.CancellationToken);

        var items = new (string Type, string DisplayName, bool Enabled)[]
        {
            (NotificationTypes.AiRouterAutoSign, NotificationTypes.AiRouterAutoSignDisplayName, aiEnabled.Count > 0),
            (NotificationTypes.KuroAutoSign, NotificationTypes.KuroAutoSignDisplayName, kuroEnabled.Count > 0),
            (NotificationTypes.MihoyoAutoSign, NotificationTypes.MihoyoAutoSignDisplayName, mihoyoEnabled.Count > 0)
        };
        var enabledNames = items.Where(item => item.Enabled).Select(item => item.DisplayName).ToArray();
        var text = MarkdownV2.Escape("[消息订阅管理]") + "\n当前已启用：" +
            (enabledNames.Length == 0
                ? "无"
                : string.Join(MarkdownV2.Escape("、"), enabledNames.Select(MarkdownV2.CodeSpan)));
        var response = CommandResponses.TelegramMarkdown(context.Identity, text, context.Request.MessageId);

        var row = new ResponseButtonRow();
        foreach (var item in items)
        {
            row.Buttons.Add(new ResponseButton
            {
                Text = item.DisplayName,
                Payload = await callbackStore.PutAsync(
                    "notify-type-select",
                    context.Identity.CoreUserId,
                    context.Request.ChatId,
                    context.Request.UserId,
                    new NotifyTypeCallbackData(item.Type),
                    cancellationToken: context.CancellationToken)
            });

            if (row.Buttons.Count == 2)
            {
                response.AddButtonRow(row);
                row = new ResponseButtonRow();
            }
        }

        if (row.Buttons.Count > 0)
        {
            response.AddButtonRow(row);
        }

        return response;
    }
}
