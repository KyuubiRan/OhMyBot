using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.AiRouter;
using OhMyBot.Core.Commands;

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
        var accountService = scope.ServiceProvider.GetRequiredService<AiRouterAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<AiRouterResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, context.CancellationToken);
        return await builder.BuildNotifyTypePanelAsync(context, accounts, cancellationToken: context.CancellationToken);
    }
}

