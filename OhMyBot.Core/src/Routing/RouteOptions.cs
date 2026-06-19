namespace OhMyBot.Core.Routing;

public sealed class RouteOptions
{
    public string Path { get; set; } = "routes/route.json";

    public TimeSpan ReloadDebounce { get; set; } = TimeSpan.FromMilliseconds(500);
}
