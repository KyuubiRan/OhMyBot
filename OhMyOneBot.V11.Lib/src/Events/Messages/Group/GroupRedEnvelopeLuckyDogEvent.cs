using System.Text.Json.Serialization;

namespace OhMyOneBot.V11.Lib.Events.Messages.Group;

public class GroupRedEnvelopeLuckyDogEvent : GroupNoticeEvent
{
    [JsonPropertyName("sub_type")] public string SubType { get; init; } = string.Empty;
    [JsonPropertyName("user_id")] public long RedEnvelopeSenderId { get; init; }
    [JsonPropertyName("target_id")] public long LuckyDogId { get; init; }
}