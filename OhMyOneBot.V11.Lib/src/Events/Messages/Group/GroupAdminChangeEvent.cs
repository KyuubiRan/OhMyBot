using System.Text.Json.Serialization;

namespace OhMyOneBot.V11.Lib.Events.Messages.Group;

public class GroupAdminChangeEvent : GroupNoticeEvent
{
    // set | unset
    [JsonPropertyName("sub_type")] public string SubType { get; init; } = string.Empty;
    [JsonPropertyName("user_id")] public long UserId { get; init; }

    public bool IsSetAdminEvent => SubType == "set";
    public bool IsUnSetAdminEvent => SubType == "unset";
}