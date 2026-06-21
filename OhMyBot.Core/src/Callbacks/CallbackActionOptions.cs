namespace OhMyBot.Core.Callbacks;

public sealed class CallbackActionOptions
{
    public TimeSpan EntryTtl { get; set; } = TimeSpan.FromMinutes(5);

    public string CacheKeyPrefix { get; set; } = "ohmybot:v2:callback-action:";
}
