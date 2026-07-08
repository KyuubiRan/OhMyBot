namespace OhMyBot.Core.Integrations.Skland;

public sealed class SklandOptions
{
    public string HgBaseUrl { get; set; } = "https://as.hypergryph.com";

    public string SklandBaseUrl { get; set; } = "https://zonai.skland.com";

    /// <summary>森空岛 OAuth app code。</summary>
    public string AppCode { get; set; } = "4ca99fa6b56cc2ba";

    public string UserAgent { get; set; } =
        "Mozilla/5.0 (Linux; Android 12; SM-A5560 Build/V417IR; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/129.0.6668.100 Mobile Safari/537.36 SKLand/1.52.1";

    public string VName { get; set; } = "1.0.0";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
