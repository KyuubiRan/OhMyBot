using System.Text.Json.Serialization;

namespace OhMyOneBot.V11.Lib.Events.Messages.Group;

public class GroupMemberHonorChangeEvent : NoticeEvent
{
    [JsonPropertyName("user_id")] public long TargetId { get; init; }
    [JsonPropertyName("honor_type")] public string HonorType { get; init; } = string.Empty;

    // 龙王
    public bool IsDragon => HonorType == "talkative";

    // 群聊之火
    public bool IsFire => HonorType == "performer";

    // 快乐源泉
    public bool IsSpringWater => HonorType == "emotion";
}