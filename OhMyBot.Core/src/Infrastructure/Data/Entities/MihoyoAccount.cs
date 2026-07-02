namespace OhMyBot.Core.Infrastructure.Data.Entities;

public enum MihoyoRegion
{
    Cn = 0,
    Os = 1
}

public class MihoyoAccount
{
    public long Id { get; set; }

    public long CoreUserId { get; set; }

    public CoreUser CoreUser { get; set; } = null!;

    public MihoyoRegion Region { get; set; }

    public long Stuid { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 完整 Cookie 字符串密文。CN 含 cookie_token(可刷新)，OS 为 ltoken 的 Cookie。
    /// </summary>
    public string CookieCiphertext { get; set; } = string.Empty;

    /// <summary>
    /// CN 专用 stoken 密文，用于刷新 cookie_token；OS 为空。失效时清空(保留 AutoSignEnabled)。
    /// </summary>
    public string StokenCiphertext { get; set; } = string.Empty;

    /// <summary>
    /// CN v2_ stoken 需要的 mid。
    /// </summary>
    public string Mid { get; set; } = string.Empty;

    public bool AutoSignEnabled { get; set; }

    /// <summary>
    /// 手动游戏签到上次勾选的游戏 Key（逗号分隔）；空字符串表示全选（默认）。
    /// </summary>
    public string GameSignSelection { get; set; } = string.Empty;

    /// <summary>
    /// 米游社任务开关位标志，仅 CN 有意义。
    /// </summary>
    public long BbsTaskFlags { get; set; } = MihoyoBbsTaskFlags.All;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<MihoyoGameRole> Roles { get; set; } = new List<MihoyoGameRole>();
}

public static class MihoyoBbsTaskFlags
{
    public const long None = 0;
    public const long SignIn = 1 << 0;
    public const long ViewPosts = 1 << 1;
    public const long LikePosts = 1 << 2;
    public const long SharePosts = 1 << 3;
    public const long All = SignIn | ViewPosts | LikePosts | SharePosts;
}
