using System.Text.Json.Serialization;

namespace OhMyOneBot.V11.Lib.Events;

public abstract class NoticeEvent : EventBase
{
    [JsonPropertyName("notice_type")] public string NoticeType { get; init; } = string.Empty;
}