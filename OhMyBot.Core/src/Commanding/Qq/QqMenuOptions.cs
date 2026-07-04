namespace OhMyBot.Core.Commanding.Qq;

public sealed class QqMenuOptions
{
    /// <summary>菜单路由索引存活时长；与回调动作一致，默认 5 分钟。</summary>
    public TimeSpan EntryTtl { get; set; } = TimeSpan.FromMinutes(5);

    public string CacheKeyPrefix { get; set; } = "ohmybot:v2:qq-menu:";
}
