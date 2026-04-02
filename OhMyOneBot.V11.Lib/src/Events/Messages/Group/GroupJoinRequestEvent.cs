using System.Text.Json.Serialization;

namespace OhMyOneBot.V11.Lib.Events.Messages.Group;

public class GroupJoinRequestEvent : GroupNoticeEvent
{
    [JsonPropertyName("sub_type")] public string SubType { get; init; } = string.Empty;
    [JsonPropertyName("comment")] public string Comment { get; init; } = string.Empty;
    [JsonPropertyName("user_id")] public long RequesterId { get; init; }
    [JsonPropertyName("flag")] public string Flag { get; init; } = string.Empty;

    public bool IsAdd => SubType == "add";
    public bool IsInvite => SubType == "invite";
}