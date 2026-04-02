using System.Text.Json.Serialization;

namespace OhMyOneBot.V11.Lib.Events.Messages;

public abstract class Sender
{
    [JsonPropertyName("user_id")] public long UserId { get; init; }
    [JsonPropertyName("nickname")] public string Nickname { get; init; } = string.Empty;
    [JsonPropertyName("sex")] public string Sex { get; init; } = string.Empty;
    [JsonPropertyName("age")] public int Age { get; init; }
}