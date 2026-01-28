using System.Collections.Frozen;
using System.Text.Json.Nodes;
using Flurl.Http;

namespace OhMyLib.Requests;

public sealed class KuroHttpClient : IDisposable
{
    private const string BaseUrl = "https://api.kurobbs.com";
    private const string KuroGameBoxVersion = "2.10.0";
    private const string FakeIpAddress = "100.100.64.60";

    private static readonly FrozenDictionary<string, string> CommonHeaders = new Dictionary<string, string>
    {
        { "Accept", "*/*" },
        { "Accept-Language", "zh-CN,zh-Hans;q=0.9" },
        { "Accept-Encoding", "gzip, deflate, br" },
        { "Connection", "keep-alive" },
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, string> BbsHeaders = new Dictionary<string, string>
    {
        { "Host", "api.kurobbs.com" },
        { "source", "ios" },
        { "lang", "zh-Hans" },
        { "User-Agent", "KuroGameBox/48 CFNetwork/1492.0.1 Darwin/23.3.0" },
        { "channelId", "1" },
        { "channel", "appstore" },
        { "version", "2.10.0" },
        { "model", "iPhone15,2" },
        { "osVersion", "17.3" },
        { "Content-Type", "application/x-www-form-urlencoded; charset=utf-8" },
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, string> GameHeaders = new Dictionary<string, string>
    {
        { "Host", "api.kurobbs.com" },
        { "Accept", "application/json, text/plain, */*" },
        { "source", "ios" },
        { "Sec-Fetch-Site", "same-site" },
        { "Sec-Fetch-Mode", "cors" },
        { "Origin", "https://web-static.kurobbs.com" },
        { "Content-Type", $"Mozilla/5.0 (iPhone; CPU iPhone OS 17_3 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) KuroGameBox/{KuroGameBoxVersion}" },
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, string> UserInfoHeaders = new Dictionary<string, string>
    {
        { "osversion", "Android" },
        { "countrycode", "CN" },
        { "ip", FakeIpAddress },
        { "model", "2211133C" },
        { "source", "android" },
        { "lang", "zh-Hans" },
        { "version", "1.0.9" },
        { "versioncode", "1090" },
        { "content-type", "application/x-www-form-urlencoded" },
        { "accept-encoding", "gzip" },
        { "user-agent", "okhttp/3.10.0" },
    }.ToFrozenDictionary();

    private readonly FlurlClient _httpClient = new();

    private readonly string _token;
    private readonly string _devCode;
    private readonly string _distinctId;

    public KuroHttpClient(string token, string? devCode = null, string? distinctId = null)
    {
        _token = token;
        _devCode = devCode ?? "";
        _distinctId = distinctId ?? "";
        _httpClient.BaseUrl = BaseUrl;
    }

    private async Task<JsonNode> PostBbsRequestAsync(string path, object body)
    {
        return await _httpClient.Request(path)
                                .WithHeaders(CommonHeaders)
                                .WithHeaders(BbsHeaders)
                                .WithHeader("Cookie", "user_token=" + _token)
                                .WithHeader("Ip", FakeIpAddress)
                                .WithHeader("token", _token)
                                .WithHeader("dev_code", _devCode)
                                .WithHeader("distinct_id", _distinctId)
                                .PostJsonAsync(body)
                                .ReceiveJson<JsonNode>();
    }

    private async Task<JsonNode> PostGameRequestAsync(string path, object body)
    {
        return await _httpClient.Request(path)
                                .WithHeaders(CommonHeaders)
                                .WithHeaders(GameHeaders)
                                .WithHeader("devCode",
                                            $"{FakeIpAddress}, Mozilla/5.0 (iPhone; CPU iPhone OS 17_3 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) KuroGameBox/{KuroGameBoxVersion}")
                                .WithHeader("token", _token)
                                .PostJsonAsync(body)
                                .ReceiveJson<JsonNode>();
    }

    private async Task<JsonNode> PostUserInfoRequestAsync(string path, object body)
    {
        return await _httpClient.Request(path)
                                .WithHeaders(CommonHeaders)
                                .WithHeaders(UserInfoHeaders)
                                .WithHeader("devcode", _devCode)
                                .WithHeader("distinct_id", _distinctId)
                                .WithHeader("token", _token)
                                .PostJsonAsync(body)
                                .ReceiveJson<JsonNode>();
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}