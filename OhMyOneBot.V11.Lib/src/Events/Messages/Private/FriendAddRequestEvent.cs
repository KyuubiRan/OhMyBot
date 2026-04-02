using System.Text.Json.Serialization;

namespace OhMyOneBot.V11.Lib.Events.Messages.Private;

public class FriendAddRequestEvent : NoticeEvent
{
    [JsonPropertyName("comment")] public string Comment { get; init; } = string.Empty;
    [JsonPropertyName("user_id")] public long RequesterId { get; init; }
    [JsonPropertyName("flag")] public string Flag { get; init; } = string.Empty;
}