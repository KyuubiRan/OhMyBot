using System.Text.Json;
using OhMyOneBot.V11.Lib.Transport;

namespace OhMyOneBot.V11.Lib;

public sealed class OneBotClient(IOneBotTransport transport) : IOneBotClient, IAsyncDisposable
{
    public OneBotTransportType TransportType => transport.TransportType;
    public OneBotConnectionState ConnectionState => transport.ConnectionState;

    public event Func<OneBotConnectionState, ValueTask>? ConnectionStateChanged
    {
        add => transport.ConnectionStateChanged += value;
        remove => transport.ConnectionStateChanged -= value;
    }

    public event Func<string, ValueTask>? RawEventReceived
    {
        add => transport.RawEventReceived += value;
        remove => transport.RawEventReceived -= value;
    }

    public static OneBotClient CreateHttpClient(OneBotHttpOptions options)
    {
        return new OneBotClient(new HttpOneBotTransport(options));
    }

    public static OneBotClient CreateWebsocketClient(OneBotWebSocketOptions options)
    {
        return new OneBotClient(new WebSocketOneBotTransport(options));
    }

    public static OneBotClient CreateReversedWebsocketClient(OneBotReverseWebSocketOptions options)
    {
        return new OneBotClient(new ReverseWebSocketOneBotTransport(options));
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        return transport.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return transport.StopAsync(cancellationToken);
    }

    public Task<OneBotActionResponse<JsonElement>> SendActionAsync(OneBotActionRequest request, CancellationToken cancellationToken = default)
    {
        return transport.SendActionAsync(request, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return transport.DisposeAsync();
    }
}
