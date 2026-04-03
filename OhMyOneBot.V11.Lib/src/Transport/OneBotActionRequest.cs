using System.Text.Json.Serialization;

namespace OhMyOneBot.V11.Lib.Transport;

public sealed record OneBotActionRequest(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("params")] object? Params = null,
    [property: JsonPropertyName("echo")] string? Echo = null
);
