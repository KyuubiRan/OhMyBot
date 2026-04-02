using System.Text.Json.Serialization;

namespace OhMyOneBot.V11.Lib.Events.Messages.Group;

public class GroupNoticeEvent : NoticeEvent
{
    [JsonPropertyName("group_id")] public long GroupId { get; init; }
}