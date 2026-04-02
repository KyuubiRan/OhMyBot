using System.Text.Json.Serialization;

namespace OhMyOneBot.V11.Lib.Events.Meta;

public class LifetimeEvent : MetaEvent
{
    [JsonPropertyName("sub_type")] public string SubType { get; init; } = string.Empty;

    public bool IsBotStarted => SubType == "enable";
    public bool IsBotStopped => SubType == "disable";
    public bool IsWebsocketConnected => SubType == "connect";
}