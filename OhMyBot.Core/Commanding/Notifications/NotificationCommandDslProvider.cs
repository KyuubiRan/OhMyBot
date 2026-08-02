using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commanding.Callbacks;
using OhMyBot.Core.Commanding.Commands;
using OhMyBot.Core.Commanding.Presentation;

namespace OhMyBot.Core.Commanding.Notifications;

public sealed class NotificationCommandDslProvider(
    CallbackActionStore callbackStore,
    PluginNotificationSourceRegistry? sourceRegistry = null) : IPlatformCommandDslProvider
{
    private readonly PluginNotificationSourceRegistry _sourceRegistry = sourceRegistry ?? new PluginNotificationSourceRegistry();

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
                Handler = context => BuildRootAsync(context, null, context.CancellationToken)
            }
        ];
    }

    public async Task<CommandResponse> BuildRootAsync(
        CommandContext context,
        string? editMessageId,
        CancellationToken cancellationToken = default)
    {
        var sources = _sourceRegistry.Sources;
        var enabledNames = new List<string>();
        foreach (var source in sources)
        {
            if (await source.HasEnabledTargetsAsync(context, cancellationToken))
            {
                enabledNames.Add(source.DisplayName);
            }
        }

        var text = MarkdownV2.Escape("[消息订阅管理]") + "\n当前已启用：" +
            TextLayout.JoinOrEmpty(
                enabledNames.Select(MarkdownV2.CodeSpan),
                MarkdownV2.Escape("、"),
                "无");
        var response = CommandResponses.TelegramMarkdown(context.Identity, text, context.Request.MessageId)
            .AsTelegramEditIfSpecified(editMessageId);

        // ownerPluginId 必须是 "core"：插件热重载时按归属清理回调，漏了会把 Core 自己的按钮一起清掉。
        var panel = new PanelBuilder(callbackStore, context, ownerPluginId: "core");
        await panel.AddGridAsync(
            response,
            sources,
            columns: 2,
            "notify-type-select",
            source => source.DisplayName,
            source => new NotificationTypeCallbackData(source.Type),
            cancellationToken);

        return response;
    }
}
