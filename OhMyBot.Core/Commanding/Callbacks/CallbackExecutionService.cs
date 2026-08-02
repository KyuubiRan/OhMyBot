using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commanding.Commands;
using OhMyBot.Core.Commanding.Notifications;
using OhMyBot.Core.Infrastructure.Identity;

namespace OhMyBot.Core.Commanding.Callbacks;

public sealed class CallbackExecutionService
{
    private readonly CoreIdentityService _identityService;
    private readonly CallbackActionStore _actionStore;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly PluginCallbackRegistry _pluginCallbacks;
    private readonly PluginNotificationSourceRegistry _notificationSources;
    private readonly NotificationCommandDslProvider _notificationProvider;

    public CallbackExecutionService(
        CoreIdentityService identityService,
        CallbackActionStore actionStore,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        PluginCallbackRegistry? pluginCallbacks = null,
        PluginNotificationSourceRegistry? notificationSources = null,
        NotificationCommandDslProvider? notificationProvider = null)
    {
        _identityService = identityService;
        _actionStore = actionStore;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _pluginCallbacks = pluginCallbacks ?? new PluginCallbackRegistry();
        _notificationSources = notificationSources ?? new PluginNotificationSourceRegistry();
        _notificationProvider = notificationProvider ?? new NotificationCommandDslProvider(actionStore, _notificationSources);
    }

    public async Task<CommandResponse> ExecuteAsync(
        CallbackRequest request,
        CancellationToken cancellationToken = default)
    {
        var identity = await _identityService.ResolveIdentityAsync(new CommandRequest
        {
            Platform = request.Platform,
            BotInstanceId = request.BotInstanceId,
            ChatId = request.ChatId,
            UserId = request.UserId,
            MessageId = request.MessageId,
            ChatType = request.ChatType
        }, cancellationToken);

        var action = await _actionStore.GetAsync(request.Payload, cancellationToken);
        if (action is null)
        {
            return PluginCallbackResponses.Noop(identity);
        }

        // 按钮只在生成它的那个会话里有效：拿到 payload 也不能在别的群/私聊里重放。
        if (!string.Equals(action.ChatId, request.ChatId, StringComparison.Ordinal))
        {
            return PluginCallbackResponses.Error(identity, request.MessageId, "这个按钮不属于当前会话。");
        }

        if (action.RequireOriginalSender
            && !string.Equals(action.SenderId, request.UserId, StringComparison.Ordinal))
        {
            return PluginCallbackResponses.Error(identity, request.MessageId, "这个按钮只能由原发起用户操作。");
        }

        if (action.CoreUserId != identity.CoreUserId)
        {
            return PluginCallbackResponses.Error(identity, request.MessageId, "当前账号无权操作这个按钮。");
        }

        await _actionStore.RemoveAsync(request.Payload, cancellationToken);
        var context = new CommandContext(new CommandRequest
        {
            Platform = request.Platform,
            BotInstanceId = request.BotInstanceId,
            ChatId = request.ChatId,
            UserId = request.UserId,
            MessageId = request.MessageId,
            ChatType = request.ChatType
        }, identity, _timeProvider.GetTimestamp(), cancellationToken);

        if (_pluginCallbacks.TryGet(action.ActionType, out var pluginHandler))
        {
            return await pluginHandler.ExecuteAsync(
                action.ActionType,
                context,
                action,
                request.MessageId,
                cancellationToken);
        }

        return action.ActionType switch
        {
            "notify-type-select" => await ExecuteNotifyTypeSelectAsync(context, action, request.MessageId, cancellationToken),
            "notify-account-toggle" => await ExecuteNotifyAccountToggleAsync(context, action, request.MessageId, cancellationToken),
            "notify-back" => await _notificationProvider.BuildRootAsync(context, request.MessageId, cancellationToken),
            "setpriv-apply" => await ExecuteSetPrivilegeApplyAsync(context, action, request.MessageId, cancellationToken),
            _ => PluginCallbackResponses.Error(identity, request.MessageId, "未知按钮操作或对应插件未加载。")
        };
    }

    private async Task<CommandResponse> ExecuteNotifyTypeSelectAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<NotificationTypeCallbackData>(action);
        if (data is null || !_notificationSources.TryGet(data.Type, out var source))
        {
            return PluginCallbackResponses.Error(context.Identity, editMessageId, "未知订阅类型或对应插件未加载。");
        }

        return await source.BuildAccountPanelAsync(context, editMessageId, cancellationToken);
    }

    private async Task<CommandResponse> ExecuteNotifyAccountToggleAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken)
    {
        var data = CallbackActionStore.ReadData<NotificationAccountCallbackData>(action);
        if (data is null || !_notificationSources.TryGet(data.Type, out var source))
        {
            return PluginCallbackResponses.Error(context.Identity, editMessageId, "未知订阅类型或对应插件未加载。");
        }

        return await source.ToggleAsync(
            context,
            data.AccountId,
            data.ToggleAll,
            editMessageId,
            cancellationToken);
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
            return PluginCallbackResponses.Error(context.Identity, editMessageId, "按钮数据无效。");
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
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
            return PluginCallbackResponses.Error(context.Identity, editMessageId, "未找到指定用户。");
        }

        if (result.IsForbidden || !result.Success || result.Target is null)
        {
            return PluginCallbackResponses.Error(context.Identity, editMessageId, "无权设置该用户权限。");
        }

        var response = CommandResponses.Text(
            $"`{result.Target.DisplayName}` 权限更新: `{SetPrivilegeService.FormatPrivilege(result.Before)}` -> `{SetPrivilegeService.FormatPrivilege(result.After)}`",
            context);
        response.AsTelegramEdit(editMessageId);
        return response;
    }
}
