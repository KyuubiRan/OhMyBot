using System.Text.Json.Serialization;

namespace OhMyBot.OneBotV11.Events;

public abstract class NoticeEvent : EventBase
{
    [JsonPropertyName("notice_type")] public string NoticeType { get; init; } = string.Empty;
}