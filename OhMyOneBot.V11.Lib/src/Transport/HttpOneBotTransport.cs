using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OhMyOneBot.V11.Lib.Transport;

internal sealed class HttpOneBotTransport(OneBotHttpOptions options) : OneBotTransportBase(OneBotTransportType.Http)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private HttpClient? _httpClient;

    public OneBotHttpOptions Options { get; } = options ?? throw new ArgumentNullException(nameof(options));

    protected override Task OnStartAsync(CancellationToken cancellationToken)
    {
        if (!Options.BaseUri.IsAbsoluteUri)
        {
            throw new InvalidOperationException("HTTP transport BaseUri must be an absolute URI.");
        }

        var scheme = Options.BaseUri.Scheme;
        if (!string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("HTTP transport BaseUri must use http or https.");
        }

        _httpClient?.Dispose();
        _httpClient = new HttpClient
        {
            BaseAddress = Options.BaseUri,
            Timeout = Options.ConnectTimeout
        };

        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(Options.AccessToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Options.AccessToken);
        }

        return Task.CompletedTask;
    }

    protected override Task OnStopAsync(CancellationToken cancellationToken)
    {
        _httpClient?.Dispose();
        _httpClient = null;
        return Task.CompletedTask;
    }

    protected override async Task<OneBotActionResponse<JsonElement>> OnSendActionAsync(OneBotActionRequest request, CancellationToken cancellationToken)
    {
        if (ConnectionState != OneBotConnectionState.Connected)
        {
            throw new InvalidOperationException("HTTP transport is not started. Call StartAsync before sending actions.");
        }

        var client = _httpClient ?? throw new InvalidOperationException("HTTP client is not initialized.");
        var actionPath = request.Action.Trim('/');
        if (string.IsNullOrWhiteSpace(actionPath))
        {
            throw new ArgumentException("Action name cannot be empty.", nameof(request));
        }

        var payload = BuildHttpPayload(request);
        using var message = new HttpRequestMessage(HttpMethod.Post, actionPath);
        message.Content = JsonContent.Create(payload, options: JsonOptions);

        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = JsonSerializer.Deserialize<OneBotActionResponse<JsonElement>>(responseBody, JsonOptions);
        if (result is null)
        {
            throw new InvalidOperationException("Failed to deserialize OneBot action response.");
        }

        return result;
    }

    private static JsonObject BuildHttpPayload(OneBotActionRequest request)
    {
        if (request.Params is null)
        {
            return request.Echo is null ? new JsonObject() : new JsonObject { ["echo"] = request.Echo };
        }

        var node = JsonSerializer.SerializeToNode(request.Params, JsonOptions);
        if (node is JsonObject obj)
        {
            if (!string.IsNullOrEmpty(request.Echo))
            {
                obj["echo"] = request.Echo;
            }

            return obj;
        }

        throw new InvalidOperationException("HTTP action params must serialize to a JSON object.");
    }
}