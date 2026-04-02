using System.Text.Json.Serialization;

namespace OhMyOneBot.V11.Lib.Events.Meta;

public class HeartBeatEvent : MetaEvent
{
    public class Status
    {
        [JsonPropertyName("online")] public bool Online { get; init; }
        [JsonPropertyName("good")] public bool Good { get; init; }
    }

    [JsonPropertyName("status")] public Status BotStatus { get; init; } = null!;
    [JsonPropertyName("interval")] public int Interval { get; init; }
}