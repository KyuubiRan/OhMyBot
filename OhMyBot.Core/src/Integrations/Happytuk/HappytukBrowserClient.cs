using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OhMyBot.Core.Integrations.Happytuk;

public sealed class HappytukBrowserClient(IOptions<HappytukOptions> options, ILogger<HappytukBrowserClient> logger)
{
    private const string LoginUrl = "https://www.mangot5.com/Index/Member/Login?ref=/Index/Billing/couponList";
    private const string CouponListUrl = "https://www.mangot5.com/Index/Billing/couponList";

    // Reads live page state in one evaluate: whether Cloudflare has cleared (login field / coupon-page
    // checksum exists) and the terminal conditions we must bail on (reCAPTCHA / OTP / error toast).
    private const string StateScript =
        "(()=>{const v=e=>!!e&&e.offsetParent!==null;const m=document.querySelector('.bootbox-body');" +
        "return{url:location.href,ready:document.readyState!=='loading'," +
        "hasLogin:!!document.querySelector('#oldPassword')," +
        "hasChecksum:!!document.querySelector('input[name=checksum]')," +
        "captcha:v(document.querySelector('#captchaContainer')),otp:v(document.querySelector('#optForm'))," +
        "msg:v(m)?m.innerText.trim():null};})()";

    // ponytail: serialize rare browser fallbacks; use a small pool only if challenge traffic becomes measurable.
    private static readonly SemaphoreSlim BrowserLock = new(1, 1);
    private readonly HappytukOptions _options = options.Value;
    private string? _lastStateSig;

    public Task<string> LoginAsync(
        string loginAccount,
        string password,
        CancellationToken cancellationToken = default) =>
        WithBrowserAsync(async (cdp, deadline) =>
        {
            await EnsureCouponPageAsync(cdp, loginAccount, password, deadline, cancellationToken);
            return await BuildSessionAsync(cdp, cancellationToken);
        }, cancellationToken);

    // The .NET HttpClient redeem path is CF-blocked on this site (JA3 mismatch), so the whole
    // redeem runs here inside the real browser via same-origin fetch, carrying the live clearance.
    public Task<HappytukRedeemResultType> RedeemAsync(
        string loginAccount,
        string password,
        string couponCode,
        CancellationToken cancellationToken = default) =>
        WithBrowserAsync(async (cdp, deadline) =>
        {
            await EnsureCouponPageAsync(cdp, loginAccount, password, deadline, cancellationToken);
            return await RunRedeemAsync(cdp, couponCode, cancellationToken);
        }, cancellationToken);

    private async Task<T> WithBrowserAsync<T>(
        Func<HappytukCdpClient, DateTimeOffset, Task<T>> action,
        CancellationToken cancellationToken)
    {
        var fallback = _options.BrowserFallback;
        if (!fallback.Enabled)
        {
            throw new InvalidOperationException("HappyTuk 浏览器回退未启用");
        }

        if (!File.Exists(fallback.ExecutablePath))
        {
            throw new InvalidOperationException($"未找到 Chrome/Chromium：{fallback.ExecutablePath}");
        }

        if (!fallback.Headless
            && OperatingSystem.IsLinux()
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            throw new InvalidOperationException("Chromium 已配置为非无头模式，但服务器未设置 DISPLAY，请先启动 Xvfb");
        }

        await BrowserLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(fallback.UserDataDir);
            await using var cdp = await HappytukCdpClient.LaunchAsync(
                new CdpLaunchOptions
                {
                    ExecutablePath = fallback.ExecutablePath,
                    UserDataDir = fallback.UserDataDir,
                    Headless = fallback.Headless,
                    NoSandbox = fallback.NoSandbox,
                    ProxyServer = NullIfEmpty(_options.Proxy.Server),
                    Timeout = fallback.Timeout
                },
                cancellationToken);
            return await action(cdp, DateTimeOffset.UtcNow + fallback.Timeout);
        }
        finally
        {
            BrowserLock.Release();
        }
    }

    // Drives the browser to a signed-in coupon page, logging in with credentials if the profile's
    // session has lapsed. Returns once the coupon page (checksum input) is present.
    private async Task EnsureCouponPageAsync(
        HappytukCdpClient cdp,
        string loginAccount,
        string password,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        // Navigating to the login URL clears Cloudflare once and covers both cases: an
        // already-authenticated profile is redirected straight to the coupon page, while a
        // lapsed one shows the login form (the ref= guarantees the post-login coupon redirect).
        // Going via the coupon page first would chain a second Cloudflare challenge and blow the
        // timeout — the login session (JSESSIONID) is not persisted across browser restarts.
        _lastStateSig = null;
        logger.LogInformation("HappyTuk browser: navigating to login page (headless={Headless})", _options.BrowserFallback.Headless);
        await cdp.NavigateAsync(LoginUrl, cancellationToken);

        // The login page pulls a parser-blocking reCAPTCHA/analytics script that a region-locked
        // proxy never finishes, so document.readyState stays 'loading' forever, jQuery's ready
        // handler never binds, and clicking the submit button only fires a no-op native form POST.
        // We sidestep the page's broken JS entirely: once the form (and its r/clientIP hidden inputs)
        // has parsed, POST the login API directly via in-page fetch — it carries the browser's real
        // cf_clearance + TLS, exactly like the site's own AJAX would.
        _lastStateSig = null;
        logger.LogInformation("HappyTuk browser: navigating to login page (headless={Headless})", _options.BrowserFallback.Headless);
        await cdp.NavigateAsync(LoginUrl, cancellationToken);

        var loggedIn = false;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = await ReadStateAsync(cdp, cancellationToken);
            if (state is null)
            {
                await Task.Delay(500, cancellationToken);
                continue;
            }

            if (state.HasChecksum)
            {
                logger.LogInformation("HappyTuk browser: coupon page ready");
                return;
            }

            ThrowOnTerminalState(state);
            if (state.Message is { Length: > 0 } message && !message.Contains("成功登入", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("登录失败：" + message);
            }

            if (!loggedIn && state.HasLogin)
            {
                var (outcome, detail) = await LoginViaFetchAsync(cdp, loginAccount, password, cancellationToken);
                switch (outcome)
                {
                    case "ok":
                        loggedIn = true;
                        await cdp.NavigateAsync(CouponListUrl, cancellationToken);
                        break;
                    case "captcha":
                        throw new InvalidOperationException("HappyTuk 登录需要 reCAPTCHA 验证，请先在官网完成验证后再绑定");
                    case "otp":
                        throw new InvalidOperationException("该账号启用了 OTP，暂不支持自动登录");
                    case "blocked":
                        throw new InvalidOperationException("HappyTuk 登录接口被 Cloudflare 拦截，浏览器会话未通过验证（" + detail + "）");
                    case "fail":
                        throw new InvalidOperationException("HappyTuk 登录失败：" + (string.IsNullOrWhiteSpace(detail) ? "请检查账号密码" : detail));
                    // "noform" — hidden fields not parsed yet; keep polling.
                }
            }

            await Task.Delay(500, cancellationToken);
        }

        throw await CloudflareFailureAsync(cdp, _options.BrowserFallback, cancellationToken);
    }

    // Logs in by POSTing the site's own login API from inside the page (real cf_clearance + TLS),
    // bypassing the button's jQuery handler which never binds when readyState is stuck on a blocked
    // subresource. Returns an outcome tag and an optional detail message.
    private async Task<(string Outcome, string? Detail)> LoginViaFetchAsync(
        HappytukCdpClient cdp,
        string loginAccount,
        string password,
        CancellationToken cancellationToken)
    {
        var value = await cdp.EvaluateAsync(BuildLoginFetchScript(loginAccount, password), cancellationToken);
        var outcome = value.ValueKind == JsonValueKind.Object && value.TryGetProperty("outcome", out var o)
            ? o.GetString() ?? "noform"
            : "noform";
        var detail = value.ValueKind == JsonValueKind.Object && value.TryGetProperty("msg", out var m)
            ? m.GetString()
            : null;
        if (outcome != "noform")
        {
            logger.LogInformation("HappyTuk browser: login fetch outcome={Outcome} detail={Detail}", outcome, detail ?? "-");
        }

        return (outcome, detail);
    }

    private async Task<HappytukRedeemResultType> RunRedeemAsync(
        HappytukCdpClient cdp,
        string couponCode,
        CancellationToken cancellationToken)
    {
        var value = await cdp.EvaluateAsync(BuildRedeemScript(couponCode), cancellationToken);
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("HappyTuk 兑换返回异常");
        }

        var stage = value.TryGetProperty("stage", out var s) ? s.GetString() : null;
        var code = value.TryGetProperty("code", out var c) ? c.GetString() ?? string.Empty : string.Empty;
        logger.LogInformation("HappyTuk browser redeem: stage={Stage} code={Code}", stage ?? "-", code);
        return stage switch
        {
            "noChecksum" => HappytukRedeemResultType.SessionExpired,
            "invalidSerial" => throw new InvalidOperationException("兑换码无效或过短"),
            "needSelection" => throw new InvalidOperationException("该兑换码需要选择服务器或角色，暂不支持自动兑换"),
            "check" => code is "40003" or "40007"
                ? HappytukRedeemResultType.AlreadyRedeemed
                : throw new InvalidOperationException(HappytukHttpClient.MessageFor(code)),
            "use" => code switch
            {
                "1" => HappytukRedeemResultType.Success,
                "40003" or "40007" => HappytukRedeemResultType.AlreadyRedeemed,
                _ => throw new InvalidOperationException(HappytukHttpClient.MessageFor(code))
            },
            _ => throw new InvalidOperationException("HappyTuk 兑换返回异常")
        };
    }

    private async Task<LoginPageState?> ReadStateAsync(HappytukCdpClient cdp, CancellationToken cancellationToken)
    {
        JsonElement value;
        try
        {
            value = await cdp.EvaluateAsync(StateScript, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            // The page is mid-navigation (context destroyed); treat as "not ready yet".
            LogStateChange("navigating (" + ex.Message + ")");
            return null;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            LogStateChange("no-state (evaluate returned " + value.ValueKind + ")");
            return null;
        }

        var state = new LoginPageState(
            value.TryGetProperty("url", out var url) ? url.GetString() ?? string.Empty : string.Empty,
            value.TryGetProperty("ready", out var ready) && ready.GetBoolean(),
            value.TryGetProperty("hasLogin", out var hasLogin) && hasLogin.GetBoolean(),
            value.TryGetProperty("hasChecksum", out var hasChecksum) && hasChecksum.GetBoolean(),
            value.TryGetProperty("captcha", out var captcha) && captcha.GetBoolean(),
            value.TryGetProperty("otp", out var otp) && otp.GetBoolean(),
            value.TryGetProperty("msg", out var msg) ? msg.GetString() : null);
        LogStateChange($"url={state.Url} ready={state.Ready} login={state.HasLogin} checksum={state.HasChecksum} captcha={state.Captcha} otp={state.Otp} msg={state.Message ?? "-"}");
        return state;
    }

    private void LogStateChange(string signature)
    {
        if (signature == _lastStateSig)
        {
            return;
        }

        _lastStateSig = signature;
        logger.LogInformation("HappyTuk browser state: {State}", signature);
    }

    private static void ThrowOnTerminalState(LoginPageState state)
    {
        if (state.Captcha)
        {
            throw new InvalidOperationException("HappyTuk 登录需要 reCAPTCHA 验证，请先在官网完成验证后再绑定");
        }

        if (state.Otp)
        {
            throw new InvalidOperationException("该账号启用了 OTP，暂不支持自动登录");
        }
    }

    private static async Task<string> BuildSessionAsync(HappytukCdpClient cdp, CancellationToken cancellationToken)
    {
        var userAgent = await cdp.GetUserAgentAsync(cancellationToken);
        var cookies = await cdp.GetCookieHeaderAsync("mangot5.com", cancellationToken);
        if (string.IsNullOrWhiteSpace(cookies))
        {
            throw new InvalidOperationException("HappyTuk 浏览器登录未返回会话 Cookie");
        }

        return new HappytukSession(cookies, userAgent).Serialize();
    }

    private async Task<InvalidOperationException> CloudflareFailureAsync(
        HappytukCdpClient cdp,
        HappytukBrowserFallbackOptions fallback,
        CancellationToken cancellationToken)
    {
        var screenshotPath = Path.Combine(fallback.UserDataDir, "cloudflare-failed.png");
        var screenshot = await cdp.CaptureScreenshotAsync(cancellationToken);
        if (screenshot is not null)
        {
            try
            {
                await File.WriteAllBytesAsync(screenshotPath, screenshot, cancellationToken);
            }
            catch
            {
                screenshotPath = "截图保存失败";
            }
        }
        else
        {
            screenshotPath = "截图保存失败";
        }

        logger.LogWarning("HappyTuk browser: timed out, last state was [{State}], screenshot={Path}", _lastStateSig ?? "-", screenshotPath);
        return new InvalidOperationException(
            $"Cloudflare 验证无法由{(fallback.Headless ? "无头" : "虚拟屏幕中的")} Chrome 自动通过，页面截图：{screenshotPath}");
    }

    // Mirrors HappytukHttpClient.LoginAsync's POST to the secure-login API, but issued from inside the
    // page via fetch so it rides the browser's cf_clearance + real TLS. Returns {outcome, msg}.
    private static string BuildLoginFetchScript(string loginAccount, string password)
    {
        var account = JsonSerializer.Serialize(loginAccount);
        var secret = JsonSerializer.Serialize(password);
        return
            "(async()=>{" +
            "if(!document.querySelector('#oldPassword'))return{outcome:'noform'};" +
            "const gv=n=>{const e=document.querySelector('input[name='+n+']');return e?e.value:'';};" +
            "const r=gv('r');if(!r)return{outcome:'noform'};" +
            "const body=new URLSearchParams({ref:'/Index/Billing/couponList',loginBtnClick:'',p:'',r:r," +
            "gname:'portal',check:'',userNoString:'',clientIP:gv('clientIP')," +
            $"userID:{account},password:{secret}}});" +
            "let resp,text;try{resp=await fetch('/api/member/secureLoginJson.json',{method:'POST',credentials:'include'," +
            "headers:{'X-Requested-With':'XMLHttpRequest','Content-Type':'application/x-www-form-urlencoded','Accept':'application/json'}," +
            "body:body.toString()});text=await resp.text();}catch(e){return{outcome:'blocked',msg:'fetch:'+e};}" +
            "let j;try{j=JSON.parse(text);}catch(e){return{outcome:'blocked',msg:resp.status+' non-json'};}" +
            "const root=(j&&(j.result||j.Result))||j||{};" +
            "const pick=k=>{for(const key in root){if(key.toLowerCase()===k)return root[key];}};" +
            "if(pick('requirecaptcha'))return{outcome:'captcha'};" +
            "if(pick('otpuser'))return{outcome:'otp'};" +
            "if(pick('ok'))return{outcome:'ok'};" +
            "return{outcome:'fail',msg:String(pick('msg')||'')};" +
            "})()";
    }

    // Replicates HappytukHttpClient.RedeemAsync entirely via same-origin fetch inside the page,
    // so the requests carry the browser's real TLS fingerprint and HttpOnly clearance cookie.
    private static string BuildRedeemScript(string couponCode)
    {
        var coupon = JsonSerializer.Serialize(couponCode);
        return
            "(async()=>{" +
            "const cs=document.querySelector('input[name=checksum]');if(!cs)return{stage:'noChecksum'};" +
            "const h={'X-Requested-With':'XMLHttpRequest'};" +
            "const g=async u=>{const r=await fetch(u,{credentials:'include',headers:h});return r.json();};" +
            "const s1=await fetch('/Index/Billing/checkStringCoupon.json',{method:'POST',credentials:'include'," +
            "headers:{'X-Requested-With':'XMLHttpRequest','Content-Type':'application/x-www-form-urlencoded'}," +
            "body:'serial='+encodeURIComponent(" + coupon + ")}).then(r=>r.json());" +
            "const serial=String(s1.returnCode??'');if(!serial)return{stage:'invalidSerial'};" +
            "const s2=await g('/Index/Billing/checkCoupon.json?serial='+encodeURIComponent(serial)+'&checksum='+encodeURIComponent(cs.value));" +
            "const checkCode=String(s2.returnCode??'');" +
            "if(checkCode!=='1')return{stage:'check',code:checkCode};" +
            "if((s2.serverData&&s2.serverData.required)||(s2.characterData&&s2.characterData.required))return{stage:'needSelection'};" +
            "const s3=await g('/Index/Billing/useCoupon.json?serial='+encodeURIComponent(serial)+'&checksum='+encodeURIComponent(s2.checksum??''));" +
            "return{stage:'use',code:String(s3.returnCode??'')};" +
            "})()";
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed record LoginPageState(string Url, bool Ready, bool HasLogin, bool HasChecksum, bool Captcha, bool Otp, string? Message);
}
