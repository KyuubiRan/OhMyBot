using System.Text.Json.Serialization;

namespace OhMyOneBot.V11.Lib.Events.Messages.Group;

public class GroupMemberDecrementEvent : GroupNoticeEvent
{
    [JsonPropertyName("sub_type")] public string SubType { get; init; } = string.Empty;
    [JsonPropertyName("operator_id")] public long OperatorId { get; init; }
    [JsonPropertyName("user_id")] public long TargetId { get; init; }

    public bool IsMemberLeave => SubType == "leave";
    public bool IsMemberKicked => SubType == "kick";
    public bool IsKickedMe => SubType == "kick_me";
}