using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flurl.Http;
using Flurl.Http.Configuration;
using FoxTail.Extensions;
using OhMyLib.Models.Kuro;
using OhMyLib.Requests.Kuro.Data;

namespace OhMyLib.Requests.Kuro;

public sealed class KuroHttpClient : IDisposable
{
    private const string BaseUrl = "https://api.kurobbs.com";
    private const string Version = "2.10.5";

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
        { "User-Agent", RequestConstants.UserAgent },
        { "source", "h5" },
        { "version", Version },
    }.ToFrozenDictionary();

    private readonly FlurlClient _httpClient = new();

    private readonly string _token;
    private readonly string _devCode;
    private readonly string _distinctId;

    public KuroHttpClient(string token, string? devCode = null, string? distinctId = null, string? ipAddress = null)
    {
        _token = token;
        _devCode = devCode ?? "";
        _distinctId = distinctId ?? "";
        _httpClient.BaseUrl = BaseUrl;
        _httpClient.WithSettings(x => x.JsonSerializer = new DefaultJsonSerializer(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        }));
    }

    public KuroHttpClient(KuroUser kuroUser) : this(kuroUser.Token ?? "", kuroUser.DevCode, kuroUser.DistinctId, kuroUser.IpAddress)
    {
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

    public async Task<KuroHttpResponse<KuroBbsPostData>> BbsGetPostsAsync(
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
        return await PostBbsRequestAsync<KuroHttpResponse<KuroBbsPostData>>("/forum/list", body);
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

    public async Task<KuroHttpResponse<KuroBbsDefaultRoleData>> BbsGetDefaultRoleAsync(long queryUserId)
    {
        var body = new { queryUserId };
        return await PostBbsRequestAsync<KuroHttpResponse<KuroBbsDefaultRoleData>>("/user/role/findUserDefaultRole", body);
    }

    public async Task<KuroHttpResponse<KuroBbsSignInData>> BbsSignInAsync(int gameId = 2)
    {
        var body = new { gameId };
        return await PostBbsRequestAsync<KuroHttpResponse<KuroBbsSignInData>>("/user/signIn", body);
    }

    public async Task<KuroHttpResponse<KuroSignInInitData>> GameSignInInitAsync(int gameId, string serverId, long roleId, long userId)
    {
        var body = new { gameId, serverId, roleId, userId };
        return await PostBbsRequestAsync<KuroHttpResponse<KuroSignInInitData>>("/encourage/signIn/initSignInV2", body);
    }

    public async Task<KuroHttpResponse<List<KuroGameSignInQueryData>>> GameSignInQueryRecordAsync(int gameId, string serverId, long roleId, long userId)
    {
        var body = new { gameId, serverId, roleId, userId };
        return await PostBbsRequestAsync<KuroHttpResponse<List<KuroGameSignInQueryData>>>("/encourage/signIn/queryRecordV2", body);
    }

    public async Task<KuroHttpResponse<KuroSignInInitData>> GameSignInReplenishAsync(int gameId, string serverId, long roleId, long userId)
    {
        var reqMonth = DateTime.Now.Month.ToString("00");
        var body = new { gameId, serverId, roleId, userId, reqMonth };
        return await PostBbsRequestAsync<KuroHttpResponse<KuroSignInInitData>>("/encourage/signIn/repleSigInV2", body);
    }

    public async Task<KuroHttpResponse<KuroGameSignInResult>> GameSignInAsync(int gameId, string serverId, long roleId, long userId)
    {
        var reqMonth = DateTime.Now.Month.ToString("00");
        var body = new { gameId, serverId, roleId, userId, reqMonth };
        return await PostBbsRequestAsync<KuroHttpResponse<KuroGameSignInResult>>("/encourage/signIn/v2", body);
    }

    public async Task<KuroHttpResponse<KuroBbsTaskProgressData>> BbsGetTaskProgressAsync(long userId, int gameId = 0)
    {
        var body = new { userId, gameId };
        return await PostBbsRequestAsync<KuroHttpResponse<KuroBbsTaskProgressData>>("/encourage/level/getTaskProcess", body);
    }

    public async Task<KuroBaseHttpResponse> BbsSharePostAsync(int gameId = 3)
    {
        var body = new { gameId };
        return await PostBbsRequestAsync<KuroBaseHttpResponse>("/encourage/level/shareTask", body);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}