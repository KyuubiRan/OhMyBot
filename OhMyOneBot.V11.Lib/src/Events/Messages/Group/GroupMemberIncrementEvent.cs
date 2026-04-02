using System.Text.Json.Serialization;

namespace OhMyOneBot.V11.Lib.Events.Messages.Group;

public class GroupMemberIncrementEvent : GroupNoticeEvent
{
    [JsonPropertyName("sub_type")] public string SubType { get; init; } = string.Empty;
    [JsonPropertyName("operator_id")] public long OperatorId { get; init; }
    [JsonPropertyName("user_id")] public long TargetId { get; init; }

    public bool IsInvite => SubType == "invite";
    public bool IsApprove => SubType == "approve";
}