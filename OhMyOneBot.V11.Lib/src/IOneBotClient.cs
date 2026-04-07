using System.Text.Json;
using OhMyOneBot.V11.Lib.Events;
using OhMyOneBot.V11.Lib.Transport;

namespace OhMyOneBot.V11.Lib;

public interface IOneBotClient
{
    OneBotTransportType TransportType { get; }
    OneBotConnectionState ConnectionState { get; }

    event Func<OneBotConnectionState, ValueTask>? ConnectionStateChanged;
    event Action<EventBase>? OnEvent;
    event Action<Exception>? OnException;

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task<OneBotActionResponse<JsonElement>> SendActionAsync(OneBotActionRequest request, CancellationToken cancellationToken = default);
}
