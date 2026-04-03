using System.Text.Json;

namespace OhMyOneBot.V11.Lib.Transport;

public interface IOneBotTransport : IAsyncDisposable
{
    OneBotTransportType TransportType { get; }
    OneBotConnectionState ConnectionState { get; }

    event Func<OneBotConnectionState, ValueTask>? ConnectionStateChanged;
    event Func<string, ValueTask>? RawEventReceived;

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task<OneBotActionResponse<JsonElement>> SendActionAsync(OneBotActionRequest request, CancellationToken cancellationToken = default);
}
