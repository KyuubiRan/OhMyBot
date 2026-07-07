using System.Text.Json;
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
// QQ 无官方可点击按钮，交互改用「编号文本菜单 + 回复序号」：Core 产出编号菜单纯文本，
// 网关发出后把「消息 id -> 选项」绑定进 Core Redis；用户回复序号时再交给 Core 执行选择。
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
            // 按段链类型解析：text 段拼接为命令/序号文本；reply 段取被回复消息 id；at 等其它段忽略。
            // 这样对 QQ 回复自动带的 @（用户可在输入框手删）都健壮，不做脆弱的按位置剥离。
            var text = ExtractText(message).Trim();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var replyToMessageId = message.GetMessageByType(MessageType.Reply)?.Parameters.GetValueOrDefault("id");

            // @ 目标：取第一个有效 at 段的 qq（排除 @全体 和 @机器人自己）。
            // 覆盖「/setpriv @某人」以及 QQ 回复时自动带的 @；纯 LINQ 无网络开销，可每条都算。
            var mentionedUserId = ExtractMentionedUserId(message);

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

            // 纯数字 = 菜单选择：群聊必须回复某条菜单，私聊可回复也可直接发数字（走最近菜单）。
            if (IsSelection(text) && (!string.IsNullOrEmpty(replyToMessageId) || chatType == BotChatType.Private))
            {
                var selectionResponse = await gateway.ExecuteMenuSelectionAsync(
                    request,
                    replyToMessageId,
                    text,
                    _options.BotInstanceId);
                await SendResponseAsync(selectionResponse, chatType, chatId, request.UserId, request.MessageId);
                return;
            }

            // 记录发送者档案（供 /info、setpriv 按 uid 查到未主动用过命令的人）。
            // Core 侧 RecordAsync 有缓存去重，相同档案不落库，只有新用户/改名才写；代价仅每条消息一次 gRPC。
            await RecordUserProfileSafeAsync(request);

            var (command, _) = GatewayCommandParser.Parse(text, _commandPrefixes);
            if (string.IsNullOrEmpty(command))
            {
                // 非命令消息只记录、不响应，避免群聊刷屏拖垮 Core。
                return;
            }

            // 「对某人」的命令（如 setpriv）需要目标 uid：优先用 @ 到的人；
            // 否则若是回复消息，用 get_msg 反查被回复消息的发送者（QQ 回复段只带消息 id，不带发送者）。
            var replyToUserId = mentionedUserId;
            if (string.IsNullOrEmpty(replyToUserId) && !string.IsNullOrEmpty(replyToMessageId))
            {
                replyToUserId = await ResolveReplySenderIdAsync(replyToMessageId);
            }

            var commandRequest = string.IsNullOrEmpty(replyToUserId)
                ? request
                : request with { ReplyToUserId = replyToUserId };

            var response = await gateway.ExecuteAsync(commandRequest, _options.BotInstanceId);
            await SendResponseAsync(response, chatType, chatId, request.UserId, request.MessageId);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "处理 QQ 消息失败。message_id={MessageId}", message.MessageId);
        }
    }

    // 发送响应的每条消息（引用回复触发消息）；带 MenuToken 的消息发出后绑定「消息 id -> 选项」到 Core。
    private async Task SendResponseAsync(
        CommandResponse response,
        BotChatType chatType,
        string chatId,
        string senderId,
        string replyToMessageId)
    {
        foreach (var rendered in renderer.Render(response))
        {
            var sentMessageId = await SendTextAsync(chatType, chatId, rendered.Text, replyToMessageId);
            if (!string.IsNullOrEmpty(rendered.MenuToken) && !string.IsNullOrEmpty(sentMessageId))
            {
                await BindMenuSafeAsync(chatId, sentMessageId, senderId, chatType, rendered.MenuToken);
            }
        }
    }

    // 返回已发出消息的 message_id（供菜单绑定）；失败返回 null。
    private async Task<string?> SendTextAsync(BotChatType chatType, string chatId, string text, string? replyToMessageId)
    {
        if (!long.TryParse(chatId, out var targetId))
        {
            logger.LogWarning("无效的 QQ 会话 ID：{ChatId}", chatId);
            return null;
        }

        // 以「数组格式的段链」发送：可选的 reply 段做引用回复（须在开头），再加纯文本段
        // （避免机器人输出中的方括号被 NapCat 当作 CQ 码解析）。
        var segments = new List<object>();
        if (!string.IsNullOrEmpty(replyToMessageId))
        {
            segments.Add(new { type = "reply", data = new { id = replyToMessageId } });
        }

        segments.Add(new { type = "text", data = new { text } });

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
            return null;
        }

        return ExtractMessageId(response.Data);
    }

    private async Task BindMenuSafeAsync(
        string chatId,
        string messageId,
        string senderId,
        BotChatType chatType,
        string menuToken)
    {
        try
        {
            await gateway.BindMenuAsync(chatId, messageId, senderId, chatType, menuToken, _options.BotInstanceId);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "绑定 QQ 菜单失败。message_id={MessageId}", messageId);
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

    // 取第一个有效 at 段的 qq（排除 @全体 "all"）。
    // 不排除机器人自身：显式 @ 就是明确目标，@到谁就是谁（含 @机器人本身）。
    private static string? ExtractMentionedUserId(MessageEvent message)
    {
        return message.GetMessagesByType(MessageType.At)
            .Select(entity => entity.Parameters.GetValueOrDefault("qq"))
            .FirstOrDefault(qq => !string.IsNullOrWhiteSpace(qq)
                && !string.Equals(qq, "all", StringComparison.OrdinalIgnoreCase));
    }

    // 用 get_msg 反查被回复消息的发送者 uid；失败返回 null（命令侧会退回「缺目标」提示）。
    private async Task<string?> ResolveReplySenderIdAsync(string replyToMessageId)
    {
        if (!long.TryParse(replyToMessageId, out var messageId))
        {
            return null;
        }

        try
        {
            var response = await oneBotClient.SendActionAsync(
                new OneBotActionRequest("get_msg", new { message_id = messageId }));
            if (!response.IsSuccess || response.Data.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (response.Data.TryGetProperty("sender", out var sender)
                && sender.ValueKind == JsonValueKind.Object
                && sender.TryGetProperty("user_id", out var userId))
            {
                return userId.ValueKind switch
                {
                    JsonValueKind.Number => userId.GetRawText(),
                    JsonValueKind.String => userId.GetString(),
                    _ => null
                };
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "get_msg 反查被回复发送者失败。message_id={MessageId}", replyToMessageId);
        }

        return null;
    }

    private static string? ExtractMessageId(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("message_id", out var element))
        {
            return element.ValueKind switch
            {
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.String => element.GetString(),
                _ => null
            };
        }

        return null;
    }

    // 纯数字（1-based 序号）。避免误判超长串，限制长度。
    private static bool IsSelection(string text)
    {
        return text.Length is > 0 and <= 6 && text.All(char.IsDigit);
    }

    private static string ExtractText(MessageEvent message)
    {
        // 拼接所有 text 段；命令/序号通常只有一个 text 段。没有 text 段时回退到 raw_message。
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
