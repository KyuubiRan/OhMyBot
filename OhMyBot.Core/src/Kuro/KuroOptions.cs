namespace OhMyBot.Core.Kuro;

public sealed class KuroOptions
{
    public string BaseUrl { get; set; } = "https://api.kurobbs.com";

    public string DevCode { get; set; } = "dPlcLQhIX8rQQ0LMiFBVI9bWxk8umqKv";

    public string DistinctId { get; set; } = string.Empty;

    public string UserAgent { get; set; } = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36";

    public string Version { get; set; } = "3.0.4";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(20);
}
