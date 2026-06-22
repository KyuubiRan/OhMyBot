namespace OhMyBot.Core.AiRouter;

public sealed class AiRouterOptions
{
    public TimeSpan TokenTtl { get; set; } = TimeSpan.FromHours(12);
}
