using System.Text.Json;
using OhMyOneBot.V11.Lib.Events;
using OhMyOneBot.V11.Lib.Transport;

namespace OhMyOneBot.V11.Lib;

public sealed class OneBotClient : IOneBotClient, IAsyncDisposable
{
    private readonly IOneBotTransport _transport;

    public OneBotClient(IOneBotTransport transport)
    {
        _transport = transport;
        _transport.RawEventReceived += HandleRawEventReceivedAsync;
    }

    public OneBotTransportType TransportType => _transport.TransportType;
    public OneBotConnectionState ConnectionState => _transport.ConnectionState;

    public event Func<OneBotConnectionState, ValueTask>? ConnectionStateChanged
    {
        add => _transport.ConnectionStateChanged += value;
        remove => _transport.ConnectionStateChanged -= value;
    }

    public event Action<EventBase>? OnEvent;
    public event Action<Exception>? OnException;

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
        return _transport.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return _transport.StopAsync(cancellationToken);
    }

    public Task<OneBotActionResponse<JsonElement>> SendActionAsync(OneBotActionRequest request, CancellationToken cancellationToken = default)
    {
        return _transport.SendActionAsync(request, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _transport.RawEventReceived -= HandleRawEventReceivedAsync;
        await _transport.DisposeAsync();
    }

    private ValueTask HandleRawEventReceivedAsync(string rawEvent)
    {
        try
        {
            var evt = EventParser.Parse(rawEvent);
            if (evt is not null)
            {
                PublishEvent(evt);
            }
        }
        catch (Exception ex)
        {
            PublishException(ex);
        }

        return ValueTask.CompletedTask;
    }

    private void PublishEvent(EventBase evt)
    {
        if (OnEvent is null)
        {
            return;
        }

        foreach (var handler in OnEvent.GetInvocationList())
        {
            if (handler is not Action<EventBase> action)
            {
                continue;
            }

            try
            {
                action(evt);
            }
            catch (Exception ex)
            {
                PublishException(ex);
            }
        }
    }

    private void PublishException(Exception exception)
    {
        if (OnException is null)
        {
            return;
        }

        foreach (var handler in OnException.GetInvocationList())
        {
            if (handler is not Action<Exception> action)
            {
                continue;
            }

            try
            {
                action(exception);
            }
            catch
            {
                // Never let exception callbacks break the receive loop.
            }
        }
    }
}
