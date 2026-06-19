namespace OhMyBot.Core.Identity;

public sealed class IdentityCacheOptions
{
    public TimeSpan EntryTtl { get; set; } = TimeSpan.FromMinutes(30);

    public string CacheKeyPrefix { get; set; } = "ohmybot:v2:identity:";
}
