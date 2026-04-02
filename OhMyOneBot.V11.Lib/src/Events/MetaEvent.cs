using System.Text.Json.Serialization;

namespace OhMyOneBot.V11.Lib.Events;

public abstract class MetaEvent : EventBase
{
    [JsonPropertyName("meta_event_type")] public string MetaEventType { get; init; } = string.Empty;
}