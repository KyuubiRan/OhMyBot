namespace OhMyBot.Core.Routing;

public sealed class RouteDocument
{
    public int Version { get; set; } = 1;

    public List<RouteDefinition> Routes { get; set; } = [];
}

public sealed class RouteDefinition
{
    public string Command { get; set; } = string.Empty;

    public string CoreCommand { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Usage { get; set; } = string.Empty;

    public string[] Aliases { get; set; } = [];

    public string RequiredPrivilege { get; set; } = "User";

    public string[] SupportPlatforms { get; set; } = [];

    public string[] SupportChatTypes { get; set; } = [];

    public bool Enabled { get; set; } = true;

    public List<RouteDefinition> Children { get; set; } = [];
}
