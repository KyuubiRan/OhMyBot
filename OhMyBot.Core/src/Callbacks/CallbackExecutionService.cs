using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.AiRouter;
using OhMyBot.Core.Commands;
using OhMyBot.Core.Notifications;

namespace OhMyBot.Core.Callbacks;

public sealed class CallbackExecutionService(
    CoreIdentityService identityService,
    CallbackActionStore actionStore,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider)
{
    public async Task<CommandResponse> ExecuteAsync(CallbackRequest request, CancellationToken cancellationToken = default)
    {
        var identity = await identityService.ResolveIdentityAsync(new CommandRequest
        {
            Platform = request.Platform,
            BotInstanceId = request.BotInstanceId,
            ChatId = request.ChatId,
            UserId = request.UserId,
            MessageId = request.MessageId,
            ChatType = BotChatType.Private
        }, cancellationToken);

        var action = await actionStore.GetAsync(request.Payload, cancellationToken);
        if (action is null)
        {
            return CallbackError(identity, request.MessageId, "按钮已过期，请重新发送命令。");
        }

        if (action.RequireOriginalSender && !string.Equals(action.SenderId, request.UserId, StringComparison.Ordinal))
        {
            return CallbackError(identity, request.MessageId, "这个按钮只能由原发起用户操作。");
        }

        if (action.CoreUserId != identity.CoreUserId)
        {
            return CallbackError(identity, request.MessageId, "当前账号无权操作这个按钮。");
        }

        var context = new CommandContext(new CommandRequest
        {
            Platform = request.Platform,
            BotInstanceId = request.BotInstanceId,
            ChatId = request.ChatId,
            UserId = request.UserId,
            MessageId = request.MessageId,
            ChatType = BotChatType.Private
        }, identity, timeProvider.GetTimestamp(), cancellationToken);

        return action.ActionType switch
        {
            "ai-router-signin-select" => await ExecuteSignInSelectAsync(context, action, request.MessageId, cancellationToken),
            "ai-router-auto-sign-toggle" => await ExecuteAutoSignToggleAsync(context, action, request.MessageId, cancellationToken),
            "ai-router-delete-select" => await ExecuteDeleteSelectAsync(context, action, request.MessageId, cancellationToken),
            "ai-router-delete-confirm" => await ExecuteDeleteConfirmAsync(context, action, request.MessageId, cancellationToken),
            "notify-type-select" => await ExecuteNotifyTypeSelectAsync(context, action, request.MessageId, cancellationToken),
            "notify-account-toggle" => await ExecuteNotifyAccountToggleAsync(context, action, request.MessageId, cancellationToken),
            _ => CallbackError(identity, request.MessageId, "未知按钮操作。")
        };
    }

    private async Task<CommandResponse> ExecuteSignInSelectAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<AiRouterAccountCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<AiRouterAccountService>();
        var signService = scope.ServiceProvider.GetRequiredService<AiRouterSignService>();
        var builder = scope.ServiceProvider.GetRequiredService<AiRouterResponseBuilder>();
        var account = await accountService.FindByIdAsync(data.AccountId, noTracking: true, cancellationToken);
        if (account is null || account.CoreUserId != context.Identity.CoreUserId)
        {
            return CallbackError(context.Identity, editMessageId, "未找到指定 AI Router 账号。");
        }

        var response = builder.BuildSignResult(context, await signService.SignInAsync(account, cancellationToken));
        response.EditMessageId = editMessageId;
        response.ReplyToMessageId = string.Empty;
        return response;
    }

    private async Task<CommandResponse> ExecuteAutoSignToggleAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<AiRouterAutoSignCallbackData>(action);
        if (data is null && CallbackActionStore.ReadData<AiRouterAccountCallbackData>(action) is { } accountData)
        {
            data = new AiRouterAutoSignCallbackData(accountData.AccountId);
        }
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<AiRouterAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<AiRouterResponseBuilder>();
        var accounts = data.ToggleAll
            ? await accountService.ToggleAllAutoSignAsync(context.Identity.CoreUserId, cancellationToken)
            : await accountService.ToggleAutoSignAsync(context.Identity.CoreUserId, data.AccountId, cancellationToken);
        if (accounts.Count == 0)
        {
            return CallbackError(context.Identity, editMessageId, "未找到 AI Router 账号。");
        }

        return await builder.BuildAutoSignPanelAsync(context, accounts, editMessageId, cancellationToken);
    }

    private async Task<CommandResponse> ExecuteDeleteSelectAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<AiRouterAccountCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<AiRouterAccountService>();
        var callbackStore = scope.ServiceProvider.GetRequiredService<CallbackActionStore>();
        var account = await accountService.FindByIdAsync(data.AccountId, noTracking: true, cancellationToken);
        if (account is null || account.CoreUserId != context.Identity.CoreUserId)
        {
            return CallbackError(context.Identity, editMessageId, "未找到指定 AI Router 账号。");
        }

        var response = CommandResponses.Text($"确认删除 AI Router 账号绑定？\n账号：`{account.DisplayName}`", context);
        response.EditMessageId = editMessageId;
        response.ReplyToMessageId = string.Empty;
        response.ButtonRows.Add(new ResponseButtonRow
        {
            Buttons =
            {
                new ResponseButton
                {
                    Text = "确认删除",
                    Payload = await callbackStore.PutAsync(
                        "ai-router-delete-confirm",
                        context.Identity.CoreUserId,
                        context.Request.ChatId,
                        context.Request.UserId,
                        new AiRouterDeleteConfirmCallbackData(account.Id, Confirm: true),
                        cancellationToken: cancellationToken)
                },
                new ResponseButton
                {
                    Text = "取消",
                    Payload = await callbackStore.PutAsync(
                        "ai-router-delete-confirm",
                        context.Identity.CoreUserId,
                        context.Request.ChatId,
                        context.Request.UserId,
                        new AiRouterDeleteConfirmCallbackData(account.Id, Confirm: false),
                        cancellationToken: cancellationToken)
                }
            }
        });
        return response;
    }

    private async Task<CommandResponse> ExecuteDeleteConfirmAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<AiRouterDeleteConfirmCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        if (!data.Confirm)
        {
            var canceled = CommandResponses.Text("删除操作已取消", context);
            canceled.EditMessageId = editMessageId;
            canceled.ReplyToMessageId = string.Empty;
            return canceled;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<AiRouterAccountService>();
        var account = await accountService.FindByIdAsync(data.AccountId, noTracking: true, cancellationToken);
        if (account is null || account.CoreUserId != context.Identity.CoreUserId)
        {
            return CallbackError(context.Identity, editMessageId, "未找到指定 AI Router 账号。");
        }

        var deleted = await accountService.DeleteAsync(context.Identity.CoreUserId, data.AccountId, cancellationToken);
        var response = CommandResponses.Text(deleted ? $"已删除 AI Router 账号绑定：`{account.DisplayName}`" : "未找到指定 AI Router 账号", context);
        response.EditMessageId = editMessageId;
        response.ReplyToMessageId = string.Empty;
        return response;
    }

    private async Task<CommandResponse> ExecuteNotifyTypeSelectAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<NotifyTypeCallbackData>(action);
        if (data?.Type != NotificationTypes.AiRouterAutoSign)
        {
            return CallbackError(context.Identity, editMessageId, "未知订阅类型。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<AiRouterAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<AiRouterResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, cancellationToken);
        return await builder.BuildNotifyAccountPanelAsync(context, accounts, editMessageId, cancellationToken);
    }

    private async Task<CommandResponse> ExecuteNotifyAccountToggleAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<NotifyAccountCallbackData>(action);
        if (data?.Type != NotificationTypes.AiRouterAutoSign)
        {
            return CallbackError(context.Identity, editMessageId, "未知订阅类型。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<AiRouterAccountService>();
        var subscriptionService = scope.ServiceProvider.GetRequiredService<NotificationSubscriptionService>();
        var builder = scope.ServiceProvider.GetRequiredService<AiRouterResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, cancellationToken: cancellationToken);
        if (data.ToggleAll)
        {
            await subscriptionService.ToggleAllAsync(
                context.Identity.CoreUserId,
                context.Request.Platform,
                context.Request.BotInstanceId,
                context.Request.ChatId,
                NotificationTypes.AiRouterAutoSign,
                accounts.Select(account => account.Id).ToArray(),
                cancellationToken);
        }
        else if (accounts.Any(account => account.Id == data.AccountId))
        {
            await subscriptionService.ToggleAsync(
                context.Identity.CoreUserId,
                context.Request.Platform,
                context.Request.BotInstanceId,
                context.Request.ChatId,
                NotificationTypes.AiRouterAutoSign,
                data.AccountId,
                cancellationToken);
        }
        else
        {
            return CallbackError(context.Identity, editMessageId, "未找到指定 AI Router 账号。");
        }

        var updatedAccounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, cancellationToken);
        return await builder.BuildNotifyAccountPanelAsync(context, updatedAccounts, editMessageId, cancellationToken);
    }

    private static CommandResponse CallbackError(Identity.ResolvedIdentity identity, string editMessageId, string message)
    {
        return new CommandResponse
        {
            Code = 1,
            ErrorCode = "CallbackRejected",
            Message = message,
            CallbackAnswerText = message,
            CallbackAnswerAlert = false,
            EditMessageId = editMessageId,
            Context = new CommandResponseContext
            {
                CallerCoreUserId = identity.CoreUserId,
                CallerPrivilege = identity.Privilege,
                Platform = identity.Platform
            }
        };
    }
}

public sealed record AiRouterDeleteConfirmCallbackData(long AccountId, bool Confirm);
