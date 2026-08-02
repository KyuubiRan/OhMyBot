using System.Text.Json.Serialization;

namespace OhMyBot.OneBotV11.Events.Messages.Group;

public class GroupPokeEvent : GroupNoticeEvent
{
    [JsonPropertyName("user_id")] public long SenderId { get; init; }
    [JsonPropertyName("target_id")] public long TargetId { get; init; }
}