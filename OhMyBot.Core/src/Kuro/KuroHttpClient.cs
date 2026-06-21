using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace OhMyBot.Core.Kuro;

public sealed class KuroHttpClient(HttpClient httpClient, IOptions<KuroOptions> options)
{
    public const int TokenExpiredCode = 220;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly KuroOptions _options = options.Value;

    public Task<KuroApiResponse<KuroMineData>> GetMineAsync(string token, CancellationToken cancellationToken = default)
    {
        return GetMineAsync(new KuroRequestCredential(token), cancellationToken);
    }

    public Task<KuroApiResponse<KuroMineData>> GetMineAsync(KuroRequestCredential credential, CancellationToken cancellationToken = default)
    {
        return PostAsync<KuroMineData>("/user/mineV2", credential, new Dictionary<string, string>
        {
            ["type"] = "1"
        }, cancellationToken);
    }

    public Task<KuroApiResponse<KuroDefaultRoleData>> GetDefaultRolesAsync(
        string token,
        long queryUserId,
        CancellationToken cancellationToken = default)
    {
        return GetDefaultRolesAsync(new KuroRequestCredential(token), queryUserId, cancellationToken);
    }

    public Task<KuroApiResponse<KuroDefaultRoleData>> GetDefaultRolesAsync(
        KuroRequestCredential credential,
        long queryUserId,
        CancellationToken cancellationToken = default)
    {
        return PostAsync<KuroDefaultRoleData>("/gamer/role/default", credential, new Dictionary<string, string>
        {
            ["queryUserId"] = queryUserId.ToString(CultureInfo.InvariantCulture)
        }, cancellationToken);
    }

    public Task<KuroApiResponse<KuroTaskProgressData>> GetTaskProgressAsync(
        string token,
        long userId,
        int gameId = 0,
        CancellationToken cancellationToken = default)
    {
        return GetTaskProgressAsync(new KuroRequestCredential(token), userId, gameId, cancellationToken);
    }

    public Task<KuroApiResponse<KuroTaskProgressData>> GetTaskProgressAsync(
        KuroRequestCredential credential,
        long userId,
        int gameId = 0,
        CancellationToken cancellationToken = default)
    {
        return PostAsync<KuroTaskProgressData>("/encourage/level/getTaskProcess", credential, new Dictionary<string, string>
        {
            ["userId"] = userId.ToString(CultureInfo.InvariantCulture),
            ["gameId"] = gameId.ToString(CultureInfo.InvariantCulture)
        }, cancellationToken);
    }

    public Task<KuroApiResponse<KuroBbsSignInData>> BbsSignInAsync(
        string token,
        int gameId = 2,
        CancellationToken cancellationToken = default)
    {
        return BbsSignInAsync(new KuroRequestCredential(token), gameId, cancellationToken);
    }

    public Task<KuroApiResponse<KuroBbsSignInData>> BbsSignInAsync(
        KuroRequestCredential credential,
        int gameId = 2,
        CancellationToken cancellationToken = default)
    {
        return PostAsync<KuroBbsSignInData>("/user/signIn", credential, new Dictionary<string, string>
        {
            ["gameId"] = gameId.ToString(CultureInfo.InvariantCulture)
        }, cancellationToken);
    }

    public Task<KuroApiResponse<KuroPostListData>> GetPostsAsync(
        string token,
        int gameId = 3,
        int forumId = 9,
        int searchType = 3,
        int pageIndex = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        return GetPostsAsync(new KuroRequestCredential(token), gameId, forumId, searchType, pageIndex, pageSize, cancellationToken);
    }

    public Task<KuroApiResponse<KuroPostListData>> GetPostsAsync(
        KuroRequestCredential credential,
        int gameId = 3,
        int forumId = 9,
        int searchType = 3,
        int pageIndex = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        return PostAsync<KuroPostListData>("/forum/list", credential, new Dictionary<string, string>
        {
            ["gameId"] = gameId.ToString(CultureInfo.InvariantCulture),
            ["forumId"] = forumId.ToString(CultureInfo.InvariantCulture),
            ["searchType"] = searchType.ToString(CultureInfo.InvariantCulture),
            ["pageIndex"] = pageIndex.ToString(CultureInfo.InvariantCulture),
            ["pageSize"] = pageSize.ToString(CultureInfo.InvariantCulture)
        }, cancellationToken);
    }

    public Task<KuroApiResponse<KuroPostDetailData>> GetPostDetailAsync(
        string token,
        string postId,
        CancellationToken cancellationToken = default)
    {
        return GetPostDetailAsync(new KuroRequestCredential(token), postId, cancellationToken);
    }

    public Task<KuroApiResponse<KuroPostDetailData>> GetPostDetailAsync(
        KuroRequestCredential credential,
        string postId,
        CancellationToken cancellationToken = default)
    {
        return PostAsync<KuroPostDetailData>("/forum/getPostDetail", credential, new Dictionary<string, string>
        {
            ["postId"] = postId,
            ["isOnlyPublisher"] = "0",
            ["showOrderType"] = "2"
        }, cancellationToken);
    }

    public Task<KuroApiResponse<bool>> LikePostAsync(
        string token,
        int gameId,
        int forumId,
        int postType,
        string postId,
        long toUserId,
        CancellationToken cancellationToken = default)
    {
        return LikePostAsync(new KuroRequestCredential(token), gameId, forumId, postType, postId, toUserId, cancellationToken);
    }

    public Task<KuroApiResponse<bool>> LikePostAsync(
        KuroRequestCredential credential,
        int gameId,
        int forumId,
        int postType,
        string postId,
        long toUserId,
        CancellationToken cancellationToken = default)
    {
        return PostAsync<bool>("/forum/like", credential, new Dictionary<string, string>
        {
            ["gameId"] = gameId.ToString(CultureInfo.InvariantCulture),
            ["forumId"] = forumId.ToString(CultureInfo.InvariantCulture),
            ["postType"] = postType.ToString(CultureInfo.InvariantCulture),
            ["likeType"] = "1",
            ["postId"] = postId,
            ["operateType"] = "1",
            ["toUserId"] = toUserId.ToString(CultureInfo.InvariantCulture)
        }, cancellationToken);
    }

    public Task<KuroBaseResponse> SharePostAsync(string token, int gameId = 3, CancellationToken cancellationToken = default)
    {
        return SharePostAsync(new KuroRequestCredential(token), gameId, cancellationToken);
    }

    public Task<KuroBaseResponse> SharePostAsync(KuroRequestCredential credential, int gameId = 3, CancellationToken cancellationToken = default)
    {
        return PostBaseAsync("/encourage/level/shareTask", credential, new Dictionary<string, string>
        {
            ["gameId"] = gameId.ToString(CultureInfo.InvariantCulture)
        }, cancellationToken);
    }

    public Task<KuroApiResponse<KuroGameSignInInitData>> GameSignInInitAsync(
        string token,
        long gameId,
        string serverId,
        long roleId,
        long userId,
        CancellationToken cancellationToken = default)
    {
        return GameSignInInitAsync(new KuroRequestCredential(token), gameId, serverId, roleId, userId, cancellationToken);
    }

    public Task<KuroApiResponse<KuroGameSignInInitData>> GameSignInInitAsync(
        KuroRequestCredential credential,
        long gameId,
        string serverId,
        long roleId,
        long userId,
        CancellationToken cancellationToken = default)
    {
        return PostAsync<KuroGameSignInInitData>("/encourage/signIn/initSignInV2", credential, GameSignBody(gameId, serverId, roleId, userId, includeMonth: false), cancellationToken);
    }

    public Task<KuroApiResponse<KuroGameSignInResult>> GameSignInAsync(
        string token,
        long gameId,
        string serverId,
        long roleId,
        long userId,
        CancellationToken cancellationToken = default)
    {
        return GameSignInAsync(new KuroRequestCredential(token), gameId, serverId, roleId, userId, cancellationToken);
    }

    public Task<KuroApiResponse<KuroGameSignInResult>> GameSignInAsync(
        KuroRequestCredential credential,
        long gameId,
        string serverId,
        long roleId,
        long userId,
        CancellationToken cancellationToken = default)
    {
        return PostAsync<KuroGameSignInResult>("/encourage/signIn/v2", credential, GameSignBody(gameId, serverId, roleId, userId, includeMonth: true), cancellationToken);
    }

    private static Dictionary<string, string> GameSignBody(long gameId, string serverId, long roleId, long userId, bool includeMonth)
    {
        var body = new Dictionary<string, string>
        {
            ["gameId"] = gameId.ToString(CultureInfo.InvariantCulture),
            ["serverId"] = serverId,
            ["roleId"] = roleId.ToString(CultureInfo.InvariantCulture),
            ["userId"] = userId.ToString(CultureInfo.InvariantCulture)
        };
        if (includeMonth)
        {
            body["reqMonth"] = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).Month.ToString("00", CultureInfo.InvariantCulture);
        }

        return body;
    }

    private async Task<KuroApiResponse<T>> PostAsync<T>(
        string path,
        KuroRequestCredential credential,
        IReadOnlyDictionary<string, string> body,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(path, credential, body);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = JsonSerializer.Deserialize<KuroApiResponse<T>>(raw, JsonOptions)
                     ?? throw new InvalidOperationException("库街区返回数据为空：" + raw);
        result.Raw = raw;
        return result;
    }

    private async Task<KuroBaseResponse> PostBaseAsync(
        string path,
        KuroRequestCredential credential,
        IReadOnlyDictionary<string, string> body,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(path, credential, body);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = JsonSerializer.Deserialize<KuroBaseResponse>(raw, JsonOptions)
                     ?? throw new InvalidOperationException("库街区返回数据为空：" + raw);
        result.Raw = raw;
        return result;
    }

    private HttpRequestMessage CreateRequest(string path, KuroRequestCredential credential, IReadOnlyDictionary<string, string> body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent(body)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd(_options.UserAgent);
        request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,zh-TW;q=0.8");
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
        request.Headers.TryAddWithoutValidation("DNT", "1");
        request.Headers.TryAddWithoutValidation("Origin", "https://www.kurobbs.com");
        request.Headers.Referrer = new Uri("https://www.kurobbs.com/");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-site");
        request.Headers.TryAddWithoutValidation("token", credential.Token);
        request.Headers.TryAddWithoutValidation("devCode", FirstNonEmpty(credential.DevCode, _options.DevCode));
        request.Headers.TryAddWithoutValidation("distinct_id", FirstNonEmpty(credential.DistinctId, _options.DistinctId));
        request.Headers.TryAddWithoutValidation("source", "h5");
        request.Headers.TryAddWithoutValidation("version", _options.Version);
        return request;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }
}

public sealed record KuroRequestCredential(string Token, string? DevCode = null, string? DistinctId = null);
