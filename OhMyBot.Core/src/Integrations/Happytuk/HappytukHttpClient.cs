using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OhMyBot.Core.Integrations.Happytuk;

public sealed partial class HappytukHttpClient(HttpClient httpClient)
{
    internal const string UserAgent = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Uri BaseUri = new("https://www.mangot5.com");

    public async Task<string> LoginAsync(string loginAccount, string password, CancellationToken cancellationToken = default)
    {
        var cookies = new CookieContainer();
        using var pageRequest = CreateRequest(HttpMethod.Get, "/Index/Member/Login?ref=/Index/Billing/couponList", UserAgent, xhr: false);
        using var pageResponse = await httpClient.SendAsync(pageRequest, cancellationToken);
        var html = await pageResponse.Content.ReadAsStringAsync(cancellationToken);
        ThrowIfPageBlocked(pageResponse, html, "登录页");
        AddCookies(cookies, pageResponse);

        var r = MatchValue(LoginRRegex(), html, "登录参数");
        var clientIp = MatchValue(ClientIpRegex(), html, "客户端地址");
        using var loginRequest = CreateRequest(
            HttpMethod.Post,
            "/api/member/secureLoginJson.json",
            UserAgent,
            cookies.GetCookieHeader(BaseUri),
            referrer: "/Index/Member/Login?ref=/Index/Billing/couponList");
        loginRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ref"] = "/Index/Billing/couponList",
            ["loginBtnClick"] = string.Empty,
            ["p"] = string.Empty,
            ["r"] = r,
            ["gname"] = "portal",
            ["check"] = string.Empty,
            ["userNoString"] = string.Empty,
            ["clientIP"] = clientIp,
            ["userID"] = loginAccount,
            ["password"] = password
        });
        using var loginResponse = await httpClient.SendAsync(loginRequest, cancellationToken);
        AddCookies(cookies, loginResponse);
        var payload = await ReadJsonAsync<HappytukLoginResponse>(loginResponse, cancellationToken);
        if (payload.Result?.RequireCaptcha == true)
        {
            throw new InvalidOperationException("登录需要验证码，暂时无法自动绑定，请稍后重试");
        }

        if (payload.Result?.OtpUser == true)
        {
            throw new InvalidOperationException("该账号启用了 OTP，暂不支持自动登录");
        }

        if (payload.Result?.Ok == true)
        {
            return new HappytukSession(cookies.GetCookieHeader(BaseUri), UserAgent).Serialize();
        }

        throw new InvalidOperationException("登录失败：" + (payload.Result?.Msg ?? "未知错误"));
    }

    public async Task<HappytukRedeemResultType> RedeemAsync(
        string session,
        string couponCode,
        CancellationToken cancellationToken = default)
    {
        var (cookies, userAgent) = HappytukSession.Parse(session, UserAgent);
        using var pageRequest = CreateRequest(HttpMethod.Get, "/Index/Billing/couponList", userAgent, cookies, xhr: false);
        using var pageResponse = await httpClient.SendAsync(pageRequest, cancellationToken);
        if (IsLoginRedirect(pageResponse))
        {
            return HappytukRedeemResultType.SessionExpired;
        }

        var html = await pageResponse.Content.ReadAsStringAsync(cancellationToken);
        ThrowIfPageBlocked(pageResponse, html, "兑换页");
        var checksumMatch = ChecksumRegex().Match(html);
        if (!checksumMatch.Success)
        {
            return HappytukRedeemResultType.SessionExpired;
        }

        using var stringRequest = CreateRequest(HttpMethod.Post, "/Index/Billing/checkStringCoupon.json", userAgent, cookies);
        stringRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["serial"] = couponCode });
        using var stringResponse = await httpClient.SendAsync(stringRequest, cancellationToken);
        var stringCoupon = await ReadJsonAsync<HappytukCouponResponse>(stringResponse, cancellationToken);
        var serial = ReturnCode(stringCoupon);
        if (string.IsNullOrWhiteSpace(serial))
        {
            throw new InvalidOperationException("兑换码无效或过短");
        }

        var checkUrl = $"/Index/Billing/checkCoupon.json?serial={Uri.EscapeDataString(serial)}&checksum={Uri.EscapeDataString(checksumMatch.Groups[1].Value)}";
        using var checkRequest = CreateRequest(HttpMethod.Get, checkUrl, userAgent, cookies);
        using var checkResponse = await httpClient.SendAsync(checkRequest, cancellationToken);
        var check = await ReadJsonAsync<HappytukCouponResponse>(checkResponse, cancellationToken);
        var checkCode = ReturnCode(check);
        if (checkCode is "40003" or "40007")
        {
            return HappytukRedeemResultType.AlreadyRedeemed;
        }

        if (checkCode != "1")
        {
            throw new InvalidOperationException(MessageFor(checkCode));
        }

        if (check.ServerData?.Required == true || check.CharacterData?.Required == true)
        {
            throw new InvalidOperationException("该兑换码需要选择服务器或角色，暂不支持自动兑换");
        }

        var useUrl = $"/Index/Billing/useCoupon.json?serial={Uri.EscapeDataString(serial)}&checksum={Uri.EscapeDataString(check.Checksum ?? string.Empty)}";
        using var useRequest = CreateRequest(HttpMethod.Get, useUrl, userAgent, cookies);
        using var useResponse = await httpClient.SendAsync(useRequest, cancellationToken);
        var used = await ReadJsonAsync<HappytukCouponResponse>(useResponse, cancellationToken);
        var useCode = ReturnCode(used);
        return useCode switch
        {
            "1" => HappytukRedeemResultType.Success,
            "40003" or "40007" => HappytukRedeemResultType.AlreadyRedeemed,
            _ => throw new InvalidOperationException(MessageFor(useCode))
        };
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string url,
        string userAgent,
        string? cookies = null,
        bool xhr = true,
        string? referrer = null)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
        request.Headers.UserAgent.ParseAdd(userAgent);
        request.Headers.AcceptLanguage.ParseAdd("zh-TW,zh;q=0.9,en;q=0.8");
        if (xhr)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Referrer = new Uri(BaseUri, referrer ?? "/Index/Billing/couponList");
            request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        }
        else
        {
            request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            request.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
        }
        if (!string.IsNullOrWhiteSpace(cookies))
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookies);
        }

        return request;
    }

    private static void ThrowIfPageBlocked(HttpResponseMessage response, string html, string pageName)
    {
        var cloudflareChallenge = response.Headers.TryGetValues("cf-mitigated", out var mitigated)
            && mitigated.Any(value => value.Contains("challenge", StringComparison.OrdinalIgnoreCase));
        cloudflareChallenge |= html.Contains("/cdn-cgi/challenge-platform/", StringComparison.OrdinalIgnoreCase)
            || html.Contains("Just a moment", StringComparison.OrdinalIgnoreCase);
        if (response.IsSuccessStatusCode && !cloudflareChallenge)
        {
            return;
        }

        var ray = response.Headers.TryGetValues("cf-ray", out var rays) ? rays.FirstOrDefault() : null;
        throw new HappytukBrowserRequiredException(
            cloudflareChallenge
                ? $"HappyTuk {pageName}触发 Cloudflare 验证 [{(int)response.StatusCode}]，cf-ray={ray ?? "-"}"
                : $"HappyTuk {pageName}请求失败 [{(int)response.StatusCode}]，cf-ray={ray ?? "-"}");
    }

    private static void AddCookies(CookieContainer cookies, HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return;
        }

        foreach (var value in values)
        {
            cookies.SetCookies(BaseUri, value);
        }
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            return JsonSerializer.Deserialize<T>(raw, JsonOptions)
                   ?? throw new InvalidOperationException("HappyTuk 返回空响应");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException($"HappyTuk 返回异常 [{(int)response.StatusCode}]：{raw[..Math.Min(raw.Length, 200)]}");
        }
    }

    private static string ReturnCode(HappytukCouponResponse response) => response.ReturnCode.ValueKind switch
    {
        JsonValueKind.String => response.ReturnCode.GetString() ?? string.Empty,
        JsonValueKind.Number => response.ReturnCode.GetRawText(),
        _ => string.Empty
    };

    internal static string MessageFor(string code) => code switch
    {
        "40002" => "兑换码不在有效期内",
        "40003" or "40007" => "兑换码已使用",
        "40008" => "需要选择服务器",
        "40100" => "兑换失败，请稍后重试",
        "50404" => "角色性别不符合兑换条件",
        "50500" => "需要选择角色",
        "54001" or "54004" => "没有角色资料",
        "59000" => "礼物箱空间不足",
        _ => $"兑换失败（{code}）"
    };

    private static bool IsLoginRedirect(HttpResponseMessage response) =>
        response.StatusCode is HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect
        && response.Headers.Location?.ToString().Contains("/Member/Login", StringComparison.OrdinalIgnoreCase) == true;

    private static string MatchValue(Regex regex, string html, string name)
    {
        var match = regex.Match(html);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : throw new HappytukBrowserRequiredException($"无法读取 HappyTuk {name}，登录页结构已变化");
    }

    [GeneratedRegex("<input\\b(?=[^>]*\\bname\\s*=\\s*[\"']r[\"'])(?=[^>]*\\bvalue\\s*=\\s*[\"']([^\"']*)[\"'])[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LoginRRegex();

    [GeneratedRegex("<input\\b(?=[^>]*\\bname\\s*=\\s*[\"']clientIP[\"'])(?=[^>]*\\bvalue\\s*=\\s*[\"']([^\"']*)[\"'])[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ClientIpRegex();

    [GeneratedRegex("<input\\b(?=[^>]*\\bname\\s*=\\s*[\"']checksum[\"'])(?=[^>]*\\bvalue\\s*=\\s*[\"']([^\"']*)[\"'])[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ChecksumRegex();
}
