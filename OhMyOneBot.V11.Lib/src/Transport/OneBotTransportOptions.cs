namespace OhMyOneBot.V11.Lib.Transport;

public abstract record OneBotTransportOptions
{
    public string? AccessToken { get; init; }
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
}

public sealed record OneBotHttpOptions : OneBotTransportOptions
{
    public required Uri BaseUri { get; init; }
}

public sealed record OneBotWebSocketOptions : OneBotTransportOptions
{
    public required Uri Uri { get; init; }
    public bool AutoReconnect { get; init; } = true;
    public TimeSpan ReconnectInterval { get; init; } = TimeSpan.FromSeconds(3);
}

public sealed record OneBotReverseWebSocketOptions : OneBotTransportOptions
{
    public required string Prefix { get; init; }
    public bool IgnoreInvalidToken { get; init; }
}
