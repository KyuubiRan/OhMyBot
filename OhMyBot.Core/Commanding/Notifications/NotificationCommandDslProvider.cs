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
            (enabledNames.Count == 0
                ? "无"
                : string.Join(MarkdownV2.Escape("、"), enabledNames.Select(MarkdownV2.CodeSpan)));
        var response = CommandResponses.TelegramMarkdown(context.Identity, text, context.Request.MessageId);
        if (!string.IsNullOrWhiteSpace(editMessageId))
        {
            response.AsTelegramEdit(editMessageId);
        }

        var row = new ResponseButtonRow();
        foreach (var source in sources)
        {
            row.Buttons.Add(new ResponseButton
            {
                Text = source.DisplayName,
                Payload = await callbackStore.PutAsync(
                    "notify-type-select",
                    context.Identity.CoreUserId,
                    context.Request.ChatId,
                    context.Request.UserId,
                    new NotificationTypeCallbackData(source.Type),
                    ownerPluginId: "core",
                    cancellationToken: cancellationToken)
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
