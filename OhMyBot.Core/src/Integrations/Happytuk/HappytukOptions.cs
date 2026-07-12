namespace OhMyBot.Core.Integrations.Happytuk;

public sealed class HappytukOptions
{
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan SessionTtl { get; set; } = TimeSpan.FromHours(1);

    public HappytukProxyOptions Proxy { get; set; } = new();

    public HappytukBrowserFallbackOptions BrowserFallback { get; set; } = new();
}

public sealed class HappytukProxyOptions
{
    public string Server { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}

public sealed class HappytukBrowserFallbackOptions
{
    private string _userDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OhMyBot",
        "happytuk-browser");

    public bool Enabled { get; set; } = true;

    public string ExecutablePath { get; set; } = "/usr/bin/chromium";

    public bool Headless { get; set; } = true;

    // Chrome refuses to run as root without this; commonly required on headless servers.
    public bool NoSandbox { get; set; }

    public string UserDataDir
    {
        get => ExpandHome(_userDataDir);
        set => _userDataDir = value;
    }

    // Browser fallback may need up to two Cloudflare passes (login page + post-login redirect),
    // so keep the budget generous.
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(120);

    private static string ExpandHome(string path)
    {
        if (path != "~" && !path.StartsWith("~/", StringComparison.Ordinal) && !path.StartsWith("~\\", StringComparison.Ordinal))
        {
            return path;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            return path;
        }

        return path.Length == 1
            ? home
            : Path.Combine(home, path[2..].Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
    }
}
