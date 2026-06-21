using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.AiRouter;
using OhMyBot.Core.Commands;
using OhMyBot.Core.Kuro;
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
            "kuro-bbs-sign-select" => await ExecuteKuroBbsSignSelectAsync(context, action, request.MessageId, cancellationToken),
            "kuro-game-sign-select" => await ExecuteKuroGameSignSelectAsync(context, action, request.MessageId, cancellationToken),
            "kuro-autosign-root-menu" => await ExecuteKuroAutoSignRootMenuAsync(context, action, request.MessageId, cancellationToken),
            "kuro-autosign-account-menu" => await ExecuteKuroAutoSignAccountMenuAsync(context, action, request.MessageId, cancellationToken),
            "kuro-autosign-bbs-menu" => await ExecuteKuroAutoSignBbsMenuAsync(context, action, request.MessageId, cancellationToken),
            "kuro-autosign-game-menu" => await ExecuteKuroAutoSignGameMenuAsync(context, action, request.MessageId, cancellationToken),
            "kuro-auto-sign-toggle" => await ExecuteKuroAutoSignToggleAsync(context, action, request.MessageId, cancellationToken),
            "kuro-bbs-task-toggle" => await ExecuteKuroBbsTaskToggleAsync(context, action, request.MessageId, cancellationToken),
            "kuro-bbs-task-toggle-all" => await ExecuteKuroBbsTaskToggleAllAsync(context, action, request.MessageId, cancellationToken),
            "kuro-game-auto-sign-toggle" => await ExecuteKuroGameAutoSignToggleAsync(context, action, request.MessageId, cancellationToken),
            "kuro-game-auto-sign-toggle-all" => await ExecuteKuroGameAutoSignToggleAllAsync(context, action, request.MessageId, cancellationToken),
            "kuro-delete-select" => await ExecuteKuroDeleteSelectAsync(context, action, request.MessageId, cancellationToken),
            "kuro-delete-confirm" => await ExecuteKuroDeleteConfirmAsync(context, action, request.MessageId, cancellationToken),
            "notify-type-select" => await ExecuteNotifyTypeSelectAsync(context, action, request.MessageId, cancellationToken),
            "notify-account-toggle" => await ExecuteNotifyAccountToggleAsync(context, action, request.MessageId, cancellationToken),
            "notify-back" => await ExecuteNotifyBackAsync(context, request.MessageId, cancellationToken),
            "setpriv-apply" => await ExecuteSetPrivilegeApplyAsync(context, action, request.MessageId, cancellationToken),
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
        if (data?.Type != NotificationTypes.AiRouterAutoSign && data?.Type != NotificationTypes.KuroAutoSign)
        {
            return CallbackError(context.Identity, editMessageId, "未知订阅类型。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        if (data.Type == NotificationTypes.KuroAutoSign)
        {
            var kuroAccountService = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
            var kuroBuilder = scope.ServiceProvider.GetRequiredService<KuroResponseBuilder>();
            var kuroAccounts = await kuroAccountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, cancellationToken);
            return await kuroBuilder.BuildNotifyAccountPanelAsync(context, kuroAccounts, editMessageId, cancellationToken);
        }

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
        if (data?.Type != NotificationTypes.AiRouterAutoSign && data?.Type != NotificationTypes.KuroAutoSign)
        {
            return CallbackError(context.Identity, editMessageId, "未知订阅类型。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        if (data.Type == NotificationTypes.KuroAutoSign)
        {
            var kuroAccountService = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
            var kuroSubscriptionService = scope.ServiceProvider.GetRequiredService<NotificationSubscriptionService>();
            var kuroBuilder = scope.ServiceProvider.GetRequiredService<KuroResponseBuilder>();
            var kuroAccounts = await kuroAccountService.ListByOwnerAsync(context.Identity.CoreUserId, cancellationToken: cancellationToken);
            if (data.ToggleAll)
            {
                await kuroSubscriptionService.ToggleAllAsync(
                    context.Identity.CoreUserId,
                    context.Request.Platform,
                    context.Request.BotInstanceId,
                    context.Request.ChatId,
                    NotificationTypes.KuroAutoSign,
                    kuroAccounts.Select(account => account.Id).ToArray(),
                    cancellationToken);
            }
            else if (kuroAccounts.Any(account => account.Id == data.AccountId))
            {
                await kuroSubscriptionService.ToggleAsync(
                    context.Identity.CoreUserId,
                    context.Request.Platform,
                    context.Request.BotInstanceId,
                    context.Request.ChatId,
                    NotificationTypes.KuroAutoSign,
                    data.AccountId,
                    cancellationToken);
            }
            else
            {
                return CallbackError(context.Identity, editMessageId, "未找到指定库街区账号。");
            }

            var updatedKuroAccounts = await kuroAccountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, cancellationToken);
            return await kuroBuilder.BuildNotifyAccountPanelAsync(context, updatedKuroAccounts, editMessageId, cancellationToken);
        }

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

    private async Task<CommandResponse> ExecuteKuroBbsSignSelectAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<KuroBbsSignCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
        var signService = scope.ServiceProvider.GetRequiredService<KuroSignService>();
        var builder = scope.ServiceProvider.GetRequiredService<KuroResponseBuilder>();
        var account = await accountService.FindByIdAsync(data.AccountId, noTracking: true, cancellationToken);
        if (account is null || account.CoreUserId != context.Identity.CoreUserId)
        {
            return CallbackError(context.Identity, editMessageId, "未找到指定库街区账号。");
        }

        var result = await signService.ExecuteBbsSignAsync(
            account,
            taskFlags: 0,
            requestedActions: data.Actions.ToHashSet(StringComparer.OrdinalIgnoreCase),
            runAllWhenNoRequestedActions: true,
            cancellationToken: cancellationToken);
        var response = builder.BuildBbsSignResult(context, result);
        response.EditMessageId = editMessageId;
        response.ReplyToMessageId = string.Empty;
        return response;
    }

    private async Task<CommandResponse> ExecuteKuroGameSignSelectAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<KuroGameSignCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
        var signService = scope.ServiceProvider.GetRequiredService<KuroSignService>();
        var builder = scope.ServiceProvider.GetRequiredService<KuroResponseBuilder>();
        var account = await accountService.FindByIdAsync(data.AccountId, noTracking: true, cancellationToken);
        if (account is null || account.CoreUserId != context.Identity.CoreUserId)
        {
            return CallbackError(context.Identity, editMessageId, "未找到指定库街区账号。");
        }

        var response = builder.BuildGameSignResult(context, await signService.ExecuteGameSignAsync(
            account,
            data.GameIds,
            includeMissingConfigMessage: true,
            cancellationToken: cancellationToken));
        response.EditMessageId = editMessageId;
        response.ReplyToMessageId = string.Empty;
        return response;
    }

    private async Task<CommandResponse> ExecuteKuroAutoSignToggleAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<KuroAutoSignCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<KuroResponseBuilder>();
        var accounts = await accountService.ToggleAutoSignAsync(context.Identity.CoreUserId, data.AccountId, cancellationToken);
        if (accounts.Count == 0)
        {
            return CallbackError(context.Identity, editMessageId, "未找到指定库街区账号。");
        }

        return await builder.BuildAutoSignAccountPanelAsync(context, accounts, data.AccountId, editMessageId, cancellationToken);
    }

    private async Task<CommandResponse> ExecuteKuroBbsTaskToggleAllAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<KuroBbsTaskToggleAllCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<KuroResponseBuilder>();
        var accounts = await accountService.ToggleAllBbsTasksAsync(context.Identity.CoreUserId, data.AccountId, cancellationToken);
        if (accounts.Count == 0)
        {
            return CallbackError(context.Identity, editMessageId, "未找到指定库街区账号。");
        }

        return await builder.BuildAutoSignBbsPanelAsync(context, accounts, data.AccountId, editMessageId, cancellationToken);
    }

    private async Task<CommandResponse> ExecuteKuroAutoSignRootMenuAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<KuroAutoSignMenuCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<KuroResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, cancellationToken);
        return await builder.BuildAutoSignPanelAsync(context, accounts, editMessageId, cancellationToken, data.Page);
    }

    private async Task<CommandResponse> ExecuteKuroAutoSignAccountMenuAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<KuroAutoSignMenuCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<KuroResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, cancellationToken);
        return await builder.BuildAutoSignAccountPanelAsync(context, accounts, data.AccountId, editMessageId, cancellationToken);
    }

    private async Task<CommandResponse> ExecuteKuroAutoSignBbsMenuAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<KuroAutoSignMenuCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<KuroResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, cancellationToken);
        return await builder.BuildAutoSignBbsPanelAsync(context, accounts, data.AccountId, editMessageId, cancellationToken);
    }

    private async Task<CommandResponse> ExecuteKuroAutoSignGameMenuAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<KuroAutoSignMenuCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<KuroResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, cancellationToken);
        return await builder.BuildAutoSignGamePanelAsync(context, accounts, data.AccountId, editMessageId, cancellationToken, data.Page);
    }

    private async Task<CommandResponse> ExecuteKuroBbsTaskToggleAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<KuroBbsTaskCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<KuroResponseBuilder>();
        var accounts = await accountService.ToggleBbsTaskAsync(context.Identity.CoreUserId, data.AccountId, data.TaskFlag, cancellationToken);
        if (accounts.Count == 0)
        {
            return CallbackError(context.Identity, editMessageId, "未找到指定库街区账号。");
        }

        return await builder.BuildAutoSignBbsPanelAsync(context, accounts, data.AccountId, editMessageId, cancellationToken);
    }

    private async Task<CommandResponse> ExecuteKuroGameAutoSignToggleAllAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<KuroGameAutoSignToggleAllCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<KuroResponseBuilder>();
        var accounts = await accountService.ToggleAllGameAutoSignAsync(context.Identity.CoreUserId, data.AccountId, cancellationToken);
        if (accounts.Count == 0)
        {
            return CallbackError(context.Identity, editMessageId, "未找到指定库街区账号。");
        }

        return await builder.BuildAutoSignGamePanelAsync(context, accounts, data.AccountId, editMessageId, cancellationToken, data.Page);
    }

    private async Task<CommandResponse> ExecuteKuroGameAutoSignToggleAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<KuroGameAutoSignCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<KuroResponseBuilder>();
        var accounts = await accountService.ToggleGameAutoSignAsync(context.Identity.CoreUserId, data.RoleId, cancellationToken);
        if (accounts.Count == 0)
        {
            return CallbackError(context.Identity, editMessageId, "未找到指定库街区角色。");
        }

        var accountId = data.AccountId == 0
            ? accounts.FirstOrDefault(account => account.Roles.Any(role => role.Id == data.RoleId))?.Id ?? 0
            : data.AccountId;
        if (accountId == 0)
        {
            return CallbackError(context.Identity, editMessageId, "未找到指定库街区账号。");
        }

        return await builder.BuildAutoSignGamePanelAsync(context, accounts, accountId, editMessageId, cancellationToken, data.Page);
    }

    private async Task<CommandResponse> ExecuteKuroDeleteSelectAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<KuroAccountCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
        var callbackStore = scope.ServiceProvider.GetRequiredService<CallbackActionStore>();
        var account = await accountService.FindByIdAsync(data.AccountId, noTracking: true, cancellationToken);
        if (account is null || account.CoreUserId != context.Identity.CoreUserId)
        {
            return CallbackError(context.Identity, editMessageId, "未找到指定库街区账号。");
        }

        var response = CommandResponses.Text($"确认删除库街区账号绑定？\n账号：`{account.DisplayName}`", context);
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
                        "kuro-delete-confirm",
                        context.Identity.CoreUserId,
                        context.Request.ChatId,
                        context.Request.UserId,
                        new KuroDeleteConfirmCallbackData(account.Id, Confirm: true),
                        cancellationToken: cancellationToken)
                },
                new ResponseButton
                {
                    Text = "取消",
                    Payload = await callbackStore.PutAsync(
                        "kuro-delete-confirm",
                        context.Identity.CoreUserId,
                        context.Request.ChatId,
                        context.Request.UserId,
                        new KuroDeleteConfirmCallbackData(account.Id, Confirm: false),
                        cancellationToken: cancellationToken)
                }
            }
        });
        return response;
    }

    private async Task<CommandResponse> ExecuteKuroDeleteConfirmAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<KuroDeleteConfirmCallbackData>(action);
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
        var accountService = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
        var account = await accountService.FindByIdAsync(data.AccountId, noTracking: true, cancellationToken);
        if (account is null || account.CoreUserId != context.Identity.CoreUserId)
        {
            return CallbackError(context.Identity, editMessageId, "未找到指定库街区账号。");
        }

        var deleted = await accountService.DeleteAsync(context.Identity.CoreUserId, data.AccountId, cancellationToken);
        var response = CommandResponses.Text(deleted ? $"已删除库街区账号绑定：`{account.DisplayName}`" : "未找到指定库街区账号", context);
        response.EditMessageId = editMessageId;
        response.ReplyToMessageId = string.Empty;
        return response;
    }

    private async Task<CommandResponse> ExecuteNotifyBackAsync(
        CommandContext context,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var aiAccountService = scope.ServiceProvider.GetRequiredService<AiRouterAccountService>();
        var kuroAccountService = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
        var callbackStore = scope.ServiceProvider.GetRequiredService<CallbackActionStore>();
        var subscriptionService = scope.ServiceProvider.GetRequiredService<NotificationSubscriptionService>();
        var aiAccounts = await aiAccountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, cancellationToken);
        var kuroAccounts = await kuroAccountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, cancellationToken);
        var response = CommandResponses.Ok(CommandResponseDataKind.NotifyTypePanel, context);
        response.EditMessageId = editMessageId;
        response.ReplyToMessageId = string.Empty;
        response.NotifyTypePanel = new NotifyTypePanelData();
        var aiEnabled = await subscriptionService.GetEnabledTargetIdsAsync(
            context.Identity.CoreUserId,
            context.Request.Platform,
            NotificationTypes.AiRouterAutoSign,
            aiAccounts.Select(account => account.Id).ToArray(),
            cancellationToken);
        var kuroEnabled = await subscriptionService.GetEnabledTargetIdsAsync(
            context.Identity.CoreUserId,
            context.Request.Platform,
            NotificationTypes.KuroAutoSign,
            kuroAccounts.Select(account => account.Id).ToArray(),
            cancellationToken);
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
                            cancellationToken: cancellationToken)
                    }
                }
            });
        }

        return response;
    }

    private async Task<CommandResponse> ExecuteSetPrivilegeApplyAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<SetPrivilegeCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<SetPrivilegeService>();
        var result = await service.SetAsync(
            context.Identity.CoreUserId,
            context.Identity.Privilege,
            data.Platform,
            data.Uid,
            data.Privilege,
            cancellationToken);

        if (result.IsNotFound)
        {
            return CallbackError(context.Identity, editMessageId, "未找到指定用户。");
        }

        if (result.IsForbidden || !result.Success || result.Target is null)
        {
            return CallbackError(context.Identity, editMessageId, "无权设置该用户权限。");
        }

        var response = CommandResponses.Text(
            $"`{result.Target.DisplayName}` 权限更新: `{SetPrivilegeService.FormatPrivilege(result.Before)}` -> `{SetPrivilegeService.FormatPrivilege(result.After)}`",
            context);
        response.EditMessageId = editMessageId;
        response.ReplyToMessageId = string.Empty;
        return response;
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

public sealed record SetPrivilegeCallbackData(BotPlatform Platform, string Uid, UserPrivilege Privilege);
