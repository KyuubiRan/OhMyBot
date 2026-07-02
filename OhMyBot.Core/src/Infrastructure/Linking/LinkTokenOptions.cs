namespace OhMyBot.Core.Infrastructure.Linking;

public sealed class LinkTokenOptions
{
    public TimeSpan TokenTtl { get; set; } = TimeSpan.FromMinutes(5);

    public string CacheKeyPrefix { get; set; } = "ohmybot:v2:link:";
}
