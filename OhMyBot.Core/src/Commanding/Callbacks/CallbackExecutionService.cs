using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Integrations.AiRouter;
using OhMyBot.Core.Commanding.Commands;
using OhMyBot.Core.Infrastructure.Data.Entities;
using OhMyBot.Core.Integrations.Kuro;
using OhMyBot.Core.Integrations.Mihoyo;
using OhMyBot.Core.Commanding.Notifications;

namespace OhMyBot.Core.Commanding.Callbacks;

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
            // 动作已被消费（重复点击）或已过期：静默忽略，避免重放，也不破坏当前消息。
            return CallbackNoop(identity);
        }

        if (action.RequireOriginalSender && !string.Equals(action.SenderId, request.UserId, StringComparison.Ordinal))
        {
            return CallbackError(identity, request.MessageId, "这个按钮只能由原发起用户操作。");
        }

        if (action.CoreUserId != identity.CoreUserId)
        {
            return CallbackError(identity, request.MessageId, "当前账号无权操作这个按钮。");
        }

        // 回调动作一次性使用：先消费再执行，防止签到等耗时操作期间按钮被重复点击造成重放。
        await actionStore.RemoveAsync(request.Payload, cancellationToken);

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
            "ai-router-signin-all" => await ExecuteSignInAllAsync(context, request.MessageId, cancellationToken),
            "ai-router-auto-sign-toggle" => await ExecuteAutoSignToggleAsync(context, action, request.MessageId, cancellationToken),
            "ai-router-delete-select" => await ExecuteDeleteSelectAsync(context, action, request.MessageId, cancellationToken),
            "ai-router-delete-confirm" => await ExecuteDeleteConfirmAsync(context, action, request.MessageId, cancellationToken),
            "kuro-bbs-sign-select" => await ExecuteKuroBbsSignSelectAsync(context, action, request.MessageId, cancellationToken),
            "kuro-bbs-sign-all" => await ExecuteKuroBbsSignAllAsync(context, request.MessageId, cancellationToken),
            "kuro-game-sign-select" => await ExecuteKuroGameSignSelectAsync(context, action, request.MessageId, cancellationToken),
            "kuro-game-sign-panel" => await ExecuteKuroGameSignPanelAsync(context, action, request.MessageId, cancellationToken),
            "kuro-game-sign-run" => await ExecuteKuroGameSignRunAsync(context, action, request.MessageId, cancellationToken),
            "kuro-game-sign-back" => await ExecuteKuroGameSignBackAsync(context, request.MessageId, cancellationToken),
            "kuro-game-sign-all" => await ExecuteKuroGameSignAllAsync(context, request.MessageId, cancellationToken),
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
            "mihoyo-bbs-sign-select" => await ExecuteMihoyoBbsSignSelectAsync(context, action, request.MessageId, cancellationToken),
            "mihoyo-bbs-sign-all" => await ExecuteMihoyoBbsSignAllAsync(context, request.MessageId, cancellationToken),
            "mihoyo-game-sign-select" => await ExecuteMihoyoGameSignSelectAsync(context, action, request.MessageId, cancellationToken),
            "mihoyo-game-sign-panel" => await ExecuteMihoyoGameSignPanelAsync(context, action, request.MessageId, cancellationToken),
            "mihoyo-game-sign-run" => await ExecuteMihoyoGameSignRunAsync(context, action, request.MessageId, cancellationToken),
            "mihoyo-game-sign-back" => await ExecuteMihoyoGameSignBackAsync(context, request.MessageId, cancellationToken),
            "mihoyo-game-sign-all" => await ExecuteMihoyoGameSignAllAsync(context, request.MessageId, cancellationToken),
            "mihoyo-autosign-root-menu" => await ExecuteMihoyoAutoSignRootMenuAsync(context, action, request.MessageId, cancellationToken),
            "mihoyo-autosign-account-menu" => await ExecuteMihoyoAutoSignAccountMenuAsync(context, action, request.MessageId, cancellationToken),
            "mihoyo-autosign-bbs-menu" => await ExecuteMihoyoAutoSignBbsMenuAsync(context, action, request.MessageId, cancellationToken),
            "mihoyo-autosign-game-menu" => await ExecuteMihoyoAutoSignGameMenuAsync(context, action, request.MessageId, cancellationToken),
            "mihoyo-auto-sign-toggle" => await ExecuteMihoyoAutoSignToggleAsync(context, action, request.MessageId, cancellationToken),
            "mihoyo-bbs-task-toggle" => await ExecuteMihoyoBbsTaskToggleAsync(context, action, request.MessageId, cancellationToken),
            "mihoyo-bbs-task-toggle-all" => await ExecuteMihoyoBbsTaskToggleAllAsync(context, action, request.MessageId, cancellationToken),
            "mihoyo-game-auto-sign-toggle" => await ExecuteMihoyoGameAutoSignToggleAsync(context, action, request.MessageId, cancellationToken),
            "mihoyo-game-auto-sign-toggle-all" => await ExecuteMihoyoGameAutoSignToggleAllAsync(context, action, request.MessageId, cancellationToken),
            "mihoyo-delete-select" => await ExecuteMihoyoDeleteSelectAsync(context, action, request.MessageId, cancellationToken),
            "mihoyo-delete-confirm" => await ExecuteMihoyoDeleteConfirmAsync(context, action, request.MessageId, cancellationToken),
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

    private async Task<CommandResponse> ExecuteSignInAllAsync(
        CommandContext context,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<AiRouterAccountService>();
        var signService = scope.ServiceProvider.GetRequiredService<AiRouterSignService>();
        var builder = scope.ServiceProvider.GetRequiredService<AiRouterResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, cancellationToken);
        if (accounts.Count == 0)
        {
            return CallbackError(context.Identity, editMessageId, "未找到 AI Router 账号。");
        }

        var results = new List<AiRouterSignResult>();
        foreach (var account in accounts)
        {
            try
            {
                results.Add(await signService.SignInAsync(account, cancellationToken));
            }
            catch (Exception exception)
            {
                results.Add(AiRouterSignResult.Failed(account.Id, account.LoginEmail, account.DisplayName, exception.GetBaseException().Message));
            }
        }

        return builder.BuildCombinedSignResult(context, results, editMessageId);
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
        if (data?.Type != NotificationTypes.AiRouterAutoSign
            && data?.Type != NotificationTypes.KuroAutoSign
            && data?.Type != NotificationTypes.MihoyoAutoSign)
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

        if (data.Type == NotificationTypes.MihoyoAutoSign)
        {
            var mihoyoAccountService = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
            var mihoyoBuilder = scope.ServiceProvider.GetRequiredService<MihoyoResponseBuilder>();
            var mihoyoAccounts = await mihoyoAccountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, cancellationToken);
            return await mihoyoBuilder.BuildNotifyAccountPanelAsync(context, mihoyoAccounts, editMessageId, cancellationToken);
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
        if (data?.Type != NotificationTypes.AiRouterAutoSign
            && data?.Type != NotificationTypes.KuroAutoSign
            && data?.Type != NotificationTypes.MihoyoAutoSign)
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

        if (data.Type == NotificationTypes.MihoyoAutoSign)
        {
            var mihoyoAccountService = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
            var mihoyoSubscriptionService = scope.ServiceProvider.GetRequiredService<NotificationSubscriptionService>();
            var mihoyoBuilder = scope.ServiceProvider.GetRequiredService<MihoyoResponseBuilder>();
            var mihoyoAccounts = await mihoyoAccountService.ListByOwnerAsync(context.Identity.CoreUserId, cancellationToken: cancellationToken);
            if (data.ToggleAll)
            {
                await mihoyoSubscriptionService.ToggleAllAsync(
                    context.Identity.CoreUserId,
                    context.Request.Platform,
                    context.Request.BotInstanceId,
                    context.Request.ChatId,
                    NotificationTypes.MihoyoAutoSign,
                    mihoyoAccounts.Select(account => account.Id).ToArray(),
                    cancellationToken);
            }
            else if (mihoyoAccounts.Any(account => account.Id == data.AccountId))
            {
                await mihoyoSubscriptionService.ToggleAsync(
                    context.Identity.CoreUserId,
                    context.Request.Platform,
                    context.Request.BotInstanceId,
                    context.Request.ChatId,
                    NotificationTypes.MihoyoAutoSign,
                    data.AccountId,
                    cancellationToken);
            }
            else
            {
                return CallbackError(context.Identity, editMessageId, "未找到指定米游社账号。");
            }

            var updatedMihoyoAccounts = await mihoyoAccountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, cancellationToken);
            return await mihoyoBuilder.BuildNotifyAccountPanelAsync(context, updatedMihoyoAccounts, editMessageId, cancellationToken);
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

    private async Task<CommandResponse> ExecuteKuroGameSignPanelAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<KuroGameSignPanelCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<KuroResponseBuilder>();
        var account = await accountService.FindByIdAsync(data.AccountId, noTracking: true, cancellationToken);
        if (account is null || account.CoreUserId != context.Identity.CoreUserId)
        {
            return CallbackError(context.Identity, editMessageId, "未找到指定库街区账号。");
        }

        // 翻转勾选走服务端原子读改写（账号级串行），避免并发点击互相覆盖；仅打开时读取当前状态。
        var selected = data.Toggle == 0
            ? KuroResponseBuilder.ResolveGameSignSelection(account)
            : await accountService.ToggleGameSignSelectionAsync(context.Identity.CoreUserId, account.Id, data.Toggle, cancellationToken);

        return await builder.BuildGameSignPanelAsync(context, account, selected, editMessageId, cancellationToken);
    }

    private async Task<CommandResponse> ExecuteKuroGameSignRunAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<KuroGameSignPanelCallbackData>(action);
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

        // 以服务端持久化的勾选为准（面板状态即为账号记录）。
        var gameIds = KuroResponseBuilder.ResolveGameSignSelection(account);
        if (gameIds.Count == 0)
        {
            var panel = await builder.BuildGameSignPanelAsync(context, account, [], editMessageId, cancellationToken);
            panel.CallbackAnswerText = "请至少勾选一个游戏";
            panel.CallbackAnswerAlert = true;
            return panel;
        }

        var response = builder.BuildGameSignResult(context, await signService.ExecuteGameSignAsync(
            account,
            gameIds,
            includeMissingConfigMessage: true,
            cancellationToken: cancellationToken));
        response.EditMessageId = editMessageId;
        response.ReplyToMessageId = string.Empty;
        return response;
    }

    private async Task<CommandResponse> ExecuteKuroGameSignBackAsync(
        CommandContext context,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<KuroResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, cancellationToken);
        if (accounts.Count <= 1)
        {
            var canceled = CommandResponses.Text("已取消游戏签到", context);
            canceled.EditMessageId = editMessageId;
            canceled.ReplyToMessageId = string.Empty;
            return canceled;
        }

        return await builder.BuildGameSignSelectionAsync(context, accounts, editMessageId, cancellationToken);
    }

    private async Task<CommandResponse> ExecuteKuroGameSignAllAsync(
        CommandContext context,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
        var signService = scope.ServiceProvider.GetRequiredService<KuroSignService>();
        var builder = scope.ServiceProvider.GetRequiredService<KuroResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, cancellationToken);
        if (accounts.Count == 0)
        {
            return CallbackError(context.Identity, editMessageId, "未找到库街区账号。");
        }

        var results = new List<(KuroAccount, IReadOnlyList<string>)>();
        foreach (var account in accounts)
        {
            try
            {
                var result = await signService.ExecuteGameSignAsync(account, includeMissingConfigMessage: true, cancellationToken: cancellationToken);
                results.Add((account, result.Lines));
            }
            catch (Exception exception)
            {
                results.Add((account, ["签到失败：" + exception.GetBaseException().Message]));
            }
        }

        return builder.BuildCombinedResult(context, "[库街区游戏签到 - 全部账号]", results, editMessageId);
    }

    private async Task<CommandResponse> ExecuteKuroBbsSignAllAsync(
        CommandContext context,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
        var signService = scope.ServiceProvider.GetRequiredService<KuroSignService>();
        var builder = scope.ServiceProvider.GetRequiredService<KuroResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, cancellationToken);
        if (accounts.Count == 0)
        {
            return CallbackError(context.Identity, editMessageId, "未找到库街区账号。");
        }

        var results = new List<(KuroAccount, IReadOnlyList<string>)>();
        foreach (var account in accounts)
        {
            try
            {
                var result = await signService.ExecuteBbsSignAsync(
                    account,
                    taskFlags: 0,
                    requestedActions: null,
                    runAllWhenNoRequestedActions: true,
                    cancellationToken: cancellationToken);
                results.Add((account, result.Lines));
            }
            catch (Exception exception)
            {
                results.Add((account, ["签到失败：" + exception.GetBaseException().Message]));
            }
        }

        return builder.BuildCombinedResult(context, "[库街区社区签到 - 全部账号]", results, editMessageId);
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
        var mihoyoAccountService = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
        var callbackStore = scope.ServiceProvider.GetRequiredService<CallbackActionStore>();
        var subscriptionService = scope.ServiceProvider.GetRequiredService<NotificationSubscriptionService>();
        var aiAccounts = await aiAccountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, cancellationToken);
        var kuroAccounts = await kuroAccountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, cancellationToken);
        var mihoyoAccounts = await mihoyoAccountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, cancellationToken);
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
        var mihoyoEnabled = await subscriptionService.GetEnabledTargetIdsAsync(
            context.Identity.CoreUserId,
            context.Request.Platform,
            NotificationTypes.MihoyoAutoSign,
            mihoyoAccounts.Select(account => account.Id).ToArray(),
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
        response.NotifyTypePanel.Items.Add(new NotifyTypeItem
        {
            Type = NotificationTypes.MihoyoAutoSign,
            DisplayName = NotificationTypes.MihoyoAutoSignDisplayName,
            Enabled = mihoyoEnabled.Count > 0
        });
        var enabledNames = response.NotifyTypePanel.Items.Where(item => item.Enabled).Select(item => item.DisplayName).ToArray();
        response.Message = "[消息订阅管理]\n当前已启用: " + (enabledNames.Length == 0 ? "无" : string.Join("、", enabledNames));
        var row = new ResponseButtonRow();
        foreach (var item in response.NotifyTypePanel.Items)
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
                    cancellationToken: cancellationToken)
            });

            if (row.Buttons.Count == 2)
            {
                response.ButtonRows.Add(row);
                row = new ResponseButtonRow();
            }
        }

        if (row.Buttons.Count > 0)
        {
            response.ButtonRows.Add(row);
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

    private async Task<CommandResponse> ExecuteMihoyoBbsSignSelectAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<MihoyoBbsSignCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
        var signService = scope.ServiceProvider.GetRequiredService<MihoyoSignService>();
        var builder = scope.ServiceProvider.GetRequiredService<MihoyoResponseBuilder>();
        var account = await accountService.FindByIdAsync(data.AccountId, noTracking: true, cancellationToken);
        if (account is null || account.CoreUserId != context.Identity.CoreUserId)
        {
            return CallbackError(context.Identity, editMessageId, "未找到指定米游社账号。");
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

    private async Task<CommandResponse> ExecuteMihoyoGameSignSelectAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<MihoyoGameSignCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
        var signService = scope.ServiceProvider.GetRequiredService<MihoyoSignService>();
        var builder = scope.ServiceProvider.GetRequiredService<MihoyoResponseBuilder>();
        var account = await accountService.FindByIdAsync(data.AccountId, noTracking: true, cancellationToken);
        if (account is null || account.CoreUserId != context.Identity.CoreUserId)
        {
            return CallbackError(context.Identity, editMessageId, "未找到指定米游社账号。");
        }

        var response = builder.BuildGameSignResult(context, await signService.ExecuteGameSignAsync(
            account,
            data.GameKeys,
            includeMissingConfigMessage: true,
            cancellationToken: cancellationToken));
        response.EditMessageId = editMessageId;
        response.ReplyToMessageId = string.Empty;
        return response;
    }

    private async Task<CommandResponse> ExecuteMihoyoGameSignPanelAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<MihoyoGameSignPanelCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<MihoyoResponseBuilder>();
        var account = await accountService.FindByIdAsync(data.AccountId, noTracking: true, cancellationToken);
        if (account is null || account.CoreUserId != context.Identity.CoreUserId)
        {
            return CallbackError(context.Identity, editMessageId, "未找到指定米游社账号。");
        }

        // 翻转勾选走服务端原子读改写（账号级串行），避免并发点击互相覆盖；仅打开时读取当前状态。
        var selected = string.IsNullOrEmpty(data.Toggle)
            ? MihoyoResponseBuilder.ResolveGameSignSelection(account)
            : await accountService.ToggleGameSignSelectionAsync(context.Identity.CoreUserId, account.Id, data.Toggle, cancellationToken);

        return await builder.BuildGameSignPanelAsync(context, account, selected, editMessageId, cancellationToken);
    }

    private async Task<CommandResponse> ExecuteMihoyoGameSignRunAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<MihoyoGameSignPanelCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
        var signService = scope.ServiceProvider.GetRequiredService<MihoyoSignService>();
        var builder = scope.ServiceProvider.GetRequiredService<MihoyoResponseBuilder>();
        var account = await accountService.FindByIdAsync(data.AccountId, noTracking: true, cancellationToken);
        if (account is null || account.CoreUserId != context.Identity.CoreUserId)
        {
            return CallbackError(context.Identity, editMessageId, "未找到指定米游社账号。");
        }

        // 以服务端持久化的勾选为准（面板状态即为账号记录）。
        var gameKeys = MihoyoResponseBuilder.ResolveGameSignSelection(account);
        if (gameKeys.Count == 0)
        {
            var panel = await builder.BuildGameSignPanelAsync(context, account, [], editMessageId, cancellationToken);
            panel.CallbackAnswerText = "请至少勾选一个游戏";
            panel.CallbackAnswerAlert = true;
            return panel;
        }

        var response = builder.BuildGameSignResult(context, await signService.ExecuteGameSignAsync(
            account,
            gameKeys,
            includeMissingConfigMessage: true,
            cancellationToken: cancellationToken));
        response.EditMessageId = editMessageId;
        response.ReplyToMessageId = string.Empty;
        return response;
    }

    private async Task<CommandResponse> ExecuteMihoyoGameSignBackAsync(
        CommandContext context,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<MihoyoResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, cancellationToken);
        if (accounts.Count <= 1)
        {
            var canceled = CommandResponses.Text("已取消游戏签到", context);
            canceled.EditMessageId = editMessageId;
            canceled.ReplyToMessageId = string.Empty;
            return canceled;
        }

        return await builder.BuildGameSignSelectionAsync(context, accounts, editMessageId, cancellationToken);
    }

    private async Task<CommandResponse> ExecuteMihoyoGameSignAllAsync(
        CommandContext context,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
        var signService = scope.ServiceProvider.GetRequiredService<MihoyoSignService>();
        var builder = scope.ServiceProvider.GetRequiredService<MihoyoResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, cancellationToken);
        if (accounts.Count == 0)
        {
            return CallbackError(context.Identity, editMessageId, "未找到米游社账号。");
        }

        var results = new List<(MihoyoAccount, IReadOnlyList<string>)>();
        foreach (var account in accounts)
        {
            try
            {
                var result = await signService.ExecuteGameSignAsync(account, includeMissingConfigMessage: true, cancellationToken: cancellationToken);
                results.Add((account, result.Lines));
            }
            catch (Exception exception)
            {
                results.Add((account, ["签到失败：" + exception.GetBaseException().Message]));
            }
        }

        return builder.BuildCombinedResult(context, "[米游社游戏签到 - 全部账号]", results, editMessageId);
    }

    private async Task<CommandResponse> ExecuteMihoyoBbsSignAllAsync(
        CommandContext context,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
        var signService = scope.ServiceProvider.GetRequiredService<MihoyoSignService>();
        var builder = scope.ServiceProvider.GetRequiredService<MihoyoResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, cancellationToken);
        var cnAccounts = accounts.Where(account => account.Region == MihoyoRegion.Cn).ToArray();
        if (cnAccounts.Length == 0)
        {
            return CallbackError(context.Identity, editMessageId, "未找到国服米游社账号。");
        }

        var results = new List<(MihoyoAccount, IReadOnlyList<string>)>();
        foreach (var account in cnAccounts)
        {
            try
            {
                var result = await signService.ExecuteBbsSignAsync(
                    account,
                    taskFlags: 0,
                    requestedActions: null,
                    runAllWhenNoRequestedActions: true,
                    cancellationToken: cancellationToken);
                results.Add((account, result.Lines));
            }
            catch (Exception exception)
            {
                results.Add((account, ["签到失败：" + exception.GetBaseException().Message]));
            }
        }

        return builder.BuildCombinedResult(context, "[米游社社区任务 - 全部账号]", results, editMessageId);
    }

    private async Task<CommandResponse> ExecuteMihoyoAutoSignRootMenuAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<MihoyoAutoSignMenuCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<MihoyoResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, cancellationToken);
        return await builder.BuildAutoSignPanelAsync(context, accounts, editMessageId, cancellationToken, data.Page);
    }

    private async Task<CommandResponse> ExecuteMihoyoAutoSignAccountMenuAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<MihoyoAutoSignMenuCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<MihoyoResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, cancellationToken);
        return await builder.BuildAutoSignAccountPanelAsync(context, accounts, data.AccountId, editMessageId, cancellationToken);
    }

    private async Task<CommandResponse> ExecuteMihoyoAutoSignBbsMenuAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<MihoyoAutoSignMenuCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<MihoyoResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, cancellationToken);
        return await builder.BuildAutoSignBbsPanelAsync(context, accounts, data.AccountId, editMessageId, cancellationToken);
    }

    private async Task<CommandResponse> ExecuteMihoyoAutoSignGameMenuAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<MihoyoAutoSignMenuCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<MihoyoResponseBuilder>();
        var accounts = await accountService.ListByOwnerAsync(context.Identity.CoreUserId, noTracking: true, cancellationToken);
        return await builder.BuildAutoSignGamePanelAsync(context, accounts, data.AccountId, editMessageId, cancellationToken, data.Page);
    }

    private async Task<CommandResponse> ExecuteMihoyoAutoSignToggleAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<MihoyoAutoSignCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<MihoyoResponseBuilder>();
        var accounts = await accountService.ToggleAutoSignAsync(context.Identity.CoreUserId, data.AccountId, cancellationToken);
        if (accounts.Count == 0)
        {
            return CallbackError(context.Identity, editMessageId, "未找到指定米游社账号。");
        }

        return await builder.BuildAutoSignAccountPanelAsync(context, accounts, data.AccountId, editMessageId, cancellationToken);
    }

    private async Task<CommandResponse> ExecuteMihoyoBbsTaskToggleAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<MihoyoBbsTaskCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<MihoyoResponseBuilder>();
        var accounts = await accountService.ToggleBbsTaskAsync(context.Identity.CoreUserId, data.AccountId, data.TaskFlag, cancellationToken);
        if (accounts.Count == 0)
        {
            return CallbackError(context.Identity, editMessageId, "未找到指定米游社账号。");
        }

        return await builder.BuildAutoSignBbsPanelAsync(context, accounts, data.AccountId, editMessageId, cancellationToken);
    }

    private async Task<CommandResponse> ExecuteMihoyoBbsTaskToggleAllAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<MihoyoBbsTaskToggleAllCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<MihoyoResponseBuilder>();
        var accounts = await accountService.ToggleAllBbsTasksAsync(context.Identity.CoreUserId, data.AccountId, cancellationToken);
        if (accounts.Count == 0)
        {
            return CallbackError(context.Identity, editMessageId, "未找到指定米游社账号。");
        }

        return await builder.BuildAutoSignBbsPanelAsync(context, accounts, data.AccountId, editMessageId, cancellationToken);
    }

    private async Task<CommandResponse> ExecuteMihoyoGameAutoSignToggleAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<MihoyoGameAutoSignCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<MihoyoResponseBuilder>();
        var accounts = await accountService.ToggleGameAutoSignAsync(context.Identity.CoreUserId, data.RoleId, cancellationToken);
        if (accounts.Count == 0)
        {
            return CallbackError(context.Identity, editMessageId, "未找到指定米游社角色。");
        }

        var accountId = data.AccountId == 0
            ? accounts.FirstOrDefault(account => account.Roles.Any(role => role.Id == data.RoleId))?.Id ?? 0
            : data.AccountId;
        if (accountId == 0)
        {
            return CallbackError(context.Identity, editMessageId, "未找到指定米游社账号。");
        }

        return await builder.BuildAutoSignGamePanelAsync(context, accounts, accountId, editMessageId, cancellationToken, data.Page);
    }

    private async Task<CommandResponse> ExecuteMihoyoGameAutoSignToggleAllAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<MihoyoGameAutoSignToggleAllCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
        var builder = scope.ServiceProvider.GetRequiredService<MihoyoResponseBuilder>();
        var accounts = await accountService.ToggleAllGameAutoSignAsync(context.Identity.CoreUserId, data.AccountId, cancellationToken);
        if (accounts.Count == 0)
        {
            return CallbackError(context.Identity, editMessageId, "未找到指定米游社账号。");
        }

        return await builder.BuildAutoSignGamePanelAsync(context, accounts, data.AccountId, editMessageId, cancellationToken, data.Page);
    }

    private async Task<CommandResponse> ExecuteMihoyoDeleteSelectAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<MihoyoAccountCallbackData>(action);
        if (data is null)
        {
            return CallbackError(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
        var callbackStore = scope.ServiceProvider.GetRequiredService<CallbackActionStore>();
        var account = await accountService.FindByIdAsync(data.AccountId, noTracking: true, cancellationToken);
        if (account is null || account.CoreUserId != context.Identity.CoreUserId)
        {
            return CallbackError(context.Identity, editMessageId, "未找到指定米游社账号。");
        }

        var response = CommandResponses.Text($"确认删除米游社账号绑定？\n账号：`{account.DisplayName}`", context);
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
                        "mihoyo-delete-confirm",
                        context.Identity.CoreUserId,
                        context.Request.ChatId,
                        context.Request.UserId,
                        new MihoyoDeleteConfirmCallbackData(account.Id, Confirm: true),
                        cancellationToken: cancellationToken)
                },
                new ResponseButton
                {
                    Text = "取消",
                    Payload = await callbackStore.PutAsync(
                        "mihoyo-delete-confirm",
                        context.Identity.CoreUserId,
                        context.Request.ChatId,
                        context.Request.UserId,
                        new MihoyoDeleteConfirmCallbackData(account.Id, Confirm: false),
                        cancellationToken: cancellationToken)
                }
            }
        });
        return response;
    }

    private async Task<CommandResponse> ExecuteMihoyoDeleteConfirmAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<MihoyoDeleteConfirmCallbackData>(action);
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
        var accountService = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
        var account = await accountService.FindByIdAsync(data.AccountId, noTracking: true, cancellationToken);
        if (account is null || account.CoreUserId != context.Identity.CoreUserId)
        {
            return CallbackError(context.Identity, editMessageId, "未找到指定米游社账号。");
        }

        var deleted = await accountService.DeleteAsync(context.Identity.CoreUserId, data.AccountId, cancellationToken);
        var response = CommandResponses.Text(deleted ? $"已删除米游社账号绑定：`{account.DisplayName}`" : "未找到指定米游社账号", context);
        response.EditMessageId = editMessageId;
        response.ReplyToMessageId = string.Empty;
        return response;
    }

    private static CommandResponse CallbackError(OhMyBot.Core.Infrastructure.Identity.ResolvedIdentity identity, string editMessageId, string message)
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

    // 无渲染输出的空响应：用于忽略已消费/过期的回调点击，既不重放也不改动消息。
    private static CommandResponse CallbackNoop(OhMyBot.Core.Infrastructure.Identity.ResolvedIdentity identity)
    {
        return new CommandResponse
        {
            Code = 0,
            DataKind = CommandResponseDataKind.Text,
            Message = string.Empty,
            Text = new TextData { Text = string.Empty },
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
