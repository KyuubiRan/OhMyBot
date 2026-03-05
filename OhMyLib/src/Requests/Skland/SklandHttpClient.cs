// reference: https://github.com/AEtherside/skland-kit

using System.Security.Cryptography;
using Flurl.Http;
using OhMyLib.Extensions;
using OhMyLib.Requests.Skland.Data;

namespace OhMyLib.Requests.Skland;

public sealed class SklandHttpClient : IDisposable
{
    private string Token { get; }

    public SklandHttpClient(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        Token = token;
        _httpClient = new();
    }

    private readonly FlurlClient _httpClient;

    #region 数美

    private static readonly Dictionary<string, string> SmConfig = new()
    {
        { "organization", "UWXspnCCJN4sfYlNfqps" },
        { "appId", "default" },
        {
            "publicKey",
            "MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQCmxMNr7n8ZeT0tE1R9j/mPixoinPkeM+k4VGIn/s0k7N5rJAfnZ0eMER+QhwFvshzo0LNmeUkpR8uIlU/GEVr8mN28sKmwd2gpygqj0ePnBmOW4v0ZVwbSYK+izkhVFk2V/doLoMbWy6b+UnA8mkjvg0iYWRByfRsK2gdl7llqCwIDAQAB"
        },
        { "protocol", "https" },
        { "apiHost", "fp-it.portal101.cn" }
    };

    private static readonly Lazy<RSA> Rsa = new(() =>
    {
        var pk = SmConfig["publicKey"];
        var rsa = RSA.Create();
        var bytes = Convert.FromBase64String(pk);
        rsa.ImportRSAPublicKey(bytes, out _);
        return rsa;
    });

    private record DesRuleData(bool IsEncrypt, string? Cipher = null, string? Key = null, string? ObfuscatedName = null);

    // ReSharper disable ArrangeObjectCreationWhenTypeNotEvident
    private static readonly Dictionary<string, DesRuleData> DesRules = new()
    {
        ["appId"] = new(IsEncrypt: true, Cipher: "DES", Key: "uy7mzc4h", ObfuscatedName: "xx"),
        ["box"] = new(IsEncrypt: false, ObfuscatedName: "jf"),
        ["canvas"] = new(IsEncrypt: true, Cipher: "DES", Key: "snrn887t", ObfuscatedName: "yk"),
        ["clientSize"] = new(IsEncrypt: true, Cipher: "DES", Key: "cpmjjgsu", ObfuscatedName: "zx"),
        ["organization"] = new(IsEncrypt: true, Cipher: "DES", Key: "78moqjfc", ObfuscatedName: "dp"),
        ["os"] = new(IsEncrypt: true, Cipher: "DES", Key: "je6vk6t4", ObfuscatedName: "pj"),
        ["platform"] = new(IsEncrypt: true, Cipher: "DES", Key: "pakxhcd2", ObfuscatedName: "gm"),
        ["plugins"] = new(IsEncrypt: true, Cipher: "DES", Key: "v51m3pzl", ObfuscatedName: "kq"),
        ["pmf"] = new(IsEncrypt: true, Cipher: "DES", Key: "2mdeslu3", ObfuscatedName: "vw"),
        ["protocol"] = new(IsEncrypt: false, ObfuscatedName: "protocol"),
        ["referer"] = new(IsEncrypt: true, Cipher: "DES", Key: "y7bmrjlc", ObfuscatedName: "ab"),
        ["res"] = new(IsEncrypt: true, Cipher: "DES", Key: "whxqm2a7", ObfuscatedName: "hf"),
        ["rtype"] = new(IsEncrypt: true, Cipher: "DES", Key: "x8o2h2bl", ObfuscatedName: "lo"),
        ["sdkver"] = new(IsEncrypt: true, Cipher: "DES", Key: "9q3dcxp2", ObfuscatedName: "sc"),
        ["status"] = new(IsEncrypt: true, Cipher: "DES", Key: "2jbrxxw4", ObfuscatedName: "an"),
        ["subVersion"] = new(IsEncrypt: true, Cipher: "DES", Key: "eo3i2puh", ObfuscatedName: "ns"),
        ["svm"] = new(IsEncrypt: true, Cipher: "DES", Key: "fzj3kaeh", ObfuscatedName: "qr"),
        ["time"] = new(IsEncrypt: true, Cipher: "DES", Key: "q2t3odsk", ObfuscatedName: "nb"),
        ["timezone"] = new(IsEncrypt: true, Cipher: "DES", Key: "1uv05lj5", ObfuscatedName: "as"),
        ["tn"] = new(IsEncrypt: true, Cipher: "DES", Key: "x9nzj1bp", ObfuscatedName: "py"),
        ["trees"] = new(IsEncrypt: true, Cipher: "DES", Key: "acfs0xo4", ObfuscatedName: "pi"),
        ["ua"] = new(IsEncrypt: true, Cipher: "DES", Key: "k92crp1t", ObfuscatedName: "bj"),
        ["url"] = new(IsEncrypt: true, Cipher: "DES", Key: "y95hjkoo", ObfuscatedName: "cf"),
        ["version"] = new(IsEncrypt: false, ObfuscatedName: "version"),
        ["vpw"] = new(IsEncrypt: true, Cipher: "DES", Key: "r9924ab5", ObfuscatedName: "ca"),
    };
    // ReSharper restore ArrangeObjectCreationWhenTypeNotEvident

    private static readonly Dictionary<string, object> BrowserEnv = new()
    {
        ["plugins"] = "MicrosoftEdgePDFPluginPortableDocumentFormatinternal-pdf-viewer1,MicrosoftEdgePDFViewermhjfbmdgcfjbbpaeojofohoefgiehjai1",
        ["ua"] = RequestConstants.UserAgent,
        ["canvas"] = "259ffe69", // 基于浏览器的canvas获得的值，不知道复用行不行
        ["timezone"] = -480,     // 时区，应该是固定值吧
        ["platform"] = "Win32",
        ["url"] = "https://www.skland.com/", // 固定值
        ["referer"] = "",
        ["res"] = "1920_1080_24_1.25",
        ["clientSize"] = "0_0_1080_1920_1920_1080_1920_1080", // 屏幕宽度_高度_色深_window.devicePixelRatio
        ["status"] = "0011"                                   // 不知道在干啥
    };

    #endregion

    private const string AppCode = "4ca99fa6b56cc2ba";

    private static readonly Dictionary<string, string> BasicHeaders = new()
    {
        { "Accept", "*/*" },
        { "Accept-Encoding", "gzip, deflate, br, zstd" },
        { "Accept-Language", "zh-CN,zh;q=0.9,zh-TW;q=0.8" },
        { "Cache-Control", "no-cache" },
        { "Dnt", "1" },
        { "Origin", "https://www.skland.com" },
        { "Pragma", "no-cache" },
        { "Priority", "u=1, i" },
        { "Referer", "https://www.skland.com/" },
        {
            "Sec-Ch-Ua", """
                         "Not:A-Brand";v="99", "Google Chrome";v="145", "Chromium";v="145"
                         """
        },
        { "Sec-Ch-Ua-Mobile", "?0" },
        { "Sec-Ch-Ua-Platform", "\"macOS\"" },
        { "Sec-Fetch-Dest", "empty" },
        { "Sec-Fetch-Mode", "cors" },
        { "Sec-Fetch-Site", "cross-site" },
        { "Sec-Fetch-Storage-Access", "active" },
        { "User-Agent", RequestConstants.UserAgent },
    };

    private async Task<T> PostAsync<T>(string path, object body, Dictionary<string, string>? extHeaders = null)
    {
        return await _httpClient.Request(path)
                                .WithHeaders(extHeaders != null ? BasicHeaders.Merge(extHeaders) : BasicHeaders)
                                .PostJsonAsync(body)
                                .ReceiveJson<T>();
    }

    public async Task<SklandHttpResponse<SklandOAuthData>> GrantUserOAuth(int type = 0)
    {
        var param = new { appCode = AppCode, token = Token, type };
        return await PostAsync<SklandHttpResponse<SklandOAuthData>>("https://as.hypergryph.com/user/oauth2/v2/grant", param);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}