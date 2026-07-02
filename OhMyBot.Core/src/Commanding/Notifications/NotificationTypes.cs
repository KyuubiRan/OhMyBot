namespace OhMyBot.Core.Commanding.Notifications;

public static class NotificationTypes
{
    public const string AiRouterAutoSign = "ai-router-auto-sign";

    public const string AiRouterAutoSignDisplayName = "AI Router 自动签到";

    public const string KuroAutoSign = "kuro-auto-sign";

    public const string KuroAutoSignDisplayName = "库街区自动签到";

    public const string MihoyoAutoSign = "mihoyo-auto-sign";

    public const string MihoyoAutoSignDisplayName = "米游社自动签到";
}

[Flags]
public enum NotificationPlatformFlags
{
    None = 0,
    Telegram = 1,
    QQ = 2,
    All = Telegram | QQ
}
