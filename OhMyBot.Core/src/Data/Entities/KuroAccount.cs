namespace OhMyBot.Core.Data.Entities;

public class KuroAccount
{
    public long Id { get; set; }

    public long CoreUserId { get; set; }

    public CoreUser CoreUser { get; set; } = null!;

    public long BbsUserId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string TokenCiphertext { get; set; } = string.Empty;

    public string DevCode { get; set; } = string.Empty;

    public string DistinctId { get; set; } = string.Empty;

    public bool AutoSignEnabled { get; set; }

    public long BbsTaskFlags { get; set; } = KuroBbsTaskFlags.All;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<KuroGameRole> Roles { get; set; } = new List<KuroGameRole>();
}

public static class KuroBbsTaskFlags
{
    public const long None = 0;
    public const long SignIn = 1 << 0;
    public const long ViewPosts = 1 << 1;
    public const long LikePosts = 1 << 2;
    public const long SharePosts = 1 << 3;
    public const long All = SignIn | ViewPosts | LikePosts | SharePosts;
}
