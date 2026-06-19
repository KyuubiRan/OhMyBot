using System.Text.Json.Serialization;

namespace OhMyBot.OneBotV11.Events.Messages.Private;

public class PrivateMessageRevokeEvent : NoticeEvent
{
    [JsonPropertyName("user_id")] public long SenderId { get; init; }
    [JsonPropertyName("message_id")] public long MessageId { get; init; }
}