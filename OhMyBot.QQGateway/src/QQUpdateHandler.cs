using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Grpc;
using OhMyBot.OneBotV11;
using OhMyBot.OneBotV11.Events;
using OhMyBot.OneBotV11.Events.Messages;
using OhMyBot.OneBotV11.Events.Messages.Group;
using OhMyBot.OneBotV11.Events.Messages.Private;
using OhMyBot.OneBotV11.Messages.Entity;
using OhMyBot.OneBotV11.Transport;

namespace OhMyBot.QQGateway;

// 订阅 OneBot 消息事件，将 QQ 私聊/群消息转成命令请求交给 Core，再把结果发回。
// 注意：QQ 无按钮、无 Markdown，回复一律为纯文本消息段（与 Telegram 的富文本分开维护）。
public sealed class QQUpdateHandler(
    QQCommandGateway gateway,
    QQResponseRenderer renderer,
    IOneBotClient oneBotClient,
    IOptions<QQGatewayOptions> options,
    ILogger<QQUpdateHandler> logger)
{
    private readonly QQGatewayOptions _options = options.Value;
    private readonly string[] _commandPrefixes = GatewayCommandParser.NormalizePrefixes(options.Value.CommandPrefixes);

    // OnEvent 在接收循环线程上同步触发，这里立即卸载到后台任务，避免阻塞收包。
    public void Handle(EventBase evt)
    {
        if (evt is not MessageEvent message)
        {
            return;
        }

        // 忽略机器人自身消息（NapCat 默认不上报，这里再加一道保险，避免自我触发死循环）。
        if (message.SenderId == message.SelfId)
        {
            return;
        }

        _ = Task.Run(() => ProcessAsync(message));
    }

    private async Task ProcessAsync(MessageEvent message)
    {
        try
        {
            var text = ExtractText(message);
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var (command, _) = GatewayCommandParser.Parse(text, _commandPrefixes);
            if (string.IsNullOrEmpty(command))
            {
                // 非命令消息：不记录、不响应，避免群聊刷屏拖垮 Core。
                return;
            }

            string chatId;
            BotChatType chatType;
            var nickname = string.Empty;
            var card = string.Empty;
            switch (message)
            {
                case GroupMessageEvent group:
                    chatType = BotChatType.Group;
                    chatId = group.GroupId.ToString();
                    nickname = group.Sender?.Nickname ?? string.Empty;
                    card = group.Sender?.Card ?? string.Empty;
                    break;
                case PrivateMessageEvent priv:
                    chatType = BotChatType.Private;
                    chatId = priv.SenderId.ToString();
                    nickname = priv.Sender?.Nickname ?? string.Empty;
                    break;
                default:
                    return;
            }

            // 群内优先用群名片，其次昵称。
            var displayName = FirstNonEmpty(card, nickname);
            var request = new GatewayCommandRequest(
                chatId,
                message.SenderId.ToString(),
                message.MessageId.ToString(),
                text,
                DisplayName: string.IsNullOrWhiteSpace(displayName) ? null : displayName,
                ChatType: chatType,
                Nickname: string.IsNullOrWhiteSpace(nickname) ? null : nickname);

            await RecordUserProfileSafeAsync(request);

            var response = await gateway.ExecuteAsync(request, _options.BotInstanceId);
            foreach (var line in renderer.Render(response).Where(line => !string.IsNullOrWhiteSpace(line)))
            {
                await SendTextAsync(chatType, chatId, line);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "处理 QQ 消息失败。message_id={MessageId}", message.MessageId);
        }
    }

    private async Task SendTextAsync(BotChatType chatType, string chatId, string text)
    {
        if (!long.TryParse(chatId, out var targetId))
        {
            logger.LogWarning("无效的 QQ 会话 ID：{ChatId}", chatId);
            return;
        }

        // 以「数组格式的纯文本段」发送，避免机器人输出中的方括号被 NapCat 当作 CQ 码解析。
        var segments = new[] { new { type = "text", data = new { text } } };
        var (action, parameters) = chatType == BotChatType.Group
            ? ("send_group_msg", (object)new { group_id = targetId, message = segments })
            : ("send_private_msg", new { user_id = targetId, message = segments });

        var response = await oneBotClient.SendActionAsync(new OneBotActionRequest(action, parameters));
        if (!response.IsSuccess)
        {
            logger.LogWarning(
                "OneBot {Action} 发送失败 retcode={RetCode} msg={Message}",
                action,
                response.RetCode,
                response.Message ?? response.Wording);
        }
    }

    private async Task RecordUserProfileSafeAsync(GatewayCommandRequest request)
    {
        try
        {
            await gateway.RecordUserProfileAsync(request, _options.BotInstanceId);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "记录 QQ 用户档案失败。uid={Uid}", request.UserId);
        }
    }

    private static string ExtractText(MessageEvent message)
    {
        // 拼接所有 text 段；命令通常只有一个 text 段。没有 text 段时回退到 raw_message。
        var text = string.Concat(
            message.GetMessagesByType(MessageType.Text)
                .Select(entity => entity.Parameters.TryGetValue("text", out var value) ? value : string.Empty));

        return string.IsNullOrWhiteSpace(text) ? message.Raw : text;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }
}
