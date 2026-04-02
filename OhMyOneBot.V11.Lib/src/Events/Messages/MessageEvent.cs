using System.Text.Json.Serialization;
using OhMyOneBot.V11.Lib.Messages.Entity;

namespace OhMyOneBot.V11.Lib.Events.Messages;

public abstract class MessageEvent : EventBase
{
    // private/group
    [JsonPropertyName("message_type")] public string MessageType { get; init; } = string.Empty;

    // Private: friend - 好友消息 | group - 群临时会话 | other - 其他
    // Group: normal - 正常消息 | anonymous - 匿名消息 | notice - 系统提示（如「管理员已禁止群内匿名聊天」）
    [JsonPropertyName("sub_type")] public string SubType { get; init; } = string.Empty;

    // 消息 ID
    [JsonPropertyName("message_id")] public long MessageId { get; init; }

    // 发送者 QQ 号
    [JsonPropertyName("user_id")] public long SenderId { get; init; }

    // 消息内容
    [JsonPropertyName("message")] public List<MessageEntity> Message { get; init; } = [];

    // 原始消息内容
    [JsonPropertyName("raw_message")] public string Raw { get; init; } = string.Empty;

    // 字体
    [JsonPropertyName("font")] public int Font { get; init; }

    // 发送人信息
    [JsonPropertyName("sender")] public Sender Sender { get; init; } = null!;
}