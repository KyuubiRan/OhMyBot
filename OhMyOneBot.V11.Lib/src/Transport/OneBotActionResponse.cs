using System.Text.Json.Serialization;

namespace OhMyOneBot.V11.Lib.Transport;

public sealed record OneBotActionResponse<TData>
{
    [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;
    [JsonPropertyName("retcode")] public int RetCode { get; init; }
    [JsonPropertyName("data")] public TData? Data { get; init; }
    [JsonPropertyName("message")] public string? Message { get; init; }
    [JsonPropertyName("wording")] public string? Wording { get; init; }
    [JsonPropertyName("echo")] public string? Echo { get; init; }

    public bool IsSuccess => string.Equals(Status, "ok", StringComparison.OrdinalIgnoreCase) && RetCode == 0;
}
