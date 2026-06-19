using System.Text.Json;
using OhMyBot.OneBotV11.Events;
using OhMyBot.OneBotV11.Transport;

namespace OhMyBot.OneBotV11;

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
