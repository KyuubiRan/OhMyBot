namespace OhMyBot.Core.Notifications;

public static class NotificationTypes
{
    public const string AiRouterAutoSign = "ai-router-auto-sign";

    public const string AiRouterAutoSignDisplayName = "AI Router 自动签到";
}

[Flags]
public enum NotificationPlatformFlags
{
    None = 0,
    Telegram = 1,
    QQ = 2,
    All = Telegram | QQ
}
