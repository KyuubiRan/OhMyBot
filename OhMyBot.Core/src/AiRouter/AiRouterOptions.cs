namespace OhMyBot.Core.AiRouter;

public sealed class AiRouterOptions
{
    public string BaseUrl { get; set; } = "https://ai.router.team";

    public TimeSpan TokenTtl { get; set; } = TimeSpan.FromHours(12);
}
