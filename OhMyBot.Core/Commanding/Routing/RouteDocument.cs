namespace OhMyBot.Core.Commanding.Routing;

public sealed class RouteDocument
{
    public int Version { get; set; } = 1;

    public List<RouteDefinition> Routes { get; set; } = [];
}

public sealed class RouteDefinition
{
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// 仅由 <c>RouteStore</c> 写出，解析时不读取：路由目标恒为 <c>Command</c> 本身。
    /// 修改此字段不会产生命令重定向效果。
    /// </summary>
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
