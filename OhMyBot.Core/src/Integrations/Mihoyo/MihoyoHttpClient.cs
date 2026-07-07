using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace OhMyBot.Core.Integrations.Mihoyo;

/// <summary>
/// 米游社(CN, api-takumi/bbs-api) 与 HoYoLAB(OS) 的 HTTP 封装，移植自 MihoyoBBSTools。
/// 所有请求使用绝对 URL（CN/OS/bbs 主机不同），不依赖 HttpClient.BaseAddress。
/// </summary>
public sealed class MihoyoHttpClient(HttpClient httpClient, IOptions<MihoyoOptions> options)
{
    public const int CookieExpiredCode = -100;

    public const int AlreadySignedCode = -5003;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly MihoyoOptions _options = options.Value;

    // ---------------- CN 账号 / token ----------------

    public Task<MihoyoApiResponse<MihoyoGameRolesData>> GetGameRolesAsync(
        string cookie, string gameBiz, CancellationToken cancellationToken = default)
    {
        var url = AppendQuery(_options.AccountInfoUrl, new() { ["game_biz"] = gameBiz });
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyCnWebHeaders(request, cookie, MihoyoDs.GetDs(_options.SaltWeb));
        return SendAsync<MihoyoGameRolesData>(request, cancellationToken);
    }

    /// <summary>国际服(HoYoLAB) 按 Cookie 查询游戏角色；绑定时用它验证 Cookie 是否为有效国际服账号。</summary>
    public Task<MihoyoApiResponse<MihoyoGameRolesData>> GetOsGameRolesAsync(
        string cookie, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, _options.OsAccountInfoUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("x-rpc-app_version", "1.5.0");
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", "4");
        request.Headers.TryAddWithoutValidation("x-rpc-language", _options.OsGameLang);
        request.Headers.Referrer = new Uri("https://act.hoyolab.com/");
        request.Headers.TryAddWithoutValidation("Origin", "https://act.hoyolab.com");
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        return SendAsync<MihoyoGameRolesData>(request, cancellationToken);
    }

    public Task<MihoyoApiResponse<MihoyoCookieTokenData>> RefreshCookieTokenAsync(
        string stokenCookie, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, _options.CookieTokenByStokenUrl);
        request.Headers.TryAddWithoutValidation("Cookie", stokenCookie);
        request.Headers.UserAgent.ParseAdd(_options.UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return SendAsync<MihoyoCookieTokenData>(request, cancellationToken);
    }

    /// <summary>获取米游社账号昵称（按 uid 公开查询，无需 cookie，需 DS）。</summary>
    public Task<MihoyoApiResponse<MihoyoUserFullInfoData>> GetBbsUserInfoAsync(
        long uid, CancellationToken cancellationToken = default)
    {
        var uidText = uid.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var url = AppendQuery(_options.BbsApi + "/user/wapi/getUserFullInfo", new()
        {
            ["gids"] = "2",
            ["uid"] = uidText
        });
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        // 与抓包验证可用的请求一致：DS + 桌面 UA + Referer，不带 cookie/channel
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("DS", MihoyoDs.GetDs(_options.SaltWeb));
        request.Headers.TryAddWithoutValidation("x-rpc-app_version", _options.Version);
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", _options.ClientTypeWeb);
        request.Headers.TryAddWithoutValidation("x-rpc-device_id", MihoyoDs.GetDeviceId(uidText));
        request.Headers.Referrer = new Uri("https://www.miyoushe.com/");
        request.Headers.TryAddWithoutValidation("Origin", "https://www.miyoushe.com");
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        return SendAsync<MihoyoUserFullInfoData>(request, cancellationToken);
    }

    // ---------------- CN 游戏签到 ----------------

    public Task<MihoyoApiResponse<MihoyoLunaInfoData>> GetCnGameInfoAsync(
        string cookie, MihoyoGameDef game, string region, string uid, CancellationToken cancellationToken = default)
    {
        var url = AppendQuery(CnEventBase(game) + "/info", new()
        {
            ["lang"] = _options.CnGameLang,
            ["act_id"] = game.CnActId!,
            ["region"] = region,
            ["uid"] = uid
        });
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyCnGameHeaders(request, cookie, game);
        return SendAsync<MihoyoLunaInfoData>(request, cancellationToken);
    }

    public Task<MihoyoApiResponse<MihoyoLunaHomeData>> GetCnGameHomeAsync(
        string cookie, MihoyoGameDef game, CancellationToken cancellationToken = default)
    {
        var url = AppendQuery(CnEventBase(game) + "/home", new()
        {
            ["lang"] = _options.CnGameLang,
            ["act_id"] = game.CnActId!
        });
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyCnGameHeaders(request, cookie, game);
        return SendAsync<MihoyoLunaHomeData>(request, cancellationToken);
    }

    public Task<MihoyoApiResponse<MihoyoLunaSignData>> CnGameSignAsync(
        string cookie, MihoyoGameDef game, string region, string uid, CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(new { act_id = game.CnActId, region, uid });
        var request = new HttpRequestMessage(HttpMethod.Post, CnEventBase(game) + "/sign")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        ApplyCnGameHeaders(request, cookie, game);
        return SendAsync<MihoyoLunaSignData>(request, cancellationToken);
    }

    // ---------------- OS 游戏签到 ----------------

    public Task<MihoyoApiResponse<MihoyoLunaInfoData>> GetOsGameInfoAsync(
        string cookie, MihoyoGameDef game, CancellationToken cancellationToken = default)
    {
        var url = AppendQuery(game.OsEventBase! + "/info", new()
        {
            ["lang"] = _options.OsGameLang,
            ["act_id"] = game.OsActId!
        });
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyOsGameHeaders(request, cookie, game);
        return SendAsync<MihoyoLunaInfoData>(request, cancellationToken);
    }

    public Task<MihoyoApiResponse<MihoyoLunaHomeData>> GetOsGameHomeAsync(
        string cookie, MihoyoGameDef game, CancellationToken cancellationToken = default)
    {
        var url = AppendQuery(game.OsEventBase! + "/home", new()
        {
            ["lang"] = _options.OsGameLang,
            ["act_id"] = game.OsActId!
        });
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyOsGameHeaders(request, cookie, game);
        return SendAsync<MihoyoLunaHomeData>(request, cancellationToken);
    }

    public Task<MihoyoApiResponse<MihoyoLunaSignData>> OsGameSignAsync(
        string cookie, MihoyoGameDef game, CancellationToken cancellationToken = default)
    {
        var url = AppendQuery(game.OsEventBase! + "/sign", new() { ["lang"] = _options.OsGameLang });
        var body = JsonSerializer.Serialize(new { act_id = game.OsActId });
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        ApplyOsGameHeaders(request, cookie, game);
        return SendAsync<MihoyoLunaSignData>(request, cancellationToken);
    }

    // ---------------- BBS 米游币 ----------------

    public Task<MihoyoApiResponse<MihoyoMissionsData>> GetMissionsAsync(
        string accountCookie, CancellationToken cancellationToken = default)
    {
        var url = AppendQuery(_options.BbsTasksListUrl, new() { ["point_sn"] = "myb" });
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyBbsTaskHeaders(request, accountCookie);
        return SendAsync<MihoyoMissionsData>(request, cancellationToken);
    }

    public Task<MihoyoBaseResponse> BbsSignInAsync(
        string stokenCookie, string deviceId, string gids, CancellationToken cancellationToken = default)
    {
        var body = $"{{\"gids\":\"{gids}\"}}";
        var ds = MihoyoDs.GetDs2(_options.SaltX6, string.Empty, body);
        var request = new HttpRequestMessage(HttpMethod.Post, _options.BbsSignUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        ApplyBbsAppHeaders(request, stokenCookie, deviceId, ds);
        return SendBaseAsync(request, cancellationToken);
    }

    public Task<MihoyoApiResponse<MihoyoPostListData>> GetPostListAsync(
        string stokenCookie, string deviceId, string forumId, CancellationToken cancellationToken = default)
    {
        var url = AppendQuery(_options.BbsPostListUrl, new()
        {
            ["forum_id"] = forumId,
            ["is_good"] = "false",
            ["is_hot"] = "false",
            ["page_size"] = "20",
            ["sort_type"] = "1"
        });
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyBbsAppHeaders(request, stokenCookie, deviceId, MihoyoDs.GetDs(_options.SaltApp));
        return SendAsync<MihoyoPostListData>(request, cancellationToken);
    }

    public Task<MihoyoBaseResponse> ReadPostAsync(
        string stokenCookie, string deviceId, string postId, CancellationToken cancellationToken = default)
    {
        var url = AppendQuery(_options.BbsPostDetailUrl, new() { ["post_id"] = postId });
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyBbsAppHeaders(request, stokenCookie, deviceId, MihoyoDs.GetDs(_options.SaltApp));
        return SendBaseAsync(request, cancellationToken);
    }

    public Task<MihoyoBaseResponse> LikePostAsync(
        string stokenCookie, string deviceId, string postId, CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(new { post_id = postId, is_cancel = false });
        var request = new HttpRequestMessage(HttpMethod.Post, _options.BbsLikeUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        ApplyBbsAppHeaders(request, stokenCookie, deviceId, MihoyoDs.GetDs(_options.SaltApp));
        return SendBaseAsync(request, cancellationToken);
    }

    public Task<MihoyoBaseResponse> SharePostAsync(
        string stokenCookie, string deviceId, string postId, CancellationToken cancellationToken = default)
    {
        var url = AppendQuery(_options.BbsShareUrl, new() { ["entity_id"] = postId, ["entity_type"] = "1" });
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyBbsAppHeaders(request, stokenCookie, deviceId, MihoyoDs.GetDs(_options.SaltApp));
        return SendBaseAsync(request, cancellationToken);
    }

    // ---------------- header 构建 ----------------

    private void ApplyCnWebHeaders(HttpRequestMessage request, string cookie, string ds)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("DS", ds);
        request.Headers.TryAddWithoutValidation("x-rpc-app_version", _options.Version);
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", _options.ClientTypeWeb);
        request.Headers.TryAddWithoutValidation("x-rpc-channel", "miyousheluodi");
        request.Headers.TryAddWithoutValidation("x-rpc-device_id", MihoyoDs.GetDeviceId(cookie));
        request.Headers.UserAgent.ParseAdd(_options.UserAgent);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
    }

    private void ApplyCnGameHeaders(HttpRequestMessage request, string cookie, MihoyoGameDef game)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("DS", MihoyoDs.GetDs(_options.SaltWeb));
        request.Headers.TryAddWithoutValidation("x-rpc-app_version", _options.Version);
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", _options.ClientTypeWeb);
        request.Headers.TryAddWithoutValidation("x-rpc-channel", "miyousheluodi");
        request.Headers.TryAddWithoutValidation("x-rpc-device_id", MihoyoDs.GetDeviceId(cookie));
        request.Headers.UserAgent.ParseAdd(_options.UserAgent);
        request.Headers.Referrer = new Uri("https://act.mihoyo.com/");
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        if (game.CnSetActOrigin)
        {
            request.Headers.TryAddWithoutValidation("Origin", "https://act.mihoyo.com");
        }

        if (game.CnSignGame is not null)
        {
            request.Headers.TryAddWithoutValidation("x-rpc-signgame", game.CnSignGame);
        }
    }

    private void ApplyOsGameHeaders(HttpRequestMessage request, string cookie, MihoyoGameDef game)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Referrer = new Uri("https://act.hoyolab.com/");
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        if (game.OsSignGame is not null)
        {
            request.Headers.TryAddWithoutValidation("x-rpc-signgame", game.OsSignGame);
        }
    }

    private void ApplyBbsTaskHeaders(HttpRequestMessage request, string accountCookie)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd(_options.UserAgent);
        request.Headers.TryAddWithoutValidation("Origin", "https://webstatic.mihoyo.com");
        request.Headers.Referrer = new Uri("https://webstatic.mihoyo.com");
        request.Headers.TryAddWithoutValidation("X-Requested-With", "com.mihoyo.hyperion");
        request.Headers.TryAddWithoutValidation("Cookie", accountCookie);
    }

    private void ApplyBbsAppHeaders(HttpRequestMessage request, string stokenCookie, string deviceId, string ds)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("DS", ds);
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", _options.ClientType);
        request.Headers.TryAddWithoutValidation("x-rpc-app_version", _options.Version);
        request.Headers.TryAddWithoutValidation("x-rpc-sys_version", "12");
        request.Headers.TryAddWithoutValidation("x-rpc-channel", "miyousheluodi");
        request.Headers.TryAddWithoutValidation("x-rpc-device_id", deviceId);
        request.Headers.TryAddWithoutValidation("x-rpc-device_name", "OhMyBot Device");
        request.Headers.TryAddWithoutValidation("x-rpc-device_model", "Unspecified Device");
        request.Headers.TryAddWithoutValidation("x-rpc-h265_supported", "1");
        request.Headers.TryAddWithoutValidation("x-rpc-verify_key", _options.VerifyKey);
        request.Headers.TryAddWithoutValidation("x-rpc-csm_source", "discussion");
        request.Headers.Referrer = new Uri("https://app.mihoyo.com");
        request.Headers.UserAgent.ParseAdd(_options.OkHttpUserAgent);
        request.Headers.TryAddWithoutValidation("Cookie", stokenCookie);
    }

    // ---------------- 发送 ----------------

    private async Task<MihoyoApiResponse<T>> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        using (var response = await httpClient.SendAsync(request, cancellationToken))
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                response.EnsureSuccessStatusCode();
            }

            var result = JsonSerializer.Deserialize<MihoyoApiResponse<T>>(raw, JsonOptions)
                         ?? throw new InvalidOperationException("米游社返回数据为空：" + raw);
            result.Raw = raw;
            return result;
        }
    }

    private async Task<MihoyoBaseResponse> SendBaseAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        using (var response = await httpClient.SendAsync(request, cancellationToken))
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();
            var result = JsonSerializer.Deserialize<MihoyoBaseResponse>(raw, JsonOptions)
                         ?? throw new InvalidOperationException("米游社返回数据为空：" + raw);
            result.Raw = raw;
            return result;
        }
    }

    private string CnEventBase(MihoyoGameDef game)
    {
        return game.CnUsesZzzHost ? _options.ZzzEventLunaBase : _options.CnEventLunaBase;
    }

    private static string AppendQuery(string url, Dictionary<string, string> query)
    {
        var builder = new StringBuilder(url);
        var first = !url.Contains('?');
        foreach (var (key, value) in query)
        {
            builder.Append(first ? '?' : '&');
            builder.Append(Uri.EscapeDataString(key));
            builder.Append('=');
            builder.Append(Uri.EscapeDataString(value));
            first = false;
        }

        return builder.ToString();
    }
}
