using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OhMyOneBot.V11.Lib.Transport;

internal sealed class ReverseWebSocketOneBotTransport(OneBotReverseWebSocketOptions options)
    : OneBotTransportBase(OneBotTransportType.ReverseWebSocket)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<OneBotActionResponse<JsonElement>>> _pendingActions = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private HttpListener? _listener;
    private WebSocket? _webSocket;
    private CancellationTokenSource? _loopCts;
    private Task? _acceptLoopTask;
    private TaskCompletionSource<bool>? _firstConnectionTcs;
    private volatile bool _stopping;

    public OneBotReverseWebSocketOptions Options { get; } = options ?? throw new ArgumentNullException(nameof(options));

    protected override async Task OnStartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Options.Prefix))
        {
            throw new InvalidOperationException("Reverse WebSocket Prefix cannot be empty.");
        }

        if (!Uri.TryCreate(Options.Prefix, UriKind.Absolute, out var prefixUri))
        {
            throw new InvalidOperationException("Reverse WebSocket Prefix must be an absolute URI.");
        }

        var scheme = prefixUri.Scheme;
        if (!string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Reverse WebSocket Prefix must use http scheme for HttpListener.");
        }

        _stopping = false;
        _firstConnectionTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _loopCts?.Dispose();
        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _listener = new HttpListener();
        _listener.Prefixes.Add(Options.Prefix);
        _listener.Start();

        _acceptLoopTask = Task.Run(() => RunAcceptLoopAsync(_loopCts.Token), CancellationToken.None);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_loopCts.Token);
        timeoutCts.CancelAfter(Options.ConnectTimeout);
        await _firstConnectionTcs.Task.WaitAsync(timeoutCts.Token);
    }

    protected override async Task OnStopAsync(CancellationToken cancellationToken)
    {
        _stopping = true;
        if (_loopCts is not null)
            await _loopCts.CancelAsync();

        try
        {
            _listener?.Stop();
            _listener?.Close();
        }
        catch
        {
            // ignore listener close failures
        }
        finally
        {
            _listener = null;
        }

        var socket = _webSocket;
        _webSocket = null;
        if (socket is not null)
        {
            try
            {
                if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server stopping", cancellationToken);
                }
            }
            catch
            {
                // ignore close failures
            }
            finally
            {
                socket.Dispose();
            }
        }

        if (_acceptLoopTask is not null)
        {
            try
            {
                await _acceptLoopTask;
            }
            catch
            {
                // loop errors are handled in loop
            }
        }

        FailPendingActions(new OperationCanceledException("Reverse WebSocket transport stopped."));
        _firstConnectionTcs?.TrySetCanceled(cancellationToken);
        _firstConnectionTcs = null;
        _loopCts?.Dispose();
        _loopCts = null;
        _acceptLoopTask = null;
    }

    protected override async Task<OneBotActionResponse<JsonElement>> OnSendActionAsync(OneBotActionRequest request, CancellationToken cancellationToken)
    {
        var socket = _webSocket;
        if (ConnectionState != OneBotConnectionState.Connected || socket is null || socket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("Reverse WebSocket transport is not connected.");
        }

        var actionName = request.Action.Trim();
        if (string.IsNullOrWhiteSpace(actionName))
        {
            throw new ArgumentException("Action name cannot be empty.", nameof(request));
        }

        var echo = string.IsNullOrWhiteSpace(request.Echo) ? Guid.NewGuid().ToString("N") : request.Echo!;
        var payload = BuildWebSocketPayload(request, echo);
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(payloadJson);

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
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
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

    private async Task RunAcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && !_stopping)
        {
            HttpListenerContext? ctx;
            try
            {
                var listener = _listener;
                if (listener is null || !listener.IsListening)
                {
                    return;
                }

                ctx = await listener.GetContextAsync().WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _stopping)
            {
                return;
            }
            catch (HttpListenerException)
            {
                if (_stopping || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                await SetConnectionStateAsync(OneBotConnectionState.Faulted);
                return;
            }

            if (!AuthorizeRequest(ctx.Request))
            {
                await RespondAsync(ctx.Response, 401, "Unauthorized");
                continue;
            }

            if (!ctx.Request.IsWebSocketRequest)
            {
                await RespondAsync(ctx.Response, 400, "WebSocket request required");
                continue;
            }

            if (_webSocket is { State: WebSocketState.Open or WebSocketState.CloseReceived })
            {
                await RespondAsync(ctx.Response, 409, "Already connected");
                continue;
            }

            HttpListenerWebSocketContext wsContext;
            try
            {
                wsContext = await ctx.AcceptWebSocketAsync(subProtocol: null);
            }
            catch
            {
                await RespondAsync(ctx.Response, 500, "Failed to accept WebSocket");
                continue;
            }

            _webSocket = wsContext.WebSocket;
            _firstConnectionTcs?.TrySetResult(true);
            await SetConnectionStateAsync(OneBotConnectionState.Connected);

            try
            {
                await RunConnectionLoopAsync(wsContext.WebSocket, cancellationToken);
            }
            finally
            {
                try
                {
                    wsContext.WebSocket.Dispose();
                }
                catch
                {
                    // ignore dispose failures
                }

                _webSocket = null;
                FailPendingActions(new WebSocketException("Reverse WebSocket disconnected."));
                if (!_stopping && !cancellationToken.IsCancellationRequested)
                {
                    await SetConnectionStateAsync(OneBotConnectionState.Reconnecting);
                }
            }
        }
    }

    private async Task RunConnectionLoopAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && !_stopping && socket.State == WebSocketState.Open)
        {
            string? raw;
            try
            {
                raw = await ReceiveMessageAsync(socket, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _stopping)
            {
                return;
            }

            if (raw is null)
            {
                return;
            }

            await HandleIncomingPayloadAsync(raw);
        }
    }

    private bool AuthorizeRequest(HttpListenerRequest request)
    {
        if (string.IsNullOrWhiteSpace(Options.AccessToken) || Options.IgnoreInvalidToken)
        {
            return true;
        }

        var auth = request.Headers["Authorization"];
        if (string.IsNullOrWhiteSpace(auth))
        {
            return false;
        }

        const string bearer = "Bearer ";
        if (!auth.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var token = auth[bearer.Length..].Trim();
        return string.Equals(token, Options.AccessToken, StringComparison.Ordinal);
    }

    private static async Task RespondAsync(HttpListenerResponse response, int statusCode, string message)
    {
        response.StatusCode = statusCode;
        response.ContentType = "text/plain; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(message);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static async Task<string?> ReceiveMessageAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[8 * 1024];

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
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
                tcs.TrySetException(new InvalidOperationException("Failed to deserialize OneBot action response from Reverse WebSocket payload."));
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
            throw new InvalidOperationException("Reverse WebSocket action params must serialize to a JSON object.");
        }

        payload["params"] = obj;
        return payload;
    }
}