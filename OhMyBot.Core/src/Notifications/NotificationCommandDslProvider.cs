using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.AiRouter;
using OhMyBot.Core.Callbacks;
using OhMyBot.Core.Commands;
using OhMyBot.Core.Kuro;

namespace OhMyBot.Core.Notifications;

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
                SupportPlatforms = SupportedPlatforms.Telegram,
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
        var callbackStore = scope.ServiceProvider.GetRequiredService<CallbackActionStore>();
        var subscriptionService = scope.ServiceProvider.GetRequiredService<NotificationSubscriptionService>();
        var aiAccounts = await aiAccountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, context.CancellationToken);
        var kuroAccounts = await kuroAccountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, context.CancellationToken);
        var response = CommandResponses.Ok(CommandResponseDataKind.NotifyTypePanel, context);
        response.NotifyTypePanel = new NotifyTypePanelData();

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

        response.NotifyTypePanel.Items.Add(new NotifyTypeItem
        {
            Type = NotificationTypes.AiRouterAutoSign,
            DisplayName = NotificationTypes.AiRouterAutoSignDisplayName,
            Enabled = aiEnabled.Count > 0
        });
        response.NotifyTypePanel.Items.Add(new NotifyTypeItem
        {
            Type = NotificationTypes.KuroAutoSign,
            DisplayName = NotificationTypes.KuroAutoSignDisplayName,
            Enabled = kuroEnabled.Count > 0
        });
        var enabledNames = response.NotifyTypePanel.Items.Where(item => item.Enabled).Select(item => item.DisplayName).ToArray();
        response.Message = "[消息订阅管理]\n当前已启用: " + (enabledNames.Length == 0 ? "无" : string.Join("、", enabledNames));

        foreach (var item in response.NotifyTypePanel.Items)
        {
            response.ButtonRows.Add(new ResponseButtonRow
            {
                Buttons =
                {
                    new ResponseButton
                    {
                        Text = item.DisplayName,
                        Payload = await callbackStore.PutAsync(
                            "notify-type-select",
                            context.Identity.CoreUserId,
                            context.Request.ChatId,
                            context.Request.UserId,
                            new NotifyTypeCallbackData(item.Type),
                            cancellationToken: context.CancellationToken)
                    }
                }
            });
        }

        return response;
    }
}
