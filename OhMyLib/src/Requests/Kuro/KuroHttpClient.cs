using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flurl.Http;
using Flurl.Http.Configuration;
using FoxTail.Extensions;
using OhMyLib.Requests.Kuro.Data;

namespace OhMyLib.Requests.Kuro;

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
        _httpClient.WithSettings(x => x.JsonSerializer = new DefaultJsonSerializer( new JsonSerializerOptions()
        {
             PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }

    private async Task<T> PostBbsRequestAsync<T>(string path, object? body = null)
    {
        return await _httpClient.Request(path)
                                .WithHeaders(CommonHeaders)
                                .WithHeaders(BbsHeaders)
                                .WithHeader("Cookie", "user_token=" + _token)
                                .WithHeader("Ip", FakeIpAddress)
                                .WithHeader("token", _token)
                                .WithHeader("dev_code", _devCode)
                                .WithHeader("distinct_id", _distinctId)
                                .Let(x => body != null
                                              ? x.PostUrlEncodedAsync(body)
                                              : x.PostAsync(new FormUrlEncodedContent([])))
                                .ReceiveJson<T>();
    }

    private async Task<T> PostGameRequestAsync<T>(string path, object? body = null)
    {
        return await _httpClient.Request(path)
                                .WithHeaders(CommonHeaders)
                                .WithHeaders(GameHeaders)
                                .WithHeader("devCode",
                                            $"{FakeIpAddress}, Mozilla/5.0 (iPhone; CPU iPhone OS 17_3 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) KuroGameBox/{KuroGameBoxVersion}")
                                .WithHeader("token", _token)
                                .Let(x => body != null
                                              ? x.PostUrlEncodedAsync(body)
                                              : x.PostAsync(new FormUrlEncodedContent([])))
                                .ReceiveJson<T>();
    }

    private async Task<T> PostUserInfoRequestAsync<T>(string path, object? body = null)
    {
        return await _httpClient.Request(path)
                                .WithHeaders(CommonHeaders)
                                .WithHeaders(UserInfoHeaders)
                                .WithHeader("devcode", _devCode)
                                .WithHeader("distinct_id", _distinctId)
                                .WithHeader("token", _token)
                                .Let(x => body != null
                                              ? x.PostUrlEncodedAsync(body)
                                              : x.PostAsync(new FormUrlEncodedContent([])))
                                .ReceiveJson<T>();
    }

    public async Task<KuroHttpResponse<List<KuroBbsPostListData>>> BbsGetPostsAsync(
        int gameId = 3,
        int forumId = 9,
        int searchType = 3,
        int pageIndex = 1,
        int pageSize = 20
    )
    {
        var body = new
        {
            gameId,
            forumId,
            searchType,
            pageIndex,
            pageSize
        };
        return await PostBbsRequestAsync<KuroHttpResponse<List<KuroBbsPostListData>>>("/forum/list", body);
    }

    public async Task<KuroHttpResponse<bool>> BbsLikePostAsync(
        int gameId,
        int forumId,
        int postType,
        string postId,
        long toUserId,
        int operateType = 1,
        int likeType = 1
    )
    {
        var body = new
        {
            gameId,
            forumId,
            postType,
            likeType,
            postId,
            operateType,
            toUserId
        };
        return await PostBbsRequestAsync<KuroHttpResponse<bool>>("/forum/like", body);
    }

    public async Task<KuroHttpResponse<KuroBbsPostDetail>> BbsGetPostDetailAsync(
        string postId,
        int isOnlyPublisher = 0,
        int showOrderType = 2
    )
    {
        var body = new
        {
            postId,
            isOnlyPublisher,
            showOrderType,
        };
        return await PostBbsRequestAsync<KuroHttpResponse<KuroBbsPostDetail>>("/forum/getPostDetail", body);
    }

    public async Task<KuroHttpResponse<KuroBbsMineData>> BbsGetMineAsync(long viewUserId)
    {
        var body = new { viewUserId };
        return await PostBbsRequestAsync<KuroHttpResponse<KuroBbsMineData>>("/user/mineV2", body);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}