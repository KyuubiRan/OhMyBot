using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace OhMyBot.Core.Integrations.Happytuk;

// Minimal Chrome DevTools Protocol driver launched against a real Chrome/Chromium.
// The whole point is to NOT look like Playwright/Puppeteer to Cloudflare: those tools
// call Runtime.enable (and inject bindings) at startup, which is the primary signal CF
// uses to flag automation. We only ever issue one-shot Runtime.evaluate calls and never
// enable any domain, so the browser is driven the way nodriver/undetected-chromedriver do.
internal sealed class HappytukCdpClient : IAsyncDisposable
{
    private readonly Process _process;
    private readonly ClientWebSocket _socket;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _receiveCts = new();
    private readonly Task _receiveLoop;
    private int _commandId;

    private HappytukCdpClient(Process process, ClientWebSocket socket)
    {
        _process = process;
        _socket = socket;
        _receiveLoop = Task.Run(ReceiveLoopAsync);
    }

    public static async Task<HappytukCdpClient> LaunchAsync(CdpLaunchOptions options, CancellationToken cancellationToken)
    {
        var port = GetFreePort();
        var startInfo = new ProcessStartInfo(options.ExecutablePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in BuildArguments(options, port))
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 Chrome 进程");
        // Chrome is chatty on stderr/stdout; drain both so its pipe buffers never fill and stall it.
        _ = process.StandardOutput.ReadToEndAsync(cancellationToken);
        _ = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            var webSocketUrl = await DiscoverPageSocketAsync(port, options.Timeout, cancellationToken);
            var socket = new ClientWebSocket();
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(options.Timeout);
            await socket.ConnectAsync(new Uri(webSocketUrl), connectCts.Token);
            return new HappytukCdpClient(process, socket);
        }
        catch
        {
            TryKill(process);
            throw;
        }
    }

    public Task NavigateAsync(string url, CancellationToken cancellationToken) =>
        SendCommandAsync("Page.navigate", new { url }, cancellationToken);

    // One-shot evaluate in the page's default context. Never enables Runtime, so it stays
    // invisible to the CF automation checks that key on Runtime.enable.
    public async Task<JsonElement> EvaluateAsync(string expression, CancellationToken cancellationToken)
    {
        var result = await SendCommandAsync(
            "Runtime.evaluate",
            new { expression, returnByValue = true, awaitPromise = true },
            cancellationToken);
        if (result.TryGetProperty("exceptionDetails", out var details))
        {
            throw new InvalidOperationException("页面脚本执行异常：" + details.GetRawText());
        }

        return result.GetProperty("result").TryGetProperty("value", out var value)
            ? value.Clone()
            : default;
    }

    public async Task<string> GetUserAgentAsync(CancellationToken cancellationToken) =>
        (await EvaluateAsync("navigator.userAgent", cancellationToken)).GetString() ?? string.Empty;

    // Network.getAllCookies is callable without Network.enable and returns HttpOnly cookies
    // (including cf_clearance), which document.cookie cannot.
    public async Task<string> GetCookieHeaderAsync(string domainNeedle, CancellationToken cancellationToken)
    {
        var result = await SendCommandAsync("Network.getAllCookies", null, cancellationToken);
        var pairs = new List<string>();
        foreach (var cookie in result.GetProperty("cookies").EnumerateArray())
        {
            var domain = cookie.TryGetProperty("domain", out var d) ? d.GetString() ?? string.Empty : string.Empty;
            if (!domain.Contains(domainNeedle, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = cookie.GetProperty("name").GetString();
            var value = cookie.GetProperty("value").GetString();
            if (!string.IsNullOrEmpty(name))
            {
                pairs.Add($"{name}={value}");
            }
        }

        return string.Join("; ", pairs);
    }

    public async Task<byte[]?> CaptureScreenshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await SendCommandAsync("Page.captureScreenshot", new { format = "png" }, cancellationToken);
            var data = result.GetProperty("data").GetString();
            return string.IsNullOrEmpty(data) ? null : Convert.FromBase64String(data);
        }
        catch
        {
            return null;
        }
    }

    private async Task<JsonElement> SendCommandAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _commandId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters ?? new object() });

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await _socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }
        finally
        {
            _sendLock.Release();
        }

        await using var registration = cancellationToken.Register(() =>
        {
            if (_pending.TryRemove(id, out var pending))
            {
                pending.TrySetCanceled(cancellationToken);
            }
        });
        return await completion.Task;
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[16 * 1024];
        var message = new StringBuilder();
        try
        {
            while (!_receiveCts.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                var received = await _socket.ReceiveAsync(buffer, _receiveCts.Token);
                if (received.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                message.Append(Encoding.UTF8.GetString(buffer, 0, received.Count));
                if (!received.EndOfMessage)
                {
                    continue;
                }

                Dispatch(message.ToString());
                message.Clear();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            FailAllPending(new InvalidOperationException("CDP 连接已关闭"));
        }
    }

    private void Dispatch(string json)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return;
        }

        // Events carry no "id"; we enable nothing so they are simply ignored.
        if (!root.TryGetProperty("id", out var idElement) || !_pending.TryRemove(idElement.GetInt32(), out var completion))
        {
            return;
        }

        if (root.TryGetProperty("error", out var error))
        {
            completion.TrySetException(new InvalidOperationException("CDP 命令失败：" + error.GetRawText()));
        }
        else
        {
            completion.TrySetResult(root.TryGetProperty("result", out var result) ? result : default);
        }
    }

    private void FailAllPending(Exception exception)
    {
        foreach (var id in _pending.Keys)
        {
            if (_pending.TryRemove(id, out var completion))
            {
                completion.TrySetException(exception);
            }
        }
    }

    private static IEnumerable<string> BuildArguments(CdpLaunchOptions options, int port)
    {
        yield return $"--remote-debugging-port={port}";
        yield return "--remote-debugging-address=127.0.0.1";
        yield return $"--user-data-dir={options.UserDataDir}";
        yield return "--no-first-run";
        yield return "--no-default-browser-check";
        yield return "--disable-blink-features=AutomationControlled";
        yield return "--disable-dev-shm-usage";
        yield return "--window-size=1280,900";
        yield return $"--lang={options.Language}";
        if (options.Headless)
        {
            yield return "--headless=new";
            yield return "--hide-scrollbars";
        }

        if (options.NoSandbox)
        {
            yield return "--no-sandbox";
        }

        if (!string.IsNullOrWhiteSpace(options.ProxyServer))
        {
            yield return $"--proxy-server={options.ProxyServer}";
        }

        yield return "about:blank";
    }

    private static async Task<string> DiscoverPageSocketAsync(int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var json = await http.GetStringAsync($"http://127.0.0.1:{port}/json", cancellationToken);
                using var document = JsonDocument.Parse(json);
                foreach (var target in document.RootElement.EnumerateArray())
                {
                    var type = target.TryGetProperty("type", out var t) ? t.GetString() : null;
                    if (type == "page" && target.TryGetProperty("webSocketDebuggerUrl", out var url))
                    {
                        var value = url.GetString();
                        if (!string.IsNullOrEmpty(value))
                        {
                            return value;
                        }
                    }
                }
            }
            catch (HttpRequestException)
            {
                // Chrome not listening yet.
            }
            catch (JsonException)
            {
            }

            await Task.Delay(200, cancellationToken);
        }

        throw new InvalidOperationException("无法连接到 Chrome 调试端口，浏览器可能启动失败");
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // best effort
        }
        finally
        {
            process.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await SendCommandAsync("Browser.close", null, closeCts.Token);
        }
        catch
        {
            // Browser.close races with the socket tearing down; the kill below is the backstop.
        }

        await _receiveCts.CancelAsync();
        try
        {
            await _receiveLoop;
        }
        catch
        {
            // already surfaced to callers
        }

        _socket.Dispose();
        _sendLock.Dispose();
        _receiveCts.Dispose();
        TryKill(_process);
    }
}

internal sealed record CdpLaunchOptions
{
    public required string ExecutablePath { get; init; }

    public required string UserDataDir { get; init; }

    public bool Headless { get; init; }

    public bool NoSandbox { get; init; }

    public string? ProxyServer { get; init; }

    public string Language { get; init; } = "zh-TW";

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
}
