using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace OhMyBot.Core.Integrations.Skland;

/// <summary>
/// 封装所有森空岛和鹰角网络 HTTP 调用。
/// 每个签名请求都需要：cred header + sign header + timestamp header + dId header + vName header。
/// 签名算法：HMAC-SHA256(signToken, message) → lowercase hex → MD5(hex string) → lowercase hex。
/// </summary>
public sealed class SklandHttpClient(HttpClient httpClient, IOptions<SklandOptions> options)
{
    public const int TokenExpiredCode = -10001;
    public const int CredExpiredCode = -10002;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SklandOptions _options = options.Value;

    // ---- Hypergryph: 鹰角网络 OAuth ----

    /// <summary>用鹰角网络 OAuth token 换取森空岛 authorize code。</summary>
    public async Task<SklandApiResponse<HgGrantData>> GrantAsync(
        string hgToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.HgBaseUrl}/user/oauth2/v2/grant");
        request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);
        request.Content = JsonContent(new
        {
            appCode = _options.AppCode,
            token = hgToken,
            type = 0
        });
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadJsonAsync<SklandApiResponse<HgGrantData>>(response, cancellationToken);
    }

    // ---- Skland: cred exchange and refresh ----

    /// <summary>用 authorize code 换取森空岛 cred + sign token（此请求不需要签名）。</summary>
    public async Task<SklandApiResponse<SklandCredData>> GenerateCredByCodeAsync(
        string code,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.SklandBaseUrl}/web/v1/user/auth/generate_cred_by_code");
        AddCommonHeaders(request, deviceId);
        request.Content = JsonContent(new { code, kind = 1 });
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadJsonAsync<SklandApiResponse<SklandCredData>>(response, cancellationToken);
    }

    /// <summary>刷新 sign token（cred 仍有效时调用）。</summary>
    public async Task<SklandApiResponse<SklandTokenRefreshData>> RefreshTokenAsync(
        string signToken,
        string cred,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        const string path = "/web/v1/auth/refresh";
        var sign = ComputeSign(signToken, path, string.Empty, string.Empty, deviceId, out var timestamp);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_options.SklandBaseUrl}{path}");
        AddSignedHeaders(request, cred, sign, timestamp, deviceId);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadJsonAsync<SklandApiResponse<SklandTokenRefreshData>>(response, cancellationToken);
    }

    // ---- Skland: game binding ----

    /// <summary>获取账号绑定的所有游戏角色列表。</summary>
    public async Task<SklandApiResponse<SklandBindingData>> GetBindingAsync(
        string signToken,
        string cred,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        const string path = "/api/v1/game/player/binding";
        var sign = ComputeSign(signToken, path, string.Empty, string.Empty, deviceId, out var timestamp);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_options.SklandBaseUrl}{path}");
        AddSignedHeaders(request, cred, sign, timestamp, deviceId);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadJsonAsync<SklandApiResponse<SklandBindingData>>(response, cancellationToken);
    }

    // ---- Skland: user and player info ----

    /// <summary>获取森空岛用户资料（昵称等）。</summary>
    public async Task<SklandApiResponse<SklandUserInfoData>> GetUserInfoAsync(
        string signToken,
        string cred,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        const string path = "/api/v1/user/me";
        var sign = ComputeSign(signToken, path, string.Empty, string.Empty, deviceId, out var timestamp);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_options.SklandBaseUrl}{path}");
        AddSignedHeaders(request, cred, sign, timestamp, deviceId);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadJsonAsync<SklandApiResponse<SklandUserInfoData>>(response, cancellationToken);
    }

    /// <summary>获取游戏角色详细信息（包含等级）。</summary>
    public async Task<SklandApiResponse<SklandPlayerInfoData>> GetPlayerInfoAsync(
        string uid,
        int gameId,
        string signToken,
        string cred,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var query = $"uid={WebUtility.UrlEncode(uid)}&gameId={gameId}";
        const string path = "/api/v1/game/player/info";
        var sign = ComputeSign(signToken, path, query, string.Empty, deviceId, out var timestamp);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_options.SklandBaseUrl}{path}?{query}");
        AddSignedHeaders(request, cred, sign, timestamp, deviceId);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadJsonAsync<SklandApiResponse<SklandPlayerInfoData>>(response, cancellationToken);
    }

    // ---- Skland: Arknights attendance ----

    /// <summary>获取明日方舟签到状态（包含历史记录和今日奖励预览）。</summary>
    public async Task<SklandApiResponse<SklandAttendanceData>> GetArknightsAttendanceAsync(
        string uid,
        string signToken,
        string cred,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var query = $"uid={WebUtility.UrlEncode(uid)}&gameId=1";
        var path = "/api/v1/game/attendance";
        var sign = ComputeSign(signToken, path, query, string.Empty, deviceId, out var timestamp);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_options.SklandBaseUrl}{path}?{query}");
        AddSignedHeaders(request, cred, sign, timestamp, deviceId);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadJsonAsync<SklandApiResponse<SklandAttendanceData>>(response, cancellationToken);
    }

    /// <summary>执行明日方舟签到。</summary>
    public async Task<SklandApiResponse<SklandAttendanceSignResult>> SignArknightsAsync(
        string uid,
        string signToken,
        string cred,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        const string path = "/api/v1/game/attendance";
        var bodyObj = new { uid, gameId = 1 };
        var body = SerializeBody(bodyObj);
        var sign = ComputeSign(signToken, path, string.Empty, body, deviceId, out var timestamp);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.SklandBaseUrl}{path}");
        AddSignedHeaders(request, cred, sign, timestamp, deviceId);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadJsonAsync<SklandApiResponse<SklandAttendanceSignResult>>(response, cancellationToken);
    }

    // ---- Skland: Endfield attendance ----

    /// <summary>获取终末地签到状态。</summary>
    public async Task<SklandApiResponse<SklandEndfieldAttendanceData>> GetEndfieldAttendanceAsync(
        string roleId,
        string serverId,
        string signToken,
        string cred,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        const string path = "/api/v1/game/endfield/attendance";
        var sign = ComputeSign(signToken, path, string.Empty, string.Empty, deviceId, out var timestamp);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_options.SklandBaseUrl}{path}");
        AddSignedHeaders(request, cred, sign, timestamp, deviceId);
        request.Headers.TryAddWithoutValidation("sk-game-role", $"3_{roleId}_{serverId}");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadJsonAsync<SklandApiResponse<SklandEndfieldAttendanceData>>(response, cancellationToken);
    }

    /// <summary>执行终末地签到。</summary>
    public async Task<SklandApiResponse<SklandEndfieldAttendanceSignResult>> SignEndfieldAsync(
        string roleId,
        string serverId,
        string signToken,
        string cred,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        const string path = "/api/v1/game/endfield/attendance";
        // 终末地 POST 签到没有 body
        var sign = ComputeSign(signToken, path, string.Empty, string.Empty, deviceId, out var timestamp);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.SklandBaseUrl}{path}");
        AddSignedHeaders(request, cred, sign, timestamp, deviceId);
        request.Headers.TryAddWithoutValidation("sk-game-role", $"3_{roleId}_{serverId}");
        request.Headers.Referrer = new Uri("https://game.skland.com/");
        request.Headers.TryAddWithoutValidation("Origin", "https://game.skland.com");
        // 空 body 避免 "Content-Length: 0" 被服务端拒绝
        request.Content = new StringContent(string.Empty, Encoding.UTF8, "application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadJsonAsync<SklandApiResponse<SklandEndfieldAttendanceSignResult>>(response, cancellationToken);
    }

    // ---- Signing ----

    /// <summary>
    /// 计算请求签名。
    /// message = path + queryString + bodyJson + timestamp + signHeaderJson
    /// sign = MD5(HMAC-SHA256(signToken, message).ToLowerHex()).ToLowerHex()
    /// </summary>
    internal static string ComputeSign(
        string signToken,
        string path,
        string query,
        string body,
        string deviceId,
        out string timestamp)
    {
        // 时间戳：当前 Unix 秒 - 2（参考实现惯例）
        timestamp = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 2).ToString();

        // 签名头 JSON — 键序必须严格固定，不能用 System.Text.Json 默认序列化
        var signHeaderJson =
            $"{{\"platform\":\"3\",\"timestamp\":\"{timestamp}\",\"dId\":\"B{deviceId}\",\"vName\":\"1.0.0\"}}";

        var message = path + query + body + timestamp + signHeaderJson;

        // Step 1: HMAC-SHA256，结果转 lowercase hex string
        var keyBytes = Encoding.UTF8.GetBytes(signToken);
        var msgBytes = Encoding.UTF8.GetBytes(message);
        var hmacBytes = HMACSHA256.HashData(keyBytes, msgBytes);
        var hmacHex = Convert.ToHexString(hmacBytes).ToLowerInvariant();

        // Step 2: MD5(hex string)，结果转 lowercase hex string
        var md5Input = Encoding.UTF8.GetBytes(hmacHex);
        var md5Bytes = MD5.HashData(md5Input);
        return Convert.ToHexString(md5Bytes).ToLowerInvariant();
    }

    // ---- Helpers ----

    private void AddSignedHeaders(
        HttpRequestMessage request,
        string cred,
        string sign,
        string timestamp,
        string deviceId)
    {
        AddCommonHeaders(request, deviceId);
        request.Headers.TryAddWithoutValidation("cred", cred);
        request.Headers.TryAddWithoutValidation("sign", sign);
        request.Headers.TryAddWithoutValidation("timestamp", timestamp);
    }

    private void AddCommonHeaders(HttpRequestMessage request, string deviceId)
    {
        request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);
        request.Headers.TryAddWithoutValidation("platform", "3");
        request.Headers.TryAddWithoutValidation("dId", "B" + deviceId);
        request.Headers.TryAddWithoutValidation("vName", _options.VName);
        request.Headers.TryAddWithoutValidation("x-requested-with", "com.hypergryph.skland");
        request.Headers.Accept.Clear();
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
    }

    private static HttpContent JsonContent(object payload)
    {
        return new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");
    }

    private static string SerializeBody(object payload)
    {
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : SklandBaseResponse
    {
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // 保留响应体，便于定位 HTTP 层拒绝的真实原因。
            var snippet = raw.Length > 500 ? raw[..500] : raw;
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // 401：sign/cred 已失效，交由上层刷新令牌后重试。
                throw new SklandUnauthorizedException($"森空岛请求未授权（HTTP 401）：{snippet}");
            }

            throw new HttpRequestException(
                $"森空岛请求失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}。响应：{snippet}");
        }

        var result = JsonSerializer.Deserialize<T>(raw, JsonOptions)
                     ?? throw new InvalidOperationException("森空岛返回数据为空：" + raw);
        result.Raw = raw;
        return result;
    }
}

/// <summary>签名请求返回 HTTP 401：sign token 或 cred 已失效，需刷新令牌后重试。</summary>
public sealed class SklandUnauthorizedException(string message) : Exception(message);
