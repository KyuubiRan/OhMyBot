using System.Text.Json.Serialization;

namespace OhMyOneBot.V11.Lib.Events.Messages.Group;

public class GroupMuteEvent : GroupNoticeEvent
{
    [JsonPropertyName("sub_type")] public string SubType { get; init; } = string.Empty;
    [JsonPropertyName("operator_id")] public long OperatorId { get; init; }
    [JsonPropertyName("user_id")] public long TargetId { get; init; }
    [JsonPropertyName("duration")] public int Duration { get; init; }

    public bool IsMuted => SubType == "ban";
    public bool IsUnmuted => SubType == "lift_ban";
}