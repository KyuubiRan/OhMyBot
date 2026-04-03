using System.Text.Json;

namespace OhMyOneBot.V11.Lib.Transport;

public abstract class OneBotTransportBase(OneBotTransportType transportType) : IOneBotTransport
{
    private bool _disposed;

    public OneBotTransportType TransportType { get; } = transportType;

    public OneBotConnectionState ConnectionState { get; private set; } = OneBotConnectionState.Stopped;

    public event Func<OneBotConnectionState, ValueTask>? ConnectionStateChanged;
    public event Func<string, ValueTask>? RawEventReceived;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (ConnectionState is not (OneBotConnectionState.Stopped or OneBotConnectionState.Faulted))
        {
            return;
        }

        await SetConnectionStateAsync(OneBotConnectionState.Connecting);
        try
        {
            await OnStartAsync(cancellationToken);
            await SetConnectionStateAsync(OneBotConnectionState.Connected);
        }
        catch
        {
            await SetConnectionStateAsync(OneBotConnectionState.Faulted);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || ConnectionState is OneBotConnectionState.Stopped)
        {
            return;
        }

        try
        {
            await OnStopAsync(cancellationToken);
        }
        finally
        {
            await SetConnectionStateAsync(OneBotConnectionState.Stopped);
        }
    }

    public Task<OneBotActionResponse<JsonElement>> SendActionAsync(OneBotActionRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return OnSendActionAsync(request, cancellationToken);
    }

    protected async Task PublishRawEventAsync(string rawPayload)
    {
        if (RawEventReceived is null)
        {
            return;
        }

        var delegates = RawEventReceived.GetInvocationList();
        foreach (var d in delegates)
        {
            if (d is Func<string, ValueTask> handler)
            {
                await handler(rawPayload);
            }
        }
    }

    protected async Task SetConnectionStateAsync(OneBotConnectionState state)
    {
        if (ConnectionState == state)
        {
            return;
        }

        ConnectionState = state;
        if (ConnectionStateChanged is null)
        {
            return;
        }

        var delegates = ConnectionStateChanged.GetInvocationList();
        foreach (var d in delegates)
        {
            if (d is Func<OneBotConnectionState, ValueTask> handler)
            {
                await handler(state);
            }
        }
    }

    protected virtual Task OnStartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected virtual Task OnStopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected abstract Task<OneBotActionResponse<JsonElement>> OnSendActionAsync(OneBotActionRequest request, CancellationToken cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync();
        await OnDisposeAsync();
        await SetConnectionStateAsync(OneBotConnectionState.Disposed);
    }

    protected virtual ValueTask OnDisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    protected void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
