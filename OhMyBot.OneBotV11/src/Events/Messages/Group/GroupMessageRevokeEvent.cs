using System.Text.Json.Serialization;

namespace OhMyBot.OneBotV11.Events.Messages.Group;

public class GroupMessageRevokeEvent : GroupNoticeEvent
{
    [JsonPropertyName("operator_id")] public long OperatorId { get; init; }
    [JsonPropertyName("user_id")] public long SenderId { get; init; }
    [JsonPropertyName("message_id")] public long MessageId { get; init; }
}