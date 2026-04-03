using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OhMyOneBot.V11.Lib.Transport;

internal sealed class WebSocketOneBotTransport(OneBotWebSocketOptions options) : OneBotTransportBase(OneBotTransportType.WebSocket)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<OneBotActionResponse<JsonElement>>> _pendingActions = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _loopCts;
    private Task? _receiveLoopTask;
    private volatile bool _stopping;

    public OneBotWebSocketOptions Options { get; } = options ?? throw new ArgumentNullException(nameof(options));

    protected override async Task OnStartAsync(CancellationToken cancellationToken)
    {
        if (!Options.Uri.IsAbsoluteUri)
        {
            throw new InvalidOperationException("WebSocket transport Uri must be an absolute URI.");
        }

        var scheme = Options.Uri.Scheme;
        if (!string.Equals(scheme, "ws", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(scheme, "wss", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("WebSocket transport Uri must use ws or wss.");
        }

        _stopping = false;
        _loopCts?.Dispose();
        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        await ConnectSocketAsync(_loopCts.Token);
        _receiveLoopTask = Task.Run(() => RunReceiveLoopAsync(_loopCts.Token), CancellationToken.None);
    }

    protected override async Task OnStopAsync(CancellationToken cancellationToken)
    {
        _stopping = true;
        if (_loopCts is not null)
            await _loopCts.CancelAsync();

        var socket = _webSocket;
        if (socket is not null)
        {
            try
            {
                if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client stopping", cancellationToken);
                }
            }
            catch
            {
                // ignore close failures while stopping
            }
            finally
            {
                socket.Dispose();
            }
        }

        _webSocket = null;

        if (_receiveLoopTask is not null)
        {
            try
            {
                await _receiveLoopTask;
            }
            catch
            {
                // receive loop errors are already handled internally
            }
        }

        FailPendingActions(new OperationCanceledException("WebSocket transport stopped."));
        _loopCts?.Dispose();
        _loopCts = null;
        _receiveLoopTask = null;
    }

    protected override async Task<OneBotActionResponse<JsonElement>> OnSendActionAsync(OneBotActionRequest request, CancellationToken cancellationToken)
    {
        var socket = _webSocket;
        if (ConnectionState != OneBotConnectionState.Connected || socket is null || socket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("WebSocket transport is not connected.");
        }

        var actionName = request.Action.Trim();
        if (string.IsNullOrWhiteSpace(actionName))
        {
            throw new ArgumentException("Action name cannot be empty.", nameof(request));
        }

        var echo = string.IsNullOrWhiteSpace(request.Echo) ? Guid.NewGuid().ToString("N") : request.Echo!;
        var payload = BuildWebSocketPayload(request, echo);
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var payloadBuffer = Encoding.UTF8.GetBytes(payloadJson);

        var tcs = new TaskCompletionSource<OneBotActionResponse<JsonElement>>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingActions.TryAdd(echo, tcs))
        {
            throw new InvalidOperationException($"Duplicate echo '{echo}' detected.");
        }

        try
        {
            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                await socket.SendAsync(payloadBuffer, WebSocketMessageType.Text, true, cancellationToken);
            }
            finally
            {
                _sendLock.Release();
            }

            return await tcs.Task.WaitAsync(cancellationToken);
        }
        catch
        {
            _pendingActions.TryRemove(echo, out _);
            throw;
        }
    }

    protected override async ValueTask OnDisposeAsync()
    {
        _sendLock.Dispose();
        await base.OnDisposeAsync();
    }

    private async Task RunReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && !_stopping)
        {
            var socket = _webSocket;
            if (socket is null || socket.State != WebSocketState.Open)
            {
                if (!await TryReconnectAsync(cancellationToken))
                {
                    return;
                }

                continue;
            }

            try
            {
                var raw = await ReceiveMessageAsync(socket, cancellationToken);
                if (raw is null)
                {
                    if (!await TryReconnectAsync(cancellationToken))
                    {
                        return;
                    }

                    continue;
                }

                await HandleIncomingPayloadAsync(raw);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _stopping)
            {
                return;
            }
            catch (Exception ex)
            {
                FailPendingActions(ex);
                if (!await TryReconnectAsync(cancellationToken))
                {
                    await SetConnectionStateAsync(OneBotConnectionState.Faulted);
                    return;
                }
            }
        }
    }

    private async Task<bool> TryReconnectAsync(CancellationToken cancellationToken)
    {
        if (!Options.AutoReconnect || _stopping || cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        await SetConnectionStateAsync(OneBotConnectionState.Reconnecting);

        while (!cancellationToken.IsCancellationRequested && !_stopping)
        {
            try
            {
                await Task.Delay(Options.ReconnectInterval, cancellationToken);
                await ConnectSocketAsync(cancellationToken);
                await SetConnectionStateAsync(OneBotConnectionState.Connected);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _stopping)
            {
                return false;
            }
            catch
            {
                // keep retrying
            }
        }

        return false;
    }

    private async Task ConnectSocketAsync(CancellationToken cancellationToken)
    {
        _webSocket?.Dispose();
        var socket = new ClientWebSocket();

        if (!string.IsNullOrWhiteSpace(Options.AccessToken))
        {
            socket.Options.SetRequestHeader("Authorization", $"Bearer {Options.AccessToken}");
        }

        await socket.ConnectAsync(Options.Uri, cancellationToken);
        _webSocket = socket;
    }

    private static async Task<string?> ReceiveMessageAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[8 * 1024];

        while (true)
        {
            var segment = new ArraySegment<byte>(buffer);
            var result = await socket.ReceiveAsync(segment, cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.Count > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
            }

            if (result.EndOfMessage)
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    private async Task HandleIncomingPayloadAsync(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        if (root.TryGetProperty("post_type", out _))
        {
            await PublishRawEventAsync(raw);
            return;
        }

        if (!root.TryGetProperty("echo", out var echoElement) || echoElement.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var echo = echoElement.GetString();
        if (string.IsNullOrWhiteSpace(echo) || !_pendingActions.TryRemove(echo, out var tcs))
        {
            return;
        }

        try
        {
            var response = JsonSerializer.Deserialize<OneBotActionResponse<JsonElement>>(raw, JsonOptions);
            if (response is null)
            {
                tcs.TrySetException(new InvalidOperationException("Failed to deserialize OneBot action response from WebSocket payload."));
                return;
            }

            tcs.TrySetResult(response);
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }
    }

    private void FailPendingActions(Exception ex)
    {
        foreach (var pair in _pendingActions)
        {
            if (_pendingActions.TryRemove(pair.Key, out var tcs))
            {
                tcs.TrySetException(ex);
            }
        }
    }

    private static JsonObject BuildWebSocketPayload(OneBotActionRequest request, string echo)
    {
        var payload = new JsonObject
        {
            ["action"] = request.Action,
            ["echo"] = echo
        };

        if (request.Params is null)
        {
            payload["params"] = new JsonObject();
            return payload;
        }

        var paramsNode = JsonSerializer.SerializeToNode(request.Params, JsonOptions);
        if (paramsNode is not JsonObject obj)
        {
            throw new InvalidOperationException("WebSocket action params must serialize to a JSON object.");
        }

        payload["params"] = obj;
        return payload;
    }
}
