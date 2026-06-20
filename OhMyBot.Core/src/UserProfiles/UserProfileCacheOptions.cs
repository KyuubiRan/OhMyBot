namespace OhMyBot.Core.UserProfiles;

public sealed class UserProfileCacheOptions
{
    public TimeSpan EntryTtl { get; set; } = TimeSpan.FromDays(3);

    public string CacheKeyPrefix { get; set; } = "ohmybot:v2:user-profile:";
}
