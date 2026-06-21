using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commands;
using OhMyBot.Core.Notifications;

namespace OhMyBot.Core.AiRouter;

public sealed class AiRouterCommandDslProvider(IServiceScopeFactory scopeFactory) : IPlatformCommandDslProvider
{
    public IEnumerable<CommandDslNode> GetNodes()
    {
        return
        [
            new CommandDslNode
            {
                Name = "ai",
                Description = "AI 相关指令",
                Usage = "/ai <分类> <命令> [参数]",
                Children =
                [
                    new CommandDslNode
                    {
                        Name = "router",
                        Description = "Router 平台相关指令",
                        Usage = "/ai router <命令> [参数]",
                        Children =
                        [
                            new CommandDslNode
                            {
                                Name = "bind",
                                Description = "绑定用户",
                                Usage = "/ai router bind <email> <password>",
                                RequiredPrivilege = UserPrivilege.VerifiedUser,
                                SupportPlatforms = SupportedPlatforms.Telegram,
                                SupportChatTypes = SupportedChatTypes.Private,
                                Handler = BindAsync
                            },
                            new CommandDslNode
                            {
                                Name = "list",
                                Description = "查看绑定账号",
                                Usage = "/ai router list",
                                RequiredPrivilege = UserPrivilege.VerifiedUser,
                                SupportPlatforms = SupportedPlatforms.Telegram,
                                SupportChatTypes = SupportedChatTypes.Private,
                                Handler = ListAsync
                            },
                            new CommandDslNode
                            {
                                Name = "signin",
                                Description = "手动签到",
                                Usage = "/ai router signin",
                                RequiredPrivilege = UserPrivilege.VerifiedUser,
                                SupportPlatforms = SupportedPlatforms.Telegram,
                                SupportChatTypes = SupportedChatTypes.Private,
                                Handler = SignInAsync
                            },
                            new CommandDslNode
                            {
                                Name = "autosign",
                                Description = "自动签到管理",
                                Usage = "/ai router autosign",
                                RequiredPrivilege = UserPrivilege.VerifiedUser,
                                SupportPlatforms = SupportedPlatforms.Telegram,
                                SupportChatTypes = SupportedChatTypes.Private,
                                Handler = AutoSignAsync
                            },
                            new CommandDslNode
                            {
                                Name = "delete",
                                Description = "删除绑定",
                                Usage = "/ai router delete",
                                RequiredPrivilege = UserPrivilege.VerifiedUser,
                                SupportPlatforms = SupportedPlatforms.Telegram,
                                SupportChatTypes = SupportedChatTypes.Private,
                                Handler = DeleteAsync
                            }
                        ]
                    }
                ]
            }
        ];
    }

    private async Task<CommandResponse> BindAsync(CommandContext context)
    {
        if (context.Request.Args.Count < 2)
        {
            return CommandResponses.Error("Usage", "用法：/ai router bind <email> <password>", context);
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AiRouterAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<AiRouterResponseBuilder>();
        var subscriptionService = scope.ServiceProvider.GetRequiredService<NotificationSubscriptionService>();
        var result = await service.BindAsync(
            context.Identity.CoreUserId,
            context.Request.Args[0],
            string.Join(' ', context.Request.Args.Skip(1)),
            context.CancellationToken);
        await subscriptionService.EnableAsync(
            context.Identity.CoreUserId,
            context.Request.Platform,
            context.Request.BotInstanceId,
            context.Request.ChatId,
            NotificationTypes.AiRouterAutoSign,
            result.Id,
            context.CancellationToken);
        return builder.BuildBindResult(context, result);
    }

    private async Task<CommandResponse> ListAsync(CommandContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AiRouterAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<AiRouterResponseBuilder>();
        var accounts = await service.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, context.CancellationToken);
        return await builder.BuildAccountListAsync(context, accounts, context.CancellationToken);
    }

    private async Task<CommandResponse> SignInAsync(CommandContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<AiRouterAccountService>();
        var signService = scope.ServiceProvider.GetRequiredService<AiRouterSignService>();
        var builder = scope.ServiceProvider.GetRequiredService<AiRouterResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, context.CancellationToken);
        if (accounts.Count == 0)
        {
            return CommandResponses.Error("AiRouterAccountMissing", "请先使用 /ai router bind <email> <password> 绑定 AI Router 账号", context);
        }

        if (accounts.Count > 1)
        {
            return await builder.BuildSignSelectionAsync(context, accounts, context.CancellationToken);
        }

        var result = await signService.SignInAsync(accounts[0], context.CancellationToken);
        return builder.BuildSignResult(context, result);
    }

    private async Task<CommandResponse> AutoSignAsync(CommandContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AiRouterAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<AiRouterResponseBuilder>();
        var accounts = await service.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, context.CancellationToken);
        if (accounts.Count == 0)
        {
            return CommandResponses.Error("AiRouterAccountMissing", "请先使用 /ai router bind <email> <password> 绑定 AI Router 账号", context);
        }

        return await builder.BuildAutoSignPanelAsync(context, accounts, cancellationToken: context.CancellationToken);
    }

    private async Task<CommandResponse> DeleteAsync(CommandContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AiRouterAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<AiRouterResponseBuilder>();
        var accounts = await service.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, context.CancellationToken);
        if (accounts.Count == 0)
        {
            return CommandResponses.Error("AiRouterAccountMissing", "尚未绑定 AI Router 账号", context);
        }

        return await builder.BuildDeletePanelAsync(context, accounts, context.CancellationToken);
    }
}
