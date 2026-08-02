namespace OhMyBot.Core.Commanding.Notifications;

public static class NotificationTypes
{
    public const string AiRouterAutoSign = "ai-router-auto-sign";

    public const string AiRouterAutoSignDisplayName = "AI Router 自动签到";

    public const string KuroAutoSign = "kuro-auto-sign";

    public const string KuroAutoSignDisplayName = "库街区自动签到";

    public const string MihoyoAutoSign = "mihoyo-auto-sign";

    public const string MihoyoAutoSignDisplayName = "米游社自动签到";

    public const string SklandAutoSign = "skland-auto-sign";

    public const string SklandAutoSignDisplayName = "森空岛自动签到";

    public const string HappytukAutoRedeem = "happytuk-auto-redeem";

    public const string HappytukAutoRedeemDisplayName = "HappyTuk 自动兑换";
}

[Flags]
public enum NotificationPlatformFlags
{
    None = 0,
    Telegram = 1,
    QQ = 2,
    All = Telegram | QQ
}
