using System.Collections.Frozen;
using System.Text.Json;
using Flurl.Http;
using Flurl.Http.Configuration;
using FoxTail.Extensions;
using OhMyLib.Requests.Kuro.Data;

namespace OhMyLib.Requests.Kuro;

public sealed class KuroHttpClient : IDisposable
{
    private const string BaseUrl = "https://api.kurobbs.com";
    private const string Version = "2.10.3";
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
        { "Accept", "application/json, text/plain, */*" },
        { "Accept-Encoding", "gzip, deflate, br, zstd" },
        { "Accept-Language", "zh-CN,zh;q=0.9,zh-TW;q=0.8" },
        { "Cache-Control", "no-cache" },
        { "Connection", "keep-alive" },
        { "Content-Type", "application/x-www-form-urlencoded;charset=UTF-8" },
        { "DNT", "1" },
        { "Host", "api.kurobbs.com" },
        { "Origin", "https://www.kurobbs.com" },
        { "Pragma", "no-cache" },
        { "Referer", "https://www.kurobbs.com" },
        { "Sec-Fetch-Dest", "empty" },
        { "Sec-Fetch-Mode", "cors" },
        { "Sec-Fetch-Site", "same-site" },
        { "User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36" },
        { "source", "h5" },
        { "version", Version },
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, string> GameHeaders = new Dictionary<string, string>
    {
        { "Host", "api.kurobbs.com" },
        { "Accept", "application/json, text/plain, */*" },
        { "source", "ios" },
        { "Sec-Fetch-Site", "same-site" },
        { "Sec-Fetch-Mode", "cors" },
        { "Origin", "https://web-static.kurobbs.com" },
        { "Content-Type", $"Mozilla/5.0 (iPhone; CPU iPhone OS 17_3 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) KuroGameBox/{Version}" },
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, string> UserInfoHeaders = new Dictionary<string, string>
    {
        { "osversion", "Android" },
        { "countrycode", "CN" },
        { "model", "2211133C" },
        { "source", "android" },
        { "lang", "zh-Hans" },
        { "version", Version },
        { "content-type", "application/x-www-form-urlencoded" },
        { "accept-encoding", "gzip" },
        { "user-agent", "okhttp/3.10.0" },
    }.ToFrozenDictionary();

    private readonly FlurlClient _httpClient = new();

    private readonly string _token;
    private readonly string _devCode;
    private readonly string _distinctId;
    private readonly string _ipAddress;

    public KuroHttpClient(string token, string? devCode = null, string? distinctId = null, string? ipAddress = null)
    {
        _token = token;
        _ipAddress = ipAddress ?? FakeIpAddress;
        _devCode = devCode ?? "";
        _distinctId = distinctId ?? "";
        _httpClient.BaseUrl = BaseUrl;
        _httpClient.WithSettings(x => x.JsonSerializer = new DefaultJsonSerializer(new JsonSerializerOptions()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }

    private async Task<T> PostBbsRequestAsync<T>(string path, object? body = null)
    {
        return await _httpClient.Request(path)
                                .WithHeaders(BbsHeaders)
                                .WithHeader("token", _token)
                                .WithHeader("devCode", _devCode)
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
                                            $"{_ipAddress}, Mozilla/5.0 (iPhone; CPU iPhone OS 17_3 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) KuroGameBox/{Version}")
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
                                .WithHeader("ip", _ipAddress)
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

    public Task<KuroHttpResponse<KuroBbsDefaultRoleData>> BbsGetDefaultRoleAsync(long queryUserId)
    {
        var body = new { queryUserId };
        return PostBbsRequestAsync<KuroHttpResponse<KuroBbsDefaultRoleData>>("/user/getDefaultRole", body);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}