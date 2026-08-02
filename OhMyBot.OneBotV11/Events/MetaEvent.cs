using System.Text.Json.Serialization;

namespace OhMyBot.OneBotV11.Events;

public abstract class MetaEvent : EventBase
{
    [JsonPropertyName("meta_event_type")] public string MetaEventType { get; init; } = string.Empty;
}