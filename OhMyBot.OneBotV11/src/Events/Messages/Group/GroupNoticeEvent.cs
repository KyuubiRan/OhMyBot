using System.Text.Json.Serialization;

namespace OhMyBot.OneBotV11.Events.Messages.Group;

public class GroupNoticeEvent : NoticeEvent
{
    [JsonPropertyName("group_id")] public long GroupId { get; init; }
}